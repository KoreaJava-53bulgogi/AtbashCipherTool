using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Atbash.App.Services;

/// <summary>
/// 사용자가 무언가를 설치해야 OCR 을 쓸 수 있음. <see cref="MessageKey"/> 는 사용자에게 보여줄 안내 문구(언어 파일의 키).
/// </summary>
public sealed class OcrSetupRequiredException(string messageKey, string message) : Exception(message)
{
    public string MessageKey { get; } = messageKey;
}

/// <summary>
/// Linux: 시스템에 설치된 tesseract 명령으로 글자를 읽음.
/// NuGet 의 Tesseract 패키지에는 Linux 용 라이브러리가 없고, 배포판마다 라이브러리 버전·이름이 달라서
/// 명령(tesseract 그림 stdout)을 실행하는 쪽이 가장 덜 깨짐.
/// 언어 데이터는 앱에 들어 있는 tessdata(Windows 판과 같은 파일)를 씀 → 사용자는 tesseract-ocr 하나만 설치하면 됨
/// (tesseract-ocr-kor 같은 언어 패키지는 필요 없음).
/// </summary>
public sealed class TesseractCliOcrEngine : IOcrEngine
{
    /// <summary>PATH 에서 tesseract 를 찾는 엔진 (실제 앱)</summary>
    public static TesseractCliOcrEngine System { get; } = new(() => FindOnPath("tesseract"), OcrService.DataPath);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);
    private readonly Func<string?> _findExecutable;
    private readonly string _dataPath;

    /// <param name="findExecutable">tesseract 실행 파일의 경로 (없으면 null). 테스트에서 바꿈</param>
    public TesseractCliOcrEngine(Func<string?> findExecutable, string dataPath)
    {
        _findExecutable = findExecutable;
        _dataPath = dataPath;
    }

    public bool IsSupported => true;

    public bool IsAvailable(out string reason)
    {
        if (_findExecutable() == null) reason = "tesseract command not found on PATH (install tesseract-ocr)";
        else if (!File.Exists(Path.Combine(_dataPath, "kor.traineddata"))) reason = "tessdata missing: " + _dataPath;
        else reason = "";
        return reason.Length == 0;
    }

    /// <exception cref="OcrSetupRequiredException">tesseract 가 설치되어 있지 않음</exception>
    public string Recognize(byte[] imageBytes)
    {
        var exe = _findExecutable()
            ?? throw new OcrSetupRequiredException("OCR_LINUX_INSTALL_MESSAGE", "tesseract is not installed.");
        var prepared = OcrImage.Prepare(imageBytes);

        var input = Path.Combine(Path.GetTempPath(), "atbash-ocr-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(input, prepared);
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in new[] { input, "stdout", "--tessdata-dir", _dataPath, "-l", "kor+eng" })
                psi.ArgumentList.Add(a);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start tesseract.");
            // 두 출력을 동시에 읽어야 버퍼가 가득 차서 멈추는 일이 없음
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new TimeoutException("tesseract took too long.");
            }
            Task.WaitAll(stdout, stderr);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("tesseract failed: " + stderr.Result.Trim());
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
