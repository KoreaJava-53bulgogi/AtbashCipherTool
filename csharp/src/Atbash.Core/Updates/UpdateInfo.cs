using System.Text.RegularExpressions;

namespace Atbash.Core.Updates;

/// <summary>
/// 인터넷에 올려둔 version.json 의 내용 (Java UpdateChecker 와 같은 형식).
/// { "version", "url", "installer", "sha256", "notes" }
/// </summary>
public sealed record UpdateInfo(string Version, string DownloadUrl, string? InstallerUrl, string? Sha256, string Notes)
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

    /// <summary>버전을 숫자 단위로 비교 ("8.0.10" &gt; "8.0.9"). a 가 새 버전이면 양수</summary>
    public static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.', '-');
        var pb = b.Split('.', '-');
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int x = i < pa.Length ? ToInt(pa[i]) : 0;
            int y = i < pb.Length ? ToInt(pb[i]) : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static int ToInt(string s)
    {
        var digits = new string(s.Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out int v) ? v : 0;
    }
}
