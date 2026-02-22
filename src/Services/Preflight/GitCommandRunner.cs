using System.Diagnostics;
using System.Text;

namespace PushDataToGitHub.App.Services.Preflight;

public sealed class GitCommandRunner : IGitCommandRunner
{
    public async Task<ProcessCommandResult> RunAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        Action<string>? standardOutputCallback = null,
        Action<string>? standardErrorCallback = null)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using Process process = new()
        {
            StartInfo = startInfo
        };

        StringBuilder outputBuilder = new();
        StringBuilder errorBuilder = new();

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrEmpty(eventArgs.Data))
            {
                outputBuilder.AppendLine(eventArgs.Data);
                standardOutputCallback?.Invoke(eventArgs.Data);
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrEmpty(eventArgs.Data))
            {
                errorBuilder.AppendLine(eventArgs.Data);
                standardErrorCallback?.Invoke(eventArgs.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessCommandResult(-1, string.Empty, "Không thể khởi chạy tiến trình git.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);

            return new ProcessCommandResult(
                process.ExitCode,
                outputBuilder.ToString().Trim(),
                errorBuilder.ToString().Trim());
        }
        catch (Exception exception)
        {
            return new ProcessCommandResult(
                -1,
                outputBuilder.ToString().Trim(),
                exception.Message);
        }
    }
}
