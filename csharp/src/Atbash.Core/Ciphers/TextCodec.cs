using System.Text;

namespace Atbash.Core.Ciphers;

/// <summary>
/// 간단한 인코딩: Base64, Hex, URL, ROT13 (Java TextCodec.java 와 같은 결과).
/// 디코딩에 실패하면 ArgumentException.
/// </summary>
public static class TextCodec
{
    public static string ToBase64(string text) => Convert.ToBase64String(JavaCompat.Utf8Bytes(text));

    /// <summary>공백, URL-safe 문자(-_), 빠진 '=' 패딩도 받아줌</summary>
    public static string FromBase64(string text)
    {
        var sb = new StringBuilder(text.Length + 3);
        foreach (char c in text)
            if (!JavaCompat.IsRegexSpace(c)) sb.Append(c == '-' ? '+' : c == '_' ? '/' : c);
        while (sb.Length % 4 != 0) sb.Append('=');
        return Utf8Strict(JavaCompat.Base64Decode(sb.ToString()));
    }

    /// <summary>"Hi" → "48 69"</summary>
    public static string ToHex(string text) =>
        string.Join(' ', JavaCompat.Utf8Bytes(text).Select(b => b.ToString("X2")));

    /// <summary>"48 69", "4869", "0x48 0x69", "48:69" 모두 받아줌</summary>
    public static string FromHex(string text)
    {
        var s = text.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (!(JavaCompat.IsRegexSpace(c) || c is ':' or ',' or '-')) sb.Append(c);
        s = sb.ToString();
        if (s.Length % 2 != 0 || !s.All(Uri.IsHexDigit)) throw new ArgumentException("not hex");
        var bytes = new byte[s.Length / 2];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return Utf8Strict(bytes);
    }

    /// <summary>Java URLEncoder 와 같은 규칙. 공백은 %20</summary>
    public static string ToUrl(string text) => JavaUrlEncode(text).Replace("+", "%20");

    /// <summary>Java URLDecoder 와 같은 규칙 ('+' 는 공백, 잘못된 % 는 실패)</summary>
    public static string FromUrl(string text) => JavaUrlDecode(text);

    /// <summary>영문자만 13칸 이동. 두 번 적용하면 원래대로</summary>
    public static string Rot13(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(c switch
            {
                >= 'a' and <= 'z' => (char)('a' + (c - 'a' + 13) % 26),
                >= 'A' and <= 'Z' => (char)('A' + (c - 'A' + 13) % 26),
                _ => c
            });
        }
        return sb.ToString();
    }

    private static string Utf8Strict(byte[] bytes)
    {
        try
        {
            return JavaCompat.Utf8StringStrict(bytes);
        }
        catch (DecoderFallbackException e)
        {
            throw new ArgumentException("decoded bytes are not UTF-8 text", e);
        }
    }

    // ---- java.net.URLEncoder / URLDecoder 를 그대로 옮김 ---------------------------

    private static bool DontNeedEncoding(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '-' or '_' or '.' or '*';

    private static string JavaUrlEncode(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length;)
        {
            char c = s[i];
            if (DontNeedEncoding(c))
            {
                sb.Append(c == ' ' ? '+' : c);
                i++;
                continue;
            }
            // 인코딩이 필요한 글자들을 이어서 모은 뒤 UTF-8 바이트로 바꿈
            int start = i;
            do
            {
                c = s[i];
                i++;
                if (char.IsHighSurrogate(c) && i < s.Length && char.IsLowSurrogate(s[i])) i++;
            } while (i < s.Length && !DontNeedEncoding(s[i]));
            foreach (byte b in JavaCompat.Utf8Bytes(s.Substring(start, i - start)))
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    private static string JavaUrlDecode(string s)
    {
        var sb = new StringBuilder(s.Length);
        int n = s.Length;
        int i = 0;
        while (i < n)
        {
            char c = s[i];
            switch (c)
            {
                case '+':
                    sb.Append(' ');
                    i++;
                    break;
                case '%':
                    var bytes = new List<byte>();
                    while (i + 2 < n && c == '%')
                    {
                        if (!JavaCompat.TryParseInt(s.Substring(i + 1, 2), 16, out int v))
                            throw new ArgumentException("Illegal hex characters in escape (%) pattern");
                        if (v < 0) throw new ArgumentException("Illegal hex characters in escape (%) pattern - negative value");
                        bytes.Add((byte)v);
                        i += 3;
                        if (i < n) c = s[i];
                    }
                    if (i < n && c == '%') throw new ArgumentException("Incomplete trailing escape (%) pattern");
                    sb.Append(JavaCompat.Utf8String(bytes.ToArray()));
                    break;
                default:
                    sb.Append(c);
                    i++;
                    break;
            }
        }
        return sb.ToString();
    }
}
