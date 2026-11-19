using System.Diagnostics;
using System.Text;

namespace MusicPlayer.Services;

public record ProcessResult(int ExitCode, string Output, string Error);

public static class ProcessRunner
{
    public static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        return info;
    }
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token, Action<string>? progress = null)
    {
        using var process = new Process { StartInfo = StartInfo(executable, arguments) };
        process.Start();
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        async Task Read(StreamReader reader, StringBuilder target)
        {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                if (target.Length < 4 * 1024 * 1024) target.AppendLine(line);
                progress?.Invoke(line);
            }
        }
        await Task.WhenAll(Read(process.StandardOutput, stdout), Read(process.StandardError, stderr), process.WaitForExitAsync(token));
        token.ThrowIfCancellationRequested();
        return new(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
