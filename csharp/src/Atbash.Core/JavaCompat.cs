using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Atbash.Core;

/// <summary>
/// Java 버전과 결과를 똑같이 맞추기 위한 도우미.
/// .NET 과 Java 는 "공백", "대문자 바꾸기", "글자 판별", UTF-8 오류 처리 규칙이 조금씩 다르다.
/// 그래서 Java 에서 뽑아 둔 표(Resources/java-chars.bin, tools/golden/JavaCharTables.java)와
/// Java 의 동작을 그대로 따라 한 함수를 쓴다.
/// </summary>
internal static class JavaCompat
{
    private static readonly byte[] LetterBits = new byte[8192];
    private static readonly byte[] UpperBits = new byte[8192]; // Java Character.isUpperCase (현재 미사용)
    private static readonly Dictionary<int, string> UpperMap = new();

    static JavaCompat()
    {
        using var stream = typeof(JavaCompat).Assembly.GetManifestResourceStream("Atbash.Core.Resources.java-chars.bin")
            ?? throw new InvalidOperationException("java-chars.bin resource is missing");
        using var reader = new BinaryReader(stream);
        reader.Read(LetterBits, 0, LetterBits.Length);
        reader.Read(UpperBits, 0, UpperBits.Length);
        int count = ReadInt(reader);
        for (int i = 0; i < count; i++)
        {
            int cp = ReadInt(reader);
            int len = BinaryPrimitives.ReverseEndianness(reader.ReadUInt16());
            var chars = new char[len];
            for (int j = 0; j < len; j++) chars[j] = (char)BinaryPrimitives.ReverseEndianness(reader.ReadUInt16());
            UpperMap[cp] = new string(chars);
        }
    }

    private static int ReadInt(BinaryReader r) => BinaryPrimitives.ReverseEndianness(r.ReadInt32());

    /// <summary>Java Character.isLetter(char) (8.1.1 의 비즈네르 수정 이후로는 쓰는 곳 없음, 표 호환을 위해 남김)</summary>
    public static bool IsLetter(char c) => (LetterBits[c >> 3] & (1 << (c & 7))) != 0;

    /// <summary>Java String.toUpperCase() (ß → SS 처럼 길이가 바뀌는 경우 포함)</summary>
    public static string ToUpper(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            int cp = s[i];
            int width = 1;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                width = 2;
            }
            if (UpperMap.TryGetValue(cp, out var upper)) sb.Append(upper);
            else sb.Append(s, i, width);
            i += width - 1;
        }
        return sb.ToString();
    }

    /// <summary>Java 정규식의 \s (ASCII 공백 6가지만)</summary>
    public static bool IsRegexSpace(char c) => c is ' ' or '\t' or '\n' or '\u000B' or '\f' or '\r';

    /// <summary>Java 정규식 "\\s+" 를 .NET 으로 옮긴 것 (.NET 의 \s 는 유니코드 공백까지 포함하므로 사용하지 않음)</summary>
    public const string RegexSpaces = "[ \\t\\n\\u000B\\f\\r]+";

    /// <summary>Java String.trim(): 앞뒤의 U+0020 이하 글자를 지움</summary>
    public static string Trim(string s)
    {
        int start = 0, end = s.Length;
        while (start < end && s[start] <= ' ') start++;
        while (end > start && s[end - 1] <= ' ') end--;
        return s.Substring(start, end - start);
    }

    /// <summary>
    /// Java String.split(regex): 하나도 안 맞으면 [원래 문자열], 맞으면 나눈 뒤 끝쪽 빈 문자열을 버림.
    /// </summary>
    public static string[] Split(string input, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        if (!regex.IsMatch(input)) return new[] { input };
        var parts = regex.Split(input).ToList();
        while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        return parts.ToArray();
    }

    /// <summary>Java String.getBytes(UTF_8): 짝이 안 맞는 서로게이트는 '?' 로 바꿈</summary>
    public static byte[] Utf8Bytes(string s) => Utf8WithQuestionMark.GetBytes(s);

    private static readonly Encoding Utf8WithQuestionMark =
        Encoding.GetEncoding("utf-8", new EncoderReplacementFallback("?"), new DecoderReplacementFallback("�"));

    /// <summary>Java new String(bytes, UTF_8): 잘못된 바이트는 U+FFFD 로 바꿈</summary>
    public static string Utf8String(byte[] bytes, int offset, int count) => Encoding.UTF8.GetString(bytes, offset, count);

    public static string Utf8String(byte[] bytes) => Utf8String(bytes, 0, bytes.Length);

    /// <summary>엄격한 UTF-8 해석: 잘못된 바이트가 있으면 예외 (Java CodingErrorAction.REPORT)</summary>
    public static string Utf8StringStrict(byte[] bytes) => StrictUtf8.GetString(bytes);

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <summary>
    /// Java Integer.parseInt(s, radix): 앞에 +/- 허용, int 범위를 넘으면 실패.
    /// </summary>
    public static bool TryParseInt(string s, int radix, out int value)
    {
        value = 0;
        if (s.Length == 0) return false;
        int i = 0;
        bool negative = false;
        if (s[0] is '+' or '-')
        {
            negative = s[0] == '-';
            i = 1;
            if (s.Length == 1) return false;
        }
        long result = 0;
        long limit = negative ? 2147483648L : int.MaxValue;
        for (; i < s.Length; i++)
        {
            int digit = HexDigit(s[i]);
            if (digit < 0 || digit >= radix) return false;
            result = result * radix + digit;
            if (result > limit) return false;
        }
        value = (int)(negative ? -result : result);
        return true;
    }

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'z' => c - 'a' + 10,
        >= 'A' and <= 'Z' => c - 'A' + 10,
        _ => -1
    };

    // ---- Java java.util.Base64.getDecoder() (기본 디코더) 를 그대로 옮김 --------------------

    private static readonly int[] Base64Table = BuildBase64Table();

    private static int[] BuildBase64Table()
    {
        var t = Enumerable.Repeat(-1, 256).ToArray();
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        for (int i = 0; i < alphabet.Length; i++) t[alphabet[i]] = i;
        t['='] = -2;
        return t;
    }

    /// <summary>Java Base64.getDecoder().decode(String). 잘못된 입력이면 ArgumentException</summary>
    public static byte[] Base64Decode(string s)
    {
        // Java 는 문자열을 ISO-8859-1 로 바꿔서 읽음: 0xFF 보다 큰 글자는 '?' 가 되어 잘못된 글자로 처리됨
        var src = new int[s.Length];
        for (int i = 0; i < s.Length; i++) src[i] = s[i] <= 0xFF ? s[i] : '?';
        int sl = src.Length;

        // decodedOutLength
        if (sl == 0) return Array.Empty<byte>();
        if (sl < 2) throw new ArgumentException("Input byte[] should at least have 2 bytes for base64 bytes");
        int paddings = 0;
        if (src[sl - 1] == '=')
        {
            paddings++;
            if (src[sl - 2] == '=') paddings++;
        }
        if (paddings == 0 && (sl & 0x3) != 0) paddings = 4 - (sl & 0x3);
        long outLength = 3 * ((sl + 3L) / 4) - paddings;
        var dst = new List<byte>((int)Math.Max(0, outLength));

        // decode0
        int sp = 0, bits = 0, shiftto = 18;
        while (sp < sl)
        {
            int b = Base64Table[src[sp++] & 0xFF];
            if (b < 0)
            {
                if (b == -2)
                {
                    if ((shiftto == 6 && (sp == sl || src[sp++] != '=')) || shiftto == 18)
                        throw new ArgumentException("Input byte array has wrong 4-byte ending unit");
                    break;
                }
                throw new ArgumentException("Illegal base64 character");
            }
            bits |= b << shiftto;
            shiftto -= 6;
            if (shiftto < 0)
            {
                dst.Add((byte)(bits >> 16));
                dst.Add((byte)(bits >> 8));
                dst.Add((byte)bits);
                shiftto = 18;
                bits = 0;
            }
        }
        if (shiftto == 6) dst.Add((byte)(bits >> 16));
        else if (shiftto == 0)
        {
            dst.Add((byte)(bits >> 16));
            dst.Add((byte)(bits >> 8));
        }
        else if (shiftto == 12) throw new ArgumentException("Last unit does not have enough valid bits");
        if (sp < sl) throw new ArgumentException("Input byte array has incorrect ending byte at " + sp);
        return dst.ToArray();
    }
}
