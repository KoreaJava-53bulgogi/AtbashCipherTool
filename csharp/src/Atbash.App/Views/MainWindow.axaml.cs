using System;
using Atbash.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace Atbash.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel vm)
        {
            Width = vm.InitialWindowWidth;
            Height = vm.InitialWindowHeight;
            vm.SetClipboardText = async text =>
            {
                if (Clipboard != null) await Clipboard.SetTextAsync(text);
            };
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.SaveWindowSize(Width, Height);
        base.OnClosing(e);
    }
}
