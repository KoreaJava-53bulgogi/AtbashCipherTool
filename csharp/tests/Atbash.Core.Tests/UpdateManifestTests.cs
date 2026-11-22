using System.Runtime.InteropServices;
using Atbash.Core.Updates;

namespace Atbash.Core.Tests;

/// <summary>
/// 플랫폼별 태그 규칙(9.1.0 / a_9.1.0 / l_9.1.0)과 플랫폼별 version.json.
/// 가장 중요한 것은 "이미 배포된 앱(Java 8.x, C# 9.0.0)이 새 형식을 여전히 올바르게 읽는다"는 것.
/// </summary>
public class UpdateManifestTests
{
    private const string Sha = "52329f5387dae3153f7e01e22764fad02741b6c7b5e3a21c19357c4155b5e258";
    private const string MacSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string WindowsInstaller = "https://github.com/KoreaJava-53bulgogi/AtbashCipherTool/releases/download/v9.0.0/Setup_9.0.0.exe";

    /// <summary>지금 GitHub 에 올라가 있는 9.0.0 version.json 과 같은 예전 형식</summary>
    private const string OldFormatJson = """
        {
          "version": "9.0.0",
          "url": "https://github.com/KoreaJava-53bulgogi/AtbashCipherTool/releases/latest",
          "installer": "https://github.com/KoreaJava-53bulgogi/AtbashCipherTool/releases/download/v9.0.0/Setup_9.0.0.exe",
          "sha256": "52329f5387dae3153f7e01e22764fad02741b6c7b5e3a21c19357c4155b5e258",
          "notes": "C#으로 새로 만들었습니다."
        }
        """;

    private static PlatformEntry MacEntry(string version = "99.0.0", string tag = "a_99.0.0") => new(
        version, tag, ReleaseTags.TagPage(tag), ReleaseTags.AssetUrl(tag, "AtbashCipherTool.dmg"), MacSha, "맥 \"베타\"\n두 번째 줄");

    // ---- 태그 이름 ---------------------------------------------------------------

    [Theory]
    [InlineData("win-x64", "9.1.0", "9.1.0")]
    [InlineData("osx-arm64", "9.1.0", "a_9.1.0")]
    [InlineData("linux-x64", "9.1.0", "l_9.1.0")]
    [InlineData("win-x64-lite", "9.1.0", "lite_9.1.0")] // 경량판 ("l_" 와 헷갈리지 않아야 함)
    [InlineData("osx-arm64", "9.1.0-beta.2", "a_9.1.0-beta.2")]
    public void Tags_follow_the_prefix_rule(string platformId, string version, string expectedTag)
    {
        var platform = PlatformInfo.ById(platformId)!;
        Assert.Equal(expectedTag, ReleaseTags.Make(platform, version));

        // 태그에서 플랫폼과 버전을 다시 읽을 수 있음
        Assert.True(ReleaseTags.TryParse(expectedTag, out var parsedPlatform, out var parsedVersion));
        Assert.Equal(platform, parsedPlatform);
        Assert.Equal(version, parsedVersion);
    }

    [Theory]
    [InlineData("v9.0.0", "win-x64", "9.0.0")]   // 예전에 만든 태그 (v8.0.0, v8.1.0, v9.0.0)
    [InlineData("v8.1.0", "win-x64", "8.1.0")]
    [InlineData("l_1.2.3", "linux-x64", "1.2.3")]
    public void Old_windows_tags_with_v_are_still_understood(string tag, string platformId, string version)
    {
        Assert.True(ReleaseTags.TryParse(tag, out var platform, out var parsedVersion));
        Assert.Equal(platformId, platform.Id);
        Assert.Equal(version, parsedVersion);
    }

    [Theory]
    [InlineData("a_v9.1.0")]   // 접두사 뒤에는 v 를 붙이지 않음
    [InlineData("9.1")]
    [InlineData("x_9.1.0")]
    [InlineData("A_9.1.0")]
    [InlineData("a_")]
    [InlineData("latest")]
    [InlineData("")]
    public void Wrong_tags_are_rejected(string tag) => Assert.False(ReleaseTags.TryParse(tag, out _, out _));

    [Fact]
    public void Platforms_without_a_prefix_cannot_make_tags_and_versions_are_checked()
    {
        var windowsArm = PlatformInfo.For("win", Architecture.Arm64);
        Assert.Null(windowsArm.TagPrefix);
        Assert.Throws<InvalidOperationException>(() => ReleaseTags.Make(windowsArm, "9.1.0"));
        Assert.Throws<ArgumentException>(() => ReleaseTags.Make(PlatformInfo.MacArm64, "9.1"));
    }

    [Fact]
    public void Finds_the_current_platform()
    {
        Assert.Equal(PlatformInfo.WindowsX64, PlatformInfo.For("win", Architecture.X64));
        Assert.Equal(PlatformInfo.MacArm64, PlatformInfo.For("osx", Architecture.Arm64));
        Assert.Equal(PlatformInfo.LinuxX64, PlatformInfo.For("linux", Architecture.X64));
        Assert.Equal("osx-x64", PlatformInfo.For("osx", Architecture.X64).Id); // 배포하지 않는 조합
        Assert.True(PlatformInfo.WindowsX64.SupportsAutoInstall);
        Assert.False(PlatformInfo.MacArm64.SupportsAutoInstall);
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            Assert.Equal(PlatformInfo.WindowsX64, PlatformInfo.Current);
    }

    // ---- 예전 형식 --------------------------------------------------------------------

    [Fact]
    public void Old_format_is_served_to_windows_only()
    {
        var manifest = UpdateManifest.Parse(OldFormatJson);
        var windows = manifest.Select(PlatformInfo.WindowsX64)!;
        Assert.Equal("9.0.0", windows.Version);
        Assert.Equal(WindowsInstaller, windows.InstallerUrl);
        Assert.True(windows.CanAutoInstall);
        Assert.Null(manifest.Select(PlatformInfo.MacArm64));
        Assert.Null(manifest.Select(PlatformInfo.LinuxX64));
    }

    // ---- 플랫폼 추가 + 예전 앱 호환 ----------------------------------------------------------

    [Fact]
    public void Adding_platforms_keeps_the_old_apps_view_of_the_file()
    {
        var manifest = UpdateManifest.Parse(OldFormatJson)
            .With(PlatformInfo.MacArm64, MacEntry())
            .With(PlatformInfo.LinuxX64, MacEntry("98.0.0", "l_98.0.0"));
        var json = manifest.ToJson();

        // 예전 앱(C# 9.0.0 과 같은 읽기 방식: 파일에서 맨 처음 나오는 값)은 그대로 Windows 9.0.0 을 봄
        var old = UpdateInfo.Parse(json);
        Assert.Equal("9.0.0", old.Version);
        Assert.Equal(WindowsInstaller, old.InstallerUrl);
        Assert.Equal(Sha, old.Sha256);
        Assert.Equal("C#으로 새로 만들었습니다.", old.Notes);

        // 새 앱은 자기 플랫폼의 항목을 봄
        var parsed = UpdateManifest.Parse(json);
        Assert.Equal("99.0.0", parsed.Select(PlatformInfo.MacArm64)!.Version);
        Assert.Equal("98.0.0", parsed.Select(PlatformInfo.LinuxX64)!.Version);
        Assert.Equal("9.0.0", parsed.Select(PlatformInfo.WindowsX64)!.Version);
    }

    [Fact]
    public void Old_windows_entry_is_migrated_with_its_real_tag_and_tag_specific_page()
    {
        var parsed = UpdateManifest.Parse(
            UpdateManifest.Parse(OldFormatJson).With(PlatformInfo.MacArm64, MacEntry()).ToJson());
        var windows = parsed.Platforms["win-x64"];
        Assert.Equal("v9.0.0", windows.Tag); // 설치 파일 주소에서 찾음 (이미 만들어진 태그를 바꾸지 않음)
        // "releases/latest" 는 다른 플랫폼 릴리스가 "최신"이 되면 엉뚱한 페이지가 열리므로 그 태그의 페이지로 바꿈
        Assert.Equal(ReleaseTags.TagPage("v9.0.0"), windows.Url);
        Assert.Equal(WindowsInstaller, windows.Installer);
        Assert.Equal(Sha, windows.Sha256);
        // 맨 위 항목은 예전 앱을 위해 tag 칸 없이 쓰므로, tag 만 빼면 platforms 안의 Windows 항목과 같음
        Assert.Equal(windows, parsed.TopLevel! with { Tag = windows.Tag });
    }

    [Fact]
    public void Top_level_windows_fields_come_before_platforms()
    {
        var json = UpdateManifest.Parse(OldFormatJson).With(PlatformInfo.MacArm64, MacEntry()).ToJson();
        Assert.True(json.IndexOf("\"version\"", StringComparison.Ordinal) < json.IndexOf("\"platforms\"", StringComparison.Ordinal));
        Assert.StartsWith("{\n  \"version\": \"9.0.0\"", json);
    }

    [Fact]
    public void Lite_entry_is_separate_and_never_touches_what_the_full_apps_read()
    {
        var lite = new PlatformEntry("99.0.0", "lite_99.0.0", null, ReleaseTags.AssetUrl("lite_99.0.0", "Setup_Lite_99.0.0.exe"), MacSha, "경량판");
        var json = UpdateManifest.Parse(OldFormatJson).With(PlatformInfo.LiteWindowsX64, lite).ToJson();

        Assert.Equal("9.0.0", UpdateInfo.Parse(json).Version);                                       // 예전 앱
        Assert.Equal("9.0.0", UpdateManifest.Parse(json).Select(PlatformInfo.WindowsX64)!.Version);  // 정식 Windows 앱
        var read = UpdateManifest.Parse(json).Select(PlatformInfo.LiteWindowsX64)!;                    // 경량판
        Assert.Equal(("99.0.0", "lite_99.0.0"), (read.Version, read.Tag));
        Assert.False(PlatformInfo.LiteWindowsX64.SupportsAutoInstall);
        Assert.Contains("\"win-x64-lite\"", json);
    }

    [Fact]
    public void Other_platforms_cannot_be_added_before_windows()
    {
        // Windows 항목이 없으면 맨 위 항목이 비어서, 예전 앱이 다른 플랫폼의 값을 읽게 됨
        Assert.Throws<InvalidOperationException>(() => UpdateManifest.Empty.With(PlatformInfo.MacArm64, MacEntry()));
        var windows = new PlatformEntry("9.1.0", "9.1.0", null, "https://x/Setup.exe", Sha, "");
        var ok = UpdateManifest.Empty.With(PlatformInfo.WindowsX64, windows).With(PlatformInfo.MacArm64, MacEntry());
        Assert.Equal(2, ok.Platforms.Count);
    }

    [Fact]
    public void Updating_one_platform_leaves_the_others_untouched()
    {
        var before = UpdateManifest.Parse(OldFormatJson).With(PlatformInfo.MacArm64, MacEntry());
        var after = before.With(PlatformInfo.MacArm64, MacEntry("99.1.0", "a_99.1.0"));
        Assert.Equal(before.Platforms["win-x64"], after.Platforms["win-x64"]);
        Assert.Equal("99.1.0", after.Platforms["osx-arm64"].Version);
        Assert.Equal(before.ToJson().Split("\"platforms\"")[0],
            after.ToJson().Split("\"platforms\"")[0]); // Mac 릴리스는 Windows 사용자에게 보이는 맨 위 항목을 바꾸지 않음
    }

    [Fact]
    public void Windows_release_updates_both_top_level_and_its_entry()
    {
        var before = UpdateManifest.Parse(OldFormatJson).With(PlatformInfo.MacArm64, MacEntry());
        var windows = new PlatformEntry("9.1.0", "9.1.0", ReleaseTags.TagPage("9.1.0"),
            ReleaseTags.AssetUrl("9.1.0", "Setup_9.1.0.exe"), MacSha, "새 Windows 버전");
        var json = before.With(PlatformInfo.WindowsX64, windows).ToJson();

        var old = UpdateInfo.Parse(json); // 예전 앱이 보는 값
        Assert.Equal("9.1.0", old.Version);
        Assert.EndsWith("/download/9.1.0/Setup_9.1.0.exe", old.InstallerUrl);
        Assert.Equal("99.0.0", UpdateManifest.Parse(json).Select(PlatformInfo.MacArm64)!.Version);
    }

    // ---- 쓰기·읽기 --------------------------------------------------------------------

    [Fact]
    public void Roundtrip_keeps_text_and_korean_stays_readable_in_the_file()
    {
        var json = UpdateManifest.Parse(OldFormatJson).With(PlatformInfo.MacArm64, MacEntry()).ToJson();
        Assert.Contains("맥 \\\"베타\\\"\\n두 번째 줄", json);   // 한글은 그대로, 따옴표·줄바꿈만 이스케이프
        Assert.DoesNotContain("\\u", json);                   // 예전 앱은 \uXXXX 를 풀지 못함
        Assert.EndsWith("}\n", json);

        var mac = UpdateManifest.Parse(json).Select(PlatformInfo.MacArm64)!;
        Assert.Equal("맥 \"베타\"\n두 번째 줄", mac.Notes);
        Assert.Equal("a_99.0.0", mac.Tag);
        Assert.Equal(ReleaseTags.TagPage("a_99.0.0"), mac.DownloadUrl);
        Assert.True(mac.CanAutoInstall); // 데이터상으로는 자동 설치 가능 (실제로는 앱이 Windows 에서만 제안함)
    }

    [Fact]
    public void Platform_entry_wins_over_the_top_level_and_missing_url_falls_back_to_the_tag_page()
    {
        var manifest = UpdateManifest.Parse("""
            { "version": "9.0.0", "platforms": { "win-x64": { "version": "9.1.0", "tag": "9.1.0" } } }
            """);
        var info = manifest.Select(PlatformInfo.WindowsX64)!;
        Assert.Equal("9.1.0", info.Version);
        Assert.Equal(ReleaseTags.TagPage("9.1.0"), info.DownloadUrl);
        Assert.False(info.CanAutoInstall);
    }

    [Fact]
    public void Tolerates_trailing_commas_and_broken_json()
    {
        var withComma = UpdateManifest.Parse("""{ "version": "9.0.0", "notes": "x", }""");
        Assert.Equal("9.0.0", withComma.Select(PlatformInfo.WindowsX64)!.Version);

        // JSON 으로는 읽을 수 없어도 (따옴표 빠짐) 예전 앱과 같은 방식으로 맨 위 값을 읽음
        var broken = UpdateManifest.Parse("{ \"version\": \"9.0.0\", \"sha256\": \"" + Sha + "\", oops }");
        Assert.Equal("9.0.0", broken.Select(PlatformInfo.WindowsX64)!.Version);

        Assert.Throws<FormatException>(() => UpdateManifest.Parse("{}"));
        Assert.Throws<FormatException>(() => UpdateManifest.Parse("[]"));
    }

    // ---- 버전 비교 (베타) ---------------------------------------------------------------

    [Theory]
    [InlineData("9.1.0", "9.1.0-beta", 1)]          // 정식이 베타보다 새 버전 → 베타 사용자에게 정식을 알릴 수 있음
    [InlineData("9.1.0-beta", "9.1.0", -1)]
    [InlineData("9.1.0-beta.2", "9.1.0-beta", 1)]
    [InlineData("9.1.0-beta.10", "9.1.0-beta.9", 1)]
    [InlineData("9.1.0-rc", "9.1.0-beta", 1)]
    [InlineData("9.1.0-beta", "9.1.0-beta", 0)]
    [InlineData("9.1.0-beta", "9.0.9", 1)]          // 베타라도 숫자가 더 크면 더 새 버전
    [InlineData("9.0.0-beta", "9.1.0", -1)]
    [InlineData("9.10.0", "9.9.0", 1)]
    [InlineData("v9.0.0", "9.0.0", 0)]
    public void Compares_versions_including_betas(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(UpdateInfo.CompareVersions(a, b)));
}
