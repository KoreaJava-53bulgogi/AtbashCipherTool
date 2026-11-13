using System.Diagnostics;
using Atbash.App.Services;
using Atbash.App.ViewModels;
using Atbash.App.Views;
using Atbash.Core;
using Atbash.Core.Crypto;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Atbash.App.Tests;

/// <summary>
/// 메인 창을 화면 없이 띄워서 동작을 확인하고, 모습을 PNG 로 남김 (실제 화면은 건드리지 않음).
/// 설정은 테스트마다 임시 파일을 써서 실제 설정에 영향이 없음.
/// </summary>
public class MainWindowTests : IDisposable
{
    private readonly string _settingsPath =
        Path.Combine(Path.GetTempPath(), "atbash-ui-test-" + Guid.NewGuid().ToString("N"), "settings.json");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_settingsPath)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private (MainWindow window, MainViewModel vm) Open(Action<AppSettings>? setup = null)
    {
        var store = new SettingsStore(_settingsPath);
        if (setup != null)
        {
            var s = new AppSettings();
            setup(s);
            store.Save(s);
        }
        var vm = new MainViewModel(store, Application.Current);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump();
        return (window, vm);
    }

    /// <summary>
    /// 타이머·비동기 작업이 끝날 때까지 기다림. await 하는 동안 테스트용 UI 스레드가
    /// 타이머와 작업을 처리함 (Thread.Sleep 으로 막으면 타이머가 돌지 않음).
    /// </summary>
    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("condition not met in time");
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static T Find<T>(Window w, string name) where T : Control => w.FindControl<T>(name)!;

    // ---- 동작 -------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Transform_button_animates_the_result()
    {
        var (w, vm) = Open();
        vm.InputText = "Hello World";
        vm.SelectedModeIndex = (int)CipherMode.Atbash;
        Find<Button>(w, "TransformButton").Command!.Execute(null);

        await WaitUntil(() => vm.OutputText == "Svool Dliow");
        Assert.True(vm.TransformCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Live_preview_updates_while_typing_but_not_for_aes()
    {
        var (_, vm) = Open();
        vm.LivePreview = true;
        vm.SelectedModeIndex = (int)CipherMode.CaesarEncrypt;
        vm.ParameterValue = 3;
        vm.InputText = "abc";
        Assert.Equal("def", vm.OutputText);
        vm.InputText = "xyz";
        Assert.Equal("abc", vm.OutputText);

        vm.SelectedModeIndex = (int)CipherMode.AesTextEncrypt;
        vm.Password = "pw";
        vm.InputText = "secret";
        Assert.Equal("abc", vm.OutputText); // AES 는 실시간 변환 안 함
    }

    [AvaloniaFact]
    public async Task Copy_shows_feedback_and_lol_when_empty()
    {
        var (_, vm) = Open();
        vm.CopyCommand.Execute(null);
        Pump();
        Assert.Equal("LOL", vm.OutputText);

        vm.CopyCommand.Execute(null);
        await WaitUntil(() => vm.FeedbackText.Length > 0);
        Assert.Equal("복사됨!", vm.FeedbackText);
        await WaitUntil(() => vm.FeedbackText.Length == 0, 4000); // 약 2초 뒤 사라짐
    }

    [AvaloniaFact]
    public void Parameter_inputs_follow_the_selected_mode()
    {
        var (w, vm) = Open();
        var number = Find<NumericUpDown>(w, "NumberBox");
        var keyword = Find<TextBox>(w, "KeywordBox");
        var password = Find<TextBox>(w, "PasswordBox");

        vm.SelectedModeIndex = (int)CipherMode.Atbash;
        Pump();
        Assert.False(number.IsVisible || keyword.IsVisible || password.IsVisible);

        vm.SelectedModeIndex = (int)CipherMode.ScytaleEncrypt;
        Pump();
        Assert.True(number.IsVisible);
        Assert.Equal(5m, vm.ParameterValue);
        Assert.Equal(1m, vm.ParameterMinimum);
        Assert.Equal("지름", vm.ParameterLabel);

        vm.SelectedModeIndex = (int)CipherMode.VigenereEncrypt;
        Pump();
        Assert.True(keyword.IsVisible);
        Assert.False(number.IsVisible);

        vm.SelectedModeIndex = (int)CipherMode.AesTextDecrypt;
        Pump();
        Assert.True(password.IsVisible);
        Assert.Equal('●', password.PasswordChar);
    }

    [AvaloniaFact]
    public async Task Aes_through_the_screen_can_be_decrypted()
    {
        var (_, vm) = Open();
        vm.SelectedModeIndex = (int)CipherMode.AesTextEncrypt;
        vm.Password = "비밀번호";
        vm.InputText = "안녕하세요 😀";
        vm.TransformCommand.Execute(null);
        await WaitUntil(() => !vm.IsTransforming && !vm.IsAnimating && vm.OutputText.Length > 0, 10000);

        Assert.Equal("안녕하세요 😀", AesCipher.DecryptText(vm.OutputText, "비밀번호"));
    }

    [AvaloniaFact]
    public async Task Errors_are_shown_as_messages()
    {
        var (_, vm) = Open();
        vm.LivePreview = true;
        vm.SelectedModeIndex = (int)CipherMode.Base64Decode;
        vm.InputText = "@@@ not base64";
        Assert.StartsWith("디코딩하지 못했습니다", vm.OutputText);

        vm.LivePreview = false;
        vm.SelectedModeIndex = (int)CipherMode.AesTextEncrypt;
        vm.Password = "";
        vm.TransformCommand.Execute(null);
        await WaitUntil(() => vm.OutputText == "비밀번호를 입력한 뒤 변환하세요.");
    }

    [AvaloniaFact]
    public void Language_switch_updates_texts_and_keeps_the_selected_mode()
    {
        var (w, vm) = Open();
        vm.SelectedModeIndex = (int)CipherMode.Rot13;
        Assert.StartsWith("아트배쉬 암호화/복호화 툴 v9.0.0", w.Title);

        vm.SetLanguageCommand.Execute(AppLanguage.English);
        Pump();
        Assert.Equal("Transform", Find<Button>(w, "TransformButton").Content);
        Assert.Equal((int)CipherMode.Rot13, vm.SelectedModeIndex);
        Assert.Equal("ROT13", Find<ComboBox>(w, "ModeBox").SelectedItem);
        Assert.True(vm.IsEnglish);

        vm.SetLanguageCommand.Execute(AppLanguage.Japanese);
        Pump();
        Assert.False(vm.L["AES_TEXT_NO_PASSWORD"].StartsWith('!')); // 빠진 번역도 키 이름으로 나오지 않음
    }

    [AvaloniaFact]
    public void Settings_are_saved_and_restored()
    {
        var (_, vm) = Open();
        vm.SetLanguageCommand.Execute(AppLanguage.English);
        vm.SetThemeCommand.Execute("DARK");
        vm.SelectedModeIndex = (int)CipherMode.HangulEncrypt;
        vm.ParameterValue = 7;
        vm.LivePreview = true;
        vm.SaveWindowSize(900, 600);

        var reopened = new MainViewModel(new SettingsStore(_settingsPath), Application.Current);
        Assert.Equal(AppLanguage.English, reopened.L.Language);
        Assert.True(reopened.IsDarkTheme);
        Assert.Equal((int)CipherMode.HangulEncrypt, reopened.SelectedModeIndex);
        Assert.Equal(7m, reopened.ParameterValue);
        Assert.True(reopened.LivePreview);
        Assert.Equal(900, reopened.InitialWindowWidth);
    }

    [AvaloniaFact]
    public void Nothing_is_saved_when_save_settings_is_off()
    {
        var (_, vm) = Open();
        vm.SaveSettings = false;
        vm.SetLanguageCommand.Execute(AppLanguage.Chinese);
        vm.SelectedModeIndex = (int)CipherMode.Morse;

        var reopened = new MainViewModel(new SettingsStore(_settingsPath), Application.Current);
        Assert.False(reopened.SaveSettings);
        Assert.Equal(AppLanguage.Korean, reopened.L.Language);
        Assert.Equal((int)CipherMode.Atbash, reopened.SelectedModeIndex);
    }

    // ---- 모습 (PNG) ---------------------------------------------------------------

    [AvaloniaFact]
    public void Snapshot_light_korean()
    {
        var (w, vm) = Open();
        w.Width = 1100;
        w.Height = 500;
        vm.SelectedModeIndex = (int)CipherMode.CaesarEncrypt;
        vm.InputText = "안녕하세요 Hello 123";
        vm.LivePreview = true;
        Pump();
        Assert.True(File.Exists(Snapshot.Save(w, "stage2-light-ko")));
    }

    [AvaloniaFact]
    public void Snapshot_dark_english_aes()
    {
        var (w, vm) = Open(s =>
        {
            s.Theme = "DARK";
            s.Language = "ENGLISH";
            s.CipherMode = (int)CipherMode.AesTextEncrypt;
        });
        w.Width = 1100;
        w.Height = 500;
        vm.InputText = "Meet me at noon";
        vm.Password = "secret";
        Pump();
        Assert.True(File.Exists(Snapshot.Save(w, "stage2-dark-en-aes")));
    }
}
