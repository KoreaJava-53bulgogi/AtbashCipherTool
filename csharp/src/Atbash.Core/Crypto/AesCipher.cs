using System.Security.Cryptography;

namespace Atbash.Core.Crypto;

/// <summary>
/// AES-256-GCM 암호화. Java 버전(AESFileCipher.java)과 형식이 같아서 서로 만든 결과를 풀 수 있음.
///   키:   PBKDF2-HMAC-SHA256(비밀번호 UTF-8, salt, 65,536회) → 256비트
///   형식: salt(16) | iv(12) | 암호문 | 인증 태그(16)
/// 텍스트 암호화는 위 바이트 전체를 Base64 문자열로 만든 것.
/// </summary>
public static class AesCipher
{
    public const int SaltLength = 16;
    public const int IvLength = 12;
    public const int TagLength = 16;
    public const int Iterations = 65536;
    public const int KeyLength = 32;
    public const string FileExtension = ".enc";

    private const int HeaderLength = SaltLength + IvLength;

    /// <summary>비밀번호가 틀렸거나 내용이 손상됨</summary>
    public sealed class WrongPasswordException : Exception
    {
        public WrongPasswordException(Exception inner) : base("Wrong password or corrupted data", inner) { }
    }

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(JavaCompat.Utf8Bytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyLength);

    /// <summary>바이트를 암호화해 salt | iv | 암호문 | 태그 형식으로 돌려줌</summary>
    public static byte[] Encrypt(byte[] plain, string password)
    {
        var output = new byte[HeaderLength + plain.Length + TagLength];
        var salt = output.AsSpan(0, SaltLength);
        var iv = output.AsSpan(SaltLength, IvLength);
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(iv);

        var key = DeriveKey(password, salt.ToArray());
        try
        {
            using var gcm = new AesGcm(key, TagLength);
            gcm.Encrypt(iv, plain,
                output.AsSpan(HeaderLength, plain.Length),
                output.AsSpan(HeaderLength + plain.Length, TagLength));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        return output;
    }

    /// <summary>Encrypt 로 만든 바이트를 풀기</summary>
    /// <exception cref="WrongPasswordException">비밀번호가 틀렸거나 손상됨</exception>
    /// <exception cref="ArgumentException">형식이 아님 (너무 짧음)</exception>
    public static byte[] Decrypt(byte[] data, string password)
    {
        if (data.Length < HeaderLength + TagLength) throw new ArgumentException("too short");
        var salt = data.AsSpan(0, SaltLength).ToArray();
        var iv = data.AsSpan(SaltLength, IvLength);
        int cipherLength = data.Length - HeaderLength - TagLength;
        var plain = new byte[cipherLength];

        var key = DeriveKey(password, salt);
        try
        {
            using var gcm = new AesGcm(key, TagLength);
            gcm.Decrypt(iv, data.AsSpan(HeaderLength, cipherLength), data.AsSpan(data.Length - TagLength), plain);
        }
        catch (AuthenticationTagMismatchException e)
        {
            throw new WrongPasswordException(e);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        return plain;
    }

    // ---- 텍스트 --------------------------------------------------------------

    /// <summary>텍스트를 암호화해 메신저 등에 붙여넣을 수 있는 Base64 문자열로 만듦</summary>
    public static string EncryptText(string plainText, string password) =>
        Convert.ToBase64String(Encrypt(JavaCompat.Utf8Bytes(plainText), password));

    /// <exception cref="WrongPasswordException">비밀번호가 틀렸거나 손상됨</exception>
    /// <exception cref="ArgumentException">암호문 형식이 아님</exception>
    public static string DecryptText(string encoded, string password)
    {
        var compact = new string(encoded.Where(c => !JavaCompat.IsRegexSpace(c)).ToArray());
        return JavaCompat.Utf8String(Decrypt(JavaCompat.Base64Decode(compact), password));
    }

    // ---- 파일 ----------------------------------------------------------------

    /// <summary>파일을 암호화해 outputPath 에 저장 (Java 와 같은 .enc 형식)</summary>
    public static void EncryptFile(string inputPath, string outputPath, string password)
    {
        var encrypted = Encrypt(File.ReadAllBytes(inputPath), password);
        WriteAtomically(outputPath, encrypted);
    }

    /// <summary>
    /// .enc 파일을 풀어 outputPath 에 저장. 비밀번호가 틀리면 아무 파일도 만들지 않음
    /// (Java 버전은 이때 빈 파일이 남았음).
    /// </summary>
    public static void DecryptFile(string inputPath, string outputPath, string password)
    {
        var data = File.ReadAllBytes(inputPath);
        if (data.Length < SaltLength) throw new IOException("Cannot read the salt. The file may be damaged or not encrypted.");
        if (data.Length < HeaderLength) throw new IOException("Cannot read the IV. The file may be damaged or not encrypted.");
        if (data.Length < HeaderLength + TagLength) throw new WrongPasswordException(new CryptographicException("too short"));
        WriteAtomically(outputPath, Decrypt(data, password));
    }

    /// <summary>임시 파일에 다 쓴 뒤 이름을 바꿔서, 중간에 실패해도 반쯤 쓰인 파일이 남지 않게 함</summary>
    private static void WriteAtomically(string path, byte[] content)
    {
        var full = Path.GetFullPath(path);
        var temp = full + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            File.WriteAllBytes(temp, content);
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>파일을 무작위 데이터로 덮어쓴 뒤 삭제 (복구를 어렵게 함)</summary>
    public static void SecureWipe(string path)
    {
        if (!File.Exists(path)) return;
        long length = new FileInfo(path).Length;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            var buffer = new byte[4096];
            long pos = 0;
            while (pos < length)
            {
                RandomNumberGenerator.Fill(buffer);
                int n = (int)Math.Min(buffer.Length, length - pos);
                fs.Write(buffer, 0, n);
                pos += n;
            }
            fs.Flush(flushToDisk: true);
        }
        File.Delete(path);
    }
}
