using System.Runtime.InteropServices;

namespace Atbash.Core.Updates;

/// <summary>
/// 배포 대상 플랫폼 (운영체제 + CPU).
/// 아이디는 version.json 의 platforms 키이고, 태그 접두사는 GitHub 릴리스 태그의 이름 규칙임:
///   Windows x64 : 9.1.0     (접두사 없음)
///   macOS arm64 : a_9.1.0
///   Linux x64   : l_9.1.0
///   경량판(Lite, Windows x64, Rust 로 만든 별도 앱): lite_9.1.0 — 버전 번호는 정식 버전과 맞춤
/// Windows ARM64 처럼 아직 접두사를 정하지 않은 대상은 정해지면 여기에 추가함.
/// </summary>
public sealed record PlatformInfo(string Id, string DisplayName, string? TagPrefix, bool SupportsAutoInstall)
{
    public static readonly PlatformInfo WindowsX64 = new("win-x64", "Windows (x64)", "", SupportsAutoInstall: true);
    public static readonly PlatformInfo MacArm64 = new("osx-arm64", "macOS (Apple Silicon)", "a_", SupportsAutoInstall: false);
    public static readonly PlatformInfo LinuxX64 = new("linux-x64", "Linux (x64)", "l_", SupportsAutoInstall: false);

    /// <summary>경량판. 정식 앱은 이 항목을 읽지 않음 (lite/ 의 Rust 앱만 읽음). 업데이트는 알림만</summary>
    public static readonly PlatformInfo LiteWindowsX64 = new("win-x64-lite", "Windows Lite (x64)", "lite_", SupportsAutoInstall: false);

    /// <summary>태그 규칙이 정해진, 실제로 배포하는 플랫폼</summary>
    public static IReadOnlyList<PlatformInfo> Released { get; } = new[] { WindowsX64, MacArm64, LinuxX64, LiteWindowsX64 };

    public static PlatformInfo? ById(string id) => Released.FirstOrDefault(p => p.Id == id);

    /// <summary>지금 실행 중인 플랫폼</summary>
    public static PlatformInfo Current { get; } = For(CurrentOs(), RuntimeInformation.ProcessArchitecture);

    /// <summary>운영체제("win", "osx", "linux")와 CPU 로 플랫폼을 찾음. 배포하지 않는 조합은 태그 접두사가 없는 플랫폼으로 돌려줌</summary>
    public static PlatformInfo For(string os, Architecture architecture)
    {
        var id = $"{os}-{architecture.ToString().ToLowerInvariant()}";
        return ById(id) ?? new PlatformInfo(id, id, TagPrefix: null, SupportsAutoInstall: false);
    }

    private static string CurrentOs() =>
        OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsLinux() ? "linux" : "other";
}
