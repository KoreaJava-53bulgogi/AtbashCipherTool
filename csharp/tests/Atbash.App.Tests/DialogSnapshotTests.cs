using Atbash.App.Services;
using Atbash.App.ViewModels;
using Atbash.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Atbash.App.Tests;

/// <summary>실제 앱의 창(비밀번호 입력, 메시지)이 제대로 보이는지 이미지로 확인</summary>
public class DialogSnapshotTests
{
    private static (MainWindow owner, AvaloniaDialogs dialogs) Setup()
    {
        var path = Path.Combine(Path.GetTempPath(), "atbash-dialog-" + Guid.NewGuid().ToString("N"), "s.json");
        var vm = new MainViewModel(new SettingsStore(path), Application.Current);
        var owner = new MainWindow { DataContext = vm };
        owner.Show();
        return (owner, new AvaloniaDialogs(owner, () => vm.L));
    }

    private static Window OpenedDialog(Window owner) =>
        owner.OwnedWindows.Single();

    [AvaloniaFact]
    public void Password_dialog_shows_strength()
    {
        var (owner, dialogs) = Setup();
        var task = dialogs.PromptPasswordAsync("파일 암호화", "파일 암호화에 사용할 비밀번호를 입력하세요:");
        Dispatcher.UIThread.RunJobs();

        var dialog = OpenedDialog(owner);
        var box = dialog.GetVisualDescendants().OfType<TextBox>().Single();
        box.Text = "Abcdef1!";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "비밀번호 강도: 강함");
        Snapshot.Save(dialog, "stage3-password-dialog");

        dialog.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompleted);
        Assert.Null(task.Result); // 확인을 누르지 않고 닫으면 취소
    }

    [AvaloniaFact]
    public void Stage4_windows_render()
    {
        var (owner, dialogs) = Setup();

        // 변환 기록
        var entries = new List<HistoryEntry>
        {
            new(DateTime.Now, 18, "한글 자모 암호 (암호화)", 3, "안녕 Hello", "첃뢐 Hello"),
            new(DateTime.Now.AddMinutes(-1), 11, "Base64 (인코딩)", 0, "Hello World 이것은 꽤 긴 문장입니다. 미리보기에서 잘려야 합니다 ...", "SGVsbG8gV29ybGQ="),
        };
        _ = dialogs.ShowHistoryAsync(entries, _ => Task.CompletedTask, () => { });
        Dispatcher.UIThread.RunJobs();
        var w = OpenedDialog(owner);
        Snapshot.Save(w, "stage4-history");
        w.Close();
        Dispatcher.UIThread.RunJobs();

        // 테마 색 고르기
        _ = dialogs.EditThemeColorsAsync("테마 사용자 정의", ThemeColors.FromTheme(AppTheme.Light));
        Dispatcher.UIThread.RunJobs();
        w = OpenedDialog(owner);
        Snapshot.Save(w, "stage4-theme-editor");
        w.Close();
        Dispatcher.UIThread.RunJobs();

        // QR 미리보기
        _ = dialogs.ShowImageAsync("QR 코드로 저장...", QrCodeService.CreatePng("안녕하세요"), "PNG로 저장", "닫기");
        Dispatcher.UIThread.RunJobs();
        w = OpenedDialog(owner);
        Snapshot.Save(w, "stage4-qr");
        w.Close();
        Dispatcher.UIThread.RunJobs();

    }

    [AvaloniaFact]
    public void Confirm_dialog_uses_localized_buttons()
    {
        var (owner, dialogs) = Setup();
        var task = dialogs.ConfirmAsync("안전 삭제", "암호화가 완료되었습니다. 원본 파일을 복구 불가능하게 삭제하시겠습니까?", MessageKind.Warning);
        Dispatcher.UIThread.RunJobs();

        var dialog = OpenedDialog(owner);
        var buttons = dialog.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
        Assert.Equal(new[] { "예", "아니요" }, buttons);
        Snapshot.Save(dialog, "stage3-confirm-dialog");

        dialog.GetVisualDescendants().OfType<Button>().First().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompleted && task.Result);
    }
}
