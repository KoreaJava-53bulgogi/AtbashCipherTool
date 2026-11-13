using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Atbash.Core.Updates;

/// <summary>
/// 업데이트 확인과 자동 설치 (Java 8.1.0 의 UpdateChecker / UpdateInstaller 와 같은 동작).
///   version.json 읽기 → 설치 파일 내려받기 → SHA-256 확인 → /SILENT 로 설치 프로그램 실행
/// 설치 프로그램(Inno Setup)이 관리자 권한을 스스로 요청하고, 끝나면 앱을 다시 실행함.
/// </summary>
public sealed class UpdateDownloader
{
    /// <summary>최신 버전 정보 (GitHub 저장소 main 브랜치)</summary>
    public const string VersionUrl =
        "https://raw.githubusercontent.com/KoreaJava-53bulgogi/AtbashCipherTool/main/version.json";

    /// <summary>테스트용: 이 환경 변수에 주소를 넣으면 그 version.json 을 읽음 (file: 주소도 허용)</summary>
    public const string TestUrlEnv = "ATBASH_UPDATE_URL";

    private readonly HttpClient _http;

    public UpdateDownloader(HttpClient http) => _http = http;

    public static bool IsTestMode => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TestUrlEnv));

    public static string CurrentVersionUrl => IsTestMode ? Environment.GetEnvironmentVariable(TestUrlEnv)!.Trim() : VersionUrl;

    /// <summary>최신 버전 정보 가져오기</summary>
    public async Task<UpdateInfo> FetchLatestAsync(CancellationToken cancel = default)
    {
        var url = CurrentVersionUrl;
        string json;
        if (IsFileUrl(url) && IsTestMode)
        {
            json = await File.ReadAllTextAsync(new Uri(url).LocalPath, cancel);
        }
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var response = await _http.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode);
            json = await response.Content.ReadAsStringAsync(cancel);
        }
        return UpdateInfo.Parse(json);
    }

    /// <summary>
    /// 설치 파일을 임시 폴더에 내려받음. progress(받은 바이트, 전체 바이트 또는 -1)
    /// </summary>
    public async Task<string> DownloadAsync(string url, IProgress<(long Done, long Total)>? progress, CancellationToken cancel)
    {
        CheckAllowedUrl(url);
        var name = Regex.Replace(url[(url.LastIndexOf('/') + 1)..].Split('?')[0], "[^A-Za-z0-9._-]", "_");
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Installer must be an .exe file: " + name);
        var dir = Directory.CreateTempSubdirectory("atbash-update-").FullName;
        var target = Path.Combine(dir, name);

        try
        {
            Stream source;
            long total;
            HttpResponseMessage? response = null;
            if (IsFileUrl(url))
            {
                var path = new Uri(url).LocalPath;
                total = new FileInfo(path).Length;
                source = File.OpenRead(path);
            }
            else
            {
                response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
                if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode);
                total = response.Content.Headers.ContentLength ?? -1;
                source = await response.Content.ReadAsStreamAsync(cancel);
            }

            await using (source)
            await using (var output = File.Create(target))
            {
                var buffer = new byte[64 * 1024];
                long done = 0;
                int n;
                while ((n = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n), cancel);
                    done += n;
                    progress?.Report((done, total));
                }
            }
            response?.Dispose();
            return target;
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    /// <summary>내려받은 파일의 지문이 version.json 의 값과 다르면 파일을 지우고 예외</summary>
    public static void Verify(string path, string expectedSha256)
    {
        var actual = Sha256(path);
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(path);
            throw new VerificationException($"SHA-256 mismatch: expected {expectedSha256}, got {actual}");
        }
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// 조용한 설치로 실행. 이 다음에 앱을 바로 종료해야 설치 프로그램이 파일을 바꿀 수 있음.
    /// /SILENT 진행률만 표시, /SUPPRESSMSGBOXES 확인 창 자동 처리, /NORESTART 재시작 안 함,
    /// /CLOSEAPPLICATIONS 켜져 있는 앱 닫기
    /// </summary>
    public static void LaunchInstaller(string path)
    {
        var psi = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
        foreach (var a in new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS" }) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    /// <summary>https 만 허용. 테스트 모드(ATBASH_UPDATE_URL)에서만 내 PC 파일(file:)도 허용</summary>
    public static void CheckAllowedUrl(string url)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        if (IsFileUrl(url) && IsTestMode) return;
        throw new IOException("Only https:// download links are allowed: " + url);
    }

    private static bool IsFileUrl(string url) => url.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>지문이 다름 (파일이 깨졌거나 바뀜)</summary>
public sealed class VerificationException : IOException
{
    public VerificationException(string message) : base(message) { }
}
