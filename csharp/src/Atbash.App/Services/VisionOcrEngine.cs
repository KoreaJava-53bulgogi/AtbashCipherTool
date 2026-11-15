using System;
using System.IO;

namespace Atbash.App.Services;

/// <summary>
/// macOS: 운영체제에 들어 있는 Apple Vision 으로 글자를 읽음 (한국어·영어, 따로 설치할 것 없음).
/// .NET 에서 Vision 을 직접 부르기는 번거로워서, 작은 Swift 프로그램(atbash-vision-ocr)을 앱 안에 넣고 실행함
/// (Linux 에서 tesseract 명령을 부르는 것과 같은 방식). 소스: csharp/packaging/macos/VisionOcr.swift
/// 찾는 순서: 환경 변수 ATBASH_VISION_OCR (테스트·CI) → 실행 파일 옆 (.app/Contents/MacOS).
/// </summary>
public sealed class VisionOcrEngine : IOcrEngine
{
    public const string HelperName = "atbash-vision-ocr";
    public const string HelperVariable = "ATBASH_VISION_OCR";

    public static VisionOcrEngine System { get; } = new(FindHelper);

    private readonly Func<string?> _findHelper;

    public VisionOcrEngine(Func<string?> findHelper) => _findHelper = findHelper;

    public bool IsSupported => true;

    public bool IsAvailable(out string reason)
    {
        reason = _findHelper() == null ? $"{HelperName} not found (set {HelperVariable} or put it next to the app)" : "";
        return reason.Length == 0;
    }

    /// <exception cref="InvalidOperationException">도우미가 없거나 실패함</exception>
    public string Recognize(byte[] imageBytes)
    {
        var helper = _findHelper()
            ?? throw new InvalidOperationException($"{HelperName} is missing from the app. Please download the app again.");
        // Vision 은 원본 그림을 그대로 주는 것이 가장 잘 읽음 (Tesseract 용 다듬기는 하지 않음).
        // 그림 종류는 내용으로 알아내므로 확장자는 상관없음.
        return OcrProcess.Run(helper, imageBytes, new[] { "{input}" }, extension: ".img");
    }

    private static string? FindHelper()
    {
        var fromEnv = Environment.GetEnvironmentVariable(HelperVariable);
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return fromEnv;
        var besideApp = Path.Combine(AppContext.BaseDirectory, HelperName);
        return File.Exists(besideApp) ? besideApp : null;
    }
}
