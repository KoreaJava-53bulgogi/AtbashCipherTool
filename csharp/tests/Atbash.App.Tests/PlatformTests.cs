using System.Runtime.InteropServices;
using System.Text.Json;
using Atbash.App.Services;
using Atbash.App.ViewModels;
using Atbash.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Atbash.App.Tests;

/// <summary>
/// 크로스 플랫폼 B단계: 운영체제마다 달라지는 부분 (설정 위치, OCR 엔진 고르기, 단축키, 안내 문구).
/// 각 운영체제를 흉내 내서 시험하므로 어느 운영체제에서 돌려도 세 운영체제의 동작을 모두 확인함.
/// </summary>
public class PlatformTests
{
    // ---- 설정 파일 위치 ---------------------------------------------------------------

    [Fact]
    public void Windows_keeps_the_9_0_0_settings_folder()
    {
        // 바꾸면 기존 사용자의 설정·사용자 테마가 사라진 것처럼 보임
        var dir = SettingsStore.DirectoryFor("win", @"C:\Users\me", @"C:\Users\me\AppData\Roaming", null);
        Assert.Equal(Path.Combine(@"C:\Users\me\AppData\Roaming", "AtbashCipherTool"), dir);
    }

    [Fact]
    public void Mac_uses_application_support()
    {
        var dir = SettingsStore.DirectoryFor("osx", "/Users/me", "/Users/me/.config", null);
        Assert.Equal(Path.Combine("/Users/me", "Library", "Application Support", "AtbashCipherTool"), dir);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("/data/cfg", true)]
    [InlineData("relative/cfg", false)] // XDG 규칙: 절대 경로가 아니면 무시
    public void Linux_follows_xdg_config_home(string? xdg, bool usesXdg)
    {
        var dir = SettingsStore.DirectoryFor("linux", "/home/me", "/home/me/.config", xdg);
        var expectedBase = usesXdg ? xdg! : Path.Combine("/home/me", ".config");
        Assert.Equal(Path.Combine(expectedBase, "AtbashCipherTool"), dir);
    }

    [Fact]
    public void Default_settings_path_is_inside_the_folder_of_this_os()
    {
        var path = new SettingsStore().Path;
        Assert.Equal(Path.Combine(SettingsStore.DefaultDirectory(), "settings.json"), path);
        if (OperatingSystem.IsWindows())
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtbashCipherTool", "settings.json"), path);
        if (OperatingSystem.IsMacOS())
            Assert.Contains("/Library/Application Support/AtbashCipherTool/", path);
    }

    // ---- OCR 엔진 고르기 -----------------------------------------------------------------

    [Theory]
    [InlineData("win", Architecture.X64, true)]
    [InlineData("win", Architecture.X86, true)]
    [InlineData("win", Architecture.Arm64, false)] // Tesseract 패키지에 ARM64 용 엔진이 없음
    [InlineData("osx", Architecture.Arm64, false)] // D단계: Apple Vision
    public void Picks_an_ocr_engine_per_os(string os, Architecture arch, bool supported)
    {
        var engine = OcrService.For(os, arch);
        Assert.Equal(supported, engine.IsSupported);
        Assert.Equal(supported ? typeof(TesseractOcrEngine) : typeof(UnsupportedOcrEngine), engine.GetType());
    }

    [Fact]
    public void Linux_uses_the_system_tesseract_command()
    {
        Assert.Same(TesseractCliOcrEngine.System, OcrService.For("linux", Architecture.X64));
        Assert.True(OcrService.For("linux", Architecture.Arm64).IsSupported);
    }

    // ---- Linux: 시스템 tesseract 명령 ------------------------------------------------------

    private static byte[] SmallPng() =>
        TextImageExporter.RenderPng("A", Avalonia.Media.Colors.White, Avalonia.Media.Colors.Black, fontSize: 20);

    [AvaloniaFact]
    public async Task Missing_tesseract_shows_how_to_install_it()
    {
        var dir = Directory.CreateTempSubdirectory("atbash-platform-").FullName;
        try
        {
            var dialogs = new FakeDialogs();
            var vm = new MainViewModel(new SettingsStore(Path.Combine(dir, "settings.json")), Application.Current)
            {
                Dialogs = dialogs,
                Ocr = new TesseractCliOcrEngine(() => null, OcrService.DataPath),
            };
            Assert.False(vm.Ocr.IsAvailable(out var reason));
            Assert.Contains("tesseract", reason);

            File.WriteAllBytes(Path.Combine(dir, "a.png"), SmallPng());
            dialogs.OpenResults.Enqueue(new[] { Path.Combine(dir, "a.png") });
            await vm.OcrFromFileCommand.ExecuteAsync(null);

            var m = Assert.Single(dialogs.Messages);
            Assert.Equal(MessageKind.Info, m.Kind); // 오류가 아니라 안내
            Assert.Contains("sudo apt install tesseract-ocr", m.Message);
            Assert.False(vm.IsBusy);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>가짜 tesseract (셸 스크립트): 받은 인자를 그대로 출력. 셸 스크립트를 실행할 수 있는 macOS·Linux 에서만</summary>
    private static string FakeTesseract(string dir, string body)
    {
        var path = Path.Combine(dir, "tesseract");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public void Runs_tesseract_with_the_bundled_language_data()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("shell script fake needs macOS/Linux");
        var dir = Directory.CreateTempSubdirectory("atbash-fake-tess-").FullName;
        try
        {
            // 인자를 한 줄에 하나씩 출력하고, 입력 그림이 실제로 있는지도 확인
            var exe = FakeTesseract(dir, "for a in \"$@\"; do echo \"$a\"; done\n[ -f \"$1\" ] && echo 'input exists'\necho '안녕 OCR'");
            var engine = new TesseractCliOcrEngine(() => exe, "/data/tess");
            var lines = engine.Recognize(SmallPng()).Split('\n');

            Assert.EndsWith(".png", lines[0]);
            Assert.Equal(new[] { "stdout", "--tessdata-dir", "/data/tess", "-l", "kor+eng" }, lines[1..6]);
            Assert.Equal("input exists", lines[6]);
            Assert.Equal("안녕 OCR", lines[7]); // 한글 출력(UTF-8)이 깨지지 않음
            Assert.False(File.Exists(lines[0]));  // 임시 그림은 지움
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Tesseract_failure_is_reported_with_its_message()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("shell script fake needs macOS/Linux");
        var dir = Directory.CreateTempSubdirectory("atbash-fake-tess-").FullName;
        try
        {
            var exe = FakeTesseract(dir, "echo 'Failed loading language kor' >&2\nexit 1");
            var e = Assert.Throws<InvalidOperationException>(() => new TesseractCliOcrEngine(() => exe, dir).Recognize(SmallPng()));
            Assert.Contains("Failed loading language kor", e.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Unsupported_engine_explains_itself()
    {
        Assert.False(UnsupportedOcrEngine.Instance.IsAvailable(out var reason));
        Assert.Contains("no OCR engine", reason);
        Assert.Throws<PlatformNotSupportedException>(() => UnsupportedOcrEngine.Instance.Recognize(new byte[] { 1 }));
    }

    // ---- 단축키 ------------------------------------------------------------------------

    [Fact]
    public void Shortcuts_use_command_on_mac_and_avoid_the_hide_app_key()
    {
        Assert.Equal(new KeyGesture(Key.V, KeyModifiers.Control | KeyModifiers.Shift), Shortcuts.OcrFromClipboard(mac: false));
        Assert.Equal(new KeyGesture(Key.H, KeyModifiers.Control), Shortcuts.History(mac: false));
        Assert.Equal(new KeyGesture(Key.V, KeyModifiers.Meta | KeyModifiers.Shift), Shortcuts.OcrFromClipboard(mac: true));
        Assert.Equal(new KeyGesture(Key.Y, KeyModifiers.Meta), Shortcuts.History(mac: true)); // ⌘H 는 macOS 의 "앱 가리기"
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Main_window_shows_and_binds_the_shortcuts_of_the_os(bool mac)
    {
        var dir = Directory.CreateTempSubdirectory("atbash-platform-").FullName;
        var before = MainWindow.IsMac;
        try
        {
            MainWindow.IsMac = mac;
            var vm = new MainViewModel(new SettingsStore(Path.Combine(dir, "settings.json")), Application.Current);
            var window = new MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // 메뉴에 보이는 단축키
            Assert.Equal(Shortcuts.OcrFromClipboard(mac), window.FindControl<MenuItem>("OcrClipboardMenu")!.InputGesture);
            Assert.Equal(Shortcuts.History(mac), window.FindControl<MenuItem>("HistoryMenu")!.InputGesture);

            // 실제로 눌렀을 때 동작하는 단축키 (같은 키 + 같은 명령)
            Assert.Collection(window.KeyBindings,
                b => { Assert.Equal(Shortcuts.OcrFromClipboard(mac), b.Gesture); Assert.Same(vm.OcrFromClipboardCommand, b.Command); },
                b => { Assert.Equal(Shortcuts.History(mac), b.Gesture); Assert.Same(vm.ShowHistoryCommand, b.Command); });

            // 화면 모델이 다시 연결돼도 단축키가 두 번 들어가지 않음
            window.DataContext = new MainViewModel(new SettingsStore(Path.Combine(dir, "settings.json")), Application.Current);
            Assert.Equal(2, window.KeyBindings.Count);
            window.Close();
        }
        finally
        {
            MainWindow.IsMac = before;
            Directory.Delete(dir, true);
        }
    }

    // ---- 언어 파일 ----------------------------------------------------------------------

    [Theory]
    [InlineData("ko")]
    [InlineData("en")]
    [InlineData("ja")]
    [InlineData("zh")]
    public void Every_language_has_the_per_os_messages(string lang)
    {
        var asm = typeof(MainViewModel).Assembly;
        using var stream = asm.GetManifestResourceStream($"Atbash.App.Lang.{lang}.json")!;
        var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        Assert.Contains("Win+Shift+S", strings["CLIPBOARD_NO_IMAGE_MESSAGE"]);
        Assert.Contains("Control+Shift+Command+4", strings["CLIPBOARD_NO_IMAGE_MESSAGE_MAC"]);
        Assert.Contains("PrtSc", strings["CLIPBOARD_NO_IMAGE_MESSAGE_LINUX"]);
        Assert.Contains("OCR", strings["OCR_UNSUPPORTED_MESSAGE"]);
        Assert.Contains("sudo apt install tesseract-ocr", strings["OCR_LINUX_INSTALL_MESSAGE"]);
    }
}
