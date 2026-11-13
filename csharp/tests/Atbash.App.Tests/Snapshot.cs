using Avalonia.Controls;
using Avalonia.Headless;

namespace Atbash.App.Tests;

/// <summary>창을 PNG로 저장하는 도우미. 저장 폴더는 ATBASH_SNAPSHOT_DIR (없으면 테스트 출력 폴더)</summary>
public static class Snapshot
{
    public static string Save(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("ATBASH_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(AppContext.BaseDirectory, "snapshots");
        Directory.CreateDirectory(dir);

        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame rendered");
        var path = Path.Combine(dir, name + ".png");
        frame.Save(path);
        return path;
    }
}
