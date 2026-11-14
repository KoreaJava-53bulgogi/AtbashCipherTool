using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Atbash.Core.Updates;

/// <summary>
/// GitHub 릴리스 태그 이름 규칙과 릴리스 주소.
///   Windows x64 : 9.1.0        (예전 태그 v8.0.0, v8.1.0, v9.0.0 도 Windows 로 읽음)
///   macOS arm64 : a_9.1.0
///   Linux x64   : l_9.1.0
/// 베타는 9.1.0-beta, 9.1.0-beta.2 처럼 뒤에 -이름 을 붙임 (정식 9.1.0 보다 낮은 버전으로 비교됨).
/// 앱은 태그를 직접 읽지 않고 version.json 만 읽음. 태그는 릴리스를 구분하고 설치 파일 주소를 만드는 데 씀.
/// </summary>
public static class ReleaseTags
{
    public const string Repo = "KoreaJava-53bulgogi/AtbashCipherTool";

    private const string VersionPattern = @"\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*)?";
    private static readonly Regex VersionOnly = new($"^{VersionPattern}$", RegexOptions.CultureInvariant);
    private static readonly Regex OldWindowsTag = new($"^v?(?<v>{VersionPattern})$", RegexOptions.CultureInvariant);

    public static bool IsValidVersion(string version) => VersionOnly.IsMatch(version);

    /// <summary>플랫폼과 버전으로 태그 이름을 만듦 (예: a_9.1.0)</summary>
    /// <exception cref="InvalidOperationException">태그 규칙이 정해지지 않은 플랫폼</exception>
    /// <exception cref="ArgumentException">버전 모양이 올바르지 않음</exception>
    public static string Make(PlatformInfo platform, string version)
    {
        if (platform.TagPrefix == null)
            throw new InvalidOperationException($"No tag prefix is defined for {platform.Id}. Decide one and add it to PlatformInfo.");
        if (!IsValidVersion(version))
            throw new ArgumentException($"Invalid version '{version}'. Use x.y.z or x.y.z-beta.", nameof(version));
        return platform.TagPrefix + version;
    }

    /// <summary>태그에서 플랫폼과 버전을 읽음. 규칙에 맞지 않으면 false</summary>
    public static bool TryParse(string tag, [NotNullWhen(true)] out PlatformInfo? platform, [NotNullWhen(true)] out string? version)
    {
        platform = null;
        version = null;
        foreach (var p in PlatformInfo.Released)
        {
            if (string.IsNullOrEmpty(p.TagPrefix) || !tag.StartsWith(p.TagPrefix, StringComparison.Ordinal)) continue;
            var rest = tag[p.TagPrefix.Length..];
            if (!VersionOnly.IsMatch(rest)) return false;
            platform = p;
            version = rest;
            return true;
        }
        var m = OldWindowsTag.Match(tag);
        if (!m.Success) return false;
        platform = PlatformInfo.WindowsX64;
        version = m.Groups["v"].Value;
        return true;
    }

    /// <summary>릴리스 페이지 주소 (다운로드 페이지 열기에 씀)</summary>
    public static string TagPage(string tag) => $"https://github.com/{Repo}/releases/tag/{Uri.EscapeDataString(tag)}";

    /// <summary>릴리스에 올린 파일의 내려받기 주소</summary>
    public static string AssetUrl(string tag, string fileName) =>
        $"https://github.com/{Repo}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(fileName)}";
}
