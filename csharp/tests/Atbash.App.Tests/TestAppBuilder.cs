using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Atbash.App.Tests.TestAppBuilder))]

namespace Atbash.App.Tests;

/// <summary>
/// 화면 없이(Headless) 앱을 띄우는 설정. UseHeadlessDrawing = false 로 실제 Skia 렌더링을 써서
/// 창을 이미지로 저장할 수 있게 함. 사용자의 실제 화면은 전혀 건드리지 않음.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
