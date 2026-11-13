using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Atbash.App.Services;

/// <summary>5단계에서 추가한 창: 버튼 여러 개 고르기, 진행률</summary>
public sealed partial class AvaloniaDialogs
{
    public Task<int> ShowOptionsAsync(string title, string message, MessageKind kind, params string[] buttons) =>
        ShowButtonsAsync(title, message, kind, buttons);

    public IProgressWindow OpenProgress(string title, string message, string cancelText, Action onCancel)
    {
        var window = NewWindow(title);
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, MinWidth = 380, ShowProgressText = true };
        var cancel = new Button { Content = cancelText, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
        bool closedByCode = false;
        cancel.Click += (_, _) => onCancel();
        window.Closing += (_, e) =>
        {
            // 창의 X 를 눌러도 취소로 처리
            if (!closedByCode) onCancel();
        };
        window.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
                bar,
                cancel,
            },
        };
        window.Show(_owner);
        return new ProgressHandle(window, bar, () => closedByCode = true);
    }

    private sealed class ProgressHandle : IProgressWindow
    {
        private readonly Window _window;
        private readonly ProgressBar _bar;
        private readonly Action _markClosing;

        public ProgressHandle(Window window, ProgressBar bar, Action markClosing)
        {
            _window = window;
            _bar = bar;
            _markClosing = markClosing;
        }

        public void Update(long done, long total) => Dispatcher.UIThread.Post(() =>
        {
            if (total > 0)
            {
                _bar.IsIndeterminate = false;
                _bar.Value = done * 100.0 / total;
                _bar.ProgressTextFormat = $"{done / 1048576.0:0.0} / {total / 1048576.0:0.0} MB";
            }
            else
            {
                _bar.IsIndeterminate = true;
                _bar.ProgressTextFormat = $"{done / 1048576.0:0.0} MB";
            }
        });

        public void Close() => Dispatcher.UIThread.Post(() =>
        {
            _markClosing();
            _window.Close();
        });
    }
}
