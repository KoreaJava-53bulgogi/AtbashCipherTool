using System.Text;

namespace Atbash.Core.Settings;

/// <summary>
/// Java Preferences 가 Windows 레지스트리에 저장할 때 바꾼 이름·값을 원래대로 되돌림
/// (java.util.prefs.WindowsPreferences 의 toJavaName / toJavaValueString 과 같음).
///   이름: 대문자 X → "/X", '/' ↔ '\', 한글 같은 글자가 하나라도 있으면 전체를 "/!" + 특수 Base64
///   값:   대문자 X → "/X", '/' ↔ '\', 한글 같은 글자는 한 글자씩 "/uXXXX"
/// </summary>
public static class JavaPrefsCodec
{
    public static string DecodeName(string raw)
    {
        if (raw.Length > 1 && raw.StartsWith("/!", StringComparison.Ordinal)) return DecodeAlt64Name(raw[2..]);
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char ch = raw[i];
            if (ch == '/')
            {
                if (i + 1 < raw.Length && raw[i + 1] is >= 'A' and <= 'Z')
                {
                    ch = raw[++i];
                }
                else if (i + 1 < raw.Length && raw[i + 1] == '/')
                {
                    ch = '\\';
                    i++;
                }
            }
            else if (ch == '\\')
            {
                ch = '/';
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    public static string DecodeValue(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char ch = raw[i];
            if (ch == '/')
            {
                char next = i + 1 < raw.Length ? raw[i + 1] : ' ';
                if (next == 'u')
                {
                    if (raw.Length < i + 6) break; // Java 와 같이 잘린 /u 는 버림
                    ch = (char)Convert.ToInt32(raw.Substring(i + 2, 4), 16);
                    i += 5;
                }
                else if (next is >= 'A' and <= 'Z')
                {
                    ch = next;
                    i++;
                }
                else if (next == '/')
                {
                    ch = '\\';
                    i++;
                }
            }
            else if (ch == '\\')
            {
                ch = '/';
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    // java.util.prefs.Base64 의 "alternate" 알파벳 (레지스트리 이름에 쓸 수 있는 글자만 사용)
    private const string AltAlphabet =
        "!\"#$%&'(),-.:;<>@[]^`_{|}~abcdefghijklmnopqrstuvwxyz0123456789+?";

    private static string DecodeAlt64Name(string s)
    {
        if (s.Length % 4 != 0) throw new FormatException("String length must be a multiple of four.");
        int groups = s.Length / 4;
        int missing = 0, fullGroups = groups;
        if (s.Length != 0)
        {
            if (s[^1] == '=')
            {
                missing++;
                fullGroups--;
            }
            if (s[^2] == '=') missing++;
        }
        var bytes = new byte[3 * groups - missing];
        int inPos = 0, outPos = 0;
        for (int g = 0; g < fullGroups; g++)
        {
            int c0 = Index(s[inPos++]), c1 = Index(s[inPos++]), c2 = Index(s[inPos++]), c3 = Index(s[inPos++]);
            bytes[outPos++] = (byte)((c0 << 2) | (c1 >> 4));
            bytes[outPos++] = (byte)((c1 << 4) | (c2 >> 2));
            bytes[outPos++] = (byte)((c2 << 6) | c3);
        }
        if (missing != 0)
        {
            int c0 = Index(s[inPos++]), c1 = Index(s[inPos++]);
            bytes[outPos++] = (byte)((c0 << 2) | (c1 >> 4));
            if (missing == 1)
            {
                int c2 = Index(s[inPos]);
                bytes[outPos] = (byte)((c1 << 4) | (c2 >> 2));
            }
        }
        // 2바이트씩 UTF-16 (big-endian) 글자
        var sb = new StringBuilder(bytes.Length / 2);
        for (int i = 0; i + 1 < bytes.Length; i += 2) sb.Append((char)((bytes[i] << 8) | bytes[i + 1]));
        return sb.ToString();
    }

    private static int Index(char c)
    {
        int i = AltAlphabet.IndexOf(c);
        if (i < 0) throw new FormatException("Illegal character in alternate base64: " + c);
        return i;
    }
}
