using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Atbash.App.Services;

/// <summary>4단계에서 추가한 창들: 글자 입력, 테마 색, 이미지, 변환 기록, 정보</summary>
public sealed partial class AvaloniaDialogs
{
    public async Task<string?> PromptTextAsync(string title, string message, string initial = "")
    {
        var l = _l();
        string? result = null;
        var window = NewWindow(title);
        var box = new TextBox { Text = initial, MinWidth = 320 };
        var (ok, cancel) = OkCancel(l);
        ok.Click += (_, _) =>
        {
            result = box.Text ?? "";
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        window.Content = Column(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 }, box, Buttons(ok, cancel));
        window.Opened += (_, _) => box.Focus();
        await window.ShowDialog(_owner);
        return result;
    }

    public async Task<ThemeColors?> EditThemeColorsAsync(string title, ThemeColors initial)
    {
        var l = _l();
        ThemeColors? result = null;
        var window = NewWindow(title);
        var labels = new[]
        {
            "CUSTOMIZE_THEME_MAIN_BG", "CUSTOMIZE_THEME_TEXT_AREA_BG", "CUSTOMIZE_THEME_TEXT_COLOR",
            "CUSTOMIZE_THEME_PRIMARY_BTN", "CUSTOMIZE_THEME_SECONDARY_BTN",
        };
        var start = new[]
        {
            initial.MainBackground, initial.TextAreaBackground, initial.TextForeground, initial.PrimaryButton, initial.SecondaryButton,
        };
        var pickers = start.Select(c => new ColorPicker { Color = c, Width = 120 }).ToArray();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto") };
        for (int i = 0; i < labels.Length; i++)
        {
            var label = new TextBlock { Text = l[labels[i]], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 16, 4) };
            Grid.SetRow(label, i);
            Grid.SetRow(pickers[i], i);
            Grid.SetColumn(pickers[i], 1);
            pickers[i].Margin = new Thickness(0, 4);
            grid.Children.Add(label);
            grid.Children.Add(pickers[i]);
        }
        var (ok, cancel) = OkCancel(l);
        ok.Click += (_, _) =>
        {
            result = new ThemeColors(pickers[0].Color, pickers[1].Color, pickers[2].Color, pickers[3].Color, pickers[4].Color);
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        window.Content = Column(grid, Buttons(ok, cancel));
        await window.ShowDialog(_owner);
        return result;
    }

    public async Task<int> ShowImageAsync(string title, byte[] png, params string[] buttons)
    {
        int result = buttons.Length - 1;
        var window = NewWindow(title);
        using var stream = new MemoryStream(png);
        var bitmap = new Bitmap(stream);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var b = new Button { Content = buttons[i], MinWidth = 90, IsDefault = i == 0, IsCancel = i == buttons.Length - 1 };
            b.Click += (_, _) =>
            {
                result = index;
                window.Close();
            };
            row.Children.Add(b);
        }
        window.Content = Column(new Image { Source = bitmap, Width = bitmap.PixelSize.Width, Height = bitmap.PixelSize.Height }, row);
        await window.ShowDialog(_owner);
        return result;
    }

    public async Task<HistoryEntry?> ShowHistoryAsync(IReadOnlyList<HistoryEntry> entries, Func<HistoryEntry, Task> copyOutput, Action clearAll)
    {
        var l = _l();
        HistoryEntry? result = null;
        var window = NewWindow(l["HISTORY_TITLE"]);
        window.SizeToContent = SizeToContent.Manual;
        window.Width = 640;
        window.Height = 420;
        window.CanResize = true;

        var items = entries.ToList();
        var list = new ListBox
        {
            ItemsSource = items.Select(e => (Control)new StackPanel
            {
                Margin = new Thickness(2, 3),
                Children =
                {
                    new TextBlock { Text = $"{e.Time:HH:mm:ss}   [{e.ModeName}]", FontWeight = FontWeight.Bold },
                    new TextBlock { Text = Preview(e.Input) + "  →  " + Preview(e.Output), TextTrimming = TextTrimming.CharacterEllipsis },
                },
            }).ToList(),
            SelectedIndex = items.Count > 0 ? 0 : -1,
        };
        var note = new TextBlock
        {
            Text = items.Count == 0 ? l["HISTORY_EMPTY"] : l["HISTORY_NOTE"],
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };

        HistoryEntry? Selected() => list.SelectedIndex >= 0 && list.SelectedIndex < items.Count ? items[list.SelectedIndex] : null;
        void Restore()
        {
            result = Selected();
            if (result != null) window.Close();
        }

        var clear = new Button { Content = l["HISTORY_CLEAR"] };
        var copy = new Button { Content = l["HISTORY_COPY_OUTPUT"] };
        var restore = new Button { Content = l["HISTORY_RESTORE"], IsDefault = true };
        var close = new Button { Content = l["HISTORY_CLOSE"], IsCancel = true };
        clear.Click += (_, _) =>
        {
            clearAll();
            items.Clear();
            list.ItemsSource = new List<Control>();
            note.Text = l["HISTORY_EMPTY"];
        };
        copy.Click += async (_, _) =>
        {
            if (Selected() is { } e) await copyOutput(e);
        };
        restore.Click += (_, _) => Restore();
        close.Click += (_, _) => window.Close();
        list.DoubleTapped += (_, _) => Restore();

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(16) };
        var buttons = Buttons(clear, copy, restore, close);
        Grid.SetRow(note, 0);
        Grid.SetRow(list, 1);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(note);
        grid.Children.Add(list);
        grid.Children.Add(buttons);
        window.Content = grid;
        await window.ShowDialog(_owner);
        return result;
    }

    private static string Preview(string s)
    {
        var one = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return one.Length <= 60 ? one : one[..60] + "…";
    }

    public async Task ShowAboutAsync(string title, string text, Action onTextClicked)
    {
        var l = _l();
        var window = NewWindow(title);
        var body = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Width = 450,
            MinHeight = 120,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        body.PointerPressed += (_, _) => onTextClicked();
        var ok = new Button { Content = l["BUTTON_OK"], MinWidth = 80, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => window.Close();
        window.Content = Column(body, Buttons(ok));
        await window.ShowDialog(_owner);
    }

    public async Task OpenUrlAsync(string url)
    {
        try
        {
            await _owner.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception)
        {
            // 브라우저를 못 열어도 앱은 계속 동작
        }
    }

    // ---- 공통 모양 ---------------------------------------------------------------

    private static (Button ok, Button cancel) OkCancel(Localizer l) =>
        (new Button { Content = l["BUTTON_OK"], MinWidth = 80, IsDefault = true },
         new Button { Content = l["BUTTON_CANCEL"], MinWidth = 80, IsCancel = true });

    private static StackPanel Buttons(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }

    private static StackPanel Column(params Control[] children)
    {
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        foreach (var c in children) panel.Children.Add(c);
        return panel;
    }
}
