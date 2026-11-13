using Atbash.App.ViewModels;
using Atbash.App.Views;
using Avalonia.Headless.XUnit;

namespace Atbash.App.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public void MainWindow_opens_and_renders()
    {
        var window = new MainWindow { DataContext = new MainViewModel(), Width = 900, Height = 300 };
        window.Show();

        var path = Snapshot.Save(window, "stage0-main-window");

        Assert.True(File.Exists(path));
        Assert.Contains("아트배쉬", window.Title);
    }
}
