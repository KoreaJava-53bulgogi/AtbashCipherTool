using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Input;
using Atbash.App.Services;
using Atbash.Core;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atbash.App.ViewModels;

/// <summary>사용자 테마 메뉴의 한 항목</summary>
public sealed partial class CustomThemeItem : ObservableObject
{
    public CustomThemeItem(string name, ICommand select)
    {
        Name = name;
        Select = select;
    }

    public string Name { get; }

    public ICommand Select { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>
/// 부가 기능 (4단계): 변환 기록, QR 코드, 네이버 검색·이미지 검색, 사용자 테마, 정보 창.
/// 동작은 Java 버전과 같음.
/// </summary>
public partial class MainViewModel
{
    private const int HistoryLimit = 50;
    private const string EasterEggUrl = "https://www.youtube.com/watch?v=2yJgwwDcgV8";

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly List<HistoryEntry> _history = new();

    /// <summary>네이버 검색에 쓰는 HTTP (테스트에서 가짜로 바꿈)</summary>
    public HttpClient Http { get; set; } = SharedHttp;

    /// <summary>앱에 들어 있는 네이버 키 (테스트에서 바꿈)</summary>
    public Func<(string Id, string Secret)?> BundledNaverKeys { get; set; } = NaverKeys.FromBundledFile;

    public IReadOnlyList<HistoryEntry> History => _history;

    public ObservableCollection<CustomThemeItem> CustomThemeItems { get; } = new();

    // ---- 변환 기록 -----------------------------------------------------------------

    /// <summary>[변환] 결과를 기록 (최근 50개, 같은 변환을 연달아 누르면 한 번만)</summary>
    private void AddHistory(int modeIndex, int key, string input, string output)
    {
        if (input.Length == 0) return;
        if (_history.Count > 0)
        {
            var last = _history[0];
            if (last.ModeIndex == modeIndex && last.Input == input && last.Key == key
                && !CipherEngine.IsAes((CipherMode)modeIndex))
                return;
        }
        var name = modeIndex >= 0 && modeIndex < ModeNames.Count ? ModeNames[modeIndex] : "?";
        _history.Insert(0, new HistoryEntry(DateTime.Now, modeIndex, name, key, input, output));
        if (_history.Count > HistoryLimit) _history.RemoveAt(_history.Count - 1);
    }

    [RelayCommand]
    private async Task ShowHistoryAsync()
    {
        if (Dialogs == null) return;
        var picked = await Dialogs.ShowHistoryAsync(_history.ToList(),
            async e =>
            {
                if (Clipboard != null) await Clipboard.SetTextAsync(e.Output);
            },
            () => _history.Clear());
        if (picked == null) return;

        // 그때의 변환 방식·값·입력·결과로 되돌림
        if (picked.ModeIndex >= 0 && picked.ModeIndex < ModeNames.Count)
        {
            SelectedModeIndex = picked.ModeIndex;
            if (IsNumberParameterVisible) ParameterValue = picked.Key;
        }
        StopAnimation();
        InputText = picked.Input;
        OutputText = picked.Output;
    }

    // ---- QR 코드 -------------------------------------------------------------------

    [RelayCommand]
    private async Task SaveQrAsync()
    {
        if (Dialogs == null) return;
        if (string.IsNullOrWhiteSpace(OutputText))
        {
            await Dialogs.ShowMessageAsync(L["SAVE_IMAGE_NO_CONTENT_TITLE"], L["SAVE_IMAGE_NO_CONTENT_MESSAGE"], MessageKind.Warning);
            return;
        }
        byte[] png;
        try
        {
            png = QrCodeService.CreatePng(OutputText);
        }
        catch (QrCodeService.TooLongException)
        {
            await Dialogs.ShowMessageAsync(L["SAVE_QR_MENU"], L["QR_TOO_LONG_MESSAGE"], MessageKind.Warning);
            return;
        }

        if (await Dialogs.ShowImageAsync(L["SAVE_QR_MENU"], png, L["QR_SAVE_BUTTON"], L["HISTORY_CLOSE"]) != 0) return;
        var path = await Dialogs.SaveFileAsync(L["SAVE_QR_MENU"], "qrcode.png", PngFilter);
        if (path == null) return;
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
        try
        {
            File.WriteAllBytes(path, png);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Error(L["SAVE_ERROR_TITLE"], L["SAVE_IMAGE_ERROR_MESSAGE"] + e.Message);
        }
    }

    // ---- 네이버 검색 -----------------------------------------------------------------

    /// <summary>
    /// 키: 사용자가 직접 입력해 저장한 키 → 앱에 들어 있는 키 → 물어보고 저장.
    /// (Java 는 앱에 든 키를 먼저 썼는데, 그 키가 막히면 고칠 방법이 없어서 순서를 바꿈)
    /// </summary>
    private async Task<NaverSearch?> NaverClientAsync()
    {
        if (!string.IsNullOrEmpty(_settings.NaverClientId) && !string.IsNullOrEmpty(_settings.NaverClientSecret))
            return new NaverSearch(Http, _settings.NaverClientId, _settings.NaverClientSecret);
        if (BundledNaverKeys() is { } bundled) return new NaverSearch(Http, bundled.Id, bundled.Secret);
        return await AskNaverKeysAsync() ? new NaverSearch(Http, _settings.NaverClientId!, _settings.NaverClientSecret!) : null;
    }

    /// <summary>Client ID / Secret 을 물어보고 저장. 취소하면 false</summary>
    private async Task<bool> AskNaverKeysAsync()
    {
        if (Dialogs == null) return false;
        var title = L["NAVER_API_CONFIG_TITLE"];
        var id = (await Dialogs.PromptTextAsync(title, L["NAVER_ID_PROMPT"], _settings.NaverClientId ?? ""))?.Trim();
        if (string.IsNullOrEmpty(id)) return false;
        var secret = (await Dialogs.PromptTextAsync(title, L["NAVER_SECRET_PROMPT"]))?.Trim();
        if (string.IsNullOrEmpty(secret)) return false;

        // 키는 "설정 저장"과 상관없이 저장 (Java 와 같음)
        _settings.NaverClientId = id;
        _settings.NaverClientSecret = secret;
        _store.Save(_settings);
        return true;
    }

    /// <summary>파일 → 네이버 API 키 설정...: 새 키를 입력해 저장 (다음 검색부터 사용)</summary>
    [RelayCommand]
    private async Task SetNaverKeysAsync()
    {
        if (await AskNaverKeysAsync() && Dialogs != null)
            await Dialogs.ShowMessageAsync(L["NAVER_API_CONFIG_TITLE"], L["NAVER_KEYS_SAVED"]);
    }

    /// <summary>
    /// 네이버 API 호출. 키가 거부되면(401/403) 이유를 알려주고 새 키를 받아 한 번 더 시도.
    /// 실패하면 안내를 보여주고 null.
    /// </summary>
    private async Task<(NaverSearch Client, T Result)?> CallNaverAsync<T>(Func<NaverSearch, Task<T>> call)
    {
        if (await NaverClientAsync() is not { } naver) return null;
        for (int attempt = 0; ; attempt++)
        {
            IsBusy = true;
            try
            {
                return (naver, await call(naver));
            }
            catch (NaverAuthException e)
            {
                IsBusy = false;
                var reason = (e.Status == 403 ? L["NAVER_FORBIDDEN"] : L["NAVER_AUTH_FAILED"]) + "\n(" + e.Message + ")";
                if (attempt > 0 || !await Dialogs!.ConfirmAsync(L["SEARCH_ERROR_TITLE"], reason + "\n\n" + L["NAVER_ENTER_NEW_KEYS"], MessageKind.Warning))
                    return null;
                if (!await AskNaverKeysAsync()) return null;
                naver = new NaverSearch(Http, _settings.NaverClientId!, _settings.NaverClientSecret!);
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                IsBusy = false;
                await Error(L["SEARCH_ERROR_TITLE"], L["NAVER_SEARCH_FAILED"] + "\n" + e.Message);
                return null;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>네이버 웹 검색 결과 5개 중 고른 것들의 제목·설명을 원본 텍스트로</summary>
    [RelayCommand]
    private async Task SearchWebAsync()
    {
        if (Dialogs == null) return;
        var keyword = await Dialogs.PromptTextAsync(L["SEARCH_DIALOG_TITLE"], L["SEARCH_KEYWORD_PROMPT"]);
        if (string.IsNullOrWhiteSpace(keyword)) return;

        if (await CallNaverAsync(n => n.SearchWebAsync(keyword)) is not { } found) return;
        var results = found.Result;
        if (results.Count == 0)
        {
            await Dialogs.ShowMessageAsync(L["SEARCH_ERROR_TITLE"], L["SEARCH_NO_RESULTS"]);
            return;
        }

        var picked = await Dialogs.ChooseAsync(L["SEARCH_DIALOG_TITLE"], L["SEARCH_SELECT_PROMPT"],
            results.Select(r => new ChoiceItem(r.Title, r.Description)).ToList(), multiple: true);
        if (picked.Count == 0) return;
        StopAnimation();
        InputText = string.Join("\n\n", picked.Select(i => results[i].Title + "\n" + results[i].Description)).Trim();
        OutputText = "";
    }

    /// <summary>네이버 이미지 검색 결과 5개 중 하나를 골라 글자를 읽음</summary>
    [RelayCommand]
    private async Task SearchImagesAsync()
    {
        if (Dialogs == null) return;
        var keyword = await Dialogs.PromptTextAsync(L["IMAGE_SEARCH_DIALOG_TITLE"], L["SEARCH_KEYWORD_PROMPT"]);
        if (string.IsNullOrWhiteSpace(keyword)) return;

        if (await CallNaverAsync(async n =>
            {
                var list = await n.SearchImagesAsync(keyword);
                var thumbs = await Task.WhenAll(list.Select(r => n.TryDownloadAsync(r.Thumbnail)));
                return (list, thumbs);
            }) is not { } found) return;
        var naver = found.Client;
        var results = found.Result.list;
        var items = results.Select((r, i) => new ChoiceItem(r.Title, "", found.Result.thumbs[i])).ToList();
        if (results.Count == 0)
        {
            await Dialogs.ShowMessageAsync(L["SEARCH_ERROR_TITLE"], L["SEARCH_NO_RESULTS"]);
            return;
        }

        var picked = await Dialogs.ChooseAsync(L["IMAGE_SEARCH_DIALOG_TITLE"], L["IMAGE_SEARCH_SELECT_PROMPT"], items, multiple: false);
        if (picked.Count == 0) return;
        var chosen = results[picked[0]];

        IsBusy = true;
        byte[]? image;
        try
        {
            image = await naver.TryDownloadAsync(chosen.Link) ?? await naver.TryDownloadAsync(chosen.Thumbnail);
        }
        finally
        {
            IsBusy = false;
        }
        if (image == null)
        {
            await Error(L["SEARCH_ERROR_TITLE"], "Image Download Error: " + chosen.Link);
            return;
        }
        await RecognizeAsync(() => image);
    }

    // ---- 사용자 테마 -----------------------------------------------------------------

    private FileFilter ThemeFilter => new("ArtBash Theme (*.theme)", "theme");

    /// <summary>저장된 이름(LIGHT, DARK, 또는 사용자 테마 이름)으로 테마를 찾음</summary>
    private AppTheme ResolveTheme(string? name)
    {
        var custom = _settings.CustomThemes.FirstOrDefault(t => t.Name == name);
        return custom != null ? custom.ToTheme() : AppTheme.FromSaved(name);
    }

    private void RebuildCustomThemeItems()
    {
        CustomThemeItems.Clear();
        foreach (var t in _settings.CustomThemes)
            CustomThemeItems.Add(new CustomThemeItem(t.Name, SelectCustomThemeCommand) { IsSelected = Theme.Name == t.Name });
    }

    [RelayCommand]
    private void SelectCustomTheme(string name) => ApplyAndRemember(ResolveTheme(name));

    private void ApplyAndRemember(AppTheme theme)
    {
        Theme = theme;
        if (_app != null) ThemeService.Apply(_app, Theme);
        Persist(s => s.Theme = Theme.Name);
    }

    public bool IsCustomTheme => _settings.CustomThemes.Any(t => t.Name == Theme.Name);

    [RelayCommand]
    private async Task SaveAsNewThemeAsync()
    {
        if (Dialogs == null) return;
        var colors = await Dialogs.EditThemeColorsAsync(L["CUSTOMIZE_THEME_DIALOG_TITLE"], ThemeColors.FromTheme(Theme));
        if (colors != null) await AddCustomThemeAsync(colors);
    }

    [RelayCommand]
    private async Task ImportThemeAsync()
    {
        if (Dialogs == null) return;
        var files = await Dialogs.OpenFilesAsync(L["IMPORT_THEME_DIALOG_TITLE"], false, ThemeFilter, AllFilter);
        if (files.Count == 0) return;
        ThemeColors colors;
        try
        {
            colors = ThemeFile.Read(files[0]);
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException)
        {
            await Error(L["IMPORT_THEME_ERROR_TITLE"], L["IMPORT_THEME_ERROR_MESSAGE"]);
            return;
        }
        await AddCustomThemeAsync(colors);
    }

    /// <summary>이름을 물어보고 사용자 테마로 저장한 뒤 바로 적용</summary>
    private async Task AddCustomThemeAsync(ThemeColors colors)
    {
        var name = await Dialogs!.PromptTextAsync(L["ENTER_THEME_NAME_TITLE"], L["ENTER_THEME_NAME_MESSAGE"]);
        if (name == null) return;
        if (!ThemeNames.IsValid(name))
        {
            await Error(L["INVALID_THEME_NAME_TITLE"], L["INVALID_THEME_NAME_MESSAGE"]);
            return;
        }
        name = name.Trim();

        // 테마 목록은 "설정 저장"과 상관없이 저장 (Java 와 같음)
        _settings.CustomThemes.RemoveAll(t => t.Name == name);
        _settings.CustomThemes.Add(CustomThemeData.From(name, colors));
        _store.Save(_settings);
        ApplyAndRemember(ResolveTheme(name));
    }

    [RelayCommand]
    private async Task DeleteThemeAsync()
    {
        if (Dialogs == null) return;
        if (!IsCustomTheme)
        {
            await Dialogs.ShowMessageAsync(L["NO_CUSTOM_THEME_SELECTED_TITLE"], L["NO_CUSTOM_THEME_SELECTED_MESSAGE"], MessageKind.Warning);
            return;
        }
        var name = Theme.Name;
        var message = L["DELETE_THEME_CONFIRM_MESSAGE"].Replace("''", "'").Replace("{0}", name);
        if (!await Dialogs.ConfirmAsync(L["DELETE_THEME_CONFIRM_TITLE"], message, MessageKind.Warning)) return;

        _settings.CustomThemes.RemoveAll(t => t.Name == name);
        _store.Save(_settings);
        ApplyAndRemember(AppTheme.Light); // 기본 테마로 (Java 와 같음)
    }

    /// <summary>지금 보이는 테마의 색 5개를 .theme 파일로 (Java 버전에서도 가져올 수 있음)</summary>
    [RelayCommand]
    private async Task ExportThemeAsync()
    {
        if (Dialogs == null) return;
        var path = await Dialogs.SaveFileAsync(L["EXPORT_THEME_DIALOG_TITLE"], Theme.Name + ".theme", ThemeFilter);
        if (path == null) return;
        if (!path.EndsWith(".theme", StringComparison.OrdinalIgnoreCase)) path += ".theme";
        try
        {
            ThemeFile.Write(path, ThemeColors.FromTheme(Theme));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Error(L["EXPORT_THEME_ERROR_TITLE"], L["EXPORT_THEME_ERROR_MESSAGE"] + e.Message);
        }
    }

    // ---- 정보 ----------------------------------------------------------------------

    /// <summary>도움말 → 정보: README 를 보여줌. 글자를 누르면 이스터 에그 링크 (Java 와 같음)</summary>
    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        if (Dialogs == null) return;
        using var stream = typeof(MainViewModel).Assembly.GetManifestResourceStream("Atbash.App.README.txt");
        var text = stream == null ? "" : await new StreamReader(stream).ReadToEndAsync();
        await Dialogs.ShowAboutAsync(L["HELP_TITLE"], text, () => _ = Dialogs.OpenUrlAsync(EasterEggUrl));
    }
}
