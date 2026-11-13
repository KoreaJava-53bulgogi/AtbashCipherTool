using System.Text;
using Atbash.Core.Files;

namespace Atbash.Core.Tests;

public class FileRulesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("atbash-files-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void Detects_text_and_binary_files_like_java()
    {
        // 15,000 바이트 한글: 8KB 경계에서 글자가 잘려도 텍스트로 봄
        Assert.True(FileRules.IsTextFile(Write("ko.txt", Encoding.UTF8.GetBytes(new string('가', 5000)))));
        Assert.True(FileRules.IsTextFile(Write("en.txt", "hello world\n"u8.ToArray())));
        Assert.False(FileRules.IsTextFile(Write("zero.bin", new byte[] { 65, 0, 66 })));
        Assert.False(FileRules.IsTextFile(Write("latin1.txt", new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x20, 0x21 })));
        Assert.False(FileRules.IsTextFile(Path.Combine(_dir, "missing.txt")));
    }

    [Fact]
    public void Reads_utf8_strictly_and_drops_bom()
    {
        var bom = Write("bom.txt", new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("안녕")).ToArray());
        Assert.Equal("안녕", FileRules.ReadUtf8Text(bom));
        var bad = Write("bad.txt", new byte[] { 0x61, 0xFF, 0x62 });
        Assert.Throws<DecoderFallbackException>(() => FileRules.ReadUtf8Text(bad));
    }

    [Fact]
    public void Writes_utf8_without_bom()
    {
        var path = Path.Combine(_dir, "out.txt");
        FileRules.WriteUtf8Text(path, "한글");
        Assert.Equal(Encoding.UTF8.GetBytes("한글"), File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(@"C:\a\photo.PNG", true)]
    [InlineData(@"C:\a\scan.jpeg", true)]
    [InlineData(@"C:\a\doc.txt", false)]
    public void Recognizes_image_extensions(string path, bool image) => Assert.Equal(image, FileRules.IsImage(path));

    [Theory]
    [InlineData(@"C:\a\report.pdf.enc", @"C:\a\report.pdf")]
    [InlineData(@"C:\a\REPORT.ENC", @"C:\a\REPORT")]
    [InlineData(@"C:\a\data.bin", @"C:\a\data.bin.dec")]
    public void Names_the_decrypted_file(string input, string expected) =>
        Assert.Equal(expected, FileRules.DecryptedPath(input));

    [Theory]
    [InlineData("", PasswordStrength.None)]
    [InlineData("abc", PasswordStrength.Weak)]
    [InlineData("abcdefgh", PasswordStrength.Weak)]
    [InlineData("Abcdefgh", PasswordStrength.Medium)]
    [InlineData("Abcdefg1", PasswordStrength.Medium)]
    [InlineData("Abcdef1!", PasswordStrength.Strong)]
    [InlineData("비밀번호1234", PasswordStrength.Medium)]
    public void Rates_passwords_like_java(string password, PasswordStrength expected) =>
        Assert.Equal(expected, PasswordRules.Evaluate(password));
}
