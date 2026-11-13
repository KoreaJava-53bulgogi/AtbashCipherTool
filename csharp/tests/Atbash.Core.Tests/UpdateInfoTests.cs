using Atbash.Core.Updates;

namespace Atbash.Core.Tests;

public class UpdateInfoTests
{
    [Theory]
    [InlineData("8.0.0", "7.1.0", 1)]
    [InlineData("8.0.10", "8.0.9", 1)]
    [InlineData("8.0", "8.0.0", 0)]
    [InlineData("7.1.0", "8.0.0", -1)]
    [InlineData("9.0.0", "8.1.0", 1)]
    [InlineData("9.0.0-beta", "9.0.0", 0)]
    public void Compares_versions_numerically(string a, string b, int expectedSign) =>
        Assert.Equal(expectedSign, Math.Sign(UpdateInfo.CompareVersions(a, b)));

    [Fact]
    public void Parses_the_published_version_json_format()
    {
        // 실제로 GitHub 에 올라간 8.1.0 version.json 과 같은 형식
        var info = UpdateInfo.Parse("""
            {
              "version": "8.1.0",
              "url": "https://github.com/KoreaJava-53bulgogi/AtbashCipherTool/releases/latest",
              "installer": "https://github.com/KoreaJava-53bulgogi/AtbashCipherTool/releases/download/v8.1.0/Setup_8.1.0.exe",
              "sha256": "2F33F49BF462DBDFEE6A82B0F8E12106EBD6F898790D017DC116215111F8D2FF",
              "notes": "자동 업데이트:\n줄바꿈 \"따옴표\""
            }
            """);
        Assert.Equal("8.1.0", info.Version);
        Assert.EndsWith("Setup_8.1.0.exe", info.InstallerUrl);
        Assert.Equal("2f33f49bf462dbdfee6a82b0f8e12106ebd6f898790d017dc116215111f8d2ff", info.Sha256);
        Assert.True(info.CanAutoInstall);
        Assert.Equal("자동 업데이트:\n줄바꿈 \"따옴표\"", info.Notes);
    }

    [Fact]
    public void Old_format_without_installer_cannot_auto_install()
    {
        var info = UpdateInfo.Parse("{\"version\":\"8.0.0\"}");
        Assert.Equal(UpdateInfo.DefaultDownloadUrl, info.DownloadUrl);
        Assert.False(info.CanAutoInstall);
        Assert.Throws<FormatException>(() => UpdateInfo.Parse("{}"));
    }
}
