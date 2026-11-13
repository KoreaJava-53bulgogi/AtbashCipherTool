using System.Text;
using System.Text.RegularExpressions;

namespace Atbash.Core.Ciphers;

// Java 버전 (AtbashCipher.java, CaesarCipher.java, ScytaleCipher.java, VigenereCipher.java,
// MorseCode.java, BinaryCipher.java, HangulCipher.java) 을 그대로 옮긴 것.
// 결과가 Java 와 한 글자도 다르지 않아야 함 (tests/Atbash.Core.Tests/GoldenTests 가 확인).

/// <summary>아트배쉬: 알파벳·숫자·한글 음절·특수문자 묶음 안에서 순서를 뒤집음</summary>
public static class AtbashCipher
{
    public static string Transform(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(c switch
            {
                >= 'a' and <= 'z' => (char)('z' - c + 'a'),
                >= 'A' and <= 'Z' => (char)('Z' - c + 'A'),
                >= '가' and <= '힣' => (char)('힣' - c + '가'),
                >= '0' and <= '9' => (char)('9' - c + '0'),
                >= '!' and <= '/' => (char)('/' - c + '!'),
                >= ':' and <= '@' => (char)('@' - c + ':'),
                >= '[' and <= '`' => (char)('`' - c + '['),
                >= '{' and <= '~' => (char)('~' - c + '{'),
                _ => c
            });
        }
        return sb.ToString();
    }
}

/// <summary>카이사르: 영문(26), 숫자(10), 한글 음절(11172) 안에서 이동</summary>
public static class CaesarCipher
{
    public static string Encrypt(string text, int shift) => Transform(text, shift);

    public static string Decrypt(string text, int shift) => Transform(text, -shift);

    private static string Transform(string text, int shift)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(c switch
            {
                >= 'a' and <= 'z' => ShiftChar(c, 'a', 26, shift),
                >= 'A' and <= 'Z' => ShiftChar(c, 'A', 26, shift),
                >= '0' and <= '9' => ShiftChar(c, '0', 10, shift),
                >= '가' and <= '힣' => ShiftChar(c, '가', 11172, shift),
                _ => c
            });
        }
        return sb.ToString();
    }

    private static char ShiftChar(char c, char @base, int range, int shift)
    {
        int pos = (c - @base + shift) % range;
        if (pos < 0) pos += range;
        return (char)(@base + pos);
    }
}

/// <summary>스퀴탈레: 공백을 지운 뒤 지름(열 수)만큼의 표에 세로로 쓰고 가로로 읽음. 빈칸은 'X'</summary>
public static class ScytaleCipher
{
    public static string Encrypt(string text, int diameter)
    {
        if (text.Length == 0 || diameter <= 0) return text;
        var clean = new StringBuilder();
        foreach (char c in text)
            if (!JavaCompat.IsRegexSpace(c)) clean.Append(c);
        if (clean.Length == 0) return "";

        int len = clean.Length;
        int rows = (int)Math.Ceiling((double)len / diameter);
        var grid = new char[rows, diameter];
        int k = 0;
        for (int col = 0; col < diameter; col++)
            for (int row = 0; row < rows; row++)
                grid[row, col] = k < len ? clean[k++] : 'X';

        var sb = new StringBuilder(rows * diameter);
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < diameter; col++)
                sb.Append(grid[row, col]);
        return sb.ToString();
    }

    public static string Decrypt(string text, int diameter)
    {
        if (text.Length == 0 || diameter <= 0) return text;
        int len = text.Length;
        int rows = (int)Math.Ceiling((double)len / diameter);
        var grid = new char[rows, diameter]; // 기본값 '\0' = 빈칸
        int k = 0;
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < diameter; col++)
                if (k < len) grid[row, col] = text[k++];

        var sb = new StringBuilder(len);
        for (int col = 0; col < diameter; col++)
            for (int row = 0; row < rows; row++)
                if (grid[row, col] != '\0') sb.Append(grid[row, col]);
        return sb.ToString();
    }
}

/// <summary>
/// 비즈네르. Java 와 똑같이 동작하도록 Java 의 글자 판별(Character.isLetter)을 그대로 씀.
/// 주의: Java 버전에서는 한글도 "글자"로 판별돼서, 아래쪽의 한글 전용 처리에는 들어가지 않음
/// (한글을 넣으면 복호화할 수 없는 결과가 나옴). 호환을 위해 그대로 둠.
/// </summary>
public static class VigenereCipher
{
    public static string Encrypt(string text, string key) => Transform(text, key, true);

    public static string Decrypt(string text, string key) => Transform(text, key, false);

    private static string Transform(string text, string key, bool encrypt)
    {
        if (key.Length == 0) return text;
        var sb = new StringBuilder(text.Length);
        int keyIndex = 0;
        key = JavaCompat.ToUpper(key);
        foreach (char c in text)
        {
            if (JavaCompat.IsLetter(c))
            {
                char @base = JavaCompat.IsUpperCase(c) ? 'A' : 'a';
                int keyShift = key[keyIndex % key.Length] - 'A';
                if (!encrypt) keyShift = 26 - (keyShift % 26);
                sb.Append((char)(@base + (c - @base + keyShift) % 26));
                keyIndex++;
            }
            else if (c >= '가' && c <= '힣')
            {
                int keyShift = key[keyIndex % key.Length];
                if (!encrypt) keyShift = 11172 - (keyShift % 11172);
                sb.Append((char)('가' + (c - '가' + keyShift) % 11172));
                keyIndex++;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

/// <summary>모스 부호. 글자 사이는 공백, 단어 사이는 " / "</summary>
public static class MorseCode
{
    private static readonly Dictionary<char, string> CharToMorse = new()
    {
        ['A'] = ".-", ['B'] = "-...", ['C'] = "-.-.", ['D'] = "-..", ['E'] = ".", ['F'] = "..-.",
        ['G'] = "--.", ['H'] = "....", ['I'] = "..", ['J'] = ".---", ['K'] = "-.-", ['L'] = ".-..",
        ['M'] = "--", ['N'] = "-.", ['O'] = "---", ['P'] = ".--.", ['Q'] = "--.-", ['R'] = ".-.",
        ['S'] = "...", ['T'] = "-", ['U'] = "..-", ['V'] = "...-", ['W'] = ".--", ['X'] = "-..-",
        ['Y'] = "-.--", ['Z'] = "--..",
        ['1'] = ".----", ['2'] = "..---", ['3'] = "...--", ['4'] = "....-", ['5'] = ".....",
        ['6'] = "-....", ['7'] = "--...", ['8'] = "---..", ['9'] = "----.", ['0'] = "-----",
        ['.'] = ".-.-.-", [','] = "--..--", ['?'] = "..--..",
    };

    private static readonly Dictionary<string, char> MorseToChar = CharToMorse.ToDictionary(p => p.Value, p => p.Key);

    private static readonly Regex MorseOnly = new("^[.\\-/ \\t\\n\\u000B\\f\\r]+$", RegexOptions.CultureInvariant);

    public static string ToMorse(string text)
    {
        if (text.Length == 0) return "";
        var words = JavaCompat.Split(JavaCompat.ToUpper(text), JavaCompat.RegexSpaces);
        return string.Join(" / ", words.Select(word =>
            string.Join(" ", word.Select(c => CharToMorse.GetValueOrDefault(c)).Where(m => m != null))));
    }

    public static string FromMorse(string morse)
    {
        if (morse.Length == 0) return "";
        var words = JavaCompat.Split(JavaCompat.Trim(morse), " / ");
        return string.Join(" ", words.Select(word =>
            string.Concat(JavaCompat.Split(JavaCompat.Trim(word), " ")
                .Select(code => MorseToChar.TryGetValue(code, out var ch) ? ch.ToString() : "?"))));
    }

    /// <summary>점, 줄, 슬래시, 공백으로만 이루어져 있으면 모스 부호로 봄</summary>
    public static bool IsMorseCode(string text) => MorseOnly.IsMatch(JavaCompat.Trim(text));
}

/// <summary>이진수: UTF-8 바이트를 8자리씩 공백으로 구분</summary>
public static class BinaryCipher
{
    private static readonly Regex BinaryOnly = new("^[01 \\t\\n\\u000B\\f\\r]+$", RegexOptions.CultureInvariant);

    public static string ToBinary(string text)
    {
        if (text.Length == 0) return "";
        var sb = new StringBuilder();
        foreach (byte b in JavaCompat.Utf8Bytes(text))
            sb.Append(Convert.ToString(b, 2).PadLeft(8, '0')).Append(' ');
        return JavaCompat.Trim(sb.ToString());
    }

    public static string FromBinary(string binary)
    {
        if (binary.Length == 0) return "";
        var parts = JavaCompat.Split(JavaCompat.Trim(binary), JavaCompat.RegexSpaces);
        var bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!JavaCompat.TryParseInt(parts[i], 2, out int value)) return "INVALID BINARY FORMAT";
            bytes[i] = unchecked((byte)value);
        }
        return JavaCompat.Utf8String(bytes);
    }

    public static bool IsBinary(string text) => BinaryOnly.IsMatch(JavaCompat.Trim(text));
}

/// <summary>
/// 한글 자모 암호: 음절을 초성·중성·종성으로 나눠 각각 이동 (받침 유무는 그대로).
/// 예) 이동 값 1: 한 → 갡
/// </summary>
public static class HangulCipher
{
    private const int Base = 0xAC00, Last = 0xD7A3, ChoCount = 19, JungCount = 21, JongCount = 28;

    public static string Encrypt(string text, int shift) => Transform(text, shift);

    public static string Decrypt(string text, int shift) => Transform(text, -shift);

    private static string Transform(string text, int shift)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c < Base || c > Last)
            {
                sb.Append(c);
                continue;
            }
            int offset = c - Base;
            int cho = offset / (JungCount * JongCount);
            int jung = offset % (JungCount * JongCount) / JongCount;
            int jong = offset % JongCount;

            cho = FloorMod(cho + shift, ChoCount);
            jung = FloorMod(jung + shift, JungCount);
            if (jong != 0) jong = FloorMod(jong - 1 + shift, JongCount - 1) + 1;
            sb.Append((char)(Base + (cho * JungCount + jung) * JongCount + jong));
        }
        return sb.ToString();
    }

    // Java Math.floorMod (int 범위에서 넘침 없이)
    private static int FloorMod(int x, int m) => (int)(((long)x % m + m) % m);
}
