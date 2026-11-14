using System.Text.RegularExpressions;

namespace Atbash.Core.Updates;

/// <summary>
/// 한 플랫폼이 받을 업데이트 정보. version.json 에서 읽음 (UpdateManifest 참고).
/// Parse 는 예전 앱(Java 8.x, C# 9.0.0)이 읽던 방식 그대로 맨 처음 나오는 값을 읽음:
/// { "version", "url", "installer", "sha256", "notes" }
/// </summary>
public sealed record UpdateInfo(string Version, string DownloadUrl, string? InstallerUrl, string? Sha256, string Notes, string? Tag = null)
{
    public const string DefaultDownloadUrl =
        "https://github.com/KoreaJava-53bulgogi/AtbashCipherTool/releases/latest";

    /// <summary>설치 파일 주소와 지문이 모두 있어야 자동 설치를 제안함</summary>
    public bool CanAutoInstall =>
        InstallerUrl != null && Sha256 != null && Regex.IsMatch(Sha256, "^[0-9a-fA-F]{64}$");

    /// <exception cref="FormatException">version 이 없음</exception>
    public static UpdateInfo Parse(string json)
    {
        var version = Field(json, "version");
        if (string.IsNullOrWhiteSpace(version)) throw new FormatException("version.json has no \"version\" field");
        var url = Field(json, "url");
        var installer = Field(json, "installer");
        var sha = Field(json, "sha256");
        var notes = Field(json, "notes");
        return new UpdateInfo(
            version.Trim(),
            string.IsNullOrWhiteSpace(url) ? DefaultDownloadUrl : url.Trim(),
            string.IsNullOrWhiteSpace(installer) ? null : installer.Trim(),
            string.IsNullOrWhiteSpace(sha) ? null : sha.Trim().ToLowerInvariant(),
            notes == null ? "" : notes.Replace("\\n", "\n").Replace("\\\"", "\""));
    }

    // Java 와 같은 방식으로 "이름": "값" 을 찾음 (값 안의 \" 같은 이스케이프 허용)
    private static string? Field(string json, string name)
    {
        var m = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// 버전을 숫자 단위로 비교 ("8.0.10" &gt; "8.0.9"). a 가 새 버전이면 양수.
    /// 베타(9.1.0-beta)는 같은 숫자의 정식(9.1.0)보다 낮은 버전이고, 베타끼리는 이름·숫자 순서로 비교함
    /// (그래서 베타를 쓰는 사람에게 정식 버전이 나왔다고 알릴 수 있음).
    /// </summary>
    public static int CompareVersions(string a, string b)
    {
        var (na, pa) = SplitVersion(a);
        var (nb, pb) = SplitVersion(b);
        int n = Math.Max(na.Length, nb.Length);
        for (int i = 0; i < n; i++)
        {
            int x = i < na.Length ? na[i] : 0;
            int y = i < nb.Length ? nb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        if (pa == null && pb == null) return 0;
        if (pa == null) return 1;   // 정식 > 베타
        if (pb == null) return -1;
        return ComparePreRelease(pa, pb);
    }

    private static (int[] Numbers, string? PreRelease) SplitVersion(string version)
    {
        var v = version.Trim().TrimStart('v', 'V');
        int dash = v.IndexOf('-');
        var core = dash < 0 ? v : v[..dash];
        var pre = dash < 0 ? null : v[(dash + 1)..];
        return (core.Split('.').Select(ToInt).ToArray(), string.IsNullOrEmpty(pre) ? null : pre);
    }

    private static int ComparePreRelease(string a, string b)
    {
        var ia = a.Split('.');
        var ib = b.Split('.');
        for (int i = 0; i < Math.Min(ia.Length, ib.Length); i++)
        {
            bool numA = long.TryParse(ia[i], out long x);
            bool numB = long.TryParse(ib[i], out long y);
            int c = numA && numB ? x.CompareTo(y)
                : numA ? -1            // 숫자 이름이 글자 이름보다 낮음
                : numB ? 1
                : string.CompareOrdinal(ia[i], ib[i]);
            if (c != 0) return c;
        }
        return ia.Length.CompareTo(ib.Length); // 이름이 더 긴 쪽이 높음 (beta < beta.2)
    }

    private static int ToInt(string s)
    {
        var digits = new string(s.Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out int v) ? v : 0;
    }
}
