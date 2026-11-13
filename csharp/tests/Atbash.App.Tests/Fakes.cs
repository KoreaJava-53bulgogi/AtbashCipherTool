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

    // ---- 4단계 -------------------------------------------------------------------

    public Queue<string?> TextResults { get; } = new();
    public List<string> TextPrompts { get; } = new();
    public Queue<IReadOnlyList<int>> ChooseResults { get; } = new();
    public List<IReadOnlyList<ChoiceItem>> ChooseItems { get; } = new();
    public Queue<ThemeColors?> ThemeResults { get; } = new();
    public Queue<int> ImageResults { get; } = new();
    public List<byte[]> ShownImages { get; } = new();
    public List<string> OpenedUrls { get; } = new();
    public string? AboutText { get; private set; }
    public Action? AboutClick { get; private set; }

    /// <summary>변환 기록 창에서 할 일: (항목들, 복사, 모두 지우기) → 불러올 항목</summary>
    public Func<IReadOnlyList<HistoryEntry>, Func<HistoryEntry, Task>, Action, Task<HistoryEntry?>>? HistoryBehavior { get; set; }

    public Task<string?> PromptTextAsync(string title, string message, string initial = "")
    {
        TextPrompts.Add(message);
        return Task.FromResult(TextResults.Count > 0 ? TextResults.Dequeue() : null);
    }

    public Task<IReadOnlyList<int>> ChooseAsync(string title, string prompt, IReadOnlyList<ChoiceItem> items, bool multiple)
    {
        ChooseItems.Add(items);
        return Task.FromResult(ChooseResults.Count > 0 ? ChooseResults.Dequeue() : (IReadOnlyList<int>)Array.Empty<int>());
    }

    public Task<ThemeColors?> EditThemeColorsAsync(string title, ThemeColors initial) =>
        Task.FromResult(ThemeResults.Count > 0 ? ThemeResults.Dequeue() : null);

    public Task<int> ShowImageAsync(string title, byte[] png, params string[] buttons)
    {
        ShownImages.Add(png);
        return Task.FromResult(ImageResults.Count > 0 ? ImageResults.Dequeue() : buttons.Length - 1);
    }

    public Task<HistoryEntry?> ShowHistoryAsync(IReadOnlyList<HistoryEntry> entries, Func<HistoryEntry, Task> copyOutput, Action clearAll) =>
        HistoryBehavior?.Invoke(entries, copyOutput, clearAll) ?? Task.FromResult<HistoryEntry?>(null);

    public Task ShowAboutAsync(string title, string text, Action onTextClicked)
    {
        AboutText = text;
        AboutClick = onTextClicked;
        return Task.CompletedTask;
    }

    public Task OpenUrlAsync(string url)
    {
        OpenedUrls.Add(url);
        return Task.CompletedTask;
    }

    // ---- 5단계 -------------------------------------------------------------------

    public Queue<int> OptionResults { get; } = new();
    public List<(string Message, string[] Buttons)> Options { get; } = new();
    public List<FakeProgress> Progresses { get; } = new();

    public Task<int> ShowOptionsAsync(string title, string message, MessageKind kind, params string[] buttons)
    {
        Options.Add((message, buttons));
        return Task.FromResult(OptionResults.Count > 0 ? OptionResults.Dequeue() : buttons.Length - 1);
    }

    public IProgressWindow OpenProgress(string title, string message, string cancelText, Action onCancel)
    {
        var p = new FakeProgress(onCancel);
        Progresses.Add(p);
        return p;
    }
}

public sealed class FakeProgress : IProgressWindow
{
    public FakeProgress(Action cancel) => Cancel = cancel;

    public Action Cancel { get; }
    public List<(long Done, long Total)> Updates { get; } = new();
    public bool Closed { get; private set; }

    public void Update(long done, long total) => Updates.Add((done, total));

    public void Close() => Closed = true;
}

/// <summary>네이버 API 대신 미리 정한 응답을 돌려주는 가짜 HTTP. 받은 요청을 기록함</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    public Dictionary<string, (string ContentType, byte[] Body)> Responses { get; } = new();
    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var url = request.RequestUri!.ToString();
        foreach (var (prefix, response) in Responses)
        {
            if (!url.StartsWith(prefix)) continue;
            var content = new ByteArrayContent(response.Body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(response.ContentType);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
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
