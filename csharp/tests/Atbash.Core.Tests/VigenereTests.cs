using Atbash.Core.Ciphers;

namespace Atbash.Core.Tests;

/// <summary>8.1.1 에서 고친 비즈네르: 어떤 텍스트·키워드든 복호화하면 원문으로 돌아와야 함</summary>
public class VigenereTests
{
    public static TheoryData<string, string> Cases()
    {
        var texts = new[]
        {
            "Hello, World!", "안녕하세요 아트배쉬 암호 7", "한글 Hangul 혼합 123 !?", "가힣 ㄱㅏ",
            "Émile Zoë ß", "日本語 カタカナ", "emoji 😀 and text", "",
        };
        var keys = new[] { "KEY", "key", "LEMON", "가나", "A1!", "keyword with space", "9", "ß", "😀" };
        var data = new TheoryData<string, string>();
        foreach (var t in texts)
            foreach (var k in keys)
                data.Add(t, k);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Decrypt_returns_the_original(string text, string key) =>
        Assert.Equal(text, VigenereCipher.Decrypt(VigenereCipher.Encrypt(text, key), key));

    [Fact]
    public void Korean_text_is_actually_encrypted()
    {
        var encrypted = VigenereCipher.Encrypt("안녕하세요", "KEY");
        Assert.NotEqual("안녕하세요", encrypted);
        Assert.All(encrypted, c => Assert.InRange(c, '가', '힣')); // 한글은 한글로
    }

    [Fact]
    public void English_with_letter_key_is_the_classic_result() =>
        Assert.Equal("LXFOPVEFRNHR", VigenereCipher.Encrypt("ATTACKATDAWN", "LEMON"));

    [Fact]
    public void Non_letters_are_kept_and_do_not_use_the_key()
    {
        Assert.Equal("Rijvs, Uyvjn!", VigenereCipher.Encrypt("Hello, World!", "KEY"));
        Assert.Equal("é 😀 ã", VigenereCipher.Encrypt("é 😀 ã", "KEY"));
    }
}
