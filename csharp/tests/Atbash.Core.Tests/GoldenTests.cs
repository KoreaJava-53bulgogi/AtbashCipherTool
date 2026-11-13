using System.Text;
using System.Text.Json;
using Atbash.Core;

namespace Atbash.Core.Tests;

/// <summary>
/// Java 8.1.0 이 낸 결과(TestData/golden.json, tools/golden/GoldenGen.java 로 생성)와
/// C# 결과가 한 글자도 다르지 않은지 확인. 변환 방식마다 테스트 하나.
/// </summary>
public class GoldenTests
{
    private sealed record Vector(int Mode, int Key, string Keyword, string Input, string? Output, string? Error);

    private static readonly Lazy<List<Vector>> Vectors = new(Load);

    private static List<Vector> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "golden.json");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var list = new List<Vector>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            list.Add(new Vector(
                e.GetProperty("mode").GetInt32(),
                e.GetProperty("key").GetInt32(),
                Units(e.GetProperty("keyword")),
                Units(e.GetProperty("input")),
                e.TryGetProperty("output", out var o) ? Units(o) : null,
                e.TryGetProperty("error", out var err) ? err.GetString() : null));
        }
        return list;
    }

    private static string Units(JsonElement array)
    {
        var sb = new StringBuilder();
        foreach (var u in array.EnumerateArray()) sb.Append((char)u.GetInt32());
        return sb.ToString();
    }

    public static TheoryData<int> Modes()
    {
        var data = new TheoryData<int>();
        foreach (var m in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 16, 17, 18, 19 }) data.Add(m);
        return data;
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Matches_java_output(int mode)
    {
        var cases = Vectors.Value.Where(v => v.Mode == mode).ToList();
        Assert.NotEmpty(cases);

        var failures = new List<string>();
        foreach (var v in cases)
        {
            var result = CipherEngine.Transform((CipherMode)v.Mode, v.Input, v.Key, v.Keyword);
            bool ok = v.Error != null
                ? result.Error == TransformError.DecodeFailed
                : result.Succeeded && result.Text == v.Output;
            if (!ok && failures.Count < 15)
            {
                failures.Add($"key={v.Key} keyword=[{Show(v.Keyword)}] input=[{Show(v.Input)}]\n" +
                             $"   java: {(v.Error ?? "[" + Show(v.Output!) + "]")}\n" +
                             $"   c#  : {(result.Succeeded ? "[" + Show(result.Text) + "]" : result.Error.ToString())}");
            }
            else if (!ok) failures.Add("...");
        }
        Assert.True(failures.Count == 0,
            $"{(CipherMode)mode}: {failures.Count(f => f != "...")}+ of {cases.Count} differ from Java\n" +
            string.Join("\n", failures.Where(f => f != "...")));
    }

    [Fact]
    public void Golden_data_covers_every_mode_except_aes()
    {
        var modes = Vectors.Value.Select(v => v.Mode).Distinct().Order().ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 16, 17, 18, 19 }, modes);
        Assert.True(Vectors.Value.Count > 6000);
    }

    // 보이지 않는 글자를 \uXXXX 로 표시
    private static string Show(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c < 0x20 || (c >= 0x7F && c < 0xA0) || char.IsSurrogate(c) || c == 0xA0 || c == 0x2028 || c == 0x3000)
                sb.Append($"\\u{(int)c:X4}");
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
