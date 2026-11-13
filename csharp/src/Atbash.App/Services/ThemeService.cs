using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Atbash.App.Services;

/// <summary>화면 색. 값은 Java 버전의 Theme.LIGHT / Theme.DARK 와 같음</summary>
public sealed record AppTheme(
    string Name,
    Color Background,
    Color TextArea,
    Color OutputArea,
    Color Foreground,
    Color Primary,
    Color Secondary,
    Color Border,
    bool IsDark)
{
    public static readonly AppTheme Light = new("LIGHT",
        Color.FromRgb(245, 247, 249), Colors.White, Color.FromRgb(235, 237, 239), Color.FromRgb(50, 50, 50),
        Color.FromRgb(0, 123, 255), Color.FromRgb(108, 117, 125), Color.FromRgb(220, 223, 226), false);

    public static readonly AppTheme Dark = new("DARK",
        Color.FromRgb(43, 43, 43), Color.FromRgb(60, 63, 65), Color.FromRgb(50, 52, 54), Color.FromRgb(220, 220, 220),
        Color.FromRgb(2, 117, 216), Color.FromRgb(90, 98, 104), Color.FromRgb(80, 80, 80), true);

    /// <summary>저장된 값("LIGHT"/"DARK")을 테마로. 모르는 값이면 라이트</summary>
    public static AppTheme FromSaved(string? saved) => saved == Dark.Name ? Dark : Light;
}

/// <summary>
/// 테마를 앱 전체에 적용. 화면(axaml)은 아래 이름의 브러시를 DynamicResource 로 씀:
///   App.Background, App.TextArea, App.OutputArea, App.Foreground,
///   App.Primary, App.Secondary, App.Border
/// </summary>
public static class ThemeService
{
    public static void Apply(Application app, AppTheme theme)
    {
        app.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        Set(app, "App.Background", theme.Background);
        Set(app, "App.TextArea", theme.TextArea);
        Set(app, "App.OutputArea", theme.OutputArea);
        Set(app, "App.Foreground", theme.Foreground);
        Set(app, "App.Primary", theme.Primary);
        Set(app, "App.Secondary", theme.Secondary);
        Set(app, "App.Border", theme.Border);
    }

    private static void Set(Application app, string key, Color color) =>
        app.Resources[key] = new SolidColorBrush(color);
}
