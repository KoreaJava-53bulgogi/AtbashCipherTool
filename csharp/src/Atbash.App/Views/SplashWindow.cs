using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Atbash.App.Views;

/// <summary>시작 스플래시: startup.png + 진행 막대 (Java 버전과 같음, 약 0.6초)</summary>
public sealed class SplashWindow : Window
{
    private readonly ProgressBar _progress;

    public SplashWindow()
    {
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;

        using var stream = AssetLoader.Open(new Uri("avares://AtbashCipherTool/Assets/startup.png"));
        var bitmap = new Bitmap(stream);
        _progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 8,
            MinHeight = 8,
            CornerRadius = new CornerRadius(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0, 123, 255)),
            Background = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
        };
        Content = new DockPanel
        {
            Children =
            {
                Dock(_progress),
                new Image { Source = bitmap, Width = bitmap.Size.Width, Height = bitmap.Size.Height },
            },
        };
    }

    private static Control Dock(Control c)
    {
        DockPanel.SetDock(c, Avalonia.Controls.Dock.Bottom);
        return c;
    }

    /// <summary>진행 막대를 0 → 100 으로 채움 (Java: 5ms × 101)</summary>
    public async Task RunAsync()
    {
        for (int i = 0; i <= 100; i += 2)
        {
            _progress.Value = i;
            await Task.Delay(10);
        }
    }
}
