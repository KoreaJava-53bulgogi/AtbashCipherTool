using System.Text.Json;
using Atbash.App.Services;
using Atbash.App.ViewModels;
using Atbash.Core.Settings;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace Atbash.App.Tests;

/// <summary>Java 버전 설정 옮기기 (5단계)</summary>
public class JavaMigrationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("atbash-migrate-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    /// <summary>tools/golden/JavaPrefsProbe.java 가 저장한 값 (레지스트리 원본은 TestData/java-prefs-registry.json)</summary>
    private static readonly Dictionary<string, string> JavaSamples = new()
    {
        ["theme"] = "LIGHT",
        ["language"] = "KOREAN",
        ["cipherMode"] = "18",
        ["windowWidth"] = "1550",
        ["saveSettingsEnabled"] = "true",
        ["custom.themes.list"] = "ttt,hight 2,갈색 테마,Mixed한글",
        ["custom.theme.갈색 테마.main.background"] = "-6724096",
        ["naverClientId"] = "KqXpD39zL6gYUTWviEJc",
        ["slash/and\\back"] = "a/b\\c",
        ["lastUpdateCheck"] = "1762992000000",
        ["emoji😀"] = "값 😀",
        ["tab\there"] = "line\nbreak",
    };

    [Fact]
    public void Decodes_names_and_values_exactly_like_java_stored_them()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "java-prefs-registry.json");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var decoded = doc.RootElement.EnumerateArray().ToDictionary(
            e => JavaPrefsCodec.DecodeName(e.GetProperty("name").GetString()!),
            e => JavaPrefsCodec.DecodeValue(e.GetProperty("value").GetString()!));
        Assert.Equal(JavaSamples.OrderBy(p => p.Key), decoded.OrderBy(p => p.Key));
    }

    /// <summary>실제 사용자의 Java 설정과 비슷한 모양</summary>
    private static readonly Dictionary<string, string> RealisticPrefs = new()
    {
        ["theme"] = "brown",
        ["language"] = "ENGLISH",
        ["cipherMode"] = "7",
        ["caesarShift"] = "13",
        ["scytaleDiameter"] = "4",
        ["hangulShift"] = "-2",
        ["livePreview"] = "true",
        ["saveSettingsEnabled"] = "true",
        ["windowWidth"] = "1550",
        ["windowHeight"] = "822",
        ["custom.themes.list"] = "brown,빈 테마",
        ["custom.theme.brown.main.background"] = "-6724096",
        ["custom.theme.brown.textarea.background"] = "-1",
        ["custom.theme.brown.text.foreground"] = "-13487566",
        ["custom.theme.brown.button.primary"] = "-16745473",
        ["custom.theme.brown.button.secondary"] = "-9669251",
        ["naverClientId"] = "my-id",
        ["naverClientSecret"] = "my-secret",
        ["skippedVersion"] = "8.1.0",
        ["lastUpdateCheck"] = "1762992000000",
        ["custom.main.background"] = "-52327", // 아주 옛날 버전의 값: 무시
    };

    [Fact]
    public void Imports_every_java_setting()
    {
        var s = JavaSettingsImporter.Import(RealisticPrefs);
        Assert.Equal("ENGLISH", s.Language);
        Assert.Equal("brown", s.Theme);
        Assert.Equal(7, s.CipherMode);
        Assert.Equal(13, s.CaesarShift);
        Assert.Equal(4, s.ScytaleDiameter);
        Assert.Equal(-2, s.HangulShift);
        Assert.True(s.LivePreview);
        Assert.Equal((1550, 822), (s.WindowWidth, s.WindowHeight));
        Assert.Equal(("my-id", "my-secret"), (s.NaverClientId, s.NaverClientSecret));
        Assert.Equal("8.1.0", s.SkippedVersion);
        Assert.Equal(1762992000000, s.LastUpdateCheck);
        Assert.True(s.MigratedFromJava);

        Assert.Equal(new[] { "brown", "빈 테마" }, s.CustomThemes.Select(t => t.Name));
        Assert.Equal(Color.FromRgb(153, 102, 0), s.CustomThemes[0].Colors.MainBackground);
        // 색이 저장되지 않은 테마는 Java 와 같이 라이트 테마 색
        Assert.Equal(ThemeColors.FromTheme(AppTheme.Light), s.CustomThemes[1].Colors);
    }

    [Fact]
    public void Bad_or_unknown_values_fall_back_to_defaults()
    {
        var s = JavaSettingsImporter.Import(new Dictionary<string, string>
        {
            ["theme"] = "deleted-theme",
            ["language"] = "KLINGON",
            ["cipherMode"] = "99",
            ["scytaleDiameter"] = "0",
            ["windowWidth"] = "abc",
        });
        Assert.Equal("LIGHT", s.Theme);
        Assert.Equal("KOREAN", s.Language);
        Assert.Equal(19, s.CipherMode);
        Assert.Equal(1, s.ScytaleDiameter);
        Assert.Equal(1100, s.WindowWidth);
    }

    private sealed class FakeSource : IJavaPrefsSource
    {
        public int Reads { get; private set; }
        public IReadOnlyDictionary<string, string>? Data { get; init; }

        public IReadOnlyDictionary<string, string>? ReadAll()
        {
            Reads++;
            return Data;
        }
    }

    [AvaloniaFact]
    public void Migrates_once_on_first_run_and_the_app_uses_it()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var source = new FakeSource { Data = RealisticPrefs };

        Assert.True(JavaSettingsImporter.MigrateIfNeeded(store, source));
        Assert.False(JavaSettingsImporter.MigrateIfNeeded(store, source)); // 두 번째부터는 C# 설정을 그대로 씀
        Assert.Equal(1, source.Reads);

        var vm = new MainViewModel(store, Application.Current);
        Assert.True(vm.IsEnglish);
        Assert.Equal("brown", vm.Theme.Name);
        Assert.Equal(7, vm.SelectedModeIndex);
        Assert.Equal(2, vm.CustomThemeItems.Count);
        Assert.Equal(1550, vm.InitialWindowWidth);
    }

    [Fact]
    public void Nothing_happens_without_java_settings()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        Assert.False(JavaSettingsImporter.MigrateIfNeeded(store, new FakeSource { Data = null }));
        Assert.False(File.Exists(store.Path));
    }

    /// <summary>이 PC 의 실제 Java 설정을 읽기만 해 봄 (값은 출력하지 않음). Java 버전을 쓴 적이 없으면 건너뜀</summary>
    [Fact]
    public void Reads_the_real_java_settings_on_this_pc()
    {
        var prefs = new RegistryJavaPrefs().ReadAll();
        if (prefs == null) Assert.Skip("Java 버전 설정이 없는 PC");
        Assert.Contains(prefs!["language"], new[] { "KOREAN", "ENGLISH", "CHINESE", "JAPANESE" });
        var imported = JavaSettingsImporter.Import(prefs);
        Assert.True(imported.MigratedFromJava);
    }
}
