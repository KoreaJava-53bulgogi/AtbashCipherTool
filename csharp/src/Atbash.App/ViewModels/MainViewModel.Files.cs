using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Atbash.App.Services;
using Atbash.Core.Crypto;
using Atbash.Core.Files;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atbash.App.ViewModels;

/// <summary>
/// 파일 기능 (3단계): 불러오기·저장, 이미지로 저장, OCR(파일·클립보드·Ctrl+V),
/// 드래그 앤 드롭, 파일 암호화·복호화, 안전 삭제. 동작은 Java 버전과 같음.
/// </summary>
public partial class MainViewModel
{
    /// <summary>창 쪽에서 넣어 줌. 테스트에서는 가짜 구현</summary>
    public IDialogs? Dialogs { get; set; }

    /// <summary>창 쪽에서 넣어 줌. 테스트에서는 가짜 구현</summary>
    public IClipboardAccess? Clipboard { get; set; }

    /// <summary>OCR·암호화 같은 오래 걸리는 작업 중 (마우스 커서가 모래시계로 바뀜)</summary>
    [ObservableProperty] public partial bool IsBusy { get; set; }

    private FileFilter ImageFilter => new(L["FILE_FILTER_IMAGES"], FileRules.ImageExtensions.ToArray());
    private FileFilter EncryptedFilter => new(L["FILE_FILTER_ENCRYPTED"], "enc");
    private FileFilter PngFilter => new(L["FILE_FILTER_PNG"], "png");
    private FileFilter AllFilter => new(L["FILE_FILTER_ALL"]);

    // ---- 불러오기 / 저장 -------------------------------------------------------------

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (Dialogs == null) return;
        var files = await Dialogs.OpenFilesAsync(L["LOAD_MENU"], false, AllFilter);
        if (files.Count > 0) await LoadTextFileAsync(files[0]);
    }

    /// <summary>UTF-8 텍스트 파일을 원본 텍스트로 불러옴 (결과창은 비움)</summary>
    public async Task LoadTextFileAsync(string path)
    {
        try
        {
            InputText = FileRules.ReadUtf8Text(path);
            OutputText = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            await Error(L["LOAD_ERROR_TITLE"], L["LOAD_ERROR_MESSAGE"] + e.Message);
        }
    }

    /// <summary>결과 텍스트를 UTF-8 파일로 저장</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Dialogs == null) return;
        var path = await Dialogs.SaveFileAsync(L["SAVE_MENU"], "output.txt", AllFilter);
        if (path == null) return;
        try
        {
            FileRules.WriteUtf8Text(path, OutputText);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Error(L["SAVE_ERROR_TITLE"], L["SAVE_ERROR_MESSAGE"] + e.Message);
        }
    }

    /// <summary>결과 텍스트를 현재 테마 색으로 PNG 이미지로 저장</summary>
    [RelayCommand]
    private async Task SaveImageAsync()
    {
        if (Dialogs == null) return;
        if (string.IsNullOrWhiteSpace(OutputText))
        {
            await Dialogs.ShowMessageAsync(L["SAVE_IMAGE_NO_CONTENT_TITLE"], L["SAVE_IMAGE_NO_CONTENT_MESSAGE"], MessageKind.Warning);
            return;
        }
        var path = await Dialogs.SaveFileAsync(L["SAVE_IMAGE_MENU"], "output.png", PngFilter);
        if (path == null) return;
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
        try
        {
            File.WriteAllBytes(path, TextImageExporter.RenderPng(OutputText, Theme.OutputArea, Theme.Foreground));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Error(L["SAVE_ERROR_TITLE"], L["SAVE_IMAGE_ERROR_MESSAGE"] + e.Message);
        }
    }

    // ---- OCR ---------------------------------------------------------------------

    [RelayCommand]
    private async Task OcrFromFileAsync()
    {
        if (Dialogs == null) return;
        var files = await Dialogs.OpenFilesAsync(L["LOAD_OCR_MENU"], false, ImageFilter);
        if (files.Count > 0) await RecognizeAsync(() => File.ReadAllBytes(files[0]));
    }

    /// <summary>클립보드의 이미지(캡처)나, 탐색기에서 복사한 이미지 파일 하나에서 글자를 읽음</summary>
    [RelayCommand]
    private async Task OcrFromClipboardAsync()
    {
        if (await TryRecognizeClipboardAsync()) return;
        if (Dialogs != null)
            await Dialogs.ShowMessageAsync(L["OCR_ERROR_TITLE"], L[ClipboardNoImageKey]);
    }

    /// <summary>글자 읽기 엔진 (운영체제마다 다름. 테스트에서 바꿈)</summary>
    public IOcrEngine Ocr { get; set; } = OcrService.Current;

    /// <summary>"클립보드에 이미지가 없음" 안내: 화면 캡처 단축키가 운영체제마다 달라서 문구도 다름</summary>
    private string ClipboardNoImageKey =>
        Platform.Id.StartsWith("osx", StringComparison.Ordinal) ? "CLIPBOARD_NO_IMAGE_MESSAGE_MAC"
        : Platform.Id.StartsWith("linux", StringComparison.Ordinal) ? "CLIPBOARD_NO_IMAGE_MESSAGE_LINUX"
        : "CLIPBOARD_NO_IMAGE_MESSAGE";

    /// <summary>
    /// 입력창에서 Ctrl+V 를 눌렀을 때: 클립보드에 글자가 없고 이미지만 있으면 OCR 하고 true.
    /// 글자가 있으면(웹페이지 복사 등) 평소처럼 글자를 붙여넣도록 false.
    /// </summary>
    public async Task<bool> PasteImageIfAnyAsync()
    {
        if (Clipboard == null) return false;
        if (!string.IsNullOrEmpty(await Clipboard.TryGetTextAsync())) return false;
        return await TryRecognizeClipboardAsync();
    }

    private async Task<bool> TryRecognizeClipboardAsync()
    {
        if (Clipboard == null) return false;
        var png = await Clipboard.TryGetImagePngAsync();
        if (png != null)
        {
            await RecognizeAsync(() => png);
            return true;
        }
        var files = await Clipboard.TryGetFilePathsAsync();
        if (files is { Count: 1 } && FileRules.IsImage(files[0]))
        {
            await RecognizeAsync(() => File.ReadAllBytes(files[0]));
            return true;
        }
        return false;
    }

    /// <summary>글자를 읽어 원본 텍스트에 넣음. 오래 걸리므로 백그라운드에서</summary>
    private async Task RecognizeAsync(Func<byte[]> imageBytes)
    {
        // 이 운영체제용 엔진이 아직 없으면 오류 대신 안내만 함
        if (!Ocr.IsSupported)
        {
            if (Dialogs != null)
                await Dialogs.ShowMessageAsync(L["OCR_ERROR_TITLE"], L["OCR_UNSUPPORTED_MESSAGE"]);
            return;
        }

        string text;
        IsBusy = true;
        try
        {
            text = await Task.Run(() => Ocr.Recognize(imageBytes()));
        }
        catch (OcrSetupRequiredException e)
        {
            // 예: Linux 에서 tesseract 가 설치되어 있지 않음 → 오류가 아니라 설치 방법을 안내
            if (Dialogs != null) await Dialogs.ShowMessageAsync(L["OCR_ERROR_TITLE"], L[e.MessageKey]);
            return;
        }
        catch (Exception e)
        {
            await Error(L["OCR_ERROR_TITLE"], L["OCR_ERROR_MESSAGE"] + "\n" + e.Message);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            if (Dialogs != null)
                await Dialogs.ShowMessageAsync(L["OCR_NO_TEXT_FOUND_TITLE"], L["OCR_NO_TEXT_FOUND_MESSAGE"]);
            return;
        }
        StopAnimation();
        InputText = text.Trim();
        OutputText = "";
        ShowFeedback(L["OCR_SUCCESS_FEEDBACK"]);
    }

    // ---- 드래그 앤 드롭 ----------------------------------------------------------------

    /// <summary>
    /// 끌어다 놓은 파일을 종류에 따라 처리 (Java 와 같음):
    /// 이미지 1개 → OCR, .enc 만 → 복호화, 텍스트 파일 1개 → 불러오기, 그 밖 → 암호화할지 물어봄
    /// </summary>
    public async Task HandleDroppedFilesAsync(IEnumerable<string> paths)
    {
        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0) return;

        if (files.Count == 1 && FileRules.IsImage(files[0]))
        {
            await RecognizeAsync(() => File.ReadAllBytes(files[0]));
            return;
        }
        if (files.All(FileRules.IsEncrypted))
        {
            await DecryptFilesAsync(files);
            return;
        }
        if (files.Count == 1 && FileRules.IsTextFile(files[0]))
        {
            await LoadTextFileAsync(files[0]);
            return;
        }
        if (Dialogs != null && await Dialogs.ConfirmAsync(L["ENCRYPT_FILE_DIALOG_TITLE"],
                L["DROP_ENCRYPT_CONFIRM"].Replace("{0}", files.Count.ToString())))
        {
            await EncryptFilesAsync(files);
        }
    }

    // ---- 파일 암호화 / 복호화 ------------------------------------------------------------

    [RelayCommand]
    private async Task EncryptFilesAsync()
    {
        if (Dialogs == null) return;
        var files = await Dialogs.OpenFilesAsync(L["ENCRYPT_FILE_DIALOG_TITLE"], true, AllFilter);
        if (files.Count > 0) await EncryptFilesAsync(files);
    }

    /// <summary>파일들을 비밀번호 하나로 암호화 (각 파일 옆에 .enc). 끝나면 원본을 안전하게 지울지 물어봄</summary>
    public async Task EncryptFilesAsync(IReadOnlyList<string> files)
    {
        if (Dialogs == null) return;
        var password = await AskPasswordAsync(L["ENCRYPT_FILE_DIALOG_TITLE"], L["ENCRYPT_PASSWORD_PROMPT"]);
        if (password == null) return;

        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                foreach (var file in files) AesCipher.EncryptFile(file, FileRules.EncryptedPath(file), password);
            });
        }
        catch (Exception e)
        {
            await Error(L["ENCRYPT_ERROR_TITLE"], L["ENCRYPT_ERROR_MESSAGE"] + e.Message);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await Dialogs.ShowMessageAsync(L["ENCRYPT_SUCCESS_TITLE"], L["ENCRYPT_SUCCESS_MESSAGE"]);
        if (await Dialogs.ConfirmAsync(L["SECURE_DELETE_TITLE"], L["SECURE_DELETE_PROMPT"], MessageKind.Warning))
        {
            IsBusy = true;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var file in files) AesCipher.SecureWipe(file);
                });
            }
            finally
            {
                IsBusy = false;
            }
            await Dialogs.ShowMessageAsync(L["SECURE_DELETE_TITLE"], L["SECURE_DELETE_SUCCESS"]);
        }
    }

    [RelayCommand]
    private async Task DecryptFilesAsync()
    {
        if (Dialogs == null) return;
        var files = await Dialogs.OpenFilesAsync(L["DECRYPT_FILE_DIALOG_TITLE"], true, EncryptedFilter, AllFilter);
        if (files.Count > 0) await DecryptFilesAsync(files);
    }

    /// <summary>.enc 파일들을 비밀번호 하나로 복호화 (.enc 를 뗀 이름으로). 같은 이름이 있으면 덮어쓸지 물어봄</summary>
    public async Task DecryptFilesAsync(IReadOnlyList<string> files)
    {
        if (Dialogs == null) return;
        var password = await AskPasswordAsync(L["DECRYPT_FILE_DIALOG_TITLE"], L["DECRYPT_PASSWORD_PROMPT"]);
        if (password == null) return;

        var jobs = new List<(string Input, string Output)>();
        foreach (var file in files)
        {
            var output = FileRules.DecryptedPath(file);
            if (File.Exists(output) && !await Dialogs.ConfirmAsync(L["OVERWRITE_FILE_CONFIRM_TITLE"],
                    L["OVERWRITE_FILE_CONFIRM_MESSAGE"].Replace("''", "'").Replace("{0}", Path.GetFileName(output)),
                    MessageKind.Warning))
            {
                continue;
            }
            jobs.Add((file, output));
        }
        if (jobs.Count == 0) return;

        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                foreach (var (input, output) in jobs) AesCipher.DecryptFile(input, output, password);
            });
        }
        catch (AesCipher.WrongPasswordException)
        {
            await Error(L["DECRYPT_ERROR_TITLE"], L["DECRYPT_WRONG_PASSWORD"]);
            return;
        }
        catch (Exception e)
        {
            await Error(L["DECRYPT_ERROR_TITLE"], L["DECRYPT_ERROR_MESSAGE"] + e.Message);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await Dialogs.ShowMessageAsync(L["DECRYPT_SUCCESS_TITLE"],
            L["DECRYPT_SUCCESS_MESSAGE"] + "\n" + string.Join("\n", jobs.Select(j => j.Output)));
    }

    /// <summary>비밀번호를 물어봄. 취소하면 null, 비워 두면 다시 안내</summary>
    private async Task<string?> AskPasswordAsync(string title, string message)
    {
        while (true)
        {
            var password = await Dialogs!.PromptPasswordAsync(title, message);
            if (password == null) return null;
            if (password.Length > 0) return password;
            await Dialogs.ShowMessageAsync(title, L["PASSWORD_REQUIRED"], MessageKind.Warning);
        }
    }

    private Task Error(string title, string message) =>
        Dialogs?.ShowMessageAsync(title, message, MessageKind.Error) ?? Task.CompletedTask;
}
