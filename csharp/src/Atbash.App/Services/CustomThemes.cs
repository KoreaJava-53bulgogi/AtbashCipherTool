using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Media;

namespace Atbash.App.Services;

/// <summary>
/// 사용자 테마 (Java 와 같은 색 5개). 색은 Java Color.getRGB() 와 같은 ARGB 정수로 저장.
/// </summary>
public sealed class CustomThemeData
{
    public string Name { get; set; } = "";
    public int MainBackground { get; set; }
    public int TextAreaBackground { get; set; }
    public int TextForeground { get; set; }
    public int PrimaryButton { get; set; }
    public int SecondaryButton { get; set; }

    public static CustomThemeData From(string name, ThemeColors c) => new()
    {
        Name = name,
        MainBackground = ToArgb(c.MainBackground),
        TextAreaBackground = ToArgb(c.TextAreaBackground),
        TextForeground = ToArgb(c.TextForeground),
        PrimaryButton = ToArgb(c.PrimaryButton),
        SecondaryButton = ToArgb(c.SecondaryButton),
    };

    public ThemeColors Colors => new(FromArgb(MainBackground), FromArgb(TextAreaBackground), FromArgb(TextForeground),
        FromArgb(PrimaryButton), FromArgb(SecondaryButton));

    /// <summary>Java applyCustomColors 와 같은 규칙으로 전체 테마를 만듦</summary>
    public AppTheme ToTheme()
    {
        var c = Colors;
        var t = c.TextAreaBackground;
        var output = Color.FromRgb((byte)Math.Max(0, t.R - 10), (byte)Math.Max(0, t.G - 10), (byte)Math.Max(0, t.B - 10));
        var m = c.MainBackground;
        // Java Color.darker(): 각 채널 × 0.7
        var border = Color.FromRgb((byte)(m.R * 0.7), (byte)(m.G * 0.7), (byte)(m.B * 0.7));
        bool dark = (0.2126 * m.R + 0.7152 * m.G + 0.0722 * m.B) / 255 < 0.5;
        return new AppTheme(Name, m, t, output, c.TextForeground, c.PrimaryButton, c.SecondaryButton, border, dark);
    }

    public static int ToArgb(Color c) => unchecked((int)((uint)c.A << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B));

    /// <summary>Java new Color(rgb) 처럼 알파는 무시하고 불투명으로</summary>
    public static Color FromArgb(int argb) =>
        Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}

/// <summary>테마 편집 창에서 고르는 색 5개 (Java 의 사용자 정의 창과 같은 순서)</summary>
public sealed record ThemeColors(Color MainBackground, Color TextAreaBackground, Color TextForeground,
    Color PrimaryButton, Color SecondaryButton)
{
    public static ThemeColors FromTheme(AppTheme t) => new(t.Background, t.TextArea, t.Foreground, t.Primary, t.Secondary);
}

public static class ThemeNames
{
    /// <summary>LIGHT, DARK, CUSTOM 은 쓸 수 없음 (Java 와 같음)</summary>
    public static bool IsValid(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !new[] { "LIGHT", "DARK", "CUSTOM" }.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// .theme 파일 (Java Properties 형식, Java 버전과 서로 주고받을 수 있음):
///   #Custom ArtBash Theme
///   main.background=-52327
///   textarea.background=-1 ...
/// </summary>
public static class ThemeFile
{
    private static readonly string[] Keys =
        { "main.background", "textarea.background", "text.foreground", "button.primary", "button.secondary" };

    public static void Write(string path, ThemeColors colors)
    {
        var values = new[]
        {
            colors.MainBackground, colors.TextAreaBackground, colors.TextForeground, colors.PrimaryButton, colors.SecondaryButton,
        };
        var sb = new StringBuilder();
        sb.Append("#Custom ArtBash Theme\n");
        sb.Append('#').Append(DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture)).Append('\n');
        for (int i = 0; i < Keys.Length; i++)
            sb.Append(Keys[i]).Append('=').Append(CustomThemeData.ToArgb(values[i]).ToString(CultureInfo.InvariantCulture)).Append('\n');
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }

    /// <exception cref="FormatException">색 5개가 모두 들어 있지 않거나 숫자가 아님</exception>
    public static ThemeColors Read(string path)
    {
        var props = new Dictionary<string, string>();
        foreach (var raw in File.ReadAllLines(path, Encoding.Latin1))
        {
            var line = raw.TrimStart();
            if (line.Length == 0 || line[0] is '#' or '!') continue;
            int sep = line.IndexOfAny(new[] { '=', ':' });
            if (sep < 0) continue;
            props[line[..sep].Trim()] = line[(sep + 1)..].Trim();
        }
        var colors = Keys.Select(k =>
        {
            if (!props.TryGetValue(k, out var v) || !int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int argb))
                throw new FormatException("missing or invalid " + k);
            return CustomThemeData.FromArgb(argb);
        }).ToArray();
        return new ThemeColors(colors[0], colors[1], colors[2], colors[3], colors[4]);
    }
}
