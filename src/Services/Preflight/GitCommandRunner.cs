using System.Diagnostics;
using System.IO;
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

        try
        {
            if (!process.Start())
            {
                return new ProcessCommandResult(-1, string.Empty, "Không thể khởi chạy tiến trình git.");
            }

            // Dùng custom reader xử lý cả \r (progress bar git) và \n (dòng thường).
            // BeginOutputReadLine/ErrorReadLine chỉ xử lý \n nên bỏ sót
            // các dòng "Counting objects:", "Writing objects:..." mà git viết bằng \r.
            Task stdoutTask = ReadStreamAsync(process.StandardOutput, outputBuilder, standardOutputCallback, cancellationToken);
            Task stderrTask = ReadStreamAsync(process.StandardError, errorBuilder, standardErrorCallback, cancellationToken);

            await Task.WhenAll(process.WaitForExitAsync(cancellationToken), stdoutTask, stderrTask);

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

    /// <summary>
    /// Đọc stream thủ công, xử lý cả \r và \n là line separator.
    /// Git dùng \r để ghi đè dòng progress (Counting objects, Writing objects...).
    /// Nếu dùng BeginOutputReadLine, các dòng này sẽ bị bỏ qua cho đến khi có \n cuối cùng.
    /// </summary>
    private static async Task ReadStreamAsync(
        StreamReader reader,
        StringBuilder fullOutput,
        Action<string>? callback,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        StringBuilder lineBuffer = new();

        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                for (int i = 0; i < read; i++)
                {
                    char c = buffer[i];

                    if (c == '\n')
                    {
                        // Bỏ '\r' ở cuối nếu là chuỗi \r\n
                        if (lineBuffer.Length > 0 && lineBuffer[lineBuffer.Length - 1] == '\r')
                        {
                            lineBuffer.Length--;
                        }

                        FlushLine(lineBuffer, fullOutput, callback);
                    }
                    else if (c == '\r')
                    {
                        // Carriage return đơn = dòng progress (git ghi đè lên dòng trước)
                        FlushLine(lineBuffer, fullOutput, callback);
                    }
                    else
                    {
                        lineBuffer.Append(c);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Bỏ qua: process vẫn chạy nền
        }

        // Flush phần còn lại trong buffer chưa kết thúc bằng newline
        FlushLine(lineBuffer, fullOutput, callback);
    }

    private static void FlushLine(StringBuilder lineBuffer, StringBuilder fullOutput, Action<string>? callback)
    {
        if (lineBuffer.Length == 0)
        {
            return;
        }

        string line = lineBuffer.ToString();
        lineBuffer.Clear();
        fullOutput.AppendLine(line);
        callback?.Invoke(line);
    }
}
