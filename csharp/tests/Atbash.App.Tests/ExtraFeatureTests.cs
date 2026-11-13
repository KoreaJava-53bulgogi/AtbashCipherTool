using System.Diagnostics;
using System.Text;
using Atbash.App.Services;
using Atbash.App.ViewModels;
using Atbash.App.Views;
using Atbash.Core;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Atbash.App.Tests;

/// <summary>4단계 부가 기능: 변환 기록, QR, 네이버 검색, 사용자 테마, 정보 창, 스플래시</summary>
public class ExtraFeatureTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("atbash-ui-extra-").FullName;
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeClipboard _clipboard = new();

    public void Dispose() => Directory.Delete(_dir, true);

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    private MainViewModel NewVm() =>
        new(new SettingsStore(SettingsPath), Application.Current)
        {
            Dialogs = _dialogs,
            Clipboard = _clipboard,
            BundledNaverKeys = () => ("test-id", "test-secret"),
        };

    private string PathOf(string name) => Path.Combine(_dir, name);

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("condition not met in time");
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Transform(MainViewModel vm, CipherMode mode, string input)
    {
        vm.SelectedModeIndex = (int)mode;
        vm.InputText = input;
        await vm.TransformCommand.ExecuteAsync(null);
        await WaitUntil(() => !vm.IsAnimating);
    }

    // ---- 변환 기록 -----------------------------------------------------------------

    [AvaloniaFact]
    public async Task History_records_transforms_and_restores_them()
    {
        var vm = NewVm();
        await Transform(vm, CipherMode.Atbash, "Hello");
        await Transform(vm, CipherMode.Atbash, "Hello"); // 같은 변환을 연달아: 한 번만
        await Transform(vm, CipherMode.Rot13, "abc");
        vm.SelectedModeIndex = (int)CipherMode.CaesarEncrypt;
        vm.ParameterValue = 5;
        await Transform(vm, CipherMode.CaesarEncrypt, "xyz");

        Assert.Equal(3, vm.History.Count);
        Assert.Equal("cde", vm.History[0].Output); // 가장 최근이 맨 위
        Assert.Equal("카이사르 (암호화)", vm.History[0].ModeName);

        // 기록 창에서 결과 복사 → 두 번째 항목(ROT13) 불러오기
        _dialogs.HistoryBehavior = async (entries, copy, _) =>
        {
            await copy(entries[2]);
            return entries[1];
        };
        vm.InputText = "something else";
        await vm.ShowHistoryCommand.ExecuteAsync(null);
        Assert.Equal("Svool", _clipboard.Text);
        Assert.Equal((int)CipherMode.Rot13, vm.SelectedModeIndex);
        Assert.Equal("abc", vm.InputText);
        Assert.Equal("nop", vm.OutputText);

        // 모두 지우기
        _dialogs.HistoryBehavior = (_, _, clear) =>
        {
            clear();
            return Task.FromResult<HistoryEntry?>(null);
        };
        await vm.ShowHistoryCommand.ExecuteAsync(null);
        Assert.Empty(vm.History);
    }

    [AvaloniaFact]
    public async Task History_keeps_only_the_last_50()
    {
        var vm = NewVm();
        for (int i = 0; i < 55; i++) await Transform(vm, CipherMode.Atbash, "text " + i);
        Assert.Equal(50, vm.History.Count);
        Assert.Equal("text 54", vm.History[0].Input);
        Assert.Equal("text 5", vm.History[^1].Input);
    }

    // ---- QR 코드 -------------------------------------------------------------------

    private static string DecodeQr(byte[] png)
    {
        using var bmp = SKBitmap.Decode(png);
        var pixels = new byte[bmp.Width * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                pixels[y * bmp.Width + x] = (byte)((c.Red + c.Green + c.Blue) / 3);
            }
        var source = new PlanarYUVLuminanceSource(pixels, bmp.Width, bmp.Height, 0, 0, bmp.Width, bmp.Height, false);
        var result = new MultiFormatReader().decode(new BinaryBitmap(new HybridBinarizer(source)),
            new Dictionary<DecodeHintType, object> { [DecodeHintType.CHARACTER_SET] = "UTF-8", [DecodeHintType.TRY_HARDER] = true });
        return result?.Text ?? "(not decoded)";
    }

    [AvaloniaFact]
    public async Task Qr_code_is_saved_and_scans_back_to_the_text()
    {
        var vm = NewVm();
        vm.OutputText = "안녕하세요 아트배쉬 9.0 😀";
        _dialogs.ImageResults.Enqueue(0); // [PNG로 저장]
        _dialogs.SaveResults.Enqueue(PathOf("qr"));
        await vm.SaveQrCommand.ExecuteAsync(null);

        var png = File.ReadAllBytes(PathOf("qr.png"));
        Assert.Equal("안녕하세요 아트배쉬 9.0 😀", DecodeQr(png));
        using var bmp = SKBitmap.Decode(png);
        Assert.InRange(bmp.Width, 360, 460);
    }

    [AvaloniaFact]
    public async Task Too_long_text_for_qr_is_reported()
    {
        var vm = NewVm();
        vm.OutputText = new string('가', 1000); // 한글은 약 770자까지
        await vm.SaveQrCommand.ExecuteAsync(null);
        Assert.Contains("너무 길어서", Assert.Single(_dialogs.Messages).Message);
        Assert.Empty(_dialogs.ShownImages);
    }

    // ---- 네이버 검색 -----------------------------------------------------------------

    private static (string, byte[]) Json(string s) => ("application/json", Encoding.UTF8.GetBytes(s));

    [AvaloniaFact]
    public async Task Naver_web_search_puts_chosen_results_in_the_input()
    {
        var handler = new FakeHttpHandler();
        handler.Responses["https://openapi.naver.com/v1/search/webkr.json"] = Json("""
            {"items":[
              {"title":"<b>아트배쉬</b> 암호란?","description":"알파벳을 &quot;거꾸로&quot; 바꾸는 암호"},
              {"title":"두 번째","description":"설명 2"},
              {"title":"세 번째 \"따옴표\"","description":"설명 3"}
            ]}
            """);
        var vm = NewVm();
        vm.Http = new HttpClient(handler);
        _dialogs.TextResults.Enqueue("아트배쉬");
        _dialogs.ChooseResults.Enqueue(new[] { 0, 2 });
        await vm.SearchWebCommand.ExecuteAsync(null);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("query=%EC%95%84%ED%8A%B8%EB%B0%B0%EC%89%AC", request.RequestUri!.AbsoluteUri);
        Assert.Equal("test-id", request.Headers.GetValues("X-Naver-Client-Id").Single());
        Assert.Equal("아트배쉬 암호란?\n알파벳을 \"거꾸로\" 바꾸는 암호\n\n세 번째 \"따옴표\"\n설명 3", vm.InputText);
    }

    [AvaloniaFact]
    public async Task Naver_image_search_runs_ocr_on_the_chosen_image()
    {
        var image = TextImageExporter.RenderPng("NAVER IMAGE 42", Colors.White, Colors.Black, 36);
        var handler = new FakeHttpHandler();
        handler.Responses["https://openapi.naver.com/v1/search/image.json"] = Json("""
            {"items":[{"title":"이미지 1","link":"https://img.example/full.png","thumbnail":"https://img.example/thumb.png"}]}
            """);
        handler.Responses["https://img.example/"] = ("image/png", image);
        var vm = NewVm();
        vm.Http = new HttpClient(handler);
        _dialogs.TextResults.Enqueue("keyword");
        _dialogs.ChooseResults.Enqueue(new[] { 0 });
        await vm.SearchImagesCommand.ExecuteAsync(null);

        Assert.NotNull(_dialogs.ChooseItems.Single()[0].Image); // 미리보기 그림을 보여줌
        Assert.Contains("NAVER", vm.InputText);
    }

    [AvaloniaFact]
    public async Task Naver_keys_are_asked_and_saved_when_not_bundled()
    {
        var handler = new FakeHttpHandler();
        handler.Responses["https://openapi.naver.com/"] = Json("""{"items":[]}""");
        var vm = NewVm();
        vm.BundledNaverKeys = () => null;
        vm.Http = new HttpClient(handler);
        _dialogs.TextResults.Enqueue("keyword"); // 검색어를 먼저 묻고
        _dialogs.TextResults.Enqueue("my-id");   // 키가 없으면 키를 물어봄
        _dialogs.TextResults.Enqueue("my-secret");
        await vm.SearchWebCommand.ExecuteAsync(null);

        Assert.Equal("my-id", handler.Requests.Single().Headers.GetValues("X-Naver-Client-Id").Single());
        Assert.Equal("검색 결과가 없습니다. 다른 키워드로 검색해보세요.", _dialogs.Messages.Single().Message);
        var saved = new SettingsStore(SettingsPath).Load();
        Assert.Equal("my-id", saved.NaverClientId);
        Assert.Equal("my-secret", saved.NaverClientSecret);
    }

    [AvaloniaFact]
    public async Task Rejected_keys_ask_for_new_keys_and_retry()
    {
        var handler = new FakeHttpHandler
        {
            // 앱에 들어 있는 키(test-id)는 거부, 새 키(new-id)는 성공
            Custom = r => r.Headers.GetValues("X-Naver-Client-Id").Single() == "test-id"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("""{"errorMessage":"NID AUTH Result Invalid (28) : Authentication failed.","errorCode":"024"}"""),
                }
                : null,
        };
        handler.Responses["https://openapi.naver.com/v1/search/webkr.json"] = Json("""{"items":[{"title":"결과","description":"설명"}]}""");
        var vm = NewVm();
        vm.Http = new HttpClient(handler);
        _dialogs.TextResults.Enqueue("검색어");
        _dialogs.ConfirmResults.Enqueue(true);      // 새 키를 입력할까요? → 예
        _dialogs.TextResults.Enqueue("new-id");
        _dialogs.TextResults.Enqueue("new-secret");
        _dialogs.ChooseResults.Enqueue(new[] { 0 });
        await vm.SearchWebCommand.ExecuteAsync(null);

        Assert.Contains("네이버가 API 키를 거부했습니다", _dialogs.Confirms.Single());
        Assert.Contains("Authentication failed", _dialogs.Confirms.Single());
        Assert.Equal("결과\n설명", vm.InputText);
        Assert.Equal(2, handler.Requests.Count); // 거부된 뒤 새 키로 한 번 더

        // 다음부터는 저장한 키를 앱에 들어 있는 키보다 먼저 씀
        Assert.Equal("new-id", new SettingsStore(SettingsPath).Load().NaverClientId);
        _dialogs.TextResults.Enqueue("두 번째");
        _dialogs.ChooseResults.Enqueue(new[] { 0 });
        await vm.SearchWebCommand.ExecuteAsync(null);
        Assert.Equal("new-id", handler.Requests.Last().Headers.GetValues("X-Naver-Client-Id").Single());
    }

    [AvaloniaFact]
    public async Task Forbidden_and_network_errors_are_explained()
    {
        var handler = new FakeHttpHandler { Custom = _ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden) };
        var vm = NewVm();
        vm.Http = new HttpClient(handler);
        _dialogs.TextResults.Enqueue("검색어");
        await vm.SearchWebCommand.ExecuteAsync(null); // 새 키 입력은 "아니요"
        Assert.Contains("검색 API 를 쓸 권한이 없습니다", _dialogs.Confirms.Single());

        handler.Custom = _ => throw new HttpRequestException("network down");
        _dialogs.TextResults.Enqueue("검색어");
        await vm.SearchWebCommand.ExecuteAsync(null);
        Assert.Contains("네이버 검색에 실패했습니다", _dialogs.Messages.Last().Message);
    }

    // ---- 사용자 테마 -----------------------------------------------------------------

    private static readonly ThemeColors Brown = new(
        Color.FromRgb(153, 102, 0), Colors.White, Color.FromRgb(50, 50, 50), Color.FromRgb(0, 123, 255), Color.FromRgb(108, 117, 125));

    [AvaloniaFact]
    public async Task Custom_theme_can_be_created_applied_and_deleted()
    {
        var vm = NewVm();
        _dialogs.ThemeResults.Enqueue(Brown);
        _dialogs.TextResults.Enqueue("  brown  ");
        await vm.SaveAsNewThemeCommand.ExecuteAsync(null);

        Assert.Equal("brown", vm.Theme.Name);
        Assert.Equal(Color.FromRgb(153, 102, 0), vm.Theme.Background);
        Assert.Equal(Color.FromRgb(245, 245, 245), vm.Theme.OutputArea); // 입력창보다 10 어둡게 (Java 와 같음)
        Assert.True(vm.IsCustomTheme && !vm.IsLightTheme && !vm.IsDarkTheme);
        Assert.True(Assert.Single(vm.CustomThemeItems).IsSelected);

        // 앱을 다시 켜도 그 테마
        var reopened = new MainViewModel(new SettingsStore(SettingsPath), Application.Current);
        Assert.Equal("brown", reopened.Theme.Name);

        // 이름 규칙: LIGHT/DARK/CUSTOM 은 안 됨
        _dialogs.ThemeResults.Enqueue(Brown);
        _dialogs.TextResults.Enqueue("Dark");
        await vm.SaveAsNewThemeCommand.ExecuteAsync(null);
        Assert.Equal(MessageKind.Error, _dialogs.Messages.Last().Kind);
        Assert.Single(vm.CustomThemeItems);

        // 삭제 → 라이트 테마로
        _dialogs.ConfirmResults.Enqueue(true);
        await vm.DeleteThemeCommand.ExecuteAsync(null);
        Assert.Contains("'brown'", _dialogs.Confirms.Last());
        Assert.True(vm.IsLightTheme);
        Assert.Empty(vm.CustomThemeItems);
    }

    [AvaloniaFact]
    public async Task Theme_files_are_compatible_with_java()
    {
        // Java 버전이 내보낸 .theme 파일 (Properties 형식, 색은 부호 있는 ARGB 정수)
        File.WriteAllText(PathOf("java.theme"),
            "#Custom ArtBash Theme\n#Fri Jun 20 12:49:01 KST 2026\n" +
            "main.background=-6724096\ntextarea.background=-1\ntext.foreground=-13487566\n" +
            "button.primary=-16745473\nbutton.secondary=-9669251\n");
        var vm = NewVm();
        _dialogs.OpenResults.Enqueue(new[] { PathOf("java.theme") });
        _dialogs.TextResults.Enqueue("from-java");
        await vm.ImportThemeCommand.ExecuteAsync(null);
        Assert.Equal("from-java", vm.Theme.Name);
        Assert.Equal(Color.FromRgb(153, 102, 0), vm.Theme.Background); // -6724096 = #996600

        // 내보낸 파일을 다시 읽으면 같은 색, 형식도 Java 와 같음
        _dialogs.SaveResults.Enqueue(PathOf("export"));
        await vm.ExportThemeCommand.ExecuteAsync(null);
        var text = File.ReadAllText(PathOf("export.theme"));
        Assert.StartsWith("#Custom ArtBash Theme", text);
        Assert.Contains("main.background=-6724096", text);
        Assert.Equal(ThemeFile.Read(PathOf("java.theme")), ThemeFile.Read(PathOf("export.theme")));

        // 깨진 파일은 안내
        File.WriteAllText(PathOf("broken.theme"), "main.background=abc");
        _dialogs.OpenResults.Enqueue(new[] { PathOf("broken.theme") });
        await vm.ImportThemeCommand.ExecuteAsync(null);
        Assert.Equal(MessageKind.Error, _dialogs.Messages.Last().Kind);
    }

    // ---- 정보 / 스플래시 ---------------------------------------------------------------

    [AvaloniaFact]
    public async Task About_shows_readme_and_the_easter_egg_link()
    {
        var vm = NewVm();
        await vm.ShowAboutCommand.ExecuteAsync(null);
        Assert.Contains("Made By June", _dialogs.AboutText);
        _dialogs.AboutClick!();
        Assert.Equal("https://www.youtube.com/watch?v=2yJgwwDcgV8", Assert.Single(_dialogs.OpenedUrls));
    }

    [AvaloniaFact]
    public async Task Splash_fills_the_progress_bar()
    {
        var splash = new SplashWindow();
        splash.Show();
        await splash.RunAsync();
        Assert.True(File.Exists(Snapshot.Save(splash, "stage4-splash")));
        splash.Close();
    }
}
