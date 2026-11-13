using Atbash.App.Services;
using Atbash.App.ViewModels;
using Atbash.Core.Updates;
using Avalonia;
using Avalonia.Headless.XUnit;

namespace Atbash.App.Tests;

/// <summary>
/// 업데이트 (5단계). ATBASH_UPDATE_URL 로 내 PC 의 시험용 version.json 을 읽게 해서
/// 실제 GitHub 나 실제 사용자에게는 영향이 없음. 설치 프로그램 실행은 가짜로 대신함.
/// </summary>
[Collection("update-env")] // 환경 변수를 쓰므로 다른 테스트와 동시에 돌지 않게
public class UpdateTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("atbash-update-test-").FullName;
    private readonly FakeDialogs _dialogs = new();
    private readonly List<string> _launched = new();
    private int _shutdowns;

    public UpdateTests() => Environment.SetEnvironmentVariable(UpdateDownloader.TestUrlEnv, new Uri(Path.Combine(_dir, "version.json")).AbsoluteUri);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(UpdateDownloader.TestUrlEnv, null);
        Directory.Delete(_dir, true);
    }

    private MainViewModel NewVm(long lastCheck = 0) => new(new SettingsStore(Path.Combine(_dir, "settings.json")), Application.Current)
    {
        Dialogs = _dialogs,
        LaunchInstaller = p => _launched.Add(p),
        RequestShutdown = () => _shutdowns++,
        Now = () => DateTimeOffset.FromUnixTimeMilliseconds(lastCheck + 1),
    };

    /// <summary>시험용 "새 버전 설치 파일"과 version.json 을 만듦</summary>
    private byte[] Publish(string version, string? sha = null, bool withInstaller = true, string? installerUrl = null)
    {
        var installer = Path.Combine(_dir, $"Setup_{version}.exe");
        var bytes = new byte[300_000];
        new Random(1).NextBytes(bytes);
        File.WriteAllBytes(installer, bytes);
        sha ??= UpdateDownloader.Sha256(installer);
        installerUrl ??= new Uri(installer).AbsoluteUri;
        var json = withInstaller
            ? $$"""{"version":"{{version}}","url":"https://example.invalid/releases","installer":"{{installerUrl}}","sha256":"{{sha}}","notes":"테스트 업데이트"}"""
            : $$"""{"version":"{{version}}","notes":"옛 형식"}""";
        File.WriteAllText(Path.Combine(_dir, "version.json"), json);
        return bytes;
    }

    [AvaloniaFact]
    public async Task Update_now_downloads_verifies_runs_the_installer_and_quits()
    {
        var bytes = Publish("9.9.9");
        var vm = NewVm();
        _dialogs.OptionResults.Enqueue(0); // [지금 업데이트]
        _dialogs.OptionResults.Enqueue(0); // 설치 준비 완료 → [확인]
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        var (message, buttons) = _dialogs.Options[0];
        Assert.Contains("새 버전 9.9.9", message);
        Assert.Contains("테스트 업데이트", message);
        Assert.Equal(new[] { "지금 업데이트", "다운로드 페이지 열기", "나중에", "이 버전 건너뛰기" }, buttons);

        var progress = Assert.Single(_dialogs.Progresses);
        Assert.True(progress.Closed);
        Assert.Equal(300_000, progress.Updates.Last().Done);

        var installer = Assert.Single(_launched);
        Assert.Equal(bytes, File.ReadAllBytes(installer)); // 내려받은 파일 그대로
        Assert.Equal(1, _shutdowns);                      // 설치를 위해 앱 종료
        Directory.Delete(Path.GetDirectoryName(installer)!, true);
    }

    [AvaloniaFact]
    public async Task Wrong_fingerprint_is_never_installed()
    {
        Publish("9.9.9", sha: new string('0', 64));
        var vm = NewVm();
        _dialogs.OptionResults.Enqueue(0); // [지금 업데이트]
        _dialogs.ConfirmResults.Enqueue(true); // 다운로드 페이지를 열까요? → 예
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Contains("파일 검사 실패", _dialogs.Confirms.Single());
        Assert.Empty(_launched);
        Assert.Equal(0, _shutdowns);
        Assert.Equal("https://example.invalid/releases", _dialogs.OpenedUrls.Single());
    }

    [AvaloniaFact]
    public async Task Only_https_downloads_are_allowed_outside_test_files()
    {
        Publish("9.9.9", installerUrl: "http://insecure.example/Setup_9.9.9.exe");
        var vm = NewVm();
        _dialogs.OptionResults.Enqueue(0);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Contains("내려받지 못했습니다", _dialogs.Confirms.Single());
        Assert.Empty(_launched);
    }

    [AvaloniaFact]
    public async Task Latest_version_and_old_format()
    {
        Publish(MainViewModel.Version);
        var vm = NewVm();
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal($"최신 버전({MainViewModel.Version})을 사용하고 있습니다.", _dialogs.Messages.Single().Message);

        // version.json 에 설치 파일이 없으면 [지금 업데이트] 없이 안내만
        Publish("9.9.9", withInstaller: false);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "다운로드 페이지 열기", "나중에", "이 버전 건너뛰기" }, _dialogs.Options.Single().Buttons);
    }

    [AvaloniaFact]
    public async Task Automatic_check_runs_once_a_day_and_respects_skip()
    {
        Publish("9.9.9");
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var day = (long)TimeSpan.FromDays(1).TotalMilliseconds;
        long now = 10 * day;
        MainViewModel Vm() => new(store, Application.Current)
        {
            Dialogs = _dialogs, LaunchInstaller = p => _launched.Add(p), Now = () => DateTimeOffset.FromUnixTimeMilliseconds(now),
        };

        _dialogs.OptionResults.Enqueue(3); // [이 버전 건너뛰기]
        await Vm().AutoCheckForUpdatesAsync();
        Assert.Single(_dialogs.Options);
        Assert.Equal("9.9.9", store.Load().SkippedVersion);

        now += day / 2; // 하루가 안 지남 → 확인 안 함
        await Vm().AutoCheckForUpdatesAsync();
        now += day;     // 하루 지남 → 확인하지만 건너뛴 버전이라 알리지 않음
        await Vm().AutoCheckForUpdatesAsync();
        Assert.Single(_dialogs.Options);

        await Vm().CheckForUpdatesCommand.ExecuteAsync(null); // 직접 누르면 건너뛴 버전도 알려줌
        Assert.Equal(2, _dialogs.Options.Count);

        Publish("9.9.10"); // 더 새 버전은 다시 알려줌
        now += 2 * day;
        await Vm().AutoCheckForUpdatesAsync();
        Assert.Equal(3, _dialogs.Options.Count);
    }

    [AvaloniaFact]
    public async Task Offline_automatic_check_is_silent()
    {
        Environment.SetEnvironmentVariable(UpdateDownloader.TestUrlEnv, new Uri(Path.Combine(_dir, "missing.json")).AbsoluteUri);
        var vm = NewVm();
        await vm.AutoCheckForUpdatesAsync();
        Assert.Empty(_dialogs.Messages);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Contains("업데이트 정보를 가져오지 못했습니다", _dialogs.Messages.Single().Message);
    }
}
