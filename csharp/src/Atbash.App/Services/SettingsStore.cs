using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Atbash.App.Services;

/// <summary>
/// 저장되는 설정. 이름과 기본값은 Java 버전과 같음.
/// 처음 실행할 때 Java 버전 설정이 있으면 JavaSettingsImporter 가 옮겨 옴.
/// </summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "KOREAN";
    public string Theme { get; set; } = "LIGHT";
    public int CipherMode { get; set; }
    public int CaesarShift { get; set; } = 3;
    public int ScytaleDiameter { get; set; } = 5;
    public int HangulShift { get; set; } = 3;
    public bool LivePreview { get; set; }
    public bool SaveSettingsEnabled { get; set; } = true;
    public int WindowWidth { get; set; } = 1100;
    public int WindowHeight { get; set; } = 500;

    /// <summary>사용자 테마들 (Theme 에 이름이 들어 있으면 그 테마를 씀)</summary>
    public List<CustomThemeData> CustomThemes { get; set; } = new();

    /// <summary>"이 버전 건너뛰기" 를 고른 버전</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>마지막으로 자동 업데이트 확인을 한 시각 (1970-01-01 부터의 밀리초, Java 와 같음)</summary>
    public long LastUpdateCheck { get; set; }

    /// <summary>Java 버전 설정을 옮겨 왔는지 (기록용)</summary>
    public bool MigratedFromJava { get; set; }
}

/// <summary>
/// 설정을 JSON 파일로 읽고 씀. 기본 위치 (운영체제마다 관례가 다름):
///   Windows: %APPDATA%\AtbashCipherTool\settings.json   (9.0.0 과 같은 곳 — 바꾸면 기존 설정이 사라진 것처럼 보임)
///   macOS:   ~/Library/Application Support/AtbashCipherTool/settings.json
///   Linux:   $XDG_CONFIG_HOME/AtbashCipherTool/settings.json  (없으면 ~/.config/...)
/// </summary>
public sealed class SettingsStore
{
    private const string AppFolder = "AtbashCipherTool";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public SettingsStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(DefaultDirectory(), "settings.json");
    }

    /// <summary>지금 운영체제의 설정 폴더</summary>
    public static string DefaultDirectory() => DirectoryFor(
        OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux",
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));

    /// <summary>운영체제별 설정 폴더 (테스트에서 각 운영체제를 흉내 내려고 값을 받음)</summary>
    public static string DirectoryFor(string os, string home, string appData, string? xdgConfigHome) => os switch
    {
        "win" => System.IO.Path.Combine(appData, AppFolder),
        "osx" => System.IO.Path.Combine(home, "Library", "Application Support", AppFolder),
        // XDG 규칙: 절대 경로가 아니면 무시
        _ => System.IO.Path.Combine(
            !string.IsNullOrEmpty(xdgConfigHome) && System.IO.Path.IsPathRooted(xdgConfigHome) ? xdgConfigHome : System.IO.Path.Combine(home, ".config"),
            AppFolder),
    };

    public string Path { get; }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // 손상된 설정 파일은 무시하고 기본값으로 시작
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 저장 실패는 앱 사용을 막지 않음
        }
    }
}
