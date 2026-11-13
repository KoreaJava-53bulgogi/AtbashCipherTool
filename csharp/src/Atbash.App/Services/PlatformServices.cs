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
}

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
