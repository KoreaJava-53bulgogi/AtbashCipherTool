using System.Diagnostics;
using Atbash.App.Services;
using Atbash.App.ViewModels;
using Atbash.Core.Crypto;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Atbash.App.Tests;

/// <summary>3단계 파일 기능. 파일 선택·비밀번호·확인 창은 가짜(FakeDialogs)로 대신함</summary>
public class FileFeatureTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("atbash-ui-files-").FullName;
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeClipboard _clipboard = new();

    public void Dispose() => Directory.Delete(_dir, true);

    private MainViewModel NewVm() =>
        new(new SettingsStore(Path.Combine(_dir, "settings.json")), Application.Current)
        {
            Dialogs = _dialogs,
            Clipboard = _clipboard,
        };

    private string PathOf(string name) => Path.Combine(_dir, name);

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 20000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("condition not met in time");
            await Task.Delay(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>OCR 시험용 이미지: 앱의 "이미지로 저장" 기능으로 글자를 그림</summary>
    private static byte[] TextImage(string text) =>
        TextImageExporter.RenderPng(text, Colors.White, Colors.Black, fontSize: 36);

    // ---- 불러오기 / 저장 -------------------------------------------------------------

    [AvaloniaFact]
    public async Task Load_and_save_utf8_text()
    {
        var vm = NewVm();
        File.WriteAllText(PathOf("in.txt"), "안녕하세요\nHello");
        _dialogs.OpenResults.Enqueue(new[] { PathOf("in.txt") });
        vm.OutputText = "old";
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal("안녕하세요\nHello", vm.InputText);
        Assert.Equal("", vm.OutputText);

        vm.OutputText = "결과 😀";
        _dialogs.SaveResults.Enqueue(PathOf("out.txt"));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("결과 😀", File.ReadAllText(PathOf("out.txt")));
    }

    [AvaloniaFact]
    public async Task Loading_non_utf8_shows_an_error_instead_of_garbage()
    {
        var vm = NewVm();
        File.WriteAllBytes(PathOf("latin1.txt"), new byte[] { 0x63, 0x61, 0x66, 0xE9 });
        _dialogs.OpenResults.Enqueue(new[] { PathOf("latin1.txt") });
        vm.InputText = "keep";
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal("keep", vm.InputText);
        Assert.Equal(MessageKind.Error, Assert.Single(_dialogs.Messages).Kind);
    }

    [AvaloniaFact]
    public async Task Save_as_image_writes_a_png_in_theme_colors()
    {
        var vm = NewVm();
        await vm.SaveImageCommand.ExecuteAsync(null); // 결과가 비어 있으면 안내만
        Assert.Equal(MessageKind.Warning, Assert.Single(_dialogs.Messages).Kind);

        vm.OutputText = "첫 줄 line one\n둘째 줄이 더 깁니다 second line";
        _dialogs.SaveResults.Enqueue(PathOf("result")); // 확장자가 없으면 .png 를 붙임
        await vm.SaveImageCommand.ExecuteAsync(null);

        using var bitmap = new Bitmap(PathOf("result.png"));
        Assert.True(bitmap.PixelSize.Width > 200 && bitmap.PixelSize.Height > 40);
    }

    // ---- OCR ---------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Ocr_reads_korean_and_english_from_an_image_file()
    {
        var vm = NewVm();
        File.WriteAllBytes(PathOf("text.png"), TextImage("HELLO 2026 안녕하세요"));
        _dialogs.OpenResults.Enqueue(new[] { PathOf("text.png") });
        await vm.OcrFromFileCommand.ExecuteAsync(null);

        Assert.Contains("HELLO", vm.InputText);
        Assert.Contains("2026", vm.InputText);
        Assert.Contains("안녕하세요", vm.InputText.Replace(" ", ""));
        Assert.Equal("텍스트를 읽었습니다!", vm.FeedbackText);
    }

    [AvaloniaFact]
    public async Task Clipboard_image_is_recognized_and_text_paste_is_left_alone()
    {
        var vm = NewVm();
        _clipboard.ImagePng = TextImage("CLIPBOARD 77");
        Assert.True(await vm.PasteImageIfAnyAsync());
        Assert.Contains("CLIPBOARD", vm.InputText);

        // 글자도 함께 있으면(웹페이지 복사 등) OCR 하지 않고 평소처럼 붙여넣게 함
        _clipboard.Text = "plain text";
        Assert.False(await vm.PasteImageIfAnyAsync());

        // 이미지가 없으면 메뉴에서 눌렀을 때 안내
        _clipboard.Text = null;
        _clipboard.ImagePng = null;
        await vm.OcrFromClipboardCommand.ExecuteAsync(null);
        Assert.Contains("Win+Shift+S", Assert.Single(_dialogs.Messages).Message);
    }

    // ---- 암호화 / 복호화 ----------------------------------------------------------------

    [AvaloniaFact]
    public async Task Encrypt_then_wipe_then_decrypt()
    {
        var vm = NewVm();
        var a = PathOf("a.txt");
        var b = PathOf("b.bin");
        File.WriteAllText(a, "첫 번째 파일");
        File.WriteAllBytes(b, new byte[] { 1, 2, 3, 0, 255 });

        _dialogs.OpenResults.Enqueue(new[] { a, b });
        _dialogs.PasswordResults.Enqueue("");        // 비워 두면 다시 물어봄
        _dialogs.PasswordResults.Enqueue("pw-1234");
        _dialogs.ConfirmResults.Enqueue(true);       // 원본 안전 삭제: 예
        await vm.EncryptFilesCommand.ExecuteAsync(null);

        Assert.True(File.Exists(a + ".enc") && File.Exists(b + ".enc"));
        Assert.False(File.Exists(a) || File.Exists(b)); // 원본은 지워짐
        Assert.Contains(_dialogs.Messages, m => m.Message == "비밀번호를 입력하세요.");

        _dialogs.Messages.Clear();
        _dialogs.OpenResults.Enqueue(new[] { a + ".enc", b + ".enc" });
        _dialogs.PasswordResults.Enqueue("pw-1234");
        await vm.DecryptFilesCommand.ExecuteAsync(null);

        Assert.Equal("첫 번째 파일", File.ReadAllText(a));
        Assert.Equal(new byte[] { 1, 2, 3, 0, 255 }, File.ReadAllBytes(b));
        Assert.Contains(a, _dialogs.Messages.Last().Message);
    }

    [AvaloniaFact]
    public async Task Wrong_password_and_overwrite_question()
    {
        var vm = NewVm();
        var plain = PathOf("doc.txt");
        File.WriteAllText(plain, "original");
        AesCipher.EncryptFile(plain, plain + ".enc", "right");

        // 같은 이름 파일이 있음 → 덮어쓸지 물어보고, "아니요"면 아무것도 안 함
        _dialogs.PasswordResults.Enqueue("right");
        _dialogs.ConfirmResults.Enqueue(false);
        await vm.DecryptFilesAsync(new[] { plain + ".enc" });
        Assert.Contains("'doc.txt'", Assert.Single(_dialogs.Confirms)); // ''doc.txt'' 처럼 따옴표가 두 개씩 나오지 않음
        Assert.Empty(_dialogs.Messages);

        File.Delete(plain);
        _dialogs.PasswordResults.Enqueue("wrong");
        await vm.DecryptFilesAsync(new[] { plain + ".enc" });
        Assert.Equal("복호화하지 못했습니다. 비밀번호가 틀렸거나 파일이 손상되었습니다.", _dialogs.Messages.Single().Message);
        Assert.False(File.Exists(plain)); // 틀린 비밀번호로는 파일을 만들지 않음
    }

    // ---- 드래그 앤 드롭 ----------------------------------------------------------------

    [AvaloniaFact]
    public async Task Dropped_files_are_handled_by_type()
    {
        var vm = NewVm();

        // 텍스트 파일 → 불러오기
        File.WriteAllText(PathOf("note.txt"), "드롭 테스트");
        await vm.HandleDroppedFilesAsync(new[] { PathOf("note.txt") });
        Assert.Equal("드롭 테스트", vm.InputText);

        // 이미지 → OCR
        File.WriteAllBytes(PathOf("shot.png"), TextImage("DROPPED IMAGE"));
        await vm.HandleDroppedFilesAsync(new[] { PathOf("shot.png") });
        Assert.Contains("DROPPED", vm.InputText);

        // 그 밖의 파일 → 암호화할지 물어봄 (아니요)
        File.WriteAllBytes(PathOf("app.exe"), new byte[] { 0x4D, 0x5A, 0, 0 });
        await vm.HandleDroppedFilesAsync(new[] { PathOf("app.exe") });
        Assert.Contains("1개", _dialogs.Confirms.Last());
        Assert.False(File.Exists(PathOf("app.exe.enc")));

        // .enc → 복호화
        AesCipher.EncryptFile(PathOf("note.txt"), PathOf("secret.txt.enc"), "pw");
        _dialogs.PasswordResults.Enqueue("pw");
        await vm.HandleDroppedFilesAsync(new[] { PathOf("secret.txt.enc") });
        Assert.Equal("드롭 테스트", File.ReadAllText(PathOf("secret.txt")));
    }
}
