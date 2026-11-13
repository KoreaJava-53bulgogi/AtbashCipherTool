using System.Text;

namespace Atbash.Core.Files;

/// <summary>파일을 다룰 때의 규칙. Java 버전(AtbashCipherGUI)과 같음</summary>
public static class FileRules
{
    /// <summary>이미지로 보고 OCR 하는 확장자</summary>
    public static readonly IReadOnlySet<string> ImageExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "png", "jpg", "jpeg", "bmp", "gif" };

    public const long MaxTextFileBytes = 5L * 1024 * 1024;

    public static string Extension(string path) =>
        Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    public static bool IsImage(string path) => ImageExtensions.Contains(Extension(path));

    public static bool IsEncrypted(string path) => Extension(path) == "enc";

    /// <summary>
    /// 텍스트 파일인지: 5MB 이하이고, 앞 8KB 에 0 바이트가 없고, UTF-8 로 읽힘.
    /// (8KB 경계에서 잘린 한글은 오류로 보지 않음)
    /// </summary>
    public static bool IsTextFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxTextFileBytes) return false;
            using var stream = info.OpenRead();
            var head = new byte[8192];
            int n = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            for (int i = 0; i < n; i++)
                if (head[i] == 0) return false;
            int end = TrimIncompleteUtf8(head, n);
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(head, 0, end);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static int TrimIncompleteUtf8(byte[] b, int length)
    {
        int i = length - 1;
        int back = 0;
        while (i >= 0 && back < 3 && (b[i] & 0xC0) == 0x80)
        {
            i--;
            back++;
        }
        if (i >= 0 && (b[i] & 0x80) != 0)
        {
            int need = (b[i] & 0xE0) == 0xC0 ? 1 : (b[i] & 0xF0) == 0xE0 ? 2 : (b[i] & 0xF8) == 0xF0 ? 3 : 0;
            if (back < need) return i;
        }
        return length;
    }

    /// <summary>
    /// 텍스트 파일을 UTF-8 로 읽음. 앞의 BOM 은 지움. 올바른 UTF-8 이 아니면 예외
    /// (Java Files.readString 처럼 깨진 글자로 읽지 않음).
    /// </summary>
    public static string ReadUtf8Text(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, start, bytes.Length - start);
    }

    /// <summary>UTF-8 (BOM 없이) 로 저장. Java OutputStreamWriter(UTF_8) 와 같음</summary>
    public static void WriteUtf8Text(string path, string text) =>
        File.WriteAllText(path, text, new UTF8Encoding(false));

    /// <summary>파일 암호화 결과 경로: 원래 이름 뒤에 .enc</summary>
    public static string EncryptedPath(string path) => path + ".enc";

    /// <summary>복호화 결과 경로: .enc 를 떼고, .enc 가 아니면 .dec 를 붙임</summary>
    public static string DecryptedPath(string path) =>
        path.EndsWith(".enc", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path + ".dec";
}

/// <summary>비밀번호 강도 (Java promptForPassword 와 같은 점수)</summary>
public enum PasswordStrength { None, Weak, Medium, Strong }

public static class PasswordRules
{
    /// <summary>8자 이상, 대문자, 소문자, 숫자, 특수문자를 하나씩 셈. 2점 이하 약함, 4점 이하 보통, 5점 강함</summary>
    public static PasswordStrength Evaluate(string password)
    {
        if (string.IsNullOrEmpty(password)) return PasswordStrength.None;
        int score = 0;
        if (password.Length >= 8) score++;
        if (password.Any(c => c is >= 'A' and <= 'Z')) score++;
        if (password.Any(c => c is >= 'a' and <= 'z')) score++;
        if (password.Any(c => c is >= '0' and <= '9')) score++;
        if (password.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))) score++;
        return score <= 2 ? PasswordStrength.Weak : score <= 4 ? PasswordStrength.Medium : PasswordStrength.Strong;
    }
}
