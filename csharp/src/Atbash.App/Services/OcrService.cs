using System;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;
using Tesseract;

namespace Atbash.App.Services;

/// <summary>
/// 이미지에서 글자를 읽는 엔진. 운영체제마다 다른 엔진을 씀 (크로스 플랫폼 계획):
///   Windows x64 → Tesseract 라이브러리,  macOS → Apple Vision (VisionOcrEngine),  Linux → 시스템 tesseract 명령 (TesseractCliOcrEngine).
/// 엔진이 아직 없는 곳은 <see cref="UnsupportedOcrEngine"/>.
/// </summary>
public interface IOcrEngine
{
    /// <summary>이 운영체제에 맞는 엔진이 있는지 (없으면 메뉴를 눌렀을 때 "지원하지 않음" 안내)</summary>
    bool IsSupported { get; }

    /// <summary>엔진을 실제로 쓸 수 있는지 (엔진 파일과 언어 데이터가 있는지). 쓸 수 없으면 이유를 돌려줌</summary>
    bool IsAvailable(out string reason);

    /// <summary>이미지 바이트(png, jpg, bmp, gif...)에서 글자를 읽음. 시간이 걸리므로 백그라운드에서 호출</summary>
    /// <exception cref="InvalidDataException">이미지를 읽을 수 없음</exception>
    /// <exception cref="PlatformNotSupportedException">이 운영체제용 엔진이 없음</exception>
    string Recognize(byte[] imageBytes);
}

/// <summary>지금 운영체제의 OCR 엔진을 고름</summary>
public static class OcrService
{
    /// <summary>지금 운영체제의 엔진 (앱 전체에서 하나)</summary>
    public static IOcrEngine Current { get; } = For(
        OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsLinux() ? "linux" : "other",
        RuntimeInformation.ProcessArchitecture);

    /// <summary>
    /// 운영체제·CPU 에 맞는 엔진. Tesseract 패키지에는 Windows x86/x64 용 엔진 파일만 들어 있어서
    /// 그 밖의 조합(Windows ARM64 포함)은 아직 "지원하지 않음".
    /// </summary>
    public static IOcrEngine For(string os, Architecture architecture) => os switch
    {
        "win" when architecture is Architecture.X64 or Architecture.X86 => TesseractOcrEngine.Instance,
        "linux" => TesseractCliOcrEngine.System,
        "osx" => VisionOcrEngine.System,
        _ => UnsupportedOcrEngine.Instance,
    };

    /// <summary>tessdata 폴더 (실행 파일 옆)</summary>
    public static string DataPath => Path.Combine(AppContext.BaseDirectory, "tessdata");
}

/// <summary>엔진이 아직 없는 운영체제용: 항상 PlatformNotSupportedException</summary>
public sealed class UnsupportedOcrEngine : IOcrEngine
{
    public static UnsupportedOcrEngine Instance { get; } = new();

    private UnsupportedOcrEngine() { }

    public bool IsSupported => false;

    public bool IsAvailable(out string reason)
    {
        reason = $"no OCR engine for {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}) yet";
        return false;
    }

    public string Recognize(byte[] imageBytes) => throw new PlatformNotSupportedException("OCR is not supported on this OS yet.");
}

/// <summary>
/// Tesseract (한국어+영어). Java 버전과 같은 tessdata 와 같은 전처리: 흑백으로 바꾸고 2배로 키운 뒤 인식.
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    public static TesseractOcrEngine Instance { get; } = new();

    private readonly object _lock = new();
    private TesseractEngine? _engine;

    private TesseractOcrEngine() { }

    public bool IsSupported => true;

    /// <exception cref="FileNotFoundException">tessdata 가 없음</exception>
    public string Recognize(byte[] imageBytes)
    {
        var prepared = OcrImage.Prepare(imageBytes);

        lock (_lock)
        {
            _engine ??= CreateEngine();
            using var pix = Pix.LoadFromMemory(prepared);
            using var page = _engine.Process(pix);
            return page.GetText() ?? "";
        }
    }

    public bool IsAvailable(out string reason)
    {
        try
        {
            lock (_lock)
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
            if (!File.Exists(Path.Combine(OcrService.DataPath, lang + ".traineddata")))
                throw new FileNotFoundException("Tesseract data files missing.", lang + ".traineddata");
        }
        return new TesseractEngine(OcrService.DataPath, "kor+eng", EngineMode.Default);
    }
}

/// <summary>OCR 전에 이미지를 다듬음 (모든 Tesseract 엔진이 같이 씀)</summary>
/// <summary>
/// OCR 전에 이미지를 Tesseract 가 잘 읽는 모양으로 다듬음:
///  1) 흑백으로 (투명한 부분은 흰 바탕)
///  2) 배경이 어두우면 뒤집음 (다크 테마 캡처: 밝은 글씨 → 검은 글씨)
///  3) 글줄 높이를 재서 약 <see cref="TargetLineHeight"/> 픽셀이 되게 줄이거나 키움
///     (예전에는 Java 버전처럼 무조건 2배였는데, 큰 글씨는 너무 커져서 못 읽었음)
///  4) 둘레에 흰 여백 (글자가 가장자리에 붙으면 Tesseract 가 아무것도 못 읽음)
/// </summary>
public static class OcrImage
{
    /// <summary>Tesseract 가 가장 잘 읽는 글줄 높이 (픽셀)</summary>
    public const int TargetLineHeight = 40;

    /// <summary>둘레에 넣는 흰 여백 (픽셀, 크기를 바꾼 뒤 기준)</summary>
    public const int Margin = 32;

    /// <summary>크기를 바꾼 뒤 그림의 긴 변 한계 (너무 커지면 느려짐)</summary>
    private const int MaxSide = 8000;

    /// <exception cref="InvalidDataException">이미지를 읽을 수 없음</exception>
    public static byte[] Prepare(byte[] imageBytes)
    {
        using var original = SKBitmap.Decode(imageBytes) ?? throw new InvalidDataException("Cannot read image.");
        int width = original.Width, height = original.Height;
        var gray = ToGray(original);
        if (IsDarkBackground(gray))
            for (int i = 0; i < gray.Length; i++) gray[i] = (byte)(255 - gray[i]);

        double scale = ScaleFor(gray, width, height);
        scale = Math.Min(scale, MaxSide / (double)Math.Max(width, height));

        using var grayBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        System.Runtime.InteropServices.Marshal.Copy(gray, 0, grayBitmap.GetPixels(), gray.Length);
        grayBitmap.NotifyPixelsChanged();

        int outW = Math.Max(1, (int)Math.Round(width * scale)), outH = Math.Max(1, (int)Math.Round(height * scale));
        var info = new SKImageInfo(outW + 2 * Margin, outH + 2 * Margin, SKColorType.Gray8, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(grayBitmap);
        using var paint = new SKPaint();
        var sampling = scale >= 1 ? new SKSamplingOptions(SKCubicResampler.Mitchell) : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawImage(image, new SKRect(Margin, Margin, Margin + outW, Margin + outH), sampling, paint);
        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>밝기 0~255 (한 줄씩 이어 붙인 배열). 투명한 부분은 흰 바탕에 올린 것처럼</summary>
    private static byte[] ToGray(SKBitmap source)
    {
        using var rgba = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (!source.CopyTo(rgba, SKColorType.Rgba8888)) throw new InvalidDataException("Cannot read image.");
        var pixels = rgba.GetPixelSpan();
        var gray = new byte[source.Width * source.Height];
        for (int i = 0; i < gray.Length; i++)
        {
            int r = pixels[i * 4], g = pixels[i * 4 + 1], b = pixels[i * 4 + 2], a = pixels[i * 4 + 3];
            int lum = (r * 299 + g * 587 + b * 114) / 1000;
            gray[i] = (byte)((lum * a + 255 * (255 - a)) / 255);
        }
        return gray;
    }

    /// <summary>배경이 어두운지 (평균 밝기가 중간보다 어두우면, 대부분을 차지하는 배경이 어두운 것)</summary>
    public static bool IsDarkBackground(byte[] gray)
    {
        if (gray.Length == 0) return false;
        long sum = 0;
        foreach (var v in gray) sum += v;
        return sum / gray.Length < 128;
    }

    /// <summary>
    /// 글줄 높이가 <see cref="TargetLineHeight"/> 가 되도록 하는 배율 (0.25~4배).
    /// 가로로 훑어서 어두운 점이 있는 줄들이 이어진 덩어리 = 글줄로 보고, 그 높이의 중앙값을 씀.
    /// 글줄을 찾지 못하면 예전과 같은 2배.
    /// </summary>
    public static double ScaleFor(byte[] gray, int width, int height)
    {
        int minInk = Math.Max(1, width / 500); // 잡티 무시
        var lines = new System.Collections.Generic.List<int>();
        int run = 0;
        for (int y = 0; y <= height; y++)
        {
            bool ink = false;
            if (y < height)
            {
                int count = 0;
                for (int x = 0, row = y * width; x < width; x++)
                    if (gray[row + x] < 128 && ++count >= minInk) { ink = true; break; }
            }
            if (ink) run++;
            else
            {
                if (run >= 3) lines.Add(run);
                run = 0;
            }
        }
        if (lines.Count == 0) return 2.0;
        lines.Sort();
        double median = lines[lines.Count / 2];
        return Math.Clamp(TargetLineHeight / median, 0.25, 4.0);
    }
}
