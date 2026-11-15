using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Atbash.App.Services;

/// <summary>
/// 글자를 읽는 외부 프로그램(Linux 의 tesseract, macOS 의 atbash-vision-ocr)을 실행하는 공통 부분.
/// 그림을 임시 파일로 쓰고, 프로그램을 실행해 표준 출력(UTF-8)을 돌려줌. 임시 파일은 항상 지움.
/// </summary>
internal static class OcrProcess
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    /// <param name="arguments">{input} 자리에 임시 그림 파일 경로가 들어감</param>
    /// <exception cref="InvalidOperationException">프로그램이 실패함 (오류 출력이 메시지에 들어감)</exception>
    /// <exception cref="TimeoutException">너무 오래 걸림</exception>
    public static string Run(string exe, byte[] image, IEnumerable<string> arguments, string extension = ".png", TimeSpan? timeout = null)
    {
        var input = Path.Combine(Path.GetTempPath(), "atbash-ocr-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            File.WriteAllBytes(input, image);
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in arguments.Select(a => a == "{input}" ? input : a))
                psi.ArgumentList.Add(a);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + Path.GetFileName(exe) + ".");
            // 두 출력을 동시에 읽어야 버퍼가 가득 차서 멈추는 일이 없음
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout ?? DefaultTimeout))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new TimeoutException(Path.GetFileName(exe) + " took too long.");
            }
            Task.WaitAll(stdout, stderr);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(Path.GetFileName(exe) + " failed: " + stderr.Result.Trim());
            return stdout.Result;
        }
        finally
        {
            try { File.Delete(input); } catch (IOException) { }
        }
    }

    /// <summary>PATH 에서 실행 파일 찾기</summary>
    public static string? FindOnPath(string name)
    {
        var file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, file))
            .FirstOrDefault(File.Exists);
    }
}
