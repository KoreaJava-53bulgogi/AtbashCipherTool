using System;
using System.IO;
using System.Text.Json;

namespace Atbash.App.Services;

/// <summary>
/// 저장되는 설정. 이름과 기본값은 Java 버전과 같음.
/// (Java 버전 설정을 옮겨오는 기능은 5단계에서 추가)
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
}

/// <summary>설정을 JSON 파일로 읽고 씀. 기본 위치: %APPDATA%\AtbashCipherTool\settings.json</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public SettingsStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtbashCipherTool", "settings.json");
    }

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
