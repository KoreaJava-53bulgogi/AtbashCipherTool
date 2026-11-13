using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Atbash.App.Services;

/// <summary>화면 언어. 이름(KOREAN 등)은 Java 버전의 저장값과 같음</summary>
public enum AppLanguage
{
    Korean,
    English,
    Chinese,
    Japanese,
}

/// <summary>
/// 언어 파일(ko/en/ja/zh.json)의 문구. Java 버전과 같은 파일을 씀 (저장소 맨 위의 json 을 그대로 포함).
/// 번역이 빠진 문구는 영어로, 영어에도 없으면 !KEY! 로 보여줌.
/// 언어를 바꾸면 새 Localizer 를 만들어 교체함 → 화면의 모든 문구가 다시 그려짐.
/// </summary>
public sealed class Localizer
{
    private readonly Dictionary<string, string> _strings;

    private Localizer(AppLanguage language, Dictionary<string, string> strings)
    {
        Language = language;
        _strings = strings;
    }

    public AppLanguage Language { get; }

    public string this[string key] => _strings.TryGetValue(key, out var value) ? value : "!" + key + "!";

    public bool Has(string key) => _strings.ContainsKey(key);

    public static Localizer Load(AppLanguage language)
    {
        var strings = new Dictionary<string, string>();
        if (language != AppLanguage.English) Merge(strings, "en");
        Merge(strings, FileCode(language));
        return new Localizer(language, strings);
    }

    public static string FileCode(AppLanguage language) => language switch
    {
        AppLanguage.Korean => "ko",
        AppLanguage.Chinese => "zh",
        AppLanguage.Japanese => "ja",
        _ => "en",
    };

    /// <summary>저장된 값("KOREAN" 등, Java 와 같음)을 언어로. 모르는 값이면 한국어</summary>
    public static AppLanguage Parse(string? saved) => saved?.ToUpperInvariant() switch
    {
        "ENGLISH" => AppLanguage.English,
        "CHINESE" => AppLanguage.Chinese,
        "JAPANESE" => AppLanguage.Japanese,
        _ => AppLanguage.Korean,
    };

    public static string ToSaved(AppLanguage language) => language.ToString().ToUpperInvariant();

    private static void Merge(Dictionary<string, string> target, string code)
    {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream($"Atbash.App.Lang.{code}.json");
        if (stream == null) return;
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        if (values == null) return;
        foreach (var (key, value) in values) target[key] = value;
    }
}
