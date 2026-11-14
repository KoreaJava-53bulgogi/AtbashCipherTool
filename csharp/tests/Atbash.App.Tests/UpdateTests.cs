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

    // ---- 플랫폼별 version.json (태그 구조) -----------------------------------------------------

    /// <summary>Windows(맨 위 항목) + Mac/Linux 항목이 함께 든 version.json 을 만듦. Windows 는 앱 자신의 버전과 같게(= 최신)</summary>
    private (string MacTagPage, string MacInstaller) PublishMultiPlatform(string windowsVersion, string macVersion = "99.0.0", string linuxVersion = "98.0.0")
    {
        var mac = new PlatformEntry(macVersion, "a_" + macVersion, null,
            new Uri(Path.Combine(_dir, "App.dmg")).AbsoluteUri, new string('a', 64), "맥 안내");
        var linux = new PlatformEntry(linuxVersion, "l_" + linuxVersion, null,
            new Uri(Path.Combine(_dir, "App.AppImage")).AbsoluteUri, new string('b', 64), "리눅스 안내");
        var windows = new PlatformEntry(windowsVersion, windowsVersion, null,
            new Uri(Path.Combine(_dir, $"Setup_{windowsVersion}.exe")).AbsoluteUri, new string('c', 64), "윈도우 안내");
        var json = UpdateManifest.Empty.With(PlatformInfo.WindowsX64, windows)
            .With(PlatformInfo.MacArm64, mac).With(PlatformInfo.LinuxX64, linux).ToJson();
        File.WriteAllText(Path.Combine(_dir, "version.json"), json);
        return (ReleaseTags.TagPage("a_" + macVersion), mac.Installer!);
    }

    [AvaloniaFact]
    public async Task Mac_app_reads_its_own_entry_and_only_offers_the_download_page()
    {
        var (macPage, _) = PublishMultiPlatform(windowsVersion: MainViewModel.Version);
        var vm = NewVm();
        vm.Platform = PlatformInfo.MacArm64;
        _dialogs.OptionResults.Enqueue(0); // 첫 버튼 = [다운로드 페이지 열기]
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        var (message, buttons) = _dialogs.Options.Single();
        Assert.Contains("새 버전 99.0.0", message);
        Assert.Contains("맥 안내", message);
        // 설치 파일 주소와 지문이 있어도 macOS 에서는 [지금 업데이트]를 제안하지 않음
        Assert.Equal(new[] { "다운로드 페이지 열기", "나중에", "이 버전 건너뛰기" }, buttons);
        Assert.Equal(macPage, _dialogs.OpenedUrls.Single());   // 그 태그(a_99.0.0)의 릴리스 페이지
        Assert.Empty(_launched);
        Assert.Equal(0, _shutdowns);
    }

    [AvaloniaFact]
    public async Task Linux_app_reads_the_linux_entry()
    {
        PublishMultiPlatform(windowsVersion: MainViewModel.Version);
        var vm = NewVm();
        vm.Platform = PlatformInfo.LinuxX64;
        _dialogs.OptionResults.Enqueue(0);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Contains("새 버전 98.0.0", _dialogs.Options.Single().Message);
        Assert.Equal(ReleaseTags.TagPage("l_98.0.0"), _dialogs.OpenedUrls.Single());
    }

    [AvaloniaFact]
    public async Task Windows_app_ignores_newer_mac_and_linux_versions()
    {
        PublishMultiPlatform(windowsVersion: MainViewModel.Version); // Windows 는 최신, Mac/Linux 는 훨씬 새 버전
        var vm = NewVm();
        vm.Platform = PlatformInfo.WindowsX64;
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Empty(_dialogs.Options);
        Assert.Equal($"최신 버전({MainViewModel.Version})을 사용하고 있습니다.", _dialogs.Messages.Single().Message);
    }

    [AvaloniaFact]
    public async Task Platform_without_an_entry_is_treated_as_up_to_date()
    {
        Publish("9.9.9", withInstaller: true); // 예전 형식: Windows 항목만 있음
        var vm = NewVm();
        vm.Platform = PlatformInfo.MacArm64;
        await vm.AutoCheckForUpdatesAsync();                       // 자동 확인: 조용히
        Assert.Empty(_dialogs.Options);
        Assert.Empty(_dialogs.Messages);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);        // 직접 확인: "최신 버전" 안내
        Assert.Empty(_dialogs.Options);
        Assert.Contains("최신 버전", _dialogs.Messages.Single().Message);
    }

    [AvaloniaFact]
    public async Task Windows_still_updates_automatically_from_a_multi_platform_file()
    {
        var installer = Path.Combine(_dir, "Setup_9.9.9.exe");
        var bytes = new byte[100_000];
        new Random(2).NextBytes(bytes);
        File.WriteAllBytes(installer, bytes);
        var windows = new PlatformEntry("9.9.9", "9.9.9", null, new Uri(installer).AbsoluteUri, UpdateDownloader.Sha256(installer), "윈도우");
        File.WriteAllText(Path.Combine(_dir, "version.json"), UpdateManifest.Empty
            .With(PlatformInfo.WindowsX64, windows)
            .With(PlatformInfo.MacArm64, new PlatformEntry("99.0.0", "a_99.0.0", null, null, null, "맥")).ToJson());

        var vm = NewVm();
        vm.Platform = PlatformInfo.WindowsX64;
        _dialogs.OptionResults.Enqueue(0); // [지금 업데이트]
        _dialogs.OptionResults.Enqueue(0); // [확인]
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Contains("지금 업데이트", _dialogs.Options[0].Buttons);
        Assert.Equal(bytes, File.ReadAllBytes(Assert.Single(_launched)));
        Assert.Equal(1, _shutdowns);
        Directory.Delete(Path.GetDirectoryName(_launched[0])!, true);
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
