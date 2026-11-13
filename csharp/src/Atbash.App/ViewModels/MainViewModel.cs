using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Tasks;
using Atbash.App.Services;
using Atbash.Core;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atbash.App.ViewModels;

/// <summary>
/// 메인 창의 상태와 동작. Java AtbashCipherGUI 의 기본 화면 동작과 같게 만듦:
///   변환(글자가 하나씩 나타나는 애니메이션), 실시간 변환, 복사(복사됨! 표시), 초기화,
///   변환 방식에 따라 바뀌는 입력칸, 언어 4종, 라이트/다크 테마, 설정 저장.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>변환 방식 목록의 문구 키. 순서 = CipherMode 번호 = 저장된 cipherMode</summary>
    private static readonly string[] ModeKeys =
    {
        "CIPHER_ATBASH", "CIPHER_MORSE", "CIPHER_BINARY",
        "CIPHER_CAESAR_ENCRYPT", "CIPHER_CAESAR_DECRYPT",
        "CIPHER_SCYTALE_ENCRYPT", "CIPHER_SCYTALE_DECRYPT",
        "CIPHER_VIGENERE_ENCRYPT", "CIPHER_VIGENERE_DECRYPT",
        "CIPHER_AES_TEXT_ENCRYPT", "CIPHER_AES_TEXT_DECRYPT",
        "CIPHER_BASE64_ENCODE", "CIPHER_BASE64_DECODE",
        "CIPHER_HEX_ENCODE", "CIPHER_HEX_DECODE",
        "CIPHER_URL_ENCODE", "CIPHER_URL_DECODE",
        "CIPHER_ROT13",
        "CIPHER_HANGUL_ENCRYPT", "CIPHER_HANGUL_DECRYPT",
    };

    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly Application? _app;
    private bool _loading;

    // 변환 방식별 숫자 값. "설정 저장"이 꺼져 있어도 앱을 켜 둔 동안은 기억함
    private int _caesarShift, _scytaleDiameter, _hangulShift;

    private DispatcherTimer? _animationTimer;
    private DispatcherTimer? _feedbackTimer;

    public static string Version { get; } = FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

    public MainViewModel() : this(new SettingsStore(), Application.Current)
    {
    }

    public MainViewModel(SettingsStore store, Application? app)
    {
        _store = store;
        _app = app;
        _settings = store.Load();
        _loading = true;
        _caesarShift = _settings.CaesarShift;
        _scytaleDiameter = _settings.ScytaleDiameter;
        _hangulShift = _settings.HangulShift;

        SaveSettings = _settings.SaveSettingsEnabled;
        LivePreview = _settings.LivePreview;
        L = Localizer.Load(Localizer.Parse(_settings.Language));
        RebuildModeNames();
        Theme = ResolveTheme(_settings.Theme);
        if (_app != null) ThemeService.Apply(_app, Theme);
        RebuildCustomThemeItems();

        int mode = _settings.CipherMode;
        SelectedModeIndex = mode >= 0 && mode < ModeKeys.Length ? mode : 0;
        UpdateParameterPanel();
        _loading = false;
    }

    // ---- 화면에 보이는 값 -------------------------------------------------------

    /// <summary>현재 언어의 문구. 화면에서 {Binding L[KEY]} 로 씀</summary>
    [ObservableProperty] public partial Localizer L { get; set; }

    public ObservableCollection<string> ModeNames { get; } = new();

    [ObservableProperty] public partial int SelectedModeIndex { get; set; }

    [ObservableProperty] public partial string InputText { get; set; } = "";

    [ObservableProperty] public partial string OutputText { get; set; } = "";

    [ObservableProperty] public partial decimal? ParameterValue { get; set; }

    [ObservableProperty] public partial decimal ParameterMinimum { get; set; }

    [ObservableProperty] public partial decimal ParameterMaximum { get; set; } = 1000;

    [ObservableProperty] public partial string ParameterLabel { get; set; } = "";

    [ObservableProperty] public partial bool IsNumberParameterVisible { get; set; }

    [ObservableProperty] public partial bool IsKeywordVisible { get; set; }

    [ObservableProperty] public partial bool IsPasswordVisible { get; set; }

    [ObservableProperty] public partial string Keyword { get; set; } = "";

    [ObservableProperty] public partial string Password { get; set; } = "";

    [ObservableProperty] public partial bool LivePreview { get; set; }

    [ObservableProperty] public partial bool SaveSettings { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TransformCommand))]
    public partial bool IsTransforming { get; set; }

    /// <summary>결과가 한 글자씩 나타나는 중인지</summary>
    [ObservableProperty] public partial bool IsAnimating { get; set; }

    [ObservableProperty] public partial string FeedbackText { get; set; } = "";

    [ObservableProperty] public partial double FeedbackOpacity { get; set; }

    [ObservableProperty] public partial AppTheme Theme { get; set; }

    public string WindowTitle => L["WINDOW_TITLE"] + " v" + Version;

    public CipherMode SelectedMode => (CipherMode)Math.Max(0, SelectedModeIndex);

    public bool IsKorean => L.Language == AppLanguage.Korean;
    public bool IsEnglish => L.Language == AppLanguage.English;
    public bool IsChinese => L.Language == AppLanguage.Chinese;
    public bool IsJapanese => L.Language == AppLanguage.Japanese;
    public bool IsLightTheme => Theme == AppTheme.Light;
    public bool IsDarkTheme => Theme == AppTheme.Dark;

    public int InitialWindowWidth => _settings.WindowWidth;
    public int InitialWindowHeight => _settings.WindowHeight;

    // ---- 동작 -------------------------------------------------------------------

    private bool CanTransform() => !IsTransforming;

    /// <summary>[변환]: 뒤에서 변환한 뒤 결과를 한 글자씩 보여줌</summary>
    [RelayCommand(CanExecute = nameof(CanTransform))]
    private async Task TransformAsync()
    {
        IsTransforming = true;
        StopAnimation();
        OutputText = "";
        var input = InputText;
        var mode = SelectedMode;
        int key = CurrentNumber();
        var keyword = CurrentKeyword();
        try
        {
            // AES 는 키를 만드는 데 시간이 걸리므로 화면이 멈추지 않게 따로 실행
            var result = await Task.Run(() => CipherEngine.Transform(mode, input, key, keyword));
            var text = Describe(result);
            AnimateOutput(text);
            AddHistory((int)mode, key, input, text);
        }
        finally
        {
            IsTransforming = false;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        StopAnimation();
        InputText = "";
        OutputText = "";
    }

    /// <summary>[복사]: 결과를 클립보드로. 결과가 비어 있으면 "LOL" (Java 버전의 이스터 에그)</summary>
    [RelayCommand]
    private async Task CopyAsync()
    {
        if (string.IsNullOrEmpty(OutputText))
        {
            OutputText = "LOL";
            return;
        }
        if (Clipboard != null) await Clipboard.SetTextAsync(OutputText);
        ShowFeedback(L["COPY_FEEDBACK"]);
    }

    [RelayCommand]
    private void SetLanguage(AppLanguage language)
    {
        L = Localizer.Load(language);
        Persist(s => s.Language = Localizer.ToSaved(language));
    }

    [RelayCommand]
    private void SetTheme(string name) => ApplyAndRemember(AppTheme.FromSaved(name));

    /// <summary>창을 닫을 때 크기 저장 (설정 저장이 켜져 있을 때만, Java 와 같음)</summary>
    public void SaveWindowSize(double width, double height) =>
        Persist(s =>
        {
            s.WindowWidth = (int)width;
            s.WindowHeight = (int)height;
        });

    // ---- 값이 바뀔 때 -------------------------------------------------------------

    partial void OnLChanged(Localizer value)
    {
        RebuildModeNames();
        UpdateParameterPanel();
        FeedbackText = ""; // 다른 언어로 남아 있지 않게 (Java 와 같음)
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(IsKorean));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(IsChinese));
        OnPropertyChanged(nameof(IsJapanese));
    }

    partial void OnThemeChanged(AppTheme value)
    {
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsCustomTheme));
        RebuildCustomThemeItems();
    }

    partial void OnSelectedModeIndexChanged(int value)
    {
        if (value < 0) return; // 목록을 다시 만드는 동안
        OnPropertyChanged(nameof(SelectedMode));
        UpdateParameterPanel();
        Persist(s => s.CipherMode = value);
        RunLivePreview();
    }

    partial void OnInputTextChanged(string value) => RunLivePreview();

    partial void OnKeywordChanged(string value) => RunLivePreview();

    partial void OnParameterValueChanged(decimal? value)
    {
        if (_loadingParameter || value == null) return;
        int v = (int)value.Value;
        switch (SelectedMode)
        {
            case CipherMode.CaesarEncrypt or CipherMode.CaesarDecrypt:
                _caesarShift = v;
                Persist(s => s.CaesarShift = v);
                break;
            case CipherMode.ScytaleEncrypt or CipherMode.ScytaleDecrypt:
                _scytaleDiameter = v;
                Persist(s => s.ScytaleDiameter = v);
                break;
            case CipherMode.HangulEncrypt or CipherMode.HangulDecrypt:
                _hangulShift = v;
                Persist(s => s.HangulShift = v);
                break;
        }
        RunLivePreview();
    }

    partial void OnLivePreviewChanged(bool value)
    {
        Persist(s => s.LivePreview = value);
        RunLivePreview();
    }

    /// <summary>"설정 저장" 켜기/끄기 자체는 항상 저장 (Java 와 같음)</summary>
    partial void OnSaveSettingsChanged(bool value)
    {
        if (_loading) return;
        _settings.SaveSettingsEnabled = value;
        _store.Save(_settings);
    }

    // ---- 내부 -------------------------------------------------------------------

    private bool _loadingParameter;

    /// <summary>변환 방식에 맞게 이동 값/지름/키워드/비밀번호 칸을 보여줌</summary>
    private void UpdateParameterPanel()
    {
        var kind = CipherEngine.ParameterFor(SelectedMode);
        IsNumberParameterVisible = kind is CipherEngine.ParameterKind.Shift or CipherEngine.ParameterKind.Diameter;
        IsKeywordVisible = kind == CipherEngine.ParameterKind.Keyword;
        IsPasswordVisible = kind == CipherEngine.ParameterKind.Password;
        ParameterLabel = kind switch
        {
            CipherEngine.ParameterKind.Shift => L["PARAM_SHIFT_KEY"],
            CipherEngine.ParameterKind.Diameter => L["PARAM_DIAMETER"],
            CipherEngine.ParameterKind.Keyword => L["PARAM_KEYWORD"],
            CipherEngine.ParameterKind.Password => L["PARAM_PASSWORD"],
            _ => "",
        };

        _loadingParameter = true;
        if (kind == CipherEngine.ParameterKind.Diameter)
        {
            ParameterMinimum = 1;
            ParameterValue = _scytaleDiameter;
        }
        else if (kind == CipherEngine.ParameterKind.Shift)
        {
            ParameterMinimum = -1000;
            ParameterValue = SelectedMode is CipherMode.HangulEncrypt or CipherMode.HangulDecrypt
                ? _hangulShift
                : _caesarShift;
        }
        _loadingParameter = false;
    }

    private void RebuildModeNames()
    {
        int selected = SelectedModeIndex;
        ModeNames.Clear();
        foreach (var key in ModeKeys) ModeNames.Add(L[key]);
        SelectedModeIndex = selected < 0 ? 0 : selected;
    }

    private int CurrentNumber() => IsNumberParameterVisible && ParameterValue != null ? (int)ParameterValue.Value : 0;

    private string CurrentKeyword() => CipherEngine.IsAes(SelectedMode) ? Password : Keyword;

    /// <summary>실시간 변환: 입력이 바뀌면 바로 결과를 보여줌 (애니메이션 없이). AES 는 느리고 매번 결과가 달라 제외</summary>
    private void RunLivePreview()
    {
        if (_loading || !LivePreview || CipherEngine.IsAes(SelectedMode)) return;
        StopAnimation();
        OutputText = Describe(CipherEngine.Transform(SelectedMode, InputText, CurrentNumber(), CurrentKeyword()));
    }

    private string Describe(TransformResult result) => result.Error switch
    {
        TransformError.None => result.Text,
        TransformError.AesNoPassword => L["AES_TEXT_NO_PASSWORD"],
        TransformError.AesDecryptFailed => L["AES_TEXT_DECRYPT_ERROR"],
        _ => L["DECODE_ERROR"],
    };

    /// <summary>
    /// 결과를 한 글자씩 보여줌. Java 는 글자당 15ms 라 긴 결과는 아주 오래 걸렸으므로,
    /// 전체가 약 1.5초 안에 끝나도록 한 번에 여러 글자씩 보여줌.
    /// </summary>
    private void AnimateOutput(string text)
    {
        StopAnimation();
        OutputText = "";
        if (text.Length == 0) return;
        IsAnimating = true;
        int index = 0;
        int step = Math.Max(1, text.Length / 100);
        _animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Normal, (_, _) =>
        {
            int next = Math.Min(text.Length, index + step);
            // 이모지처럼 두 칸짜리 글자가 반으로 잘리지 않게
            if (next < text.Length && char.IsHighSurrogate(text[next - 1])) next++;
            OutputText = text[..next];
            index = next;
            if (index >= text.Length) StopAnimation();
        });
        _animationTimer.Start();
    }

    private void StopAnimation()
    {
        _animationTimer?.Stop();
        _animationTimer = null;
        IsAnimating = false;
    }

    /// <summary>"복사됨!" 을 1초 보여준 뒤 1초 동안 흐려지며 사라지게 함 (Java 와 같음)</summary>
    private void ShowFeedback(string text)
    {
        _feedbackTimer?.Stop();
        FeedbackText = text;
        FeedbackOpacity = 1;
        var start = DateTime.UtcNow;
        _feedbackTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Normal, (_, _) =>
        {
            double elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
            if (elapsed >= 2000)
            {
                FeedbackText = "";
                FeedbackOpacity = 0;
                _feedbackTimer?.Stop();
                return;
            }
            if (elapsed > 1000) FeedbackOpacity = 1 - (elapsed - 1000) / 1000;
        });
        _feedbackTimer.Start();
    }

    /// <summary>설정 저장이 켜져 있을 때만 바꾸고 저장 (Java 와 같음)</summary>
    private void Persist(Action<AppSettings> change)
    {
        if (_loading || !SaveSettings) return;
        change(_settings);
        _store.Save(_settings);
    }

    private static string FormatVersion(Version? v) => v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
}
