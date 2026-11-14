using System.Text;
using Atbash.Core.Updates;

namespace Atbash.ReleaseTool;

/// <summary>
/// 사용법 (출력은 한글이 깨지지 않게 영어로만 씀):
///   ReleaseTool tag   --platform win-x64 --version 9.1.0
///       → 그 플랫폼·버전의 GitHub 태그 이름을 출력 (win-x64: 9.1.0 / osx-arm64: a_9.1.0 / linux-x64: l_9.1.0)
///   ReleaseTool merge --manifest version.json --platform osx-arm64 --version 9.1.0 --file Atbash.dmg
///                     [--tag a_9.1.0] [--notes "변경 내용"]
///       → 설치 파일의 SHA-256 을 계산해서 그 플랫폼의 항목만 version.json 에 넣거나 바꿈 (다른 플랫폼 항목은 그대로).
///         --notes 를 주지 않으면 그 플랫폼의 예전 변경 내용을 그대로 둠.
/// </summary>
public static class ReleaseCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                output.WriteLine("Usage: ReleaseTool tag|merge --platform <id> --version <x.y.z> [options]");
                output.WriteLine("Platforms: " + string.Join(", ", PlatformInfo.Released.Select(p => p.Id)));
                return args.Length == 0 ? 1 : 0;
            }
            var options = ParseOptions(args.Skip(1).ToArray());
            return args[0] switch
            {
                "tag" => Tag(options, output),
                "merge" => Merge(options, output),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'. Use tag or merge."),
            };
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine("ERROR: " + e.Message);
            return 1;
        }
    }

    private static int Tag(Dictionary<string, string> o, TextWriter output)
    {
        var (platform, version) = PlatformAndVersion(o);
        output.WriteLine(ReleaseTags.Make(platform, version));
        return 0;
    }

    private static int Merge(Dictionary<string, string> o, TextWriter output)
    {
        var (platform, version) = PlatformAndVersion(o);
        var manifestPath = Require(o, "manifest");
        var file = Require(o, "file");
        if (!File.Exists(file)) throw new FileNotFoundException("Installer file not found: " + file);

        var existing = File.Exists(manifestPath)
            ? UpdateManifest.Parse(File.ReadAllText(manifestPath, Encoding.UTF8))
            : UpdateManifest.Empty;
        var current = existing.EntryFor(platform);

        // 같은 버전을 다시 합치면 이미 만들어진 태그를 그대로 씀 (예: 예전 v9.0.0 을 새 규칙의 9.0.0 으로 바꾸면 주소가 깨짐)
        var tag = o.GetValueOrDefault("tag")
            ?? (current is { Tag: not null } && current.Version == version ? current.Tag : ReleaseTags.Make(platform, version));
        if (!ReleaseTags.TryParse(tag, out var tagPlatform, out var tagVersion) || tagPlatform.Id != platform.Id || tagVersion != version)
            throw new ArgumentException($"Tag '{tag}' does not match platform {platform.Id} and version {version}. Expected: {ReleaseTags.Make(platform, version)}");

        var notes = o.GetValueOrDefault("notes") ?? current?.Notes ?? "";

        var fileName = Path.GetFileName(file);
        var sha = UpdateDownloader.Sha256(file);
        var entry = new PlatformEntry(version, tag, ReleaseTags.TagPage(tag), ReleaseTags.AssetUrl(tag, fileName), sha, notes);
        var updated = existing.With(platform, entry);
        File.WriteAllText(manifestPath, updated.ToJson(), new UTF8Encoding(false));

        output.WriteLine($"version.json updated: {Path.GetFullPath(manifestPath)}");
        output.WriteLine($"  platform : {platform.Id} ({platform.DisplayName})");
        output.WriteLine($"  version  : {version}");
        output.WriteLine($"  tag      : {tag}   <- create the GitHub release with this tag");
        output.WriteLine($"  upload   : {fileName}   <- upload exactly this file (do not rebuild it)");
        output.WriteLine($"  sha256   : {sha}");
        output.WriteLine(platform.Id == PlatformInfo.WindowsX64.Id
            ? "  latest   : check 'Set as the latest release'"
            : "  latest   : do NOT check 'Set as the latest release' (only Windows releases are 'latest')");
        output.WriteLine("Push version.json only AFTER the file is uploaded to the release.");
        return 0;
    }

    private static (PlatformInfo Platform, string Version) PlatformAndVersion(Dictionary<string, string> o)
    {
        var id = Require(o, "platform");
        var platform = PlatformInfo.ById(id)
            ?? throw new ArgumentException($"Unknown platform '{id}'. Known: {string.Join(", ", PlatformInfo.Released.Select(p => p.Id))}");
        var version = Require(o, "version");
        if (!ReleaseTags.IsValidVersion(version))
            throw new ArgumentException($"Invalid version '{version}'. Use x.y.z or x.y.z-beta.");
        return (platform, version);
    }

    private static string Require(Dictionary<string, string> o, string name) =>
        o.TryGetValue(name, out var v) && v.Length > 0 ? v : throw new ArgumentException($"Missing --{name}");

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{args[i]}'");
            if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[i]}");
            result[args[i][2..]] = args[++i];
        }
        return result;
    }
}
