using Atbash.App.Services;

namespace Atbash.App.Tests;

/// <summary>
/// 실제로 글자를 읽어 보는 OCR 테스트의 출입문.
/// Windows x64 에서는 OCR 이 반드시 동작해야 하므로 그냥 통과시킴 (고장 나면 테스트가 실패해야 함).
/// Linux 는 시스템 tesseract 가 설치되어 있으면, macOS 는 Vision 도우미(ATBASH_VISION_OCR)가 있으면 실행됨 (CI 는 둘 다 준비함).
/// 엔진이 없거나 준비되지 않은 곳에서만 이유를 적고 건너뜀.
/// 엔진이 생기면 건너뛰지 않고 저절로 실행됨.
/// </summary>
internal static class OcrGuard
{
    public static void RequireOrSkip()
    {
        if (OperatingSystem.IsWindows() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                is System.Runtime.InteropServices.Architecture.X64)
            return;
        if (!OcrService.Current.IsAvailable(out var reason))
            Assert.Skip("No OCR engine on this OS yet: " + reason);
    }
}
