using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Atbash.App.Services;

/// <summary>파일 선택 창에 보여줄 종류 (이름, 확장자들). 확장자가 비어 있으면 모든 파일</summary>
public sealed record FileFilter(string Name, params string[] Extensions);

public enum MessageKind { Info, Warning, Error }

/// <summary>
/// 화면에 띄우는 창들. 실제 앱은 AvaloniaDialogs, 테스트는 가짜 구현을 씀.
/// (화면 동작을 사람이 누르지 않고도 테스트하기 위해 나눠 둠)
/// </summary>
public interface IDialogs
{
    /// <summary>파일 열기. 취소하면 빈 목록</summary>
    Task<IReadOnlyList<string>> OpenFilesAsync(string title, bool allowMultiple, params FileFilter[] filters);

    /// <summary>파일 저장 위치 고르기. 취소하면 null</summary>
    Task<string?> SaveFileAsync(string title, string suggestedName, params FileFilter[] filters);

    Task ShowMessageAsync(string title, string message, MessageKind kind = MessageKind.Info);

    /// <summary>예/아니요</summary>
    Task<bool> ConfirmAsync(string title, string message, MessageKind kind = MessageKind.Info);

    /// <summary>비밀번호 입력 (강도 표시). 취소하면 null</summary>
    Task<string?> PromptPasswordAsync(string title, string message);

    /// <summary>글자 한 줄 입력. 취소하면 null</summary>
    Task<string?> PromptTextAsync(string title, string message, string initial = "");

    /// <summary>목록에서 고르기 (제목·설명·작은 그림). 취소하거나 아무것도 안 고르면 빈 목록</summary>
    Task<IReadOnlyList<int>> ChooseAsync(string title, string prompt, IReadOnlyList<ChoiceItem> items, bool multiple);

    /// <summary>사용자 테마의 색 5개 고르기. 취소하면 null</summary>
    Task<ThemeColors?> EditThemeColorsAsync(string title, ThemeColors initial);

    /// <summary>이미지 미리보기 + 버튼들. 누른 버튼 번호 (그냥 닫으면 마지막 버튼)</summary>
    Task<int> ShowImageAsync(string title, byte[] png, params string[] buttons);

    /// <summary>변환 기록 창. 불러오기를 누르면 그 항목, 그냥 닫으면 null</summary>
    Task<HistoryEntry?> ShowHistoryAsync(IReadOnlyList<HistoryEntry> entries, Func<HistoryEntry, Task> copyOutput, Action clearAll);

    /// <summary>정보 창 (README). 글자를 누르면 onTextClicked</summary>
    Task ShowAboutAsync(string title, string text, Action onTextClicked);

    /// <summary>브라우저로 주소 열기</summary>
    Task OpenUrlAsync(string url);
}

/// <summary>목록의 한 항목</summary>
public sealed record ChoiceItem(string Title, string Detail = "", byte[]? Image = null);

/// <summary>변환 기록 한 건. 앱을 닫으면 사라짐 (비밀번호·원문이 디스크에 남지 않도록)</summary>
public sealed record HistoryEntry(System.DateTime Time, int ModeIndex, string ModeName, int Key, string Input, string Output);

/// <summary>클립보드. 실제 앱은 창의 클립보드, 테스트는 가짜 구현</summary>
public interface IClipboardAccess
{
    Task SetTextAsync(string text);

    Task<string?> TryGetTextAsync();

    /// <summary>클립보드의 이미지를 PNG 바이트로. 없으면 null</summary>
    Task<byte[]?> TryGetImagePngAsync();

    /// <summary>탐색기에서 복사한 파일들의 경로. 없으면 null</summary>
    Task<IReadOnlyList<string>?> TryGetFilePathsAsync();
}
