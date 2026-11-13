using Atbash.Core.Ciphers;
using Atbash.Core.Crypto;

namespace Atbash.Core;

/// <summary>
/// 변환 방식. 번호는 Java 버전의 목록 순서이자 저장된 설정(cipherMode) 값이므로 바꾸지 말 것.
/// 새 방식은 맨 뒤에 추가.
/// </summary>
public enum CipherMode
{
    Atbash = 0,
    Morse = 1,
    Binary = 2,
    CaesarEncrypt = 3,
    CaesarDecrypt = 4,
    ScytaleEncrypt = 5,
    ScytaleDecrypt = 6,
    VigenereEncrypt = 7,
    VigenereDecrypt = 8,
    AesTextEncrypt = 9,
    AesTextDecrypt = 10,
    Base64Encode = 11,
    Base64Decode = 12,
    HexEncode = 13,
    HexDecode = 14,
    UrlEncode = 15,
    UrlDecode = 16,
    Rot13 = 17,
    HangulEncrypt = 18,
    HangulDecrypt = 19,
}

/// <summary>변환에 실패했을 때 화면에 보여줄 안내의 종류</summary>
public enum TransformError
{
    None,
    /// <summary>입력이 그 형식으로 인코딩된 문자열이 아님 (언어 파일 DECODE_ERROR)</summary>
    DecodeFailed,
    /// <summary>AES 인데 비밀번호가 비어 있음 (AES_TEXT_NO_PASSWORD)</summary>
    AesNoPassword,
    /// <summary>비밀번호가 틀렸거나 AES 텍스트 암호문이 아님 (AES_TEXT_DECRYPT_ERROR)</summary>
    AesDecryptFailed,
}

public readonly record struct TransformResult(string Text, TransformError Error)
{
    public bool Succeeded => Error == TransformError.None;

    public static TransformResult Ok(string text) => new(text, TransformError.None);

    public static TransformResult Fail(TransformError error) => new("", error);
}

/// <summary>모든 변환의 입구. Java AtbashCipherGUI.performTransformation 과 같은 규칙</summary>
public static class CipherEngine
{
    /// <summary>입력칸 종류: 이동 값/지름(숫자), 키워드, 비밀번호, 없음</summary>
    public enum ParameterKind { None, Shift, Diameter, Keyword, Password }

    public static ParameterKind ParameterFor(CipherMode mode) => mode switch
    {
        CipherMode.CaesarEncrypt or CipherMode.CaesarDecrypt or
        CipherMode.HangulEncrypt or CipherMode.HangulDecrypt => ParameterKind.Shift,
        CipherMode.ScytaleEncrypt or CipherMode.ScytaleDecrypt => ParameterKind.Diameter,
        CipherMode.VigenereEncrypt or CipherMode.VigenereDecrypt => ParameterKind.Keyword,
        CipherMode.AesTextEncrypt or CipherMode.AesTextDecrypt => ParameterKind.Password,
        _ => ParameterKind.None,
    };

    public static bool IsAes(CipherMode mode) => mode is CipherMode.AesTextEncrypt or CipherMode.AesTextDecrypt;

    /// <param name="key">이동 값 또는 지름</param>
    /// <param name="keyword">비즈네르 키워드, 또는 AES 비밀번호</param>
    public static TransformResult Transform(CipherMode mode, string input, int key, string keyword)
    {
        switch (mode)
        {
            case CipherMode.Atbash: return TransformResult.Ok(AtbashCipher.Transform(input));
            case CipherMode.Morse:
                return TransformResult.Ok(MorseCode.IsMorseCode(input) ? MorseCode.FromMorse(input) : MorseCode.ToMorse(input));
            case CipherMode.Binary:
                return TransformResult.Ok(BinaryCipher.IsBinary(input) ? BinaryCipher.FromBinary(input) : BinaryCipher.ToBinary(input));
            case CipherMode.CaesarEncrypt: return TransformResult.Ok(CaesarCipher.Encrypt(input, key));
            case CipherMode.CaesarDecrypt: return TransformResult.Ok(CaesarCipher.Decrypt(input, key));
            case CipherMode.ScytaleEncrypt: return TransformResult.Ok(ScytaleCipher.Encrypt(input, key));
            case CipherMode.ScytaleDecrypt: return TransformResult.Ok(ScytaleCipher.Decrypt(input, key));
            case CipherMode.VigenereEncrypt: return TransformResult.Ok(VigenereCipher.Encrypt(input, keyword));
            case CipherMode.VigenereDecrypt: return TransformResult.Ok(VigenereCipher.Decrypt(input, keyword));
        }

        // 8.0.0 에서 추가된 변환: 되돌리기에 실패하면 안내 문구 종류를 돌려줌
        try
        {
            return mode switch
            {
                CipherMode.AesTextEncrypt or CipherMode.AesTextDecrypt => TransformAes(mode, input, keyword),
                CipherMode.Base64Encode => TransformResult.Ok(TextCodec.ToBase64(input)),
                CipherMode.Base64Decode => TransformResult.Ok(TextCodec.FromBase64(input)),
                CipherMode.HexEncode => TransformResult.Ok(TextCodec.ToHex(input)),
                CipherMode.HexDecode => TransformResult.Ok(TextCodec.FromHex(input)),
                CipherMode.UrlEncode => TransformResult.Ok(TextCodec.ToUrl(input)),
                CipherMode.UrlDecode => TransformResult.Ok(TextCodec.FromUrl(input)),
                CipherMode.Rot13 => TransformResult.Ok(TextCodec.Rot13(input)),
                CipherMode.HangulEncrypt => TransformResult.Ok(HangulCipher.Encrypt(input, key)),
                CipherMode.HangulDecrypt => TransformResult.Ok(HangulCipher.Decrypt(input, key)),
                _ => TransformResult.Ok(""),
            };
        }
        catch (Exception)
        {
            return TransformResult.Fail(IsAes(mode) ? TransformError.AesDecryptFailed : TransformError.DecodeFailed);
        }
    }

    private static TransformResult TransformAes(CipherMode mode, string input, string password)
    {
        if (string.IsNullOrEmpty(password)) return TransformResult.Fail(TransformError.AesNoPassword);
        if (input.Length == 0) return TransformResult.Ok("");
        return TransformResult.Ok(mode == CipherMode.AesTextEncrypt
            ? AesCipher.EncryptText(input, password)
            : AesCipher.DecryptText(input, password));
    }
}
