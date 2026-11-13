using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Atbash.App.ViewModels;
using Atbash.App.Views;

namespace Atbash.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        // 스플래시를 먼저 보여주고, 끝나면 메인 창으로 (스플래시를 닫아도 앱이 꺼지지 않게 메인 창을 먼저 띄움)
        var splash = new SplashWindow();
        splash.Show();
        await splash.RunAsync();

        var main = new MainWindow { DataContext = new MainViewModel() };
        desktop.MainWindow = main;
        main.Show();
        splash.Close();
    }
}
