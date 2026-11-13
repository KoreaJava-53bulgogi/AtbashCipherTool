using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atbash.Core.Files;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Atbash.App.Services;

/// <summary>실제 앱에서 쓰는 창들 (Avalonia 에는 기본 메시지 창이 없어서 직접 만듦)</summary>
public sealed partial class AvaloniaDialogs : IDialogs
{
    private readonly Window _owner;
    private readonly Func<Localizer> _l;

    public AvaloniaDialogs(Window owner, Func<Localizer> localizer)
    {
        _owner = owner;
        _l = localizer;
    }

    // ---- 파일 선택 ---------------------------------------------------------------

    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, bool allowMultiple, params FileFilter[] filters)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = ToTypes(filters),
        });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName, params FileFilter[] filters)
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = ToTypes(filters),
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    private static List<FilePickerFileType>? ToTypes(FileFilter[] filters) =>
        filters.Length == 0
            ? null
            : filters.Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = f.Extensions.Length == 0 ? new[] { "*" } : f.Extensions.Select(e => "*." + e).ToArray(),
            }).ToList();

    // ---- 메시지 -----------------------------------------------------------------

    public async Task ShowMessageAsync(string title, string message, MessageKind kind = MessageKind.Info) =>
        await ShowButtonsAsync(title, message, kind, _l()["BUTTON_OK"]);

    public async Task<bool> ConfirmAsync(string title, string message, MessageKind kind = MessageKind.Info) =>
        await ShowButtonsAsync(title, message, kind, _l()["BUTTON_YES"], _l()["BUTTON_NO"]) == 0;

    /// <returns>누른 버튼 번호. 창을 그냥 닫으면 마지막 버튼(아니요/확인)으로 봄</returns>
    private async Task<int> ShowButtonsAsync(string title, string message, MessageKind kind, params string[] buttons)
    {
        int result = buttons.Length - 1;
        var window = NewWindow(title);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var button = new Button { Content = buttons[i], MinWidth = 80, IsDefault = i == 0, IsCancel = i == buttons.Length - 1 };
            button.Click += (_, _) =>
            {
                result = index;
                window.Close();
            };
            row.Children.Add(button);
        }

        window.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = Icon(kind), FontSize = 22, VerticalAlignment = VerticalAlignment.Top },
                        new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
                    },
                },
                row,
            },
        };
        await window.ShowDialog(_owner);
        return result;
    }

    private static string Icon(MessageKind kind) => kind switch
    {
        MessageKind.Warning => "⚠️",
        MessageKind.Error => "❌",
        _ => "ℹ️",
    };

    // ---- 비밀번호 ---------------------------------------------------------------

    public async Task<string?> PromptPasswordAsync(string title, string message)
    {
        var l = _l();
        string? result = null;
        var window = NewWindow(title);
        var box = new TextBox { PasswordChar = '●', MinWidth = 320 };
        var strength = new TextBlock { FontWeight = FontWeight.Bold, FontSize = 12, Text = " " };
        box.TextChanged += (_, _) =>
        {
            (strength.Text, strength.Foreground) = PasswordRules.Evaluate(box.Text ?? "") switch
            {
                PasswordStrength.Weak => (l["PASSWORD_STRENGTH_WEAK"], Brushes.Red),
                PasswordStrength.Medium => (l["PASSWORD_STRENGTH_MEDIUM"], new SolidColorBrush(Color.FromRgb(255, 140, 0))),
                PasswordStrength.Strong => (l["PASSWORD_STRENGTH_STRONG"], new SolidColorBrush(Color.FromRgb(0, 128, 0))),
                _ => (" ", strength.Foreground),
            };
        };

        var ok = new Button { Content = l["BUTTON_OK"], MinWidth = 80, IsDefault = true };
        var cancel = new Button { Content = l["BUTTON_CANCEL"], MinWidth = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            result = box.Text ?? "";
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
                box,
                strength,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 8, 0, 0),
                    Children = { ok, cancel },
                },
            },
        };
        window.Opened += (_, _) => box.Focus();
        await window.ShowDialog(_owner);
        return result;
    }

    internal Window NewWindow(string title) => new()
    {
        Title = title,
        SizeToContent = SizeToContent.WidthAndHeight,
        MinWidth = 320,
        CanResize = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Icon = _owner.Icon,
    };
}

/// <summary>실제 앱의 클립보드</summary>
public sealed class AvaloniaClipboard : IClipboardAccess
{
    private readonly TopLevel _top;

    public AvaloniaClipboard(TopLevel top) => _top = top;

    public async Task SetTextAsync(string text)
    {
        if (_top.Clipboard != null) await _top.Clipboard.SetTextAsync(text);
    }

    public async Task<string?> TryGetTextAsync() =>
        _top.Clipboard == null ? null : await _top.Clipboard.TryGetTextAsync();

    public async Task<byte[]?> TryGetImagePngAsync()
    {
        if (_top.Clipboard == null) return null;
        using var bitmap = await _top.Clipboard.TryGetBitmapAsync();
        if (bitmap == null) return null;
        using var stream = new System.IO.MemoryStream();
        bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    public async Task<IReadOnlyList<string>?> TryGetFilePathsAsync()
    {
        if (_top.Clipboard == null) return null;
        var files = await _top.Clipboard.TryGetFilesAsync();
        return files?.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
    }
}
