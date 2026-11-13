using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Atbash.Core;
using Atbash.Core.Crypto;

namespace Atbash.Core.Tests;

/// <summary>
/// AES 가 Java 버전과 서로 호환되는지 확인.
///   Java → C#: TestData/aes 의 Java 결과물을 C# 으로 풀기 (항상 실행)
///   C# → Java: C# 결과물을 Java(AesVerify)로 풀기 (Java 와 컴파일된 클래스가 있을 때만 실행)
/// </summary>
public class AesTests
{
    private const string FilePassword = "파일-pw-1234";
    private static readonly string AesDir = Path.Combine(AppContext.BaseDirectory, "TestData", "aes");

    public static TheoryData<string> JavaEncryptedFiles()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "aes"), "*.enc").Order())
            data.Add(Path.GetFileName(f));
        return data;
    }

    // ---- Java → C# ------------------------------------------------------------

    [Fact]
    public void Decrypts_text_encrypted_by_java()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AesDir, "text.json")));
        int count = 0;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var password = Units(e.GetProperty("password"));
            var plain = Units(e.GetProperty("plain"));
            Assert.Equal(plain, AesCipher.DecryptText(e.GetProperty("cipher").GetString()!, password));
            count++;
        }
        Assert.Equal(6, count);
    }

    [Theory]
    [MemberData(nameof(JavaEncryptedFiles))]
    public void Decrypts_file_encrypted_by_java(string encName)
    {
        var output = Path.Combine(Path.GetTempPath(), "atbash-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            AesCipher.DecryptFile(Path.Combine(AesDir, encName), output, FilePassword);
            var original = File.ReadAllBytes(Path.Combine(AesDir, encName[..^".enc".Length]));
            Assert.Equal(original, File.ReadAllBytes(output));
        }
        finally
        {
            File.Delete(output);
        }
    }

    // ---- C# 자체 동작 -----------------------------------------------------------

    [Fact]
    public void Text_roundtrip_and_random_salt()
    {
        const string text = "안녕 Hello 😀";
        var a = AesCipher.EncryptText(text, "pw");
        var b = AesCipher.EncryptText(text, "pw");
        Assert.NotEqual(a, b); // salt/iv 가 매번 달라야 함
        Assert.Equal(text, AesCipher.DecryptText(a, "pw"));
        Assert.Equal(text, AesCipher.DecryptText(b, "pw"));
    }

    [Fact]
    public void Wrong_password_and_garbage_are_reported()
    {
        var enc = AesCipher.EncryptText("secret", "right");
        Assert.Throws<AesCipher.WrongPasswordException>(() => AesCipher.DecryptText(enc, "wrong"));
        Assert.ThrowsAny<ArgumentException>(() => AesCipher.DecryptText("abc", "pw"));

        // 화면에 보여줄 안내 종류
        Assert.Equal(TransformError.AesDecryptFailed, CipherEngine.Transform(CipherMode.AesTextDecrypt, enc, 0, "wrong").Error);
        Assert.Equal(TransformError.AesDecryptFailed, CipherEngine.Transform(CipherMode.AesTextDecrypt, "not cipher", 0, "pw").Error);
        Assert.Equal(TransformError.AesNoPassword, CipherEngine.Transform(CipherMode.AesTextEncrypt, "x", 0, "").Error);
        Assert.Equal("", CipherEngine.Transform(CipherMode.AesTextEncrypt, "", 0, "pw").Text);
    }

    [Fact]
    public void Failed_file_decryption_leaves_no_output_file()
    {
        var dir = Directory.CreateTempSubdirectory("atbash-test-").FullName;
        try
        {
            var plain = Path.Combine(dir, "a.txt");
            File.WriteAllText(plain, "hello");
            AesCipher.EncryptFile(plain, plain + ".enc", "right");
            var output = Path.Combine(dir, "out.txt");
            Assert.Throws<AesCipher.WrongPasswordException>(() => AesCipher.DecryptFile(plain + ".enc", output, "wrong"));
            // 결과 파일도, 쓰다 만 임시 파일도 남지 않아야 함
            Assert.Empty(Directory.GetFiles(dir, "out.txt*"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Secure_wipe_deletes_the_file()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, new byte[10000]);
        AesCipher.SecureWipe(path);
        Assert.False(File.Exists(path));
    }

    // ---- C# → Java ------------------------------------------------------------

    public static TheoryData<string, string> TextSamples() => new()
    {
        { "pw1234", "Hello, World!" },
        { "비밀번호", "안녕하세요 아트배쉬 암호 7" },
        { "😀🔑", "mixed 한글 English 日本語 😀\nline2" },
    };

    [Theory]
    [MemberData(nameof(TextSamples))]
    public void Java_decrypts_text_encrypted_by_csharp(string password, string plain)
    {
        var java = JavaInterop.RequireOrSkip();
        var dir = Directory.CreateTempSubdirectory("atbash-test-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "pw.txt"), password, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "cipher.txt"), AesCipher.EncryptText(plain, password), new UTF8Encoding(false));
            var result = java.Run("text", Path.Combine(dir, "pw.txt"), Path.Combine(dir, "cipher.txt"), Path.Combine(dir, "out.txt"));
            Assert.Equal("OK", result);
            Assert.Equal(plain, File.ReadAllText(Path.Combine(dir, "out.txt"), Encoding.UTF8));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(100003)]
    public void Java_decrypts_file_encrypted_by_csharp(int size)
    {
        var java = JavaInterop.RequireOrSkip();
        var dir = Directory.CreateTempSubdirectory("atbash-test-").FullName;
        try
        {
            var data = new byte[size];
            new Random(size).NextBytes(data);
            var plain = Path.Combine(dir, "plain.bin");
            File.WriteAllBytes(plain, data);
            File.WriteAllText(Path.Combine(dir, "pw.txt"), FilePassword, new UTF8Encoding(false));
            AesCipher.EncryptFile(plain, plain + ".enc", FilePassword);

            var result = java.Run("file", Path.Combine(dir, "pw.txt"), plain + ".enc", Path.Combine(dir, "out.bin"));
            Assert.Equal("OK", result);
            Assert.Equal(data, File.ReadAllBytes(Path.Combine(dir, "out.bin")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string Units(JsonElement array)
    {
        var sb = new StringBuilder();
        foreach (var u in array.EnumerateArray()) sb.Append((char)u.GetInt32());
        return sb.ToString();
    }
}

/// <summary>Java 버전 클래스(target/classes)로 AesVerify 를 실행하는 도우미</summary>
internal sealed class JavaInterop
{
    private readonly string _java;
    private readonly string _classpath;

    private JavaInterop(string java, string classpath)
    {
        _java = java;
        _classpath = classpath;
    }

    /// <summary>Java, 컴파일된 Java 클래스, AesVerify 가 없으면 테스트를 건너뜀</summary>
    public static JavaInterop RequireOrSkip()
    {
        var root = FindRepoRoot();
        if (root == null) Assert.Skip("repo root (pom.xml) not found");
        var classes = Path.Combine(root, "target", "classes");
        var tools = Path.Combine(root, "csharp", "tools", "golden", "out");
        if (!File.Exists(Path.Combine(classes, "AESFileCipher.class")))
            Assert.Skip("Java classes not built (run mvn compile)");

        var java = FindJdkTool("java");
        if (java == null) Assert.Skip("java not found");
        if (!File.Exists(Path.Combine(tools, "AesVerify.class")))
        {
            var javac = FindJdkTool("javac");
            if (javac == null) Assert.Skip("javac not found");
            Exec(javac, "-encoding", "UTF-8", "-cp", classes, "-d", tools,
                Path.Combine(root, "csharp", "tools", "golden", "AesVerify.java"));
        }
        return new JavaInterop(java, classes + Path.PathSeparator + tools);
    }

    public string Run(params string[] args) =>
        Exec(_java, new[] { "-cp", _classpath, "AesVerify" }.Concat(args).ToArray()).Trim();

    private static string Exec(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return stdout.Length > 0 ? stdout : stderr;
    }

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "pom.xml"))) return dir.FullName;
        return null;
    }

    private static string? FindJdkTool(string name)
    {
        var candidates = new List<string>();
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome)) candidates.Add(Path.Combine(javaHome, "bin", name + ".exe"));
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var javaDir = Path.Combine(programFiles, "Java");
        if (Directory.Exists(javaDir))
            candidates.AddRange(Directory.GetDirectories(javaDir, "jdk-*").OrderDescending()
                .Select(d => Path.Combine(d, "bin", name + ".exe")));
        return candidates.FirstOrDefault(File.Exists);
    }
}
