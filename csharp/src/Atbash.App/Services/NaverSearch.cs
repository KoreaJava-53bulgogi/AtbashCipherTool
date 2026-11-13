using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Atbash.App.Services;

public sealed record WebResult(string Title, string Description);

public sealed record ImageResult(string Title, string Link, string Thumbnail);

/// <summary>네이버가 API 키를 거부함 (401 인증 실패 / 403 검색 API 권한 없음)</summary>
public sealed class NaverAuthException : Exception
{
    public NaverAuthException(int status, string detail) : base($"HTTP {status}: {detail}") => Status = status;

    public int Status { get; }
}

/// <summary>
/// 네이버 검색 API (웹 문서 / 이미지), 결과 5개.
/// 2026년에 네이버 개발자 센터(openapi.naver.com)에서 네이버 클라우드 플랫폼의
/// NAVER API HUB 로 옮겨져서 주소와 키 헤더가 바뀌었음 (결과 형식은 같음).
///   https://api.ncloud-docs.com/docs/naver-api-hub-search-webkr
///   https://api.ncloud-docs.com/docs/naver-api-hub-search-image
/// 키(Client ID / Secret)는 네이버 클라우드 플랫폼 콘솔 → NAVER API HUB 에서 발급.
/// 결과는 정규식이 아니라 JSON 으로 제대로 읽어서, 따옴표·특수문자가 깨지지 않음.
/// </summary>
public sealed class NaverSearch
{
    public const string BaseUrl = "https://naverapihub.apigw.ntruss.com/search/v1/";

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;

    public NaverSearch(HttpClient http, string clientId, string clientSecret)
    {
        _http = http;
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    public async Task<IReadOnlyList<WebResult>> SearchWebAsync(string keyword)
    {
        using var doc = await GetAsync("webkr", keyword);
        return Items(doc).Select(i => new WebResult(Clean(Str(i, "title")), Clean(Str(i, "description")))).ToList();
    }

    public async Task<IReadOnlyList<ImageResult>> SearchImagesAsync(string keyword)
    {
        using var doc = await GetAsync("image", keyword);
        return Items(doc).Select(i => new ImageResult(Clean(Str(i, "title")), Str(i, "link"), Str(i, "thumbnail"))).ToList();
    }

    /// <summary>이미지 내려받기 (실패하면 null)</summary>
    public async Task<byte[]?> TryDownloadAsync(string url)
    {
        try
        {
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
            return await _http.GetByteArrayAsync(url);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<JsonDocument> GetAsync(string kind, string keyword)
    {
        var url = $"{BaseUrl}{kind}?display=5&format=json&query={Uri.EscapeDataString(keyword)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-NCP-APIGW-API-KEY-ID", _clientId);
        request.Headers.Add("X-NCP-APIGW-API-KEY", _clientSecret);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var detail = ErrorMessage(await response.Content.ReadAsStringAsync());
            // 401: 키가 틀렸거나 지워짐 (예전 개발자 센터 키 포함), 403: 그 애플리케이션에 이 검색 API 를 고르지 않음
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new NaverAuthException((int)response.StatusCode, detail);
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}{(detail.Length > 0 ? ": " + detail : "")}");
        }
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
    }

    /// <summary>
    /// 오류 응답의 설명 (없으면 빈 문자열). 검색 오류는 {"errorCode","errorMessage"},
    /// 네이버 클라우드 게이트웨이 오류(키 문제 등)는 {"error":{"errorCode","message","details"}} 모양
    /// </summary>
    private static string ErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return "";
            if (root.TryGetProperty("errorMessage", out var m) && m.ValueKind == JsonValueKind.String) return m.GetString() ?? "";
            if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object)
            {
                var parts = new[] { "errorCode", "message", "details" }
                    .Select(k => e.TryGetProperty(k, out var v) ? v.ToString() : "")
                    .Where(s => s.Length > 0);
                return string.Join(" / ", parts);
            }
            return "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static IEnumerable<JsonElement> Items(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().ToList()
            : Enumerable.Empty<JsonElement>();

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>&lt;b&gt; 같은 HTML 태그를 지우고 &amp;quot; 같은 표기를 글자로</summary>
    private static string Clean(string s) => System.Net.WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]*>", ""));
}

/// <summary>
/// 네이버 API 키 찾기 (Java 와 같은 순서):
/// 1) 앱에 들어 있는 naver.properties (빌드할 때 넣음, git 에는 없음)
/// 2) 사용자가 예전에 입력해 저장한 값
/// 3) 둘 다 없으면 호출한 쪽에서 물어봄
/// </summary>
public static class NaverKeys
{
    public static (string Id, string Secret)? FromBundledFile()
    {
        using var stream = typeof(NaverKeys).Assembly.GetManifestResourceStream("Atbash.App.naver.properties");
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        var props = new Dictionary<string, string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] is '#' or '!') continue;
            int sep = line.IndexOf('=');
            if (sep > 0) props[line[..sep].Trim()] = line[(sep + 1)..].Trim();
        }
        return props.TryGetValue("naver.clientId", out var id) && props.TryGetValue("naver.clientSecret", out var secret)
               && id.Length > 0 && secret.Length > 0
            ? (id, secret)
            : null;
    }
}
