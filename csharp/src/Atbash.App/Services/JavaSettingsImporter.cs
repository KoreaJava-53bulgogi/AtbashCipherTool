using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Atbash.Core.Settings;
using Avalonia.Media;

namespace Atbash.App.Services;

/// <summary>Java 버전 설정 읽기 (테스트에서는 가짜로)</summary>
public interface IJavaPrefsSource
{
    /// <summary>원래 이름·값으로 되돌린 설정. Java 버전을 쓴 적이 없으면 null</summary>
    IReadOnlyDictionary<string, string>? ReadAll();
}

/// <summary>레지스트리 HKCU\Software\JavaSoft\Prefs\&lt;unnamed&gt; (Java 버전이 설정을 저장한 곳)</summary>
public sealed class RegistryJavaPrefs : IJavaPrefsSource
{
    public const string KeyPath = @"Software\JavaSoft\Prefs\<unnamed>";

    public IReadOnlyDictionary<string, string>? ReadAll() => OperatingSystem.IsWindows() ? Read() : null;

    [SupportedOSPlatform("windows")]
    private static IReadOnlyDictionary<string, string>? Read()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key == null) return null;
        var result = new Dictionary<string, string>();
        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is not string raw) continue;
            try
            {
                result[JavaPrefsCodec.DecodeName(name)] = JavaPrefsCodec.DecodeValue(raw);
            }
            catch (FormatException)
            {
                // 알 수 없는 값은 건너뜀
            }
        }
        return result;
    }
}

/// <summary>
/// Java 버전(7.x~8.x)의 설정을 C# 설정으로 옮김. C# 설정 파일이 아직 없을 때 처음 한 번만.
/// 언어, 테마(사용자 테마 포함), 변환 방식, 옵션 값, 실시간 변환, 설정 저장, 창 크기,
/// 건너뛴 버전, 마지막 업데이트 확인 시각. (네이버 키는 기능을 뺐으므로 옮기지 않음)
/// </summary>
public static class JavaSettingsImporter
{
    /// <returns>옮겼으면 true</returns>
    public static bool MigrateIfNeeded(SettingsStore store, IJavaPrefsSource source)
    {
        if (File.Exists(store.Path)) return false;
        IReadOnlyDictionary<string, string>? prefs;
        try
        {
            prefs = source.ReadAll();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
        if (prefs == null || prefs.Count == 0) return false;
        store.Save(Import(prefs));
        return true;
    }

    public static AppSettings Import(IReadOnlyDictionary<string, string> p)
    {
        var s = new AppSettings { MigratedFromJava = true };

        if (p.TryGetValue("language", out var lang)) s.Language = Localizer.ToSaved(Localizer.Parse(lang));
        s.CipherMode = Int(p, "cipherMode", s.CipherMode, 0, 19);
        s.CaesarShift = Int(p, "caesarShift", s.CaesarShift, -1000, 1000);
        s.ScytaleDiameter = Int(p, "scytaleDiameter", s.ScytaleDiameter, 1, 1000);
        s.HangulShift = Int(p, "hangulShift", s.HangulShift, -1000, 1000);
        s.LivePreview = Bool(p, "livePreview", s.LivePreview);
        s.SaveSettingsEnabled = Bool(p, "saveSettingsEnabled", s.SaveSettingsEnabled);
        s.WindowWidth = Int(p, "windowWidth", s.WindowWidth, 700, 10000);
        s.WindowHeight = Int(p, "windowHeight", s.WindowHeight, 400, 10000);
        s.SkippedVersion = Text(p, "skippedVersion");
        if (p.TryGetValue("lastUpdateCheck", out var last) && long.TryParse(last, out var ms)) s.LastUpdateCheck = ms;

        // 사용자 테마: 이름 목록(쉼표) + 이름마다 색 5개. 없는 색은 Java 와 같이 라이트 테마 색으로
        if (p.TryGetValue("custom.themes.list", out var list) && list.Length > 0)
        {
            var light = ThemeColors.FromTheme(AppTheme.Light);
            foreach (var name in list.Split(',').Where(n => n.Length > 0).Distinct())
            {
                s.CustomThemes.Add(new CustomThemeData
                {
                    Name = name,
                    MainBackground = Color(p, name, "main.background", light.MainBackground),
                    TextAreaBackground = Color(p, name, "textarea.background", light.TextAreaBackground),
                    TextForeground = Color(p, name, "text.foreground", light.TextForeground),
                    PrimaryButton = Color(p, name, "button.primary", light.PrimaryButton),
                    SecondaryButton = Color(p, name, "button.secondary", light.SecondaryButton),
                });
            }
        }

        // 테마: LIGHT / DARK / 사용자 테마 이름. 모르는 이름이면 라이트
        if (p.TryGetValue("theme", out var theme))
            s.Theme = theme is "LIGHT" or "DARK" || s.CustomThemes.Any(t => t.Name == theme) ? theme : "LIGHT";
        return s;
    }

    private static int Int(IReadOnlyDictionary<string, string> p, string key, int fallback, int min, int max) =>
        p.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n, min, max)
            : fallback;

    private static bool Bool(IReadOnlyDictionary<string, string> p, string key, bool fallback) =>
        p.TryGetValue(key, out var v) ? string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) : fallback;

    private static string? Text(IReadOnlyDictionary<string, string> p, string key) =>
        p.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    private static int Color(IReadOnlyDictionary<string, string> p, string theme, string part, Color fallback) =>
        p.TryGetValue($"custom.theme.{theme}.{part}", out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var argb)
            ? argb
            : CustomThemeData.ToArgb(fallback);
}
