using Atbash.App.Services;

namespace Atbash.App.Tests;

/// <summary>사람이 누르는 대신 미리 정한 답을 돌려주는 가짜 창. 무엇을 물었는지도 기록함</summary>
public sealed class FakeDialogs : IDialogs
{
    public Queue<IReadOnlyList<string>> OpenResults { get; } = new();
    public Queue<string?> SaveResults { get; } = new();
    public Queue<bool> ConfirmResults { get; } = new();
    public Queue<string?> PasswordResults { get; } = new();

    public List<(string Title, string Message, MessageKind Kind)> Messages { get; } = new();
    public List<string> Confirms { get; } = new();

    public Task<IReadOnlyList<string>> OpenFilesAsync(string title, bool allowMultiple, params FileFilter[] filters) =>
        Task.FromResult(OpenResults.Count > 0 ? OpenResults.Dequeue() : (IReadOnlyList<string>)Array.Empty<string>());

    public Task<string?> SaveFileAsync(string title, string suggestedName, params FileFilter[] filters) =>
        Task.FromResult(SaveResults.Count > 0 ? SaveResults.Dequeue() : null);

    public Task ShowMessageAsync(string title, string message, MessageKind kind = MessageKind.Info)
    {
        Messages.Add((title, message, kind));
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message, MessageKind kind = MessageKind.Info)
    {
        Confirms.Add(message);
        return Task.FromResult(ConfirmResults.Count > 0 && ConfirmResults.Dequeue());
    }

    public Task<string?> PromptPasswordAsync(string title, string message) =>
        Task.FromResult(PasswordResults.Count > 0 ? PasswordResults.Dequeue() : null);
}

public sealed class FakeClipboard : IClipboardAccess
{
    public string? Text { get; set; }
    public byte[]? ImagePng { get; set; }
    public IReadOnlyList<string>? Files { get; set; }

    public Task SetTextAsync(string text)
    {
        Text = text;
        return Task.CompletedTask;
    }

    public Task<string?> TryGetTextAsync() => Task.FromResult(Text);

    public Task<byte[]?> TryGetImagePngAsync() => Task.FromResult(ImagePng);

    public Task<IReadOnlyList<string>?> TryGetFilePathsAsync() => Task.FromResult(Files);
}
