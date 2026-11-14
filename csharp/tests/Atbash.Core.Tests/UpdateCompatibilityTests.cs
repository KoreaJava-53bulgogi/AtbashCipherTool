using System.Text;
using Atbash.Core.Updates;
using Atbash.ReleaseTool;

namespace Atbash.Core.Tests;

/// <summary>
/// 저장소의 version.json 이 이미 배포된 앱(Java 8.1.0, C# 9.0.0)에게 계속 올바르게 읽히는지,
/// 그리고 배포 도구(ReleaseTool)가 그 규칙을 지키는지 확인.
/// 이 테스트가 실패하면 version.json 을 올리기 전에 고쳐야 함 (그대로 올리면 사용자 업데이트가 깨질 수 있음).
/// </summary>
public class UpdateCompatibilityTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("atbash-compat-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static string? RepoVersionJson()
    {
        var root = JavaInterop.FindRepoRoot();
        var path = root == null ? null : Path.Combine(root, "version.json");
        return path != null && File.Exists(path) ? path : null;
    }

    // ---- 저장소의 version.json ------------------------------------------------------------

    [Fact]
    public void Committed_version_json_is_consistent_and_readable_the_old_way()
    {
        var path = RepoVersionJson();
        if (path == null) Assert.Skip("repo version.json not found");
        var text = File.ReadAllText(path, Encoding.UTF8);

        var manifest = UpdateManifest.Parse(text);
        var windows = manifest.Select(PlatformInfo.WindowsX64);
        Assert.NotNull(windows);
        Assert.True(windows.CanAutoInstall);

        // 예전 앱이 읽는 값 = Windows 항목
        var old = UpdateInfo.Parse(text);
        Assert.Equal(windows.Version, old.Version);
        Assert.Equal(windows.InstallerUrl, old.InstallerUrl);
        Assert.Equal(windows.Sha256, old.Sha256);
        Assert.Equal(windows.DownloadUrl, old.DownloadUrl);
        // platforms 가 있으면 그 안의 Windows 항목이 맨 위 항목과 같아야 함 (맨 위 항목은 tag 칸이 없으므로 tag 는 뺌)
        if (manifest.Platforms.TryGetValue("win-x64", out var windowsEntry))
            Assert.Equal(windowsEntry, manifest.TopLevel! with { Tag = windowsEntry.Tag });

        // 각 플랫폼 항목: 태그가 그 플랫폼·버전과 맞고, 주소가 그 태그를 가리킴
        foreach (var (id, entry) in manifest.Platforms)
        {
            Assert.NotNull(entry.Tag);
            Assert.True(ReleaseTags.TryParse(entry.Tag, out var platform, out var version), $"{id}: bad tag {entry.Tag}");
            Assert.Equal(id, platform.Id);
            Assert.Equal(entry.Version, version);
            Assert.Equal(ReleaseTags.TagPage(entry.Tag), entry.Url);
            Assert.Contains($"/releases/download/{entry.Tag}/", entry.Installer);
            Assert.Matches("^[0-9a-f]{64}$", entry.Sha256);
        }
    }

    [Fact]
    public void Java_8_1_0_reads_the_committed_version_json_as_the_windows_entry()
    {
        var path = RepoVersionJson();
        if (path == null) Assert.Skip("repo version.json not found");
        var java = JavaInterop.RequireOrSkip("VersionJsonVerify");

        var windows = UpdateManifest.Parse(File.ReadAllText(path, Encoding.UTF8)).Select(PlatformInfo.WindowsX64)!;
        Assert.Equal($"{windows.Version}|{windows.DownloadUrl}|{windows.InstallerUrl}|{windows.Sha256}", java.Run(path));
    }

    [Fact]
    public void Java_8_1_0_ignores_mac_and_linux_entries_even_when_they_are_newer()
    {
        var java = JavaInterop.RequireOrSkip("VersionJsonVerify");
        var windows = new PlatformEntry("9.0.0", "v9.0.0", null, "https://x/releases/download/v9.0.0/Setup_9.0.0.exe", new string('b', 64), "윈도우");
        var mac = new PlatformEntry("99.0.0", "a_99.0.0", null, "https://x/releases/download/a_99.0.0/App.dmg", new string('c', 64), "맥");
        var linux = new PlatformEntry("98.0.0", "l_98.0.0", null, "https://x/releases/download/l_98.0.0/App.AppImage", new string('d', 64), "리눅스");
        var json = UpdateManifest.Empty.With(PlatformInfo.WindowsX64, windows)
            .With(PlatformInfo.MacArm64, mac).With(PlatformInfo.LinuxX64, linux).ToJson();
        var file = Path.Combine(_dir, "version.json");
        File.WriteAllText(file, json, new UTF8Encoding(false));

        var parts = java.Run(file).Split('|');
        Assert.Equal("9.0.0", parts[0]);
        Assert.EndsWith("/v9.0.0/Setup_9.0.0.exe", parts[2]);
        Assert.Equal(new string('b', 64), parts[3]);
    }

    // ---- 배포 도구 ----------------------------------------------------------------------

    private (int Code, string Out, string Err) Cli(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        return (ReleaseCli.Run(args, o, e), o.ToString(), e.ToString());
    }

    [Theory]
    [InlineData("win-x64", "9.1.0", "9.1.0")]
    [InlineData("osx-arm64", "9.1.0", "a_9.1.0")]
    [InlineData("linux-x64", "9.1.0-beta", "l_9.1.0-beta")]
    public void Tool_prints_the_tag_name(string platform, string version, string tag)
    {
        var (code, output, _) = Cli("tag", "--platform", platform, "--version", version);
        Assert.Equal(0, code);
        Assert.Equal(tag, output.Trim());
    }

    [Fact]
    public void Tool_merges_a_release_without_touching_the_other_platforms()
    {
        var manifest = Path.Combine(_dir, "version.json");
        File.WriteAllText(manifest, """
            {
              "version": "9.0.0",
              "url": "https://github.com/KoreaJava-53bulgogi/AtbashCipherTool/releases/latest",
              "installer": "https://github.com/KoreaJava-53bulgogi/AtbashCipherTool/releases/download/v9.0.0/Setup_9.0.0.exe",
              "sha256": "52329f5387dae3153f7e01e22764fad02741b6c7b5e3a21c19357c4155b5e258",
              "notes": "윈도우 안내"
            }
            """, new UTF8Encoding(false));
        var dmg = Path.Combine(_dir, "AtbashCipherTool-9.1.0-osx-arm64.dmg");
        File.WriteAllBytes(dmg, new byte[] { 1, 2, 3, 4, 5 });

        var (code, output, error) = Cli("merge", "--manifest", manifest, "--platform", "osx-arm64", "--version", "9.1.0",
            "--file", dmg, "--notes", "맥 첫 버전");
        Assert.True(code == 0, error);
        Assert.Contains("a_9.1.0", output);
        Assert.Contains("do NOT check 'Set as the latest release'", output); // Mac 릴리스는 "최신 릴리스"로 지정하지 않음

        var parsed = UpdateManifest.Parse(File.ReadAllText(manifest, Encoding.UTF8));
        var mac = parsed.Select(PlatformInfo.MacArm64)!;
        Assert.Equal("9.1.0", mac.Version);
        Assert.Equal("a_9.1.0", mac.Tag);
        Assert.Equal("맥 첫 버전", mac.Notes);
        Assert.Equal(ReleaseTags.AssetUrl("a_9.1.0", "AtbashCipherTool-9.1.0-osx-arm64.dmg"), mac.InstallerUrl);
        Assert.Equal(UpdateDownloader.Sha256(dmg), mac.Sha256);

        // Windows 항목(과 맨 위 항목)은 그대로, 태그는 이미 만들어진 v9.0.0 그대로
        var windows = parsed.Select(PlatformInfo.WindowsX64)!;
        Assert.Equal("9.0.0", windows.Version);
        Assert.Equal("v9.0.0", windows.Tag);
        Assert.Equal("윈도우 안내", windows.Notes);
        Assert.Equal("9.0.0", UpdateInfo.Parse(File.ReadAllText(manifest, Encoding.UTF8)).Version);

        // 같은 플랫폼을 다시 합치면 변경 내용을 유지함 (--notes 를 주지 않으면)
        var (code2, _, error2) = Cli("merge", "--manifest", manifest, "--platform", "osx-arm64", "--version", "9.1.1", "--file", dmg);
        Assert.True(code2 == 0, error2);
        var mac2 = UpdateManifest.Parse(File.ReadAllText(manifest, Encoding.UTF8)).Select(PlatformInfo.MacArm64)!;
        Assert.Equal(("9.1.1", "a_9.1.1", "맥 첫 버전"), (mac2.Version, mac2.Tag, mac2.Notes));
    }

    [Fact]
    public void Tool_keeps_the_existing_tag_when_the_same_version_is_merged_again()
    {
        var manifest = Path.Combine(_dir, "version.json");
        var exe = Path.Combine(_dir, "Setup_9.0.0.exe");
        File.WriteAllBytes(exe, new byte[] { 9, 0, 0 });
        File.WriteAllText(manifest, UpdateManifest.Empty.With(PlatformInfo.WindowsX64, new PlatformEntry(
            "9.0.0", "v9.0.0", ReleaseTags.TagPage("v9.0.0"), ReleaseTags.AssetUrl("v9.0.0", "Setup_9.0.0.exe"),
            UpdateDownloader.Sha256(exe), "안내")).ToJson(), new UTF8Encoding(false));
        var before = File.ReadAllText(manifest, Encoding.UTF8);

        // 같은 버전을 다시 실행해도 태그가 9.0.0 으로 바뀌지 않고, 파일도 그대로
        var (code, output, error) = Cli("merge", "--manifest", manifest, "--platform", "win-x64", "--version", "9.0.0", "--file", exe);
        Assert.True(code == 0, error);
        Assert.Contains("tag      : v9.0.0", output);
        Assert.Equal(before, File.ReadAllText(manifest, Encoding.UTF8));

        // 새 버전은 새 규칙(v 없이)의 태그
        var (code2, output2, _) = Cli("merge", "--manifest", manifest, "--platform", "win-x64", "--version", "9.1.0", "--file", exe);
        Assert.Equal(0, code2);
        Assert.Contains("tag      : 9.1.0", output2);
    }

    [Fact]
    public void Tool_refuses_mismatched_tags_unknown_platforms_and_bad_input()
    {
        var manifest = Path.Combine(_dir, "version.json");
        var file = Path.Combine(_dir, "Setup.exe");
        File.WriteAllBytes(file, new byte[] { 1 });

        // 맥 태그를 Windows 항목에 쓰려는 실수
        var wrongTag = Cli("merge", "--manifest", manifest, "--platform", "win-x64", "--version", "9.1.0", "--file", file, "--tag", "a_9.1.0");
        Assert.Equal(1, wrongTag.Code);
        Assert.Contains("does not match", wrongTag.Err);

        Assert.Equal(1, Cli("merge", "--manifest", manifest, "--platform", "win-arm64", "--version", "9.1.0", "--file", file).Code);
        Assert.Equal(1, Cli("merge", "--manifest", manifest, "--platform", "win-x64", "--version", "9.1", "--file", file).Code);
        Assert.Equal(1, Cli("merge", "--manifest", manifest, "--platform", "win-x64", "--version", "9.1.0", "--file", Path.Combine(_dir, "none.exe")).Code);
        Assert.Equal(1, Cli("bogus").Code);

        // Windows 항목이 없는 새 파일에 맥을 먼저 넣으려는 실수
        var macFirst = Cli("merge", "--manifest", manifest, "--platform", "osx-arm64", "--version", "9.1.0", "--file", file);
        Assert.Equal(1, macFirst.Code);
        Assert.Contains("Windows", macFirst.Err);
        Assert.False(File.Exists(manifest));

        // 정상: 이미 만들어진 v 태그를 그대로 쓸 수 있음
        var ok = Cli("merge", "--manifest", manifest, "--platform", "win-x64", "--version", "9.0.0", "--file", file, "--tag", "v9.0.0");
        Assert.Equal(0, ok.Code);
        Assert.Equal("v9.0.0", UpdateManifest.Parse(File.ReadAllText(manifest, Encoding.UTF8)).Select(PlatformInfo.WindowsX64)!.Tag);
    }
}
