using System;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;
using Tesseract;

namespace Atbash.App.Services;

/// <summary>
/// 이미지에서 글자를 읽는 엔진. 운영체제마다 다른 엔진을 씀 (크로스 플랫폼 계획):
///   Windows x64 → Tesseract (지금),  macOS → Apple Vision (D단계),  Linux → 시스템 Tesseract (C단계).
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
    public static IOcrEngine For(string os, Architecture architecture) =>
        os == "win" && architecture is Architecture.X64 or Architecture.X86
            ? TesseractOcrEngine.Instance
            : UnsupportedOcrEngine.Instance;

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
        using var original = SKBitmap.Decode(imageBytes) ?? throw new InvalidDataException("Cannot read image.");
        var prepared = Preprocess(original);

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
