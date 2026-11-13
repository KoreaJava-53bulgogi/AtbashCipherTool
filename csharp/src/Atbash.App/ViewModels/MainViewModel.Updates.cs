using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Atbash.App.Services;
using Atbash.Core.Updates;
using CommunityToolkit.Mvvm.Input;

namespace Atbash.App.ViewModels;

/// <summary>
/// 업데이트 (5단계): 하루 한 번 자동 확인 + 도움말 → 업데이트 확인, [지금 업데이트]로 자동 설치.
/// 동작은 Java 8.1.0 과 같음.
/// </summary>
public partial class MainViewModel
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    /// <summary>지금 시각 (테스트에서 바꿈)</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>설치 프로그램 실행 (테스트에서 가짜로)</summary>
    public Action<string> LaunchInstaller { get; set; } = UpdateDownloader.LaunchInstaller;

    /// <summary>앱 종료 (창 쪽에서 넣어 줌)</summary>
    public Action? RequestShutdown { get; set; }

    /// <summary>도움말 → 업데이트 확인: 결과를 항상 알려줌</summary>
    [RelayCommand]
    private Task CheckForUpdatesAsync() => CheckUpdatesAsync(manual: true);

    /// <summary>시작할 때: 하루 한 번, 새 버전이 있을 때만 알림</summary>
    public Task AutoCheckForUpdatesAsync() => CheckUpdatesAsync(manual: false);

    private async Task CheckUpdatesAsync(bool manual)
    {
        if (Dialogs == null) return;
        long now = Now().ToUnixTimeMilliseconds();
        if (!manual && now - _settings.LastUpdateCheck < (long)CheckInterval.TotalMilliseconds) return;

        UpdateInfo info;
        try
        {
            info = await new UpdateDownloader(Http).FetchLatestAsync();
        }
        catch (Exception e)
        {
            // 자동 확인 실패(오프라인 등)는 조용히 넘어감
            if (manual)
                await Dialogs.ShowMessageAsync(L["CHECK_UPDATE_MENU"], L["UPDATE_CHECK_FAILED"] + "\n" + e.Message, MessageKind.Warning);
            return;
        }
        // 확인 기록은 "설정 저장"과 상관없이 저장 (Java 와 같음)
        _settings.LastUpdateCheck = now;
        _store.Save(_settings);

        bool newer = UpdateInfo.CompareVersions(info.Version, Version) > 0;
        bool skipped = info.Version == _settings.SkippedVersion;
        if (!newer || (!manual && skipped))
        {
            if (manual)
                await Dialogs.ShowMessageAsync(L["CHECK_UPDATE_MENU"], L["UPDATE_LATEST_MESSAGE"].Replace("{0}", Version));
            return;
        }

        var message = L["UPDATE_AVAILABLE_MESSAGE"].Replace("{0}", info.Version).Replace("{1}", Version);
        if (info.Notes.Length > 0) message += "\n\n" + info.Notes;
        var install = L["UPDATE_INSTALL_NOW"];
        var download = L["UPDATE_DOWNLOAD"];
        var later = L["UPDATE_LATER"];
        var skip = L["UPDATE_SKIP"];
        var buttons = info.CanAutoInstall ? new[] { install, download, later, skip } : new[] { download, later, skip };
        var picked = buttons[await Dialogs.ShowOptionsAsync(L["UPDATE_AVAILABLE_TITLE"], message, MessageKind.Info, buttons)];

        if (picked == install) await InstallUpdateAsync(info);
        else if (picked == download) await Dialogs.OpenUrlAsync(info.DownloadUrl);
        else if (picked == skip)
        {
            _settings.SkippedVersion = info.Version;
            _store.Save(_settings);
        }
    }

    /// <summary>내려받기(진행률) → 지문 확인 → 설치 프로그램 실행 → 앱 종료. 실패하면 다운로드 페이지 안내</summary>
    private async Task InstallUpdateAsync(UpdateInfo info)
    {
        var dialogs = Dialogs!;
        using var cancel = new CancellationTokenSource();
        var progressWindow = dialogs.OpenProgress(L["UPDATE_AVAILABLE_TITLE"],
            L["UPDATE_DOWNLOADING"].Replace("{0}", info.Version), L["UPDATE_CANCEL"], () => cancel.Cancel());

        string installer;
        try
        {
            var progress = new Progress<(long Done, long Total)>(p => progressWindow.Update(p.Done, p.Total));
            installer = await new UpdateDownloader(Http).DownloadAsync(info.InstallerUrl!, progress, cancel.Token);
            await Task.Run(() => UpdateDownloader.Verify(installer, info.Sha256!));
        }
        catch (OperationCanceledException)
        {
            return; // [취소]
        }
        catch (VerificationException)
        {
            await OfferManualDownloadAsync(info, L["UPDATE_VERIFY_FAILED"]);
            return;
        }
        catch (Exception e) when (e is IOException or System.Net.Http.HttpRequestException or UnauthorizedAccessException)
        {
            await OfferManualDownloadAsync(info, L["UPDATE_DOWNLOAD_FAILED"]);
            return;
        }
        finally
        {
            progressWindow.Close();
        }

        var ready = L["UPDATE_READY_MESSAGE"].Replace("{0}", info.Version);
        if (await dialogs.ShowOptionsAsync(L["UPDATE_AVAILABLE_TITLE"], ready, MessageKind.Info, L["BUTTON_OK"], L["BUTTON_CANCEL"]) != 0)
            return;

        try
        {
            LaunchInstaller(installer);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            await OfferManualDownloadAsync(info, L["UPDATE_LAUNCH_FAILED"]);
            return;
        }
        // 설치 프로그램이 앱 파일을 바꿀 수 있도록 종료 (창 크기는 창이 닫힐 때 저장됨)
        RequestShutdown?.Invoke();
    }

    private async Task OfferManualDownloadAsync(UpdateInfo info, string reason)
    {
        if (await Dialogs!.ConfirmAsync(L["UPDATE_AVAILABLE_TITLE"], reason + "\n\n" + L["UPDATE_OPEN_PAGE_INSTEAD"], MessageKind.Warning))
            await Dialogs.OpenUrlAsync(info.DownloadUrl);
    }
}
