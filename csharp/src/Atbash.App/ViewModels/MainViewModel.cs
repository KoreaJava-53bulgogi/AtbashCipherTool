using CommunityToolkit.Mvvm.ComponentModel;

namespace Atbash.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "아트배쉬 암호화/복호화 툴 9.0.0 (C# 개발 중) · 日本語 · 中文";
}
