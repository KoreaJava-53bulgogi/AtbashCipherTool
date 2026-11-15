using Avalonia.Input;

namespace Atbash.App.Services;

/// <summary>
/// 단축키. macOS 에서는 Ctrl 대신 ⌘(Command) 를 씀.
/// 기록은 Windows·Linux 에서 Ctrl+H 이지만, macOS 의 ⌘H 는 "앱 가리기"(시스템 단축키)라서 ⌘Y (Safari 의 방문 기록과 같은 키).
/// 붙여넣기(Ctrl+V / ⌘V)는 입력창이 운영체제에 맞게 알아서 처리함.
/// </summary>
public static class Shortcuts
{
    public static KeyGesture OcrFromClipboard(bool mac) =>
        new(Key.V, (mac ? KeyModifiers.Meta : KeyModifiers.Control) | KeyModifiers.Shift);

    public static KeyGesture History(bool mac) =>
        mac ? new KeyGesture(Key.Y, KeyModifiers.Meta) : new KeyGesture(Key.H, KeyModifiers.Control);
}
