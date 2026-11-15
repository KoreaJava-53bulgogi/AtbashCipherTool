using Atbash.App.Services;

namespace Atbash.App.Tests;

/// <summary>
/// OCR 이 필요한 테스트의 출입문.
/// Windows 에서는 OCR 이 반드시 동작해야 하므로 그냥 통과시킴 (고장 나면 테스트가 실패해야 함).
/// macOS·Linux 용 엔진은 아직 없어서(계획: macOS Apple Vision, Linux 시스템 Tesseract) 그 환경에서만 이유를 적고 건너뜀.
/// 엔진이 생기면 건너뛰지 않고 저절로 실행됨.
/// </summary>
internal static class OcrGuard
{
    public static void RequireOrSkip()
    {
        if (OperatingSystem.IsWindows()) return;
        if (!OcrService.IsAvailable(out var reason))
            Assert.Skip("No OCR engine on this OS yet: " + reason);
    }
}
