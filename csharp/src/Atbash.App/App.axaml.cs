using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Atbash.App.Services;
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

        // 처음 실행이면 Java 버전(7.x~8.x)의 설정을 옮겨 옴
        var store = new SettingsStore();
        JavaSettingsImporter.MigrateIfNeeded(store, new RegistryJavaPrefs());

        var vm = new MainViewModel(store, this) { RequestShutdown = () => desktop.Shutdown() };
        var main = new MainWindow { DataContext = vm };
        desktop.MainWindow = main;
        main.Show();
        splash.Close();

        // 하루 한 번 새 버전 확인 (창이 뜬 뒤, 조용히)
        await vm.AutoCheckForUpdatesAsync();
    }
}
