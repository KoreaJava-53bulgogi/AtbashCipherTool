using System;
using System.ComponentModel;
using System.Linq;
using Atbash.App.Services;
using Atbash.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Atbash.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 창 어디에 떨어뜨려도 처리 (입력창이 먼저 처리한 경우도 받음)
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, handledEventsToo: true);

        // 입력창에서 Ctrl+V: 이미지만 있으면 OCR, 글자가 있으면 평소처럼 붙여넣기
        InputBox.AddHandler(TextBox.PastingFromClipboardEvent, OnPasting, RoutingStrategies.Bubble);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm is not { } vm) return;
        Width = vm.InitialWindowWidth;
        Height = vm.InitialWindowHeight;
        vm.Dialogs ??= new AvaloniaDialogs(this, () => vm.L);
        vm.Clipboard ??= new AvaloniaClipboard(this);
        vm.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsBusy))
            Cursor = Vm?.IsBusy == true ? new Cursor(StandardCursorType.Wait) : Cursor.Default;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files == null || Vm is not { } vm) return;
        e.Handled = true;
        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
        await vm.HandleDroppedFilesAsync(paths);
    }

    private async void OnPasting(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || vm.Clipboard is not { } clipboard) return;
        // 클립보드 확인은 비동기라서 기본 붙여넣기를 막고 직접 처리
        e.Handled = true;
        if (await vm.PasteImageIfAnyAsync()) return;
        var text = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text)) InputBox.SelectedText = text;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Vm?.SaveWindowSize(Width, Height);
        base.OnClosing(e);
    }
}
