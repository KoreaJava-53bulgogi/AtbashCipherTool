using System;
using System.IO;
using SkiaSharp;
using Tesseract;

namespace Atbash.App.Services;

/// <summary>
/// 이미지에서 글자 읽기 (Tesseract, 한국어+영어). Java 버전과 같은 tessdata 와 같은 전처리:
/// 흑백으로 바꾸고 2배로 키운 뒤 인식.
/// </summary>
public static class OcrService
{
    private static readonly object Lock = new();
    private static TesseractEngine? _engine;

    /// <summary>tessdata 폴더 (실행 파일 옆)</summary>
    public static string DataPath => Path.Combine(AppContext.BaseDirectory, "tessdata");

    /// <summary>이미지 파일(png, jpg, bmp, gif...) 또는 그 바이트에서 글자를 읽음. 시간이 걸리므로 백그라운드에서 호출</summary>
    /// <exception cref="InvalidDataException">이미지를 읽을 수 없음</exception>
    /// <exception cref="FileNotFoundException">tessdata 가 없음</exception>
    public static string Recognize(byte[] imageBytes)
    {
        using var original = SKBitmap.Decode(imageBytes) ?? throw new InvalidDataException("Cannot read image.");
        var prepared = Preprocess(original);

        lock (Lock)
        {
            _engine ??= CreateEngine();
            using var pix = Pix.LoadFromMemory(prepared);
            using var page = _engine.Process(pix);
            return page.GetText() ?? "";
        }
    }

    public static string RecognizeFile(string path) => Recognize(File.ReadAllBytes(path));

    /// <summary>
    /// 이 운영체제에서 OCR 엔진을 쓸 수 있는지 확인 (엔진 파일과 언어 데이터가 있는지).
    /// 지금 쓰는 Tesseract 패키지는 Windows 용 엔진만 들어 있어서 macOS·Linux 에서는 false 임.
    /// 쓸 수 없으면 이유를 돌려줌.
    /// </summary>
    public static bool IsAvailable(out string reason)
    {
        try
        {
            lock (Lock)
            {
                _engine ??= CreateEngine();
            }
            reason = "";
            return true;
        }
        catch (Exception e)
        {
            // 겉에 싸여 있는 예외(TargetInvocationException 등)를 벗겨서 진짜 이유(어떤 파일을 못 찾는지)를 보여줌
            var root = e.GetBaseException();
            reason = root.GetType().Name + ": " + root.Message;
            return false;
        }
    }

    private static TesseractEngine CreateEngine()
    {
        foreach (var lang in new[] { "kor", "eng" })
        {
            if (!File.Exists(Path.Combine(DataPath, lang + ".traineddata")))
                throw new FileNotFoundException("Tesseract data files missing.", lang + ".traineddata");
        }
        return new TesseractEngine(DataPath, "kor+eng", EngineMode.Default);
    }

    /// <summary>흑백 + 2배 확대 (Java 버전과 같은 전처리) 후 PNG 로</summary>
    private static byte[] Preprocess(SKBitmap source)
    {
        var info = new SKImageInfo(source.Width * 2, source.Height * 2, SKColorType.Gray8, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White); // 투명한 부분은 흰 바탕으로
        using var image = SKImage.FromBitmap(source);
        using var paint = new SKPaint();
        canvas.DrawImage(image, new SKRect(0, 0, info.Width, info.Height),
            new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
