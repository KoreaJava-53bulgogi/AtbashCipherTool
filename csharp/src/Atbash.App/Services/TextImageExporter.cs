using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Atbash.App.Services;

/// <summary>
/// 결과 텍스트를 PNG 이미지로 (Java saveOutputAsImage 와 같음):
/// 줄은 자동으로 바꾸지 않고, 둘레 여백 20, 현재 테마의 결과창 색과 글자색 사용.
/// Avalonia 로 그리므로 한글·일본어·이모지 글꼴도 알아서 찾아 씀.
/// </summary>
public static class TextImageExporter
{
    public const int Padding = 20;

    public static byte[] RenderPng(string text, Color background, Color foreground, double fontSize = 14)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(foreground),
            TextWrapping = TextWrapping.NoWrap,
        };
        var root = new Border
        {
            Background = new SolidColorBrush(background),
            Padding = new Thickness(Padding),
            Child = block,
        };
        root.Measure(Size.Infinity);
        var size = root.DesiredSize;
        root.Arrange(new Rect(size));

        int width = Math.Max(1, (int)Math.Ceiling(size.Width));
        int height = Math.Max(1, (int)Math.Ceiling(size.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(root);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }
}
