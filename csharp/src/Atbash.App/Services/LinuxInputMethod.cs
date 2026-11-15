using System;
using System.Diagnostics;

namespace Atbash.App.Services;

/// <summary>
/// Linux 에서 한글 입력기(fcitx5 / ibus)에 연결.
/// Avalonia 는 환경 변수(AVALONIA_IM_MODULE, GTK_IM_MODULE, QT_IM_MODULE, XMODIFIERS)를 보고 입력기를 고르는데,
/// KDE Plasma 6 (Wayland) 에서 "가상 키보드 → Fcitx 5" 로 설정하면 KDE 가 이 변수들을 일부러 비워 둠.
/// 그러면 다른 앱에서는 한글이 되는데 이 앱에서만 안 됨 → 실행 중인 입력기를 찾아서 알려 줌.
/// </summary>
public static class LinuxInputMethod
{
    public const string Variable = "AVALONIA_IM_MODULE";

    /// <summary>앱이 켜질 때 (Avalonia 를 시작하기 전에) 호출</summary>
    public static void Configure()
    {
        if (!OperatingSystem.IsLinux()) return;
        var module = Choose(Environment.GetEnvironmentVariable, IsRunning);
        if (module != null) Environment.SetEnvironmentVariable(Variable, module);
    }

    /// <summary>
    /// 알려 줄 입력기 이름 ("fcitx" / "ibus"). 이미 설정되어 있거나 실행 중인 입력기가 없으면 null (그대로 둠).
    /// </summary>
    public static string? Choose(Func<string, string?> env, Func<string, bool> isRunning)
    {
        // 사용자가 직접 정한 것은 그대로 존중
        if (!string.IsNullOrEmpty(env(Variable))) return null;
        foreach (var name in new[] { "GTK_IM_MODULE", "QT_IM_MODULE", "XMODIFIERS" })
        {
            var value = env(name) ?? "";
            if (value.Contains("fcitx", StringComparison.OrdinalIgnoreCase) || value.Contains("ibus", StringComparison.OrdinalIgnoreCase))
                return null; // Avalonia 가 이것을 보고 알아서 연결함
        }

        if (isRunning("fcitx5") || isRunning("fcitx")) return "fcitx";
        if (isRunning("ibus-daemon")) return "ibus";
        return null;
    }

    private static bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }
}
