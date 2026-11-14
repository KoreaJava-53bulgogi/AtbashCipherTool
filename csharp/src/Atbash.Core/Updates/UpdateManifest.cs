using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Atbash.Core.Updates;

/// <summary>version.json 에 적는 플랫폼 하나의 최신 버전 정보</summary>
public sealed record PlatformEntry(string Version, string? Tag, string? Url, string? Installer, string? Sha256, string? Notes);

/// <summary>
/// version.json 전체. 모양:
/// {
///   "version", "url", "installer", "sha256", "notes",     ← Windows x64 (예전 앱 8.x / 9.0.0 이 읽는 부분, 항상 맨 앞)
///   "platforms": {
///     "win-x64":    { "version", "tag", "url", "installer", "sha256", "notes" },
///     "osx-arm64":  { ... },
///     "linux-x64":  { ... }
///   }
/// }
/// 예전 앱은 파일에서 "맨 처음 나오는" "version" 같은 값을 읽으므로, 맨 위 항목은 언제나 Windows x64 이고
/// 다른 플랫폼 항목은 반드시 platforms 안에만 둠. 새 앱은 자기 플랫폼의 platforms 항목을 읽음.
/// </summary>
public sealed class UpdateManifest
{
    private static readonly string WindowsId = PlatformInfo.WindowsX64.Id;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        // 한글을 \uXXXX 로 바꾸지 않음 (예전 앱의 읽기 방식은 \u 를 풀지 못함)
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly UpdateManifest Empty = new(new Dictionary<string, PlatformEntry>(), null);

    private UpdateManifest(IReadOnlyDictionary<string, PlatformEntry> platforms, PlatformEntry? topLevel)
    {
        Platforms = platforms;
        TopLevel = topLevel;
    }

    /// <summary>맨 위 항목 (예전 앱이 읽는 Windows x64 정보)</summary>
    public PlatformEntry? TopLevel { get; }

    public IReadOnlyDictionary<string, PlatformEntry> Platforms { get; }

    // ---- 읽기 -------------------------------------------------------------------

    /// <exception cref="FormatException">읽을 수 있는 버전 정보가 하나도 없음</exception>
    public static UpdateManifest Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("version.json is not a JSON object");

            var platforms = new Dictionary<string, PlatformEntry>();
            if (root.TryGetProperty("platforms", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in p.EnumerateObject())
                    if (ReadEntry(prop.Value) is { } entry) platforms[prop.Name] = entry;
            }
            var top = ReadEntry(root);
            if (top == null && platforms.Count == 0) throw new FormatException("version.json has no version information");
            return new UpdateManifest(platforms, top);
        }
        catch (JsonException)
        {
            // 따옴표가 빠지는 등 JSON 으로는 읽을 수 없으면 예전 앱과 같은 방식(정규식)으로 맨 위 값만 읽음
            var info = UpdateInfo.Parse(json);
            return new UpdateManifest(new Dictionary<string, PlatformEntry>(),
                new PlatformEntry(info.Version, null, info.DownloadUrl, info.InstallerUrl, info.Sha256, info.Notes));
        }
    }

    private static PlatformEntry? ReadEntry(JsonElement e)
    {
        var version = Str(e, "version");
        if (string.IsNullOrWhiteSpace(version)) return null;
        return new PlatformEntry(version.Trim(), Str(e, "tag"), Str(e, "url"), Str(e, "installer"), Str(e, "sha256"), Str(e, "notes"));
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>그 플랫폼의 항목 (Windows x64 는 맨 위 항목도 봄)</summary>
    public PlatformEntry? EntryFor(PlatformInfo platform)
    {
        if (Platforms.TryGetValue(platform.Id, out var e)) return e;
        return platform.Id == WindowsId ? TopLevel : null;
    }

    /// <summary>그 플랫폼 앱이 볼 업데이트 정보. 이 플랫폼의 항목이 없으면 null (알릴 새 버전 없음)</summary>
    public UpdateInfo? Select(PlatformInfo platform) => EntryFor(platform) is { } e ? ToInfo(e) : null;

    private static UpdateInfo ToInfo(PlatformEntry e) => new(
        e.Version.Trim(),
        Blank(e.Url) ?? (Blank(e.Tag) is { } tag ? ReleaseTags.TagPage(tag) : UpdateInfo.DefaultDownloadUrl),
        Blank(e.Installer),
        Blank(e.Sha256)?.ToLowerInvariant(),
        e.Notes ?? "",
        Blank(e.Tag));

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ---- 바꾸기 -----------------------------------------------------------------

    /// <summary>
    /// 한 플랫폼의 항목을 새로 넣거나 바꾼 새 version.json 을 돌려줌 (다른 플랫폼 항목은 그대로).
    /// 예전 형식(맨 위 항목만 있음)이면 Windows x64 항목으로 옮겨 적음.
    /// </summary>
    /// <exception cref="InvalidOperationException">Windows 항목이 없는데 다른 플랫폼을 넣으려 함 (예전 앱이 다른 플랫폼 정보를 읽게 됨)</exception>
    public UpdateManifest With(PlatformInfo platform, PlatformEntry entry)
    {
        var map = new Dictionary<string, PlatformEntry>(Platforms);
        if (!map.ContainsKey(WindowsId) && TopLevel != null) map[WindowsId] = Normalize(TopLevel);
        if (platform.Id != WindowsId && !map.ContainsKey(WindowsId))
            throw new InvalidOperationException(
                "The Windows (win-x64) entry must exist first: already released Windows apps read the top-level fields of version.json.");
        map[platform.Id] = Normalize(entry);
        return new UpdateManifest(map, map.GetValueOrDefault(WindowsId));
    }

    /// <summary>태그가 없으면 설치 파일 주소에서 찾고, 다운로드 페이지가 "최신 릴리스"로 되어 있으면 그 태그의 페이지로 바꿈</summary>
    private static PlatformEntry Normalize(PlatformEntry e)
    {
        var tag = Blank(e.Tag) ?? TagFromInstallerUrl(e.Installer) ?? (ReleaseTags.IsValidVersion(e.Version) ? "v" + e.Version : null);
        var url = Blank(e.Url);
        if (tag != null && (url == null || url == UpdateInfo.DefaultDownloadUrl)) url = ReleaseTags.TagPage(tag);
        return e with { Tag = tag, Url = url, Sha256 = Blank(e.Sha256)?.ToLowerInvariant() };
    }

    private static string? TagFromInstallerUrl(string? installer)
    {
        if (installer == null) return null;
        var m = Regex.Match(installer, "/releases/download/(?<tag>[^/]+)/");
        return m.Success ? Uri.UnescapeDataString(m.Groups["tag"].Value) : null;
    }

    // ---- 쓰기 -------------------------------------------------------------------

    /// <summary>version.json 글. 맨 위에 Windows x64 항목, 그 뒤에 platforms</summary>
    public string ToJson()
    {
        var windows = Platforms.TryGetValue(WindowsId, out var w) ? w : TopLevel;
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, WriterOptions))
        {
            json.WriteStartObject();
            if (windows != null) WriteFields(json, windows, includeTag: false);
            if (Platforms.Count > 0)
            {
                json.WriteStartObject("platforms");
                foreach (var id in OrderedIds())
                {
                    json.WriteStartObject(id);
                    WriteFields(json, Platforms[id], includeTag: true);
                    json.WriteEndObject();
                }
                json.WriteEndObject();
            }
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private IEnumerable<string> OrderedIds()
    {
        var known = PlatformInfo.Released.Select(p => p.Id).Where(Platforms.ContainsKey);
        return known.Concat(Platforms.Keys.Except(known).Order(StringComparer.Ordinal));
    }

    private static void WriteFields(Utf8JsonWriter json, PlatformEntry e, bool includeTag)
    {
        json.WriteString("version", e.Version);
        if (includeTag && Blank(e.Tag) is { } tag) json.WriteString("tag", tag);
        if (Blank(e.Url) is { } url) json.WriteString("url", url);
        if (Blank(e.Installer) is { } installer) json.WriteString("installer", installer);
        if (Blank(e.Sha256) is { } sha) json.WriteString("sha256", sha.ToLowerInvariant());
        if (e.Notes != null) json.WriteString("notes", e.Notes);
    }
}
