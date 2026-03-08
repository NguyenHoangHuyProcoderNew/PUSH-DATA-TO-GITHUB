using System.Diagnostics;
using System.Text;
using PushDataToGitHub.App.Models;
using PushDataToGitHub.App.Services.Preflight;

namespace PushDataToGitHub.App.Services.Security;

public sealed class CredentialService : ICredentialService
{
    private readonly IGitCommandRunner _gitCommandRunner;

    public CredentialService(IGitCommandRunner gitCommandRunner)
    {
        _gitCommandRunner = gitCommandRunner;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(
        AuthenticationMode mode,
        string repositoryUrl,
        string? personalAccessToken,
        Action<string, string> log,
        CancellationToken cancellationToken = default)
    {
        return mode switch
        {
            AuthenticationMode.BrowserDevice => await AuthenticateBrowserAsync(log, cancellationToken),
            AuthenticationMode.PersonalAccessToken => await AuthenticatePatAsync(repositoryUrl, personalAccessToken, log, cancellationToken),
            _ => new AuthenticationResult
            {
                IsSuccess = false,
                Message = "Chế độ xác thực không hợp lệ."
            }
        };
    }

    private async Task<AuthenticationResult> AuthenticateBrowserAsync(
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        log("INFO", "Đang mở Git Credential Manager (Browser/Device)...");

        IReadOnlyList<string> gitCommands =
        [
            "credential-manager github login",
            "credential-manager login https://github.com"
        ];

        foreach (string command in gitCommands)
        {
            ProcessCommandResult result = await _gitCommandRunner.RunAsync(command, cancellationToken: cancellationToken);
            if (result.IsSuccess)
            {
                return new AuthenticationResult
                {
                    IsSuccess = true,
                    Message = "Đăng nhập Browser/Device thành công."
                };
            }
        }

        IReadOnlyList<(string FileName, string Arguments)> executableFallbacks =
        [
            ("git-credential-manager.exe", "github login"),
            ("git-credential-manager-core.exe", "github login")
        ];

        foreach ((string fileName, string arguments) in executableFallbacks)
        {
            ProcessCommandResult result = await RunProcessAsync(fileName, arguments, cancellationToken);
            if (result.IsSuccess)
            {
                return new AuthenticationResult
                {
                    IsSuccess = true,
                    Message = "Đăng nhập Browser/Device thành công."
                };
            }
        }

        return new AuthenticationResult
        {
            IsSuccess = false,
            Message = "Không thể mở luồng đăng nhập Browser/Device. Hãy thử mode PAT."
        };
    }

    private async Task<AuthenticationResult> AuthenticatePatAsync(
        string repositoryUrl,
        string? personalAccessToken,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(personalAccessToken))
        {
            return new AuthenticationResult
            {
                IsSuccess = false,
                Message = "PAT đang rỗng."
            };
        }

        string username = ExtractOwnerFromRepositoryUrl(repositoryUrl);
        log("INFO", "Đang lưu PAT vào Windows Credential Manager...");

        string arguments = $"/generic:git:https://github.com /user:{EscapeCredentialArgument(username)} /pass:{EscapeCredentialArgument(personalAccessToken)}";
        ProcessCommandResult storeResult = await RunProcessAsync("cmdkey.exe", arguments, cancellationToken);
        if (!storeResult.IsSuccess)
        {
            return new AuthenticationResult
            {
                IsSuccess = false,
                Message = string.IsNullOrWhiteSpace(storeResult.StandardError)
                    ? "Không thể lưu PAT vào Windows Credential Manager."
                    : storeResult.StandardError
            };
        }

        return new AuthenticationResult
        {
            IsSuccess = true,
            Message = "Đã lưu PAT vào Windows Credential Manager."
        };
    }

    private static string ExtractOwnerFromRepositoryUrl(string repositoryUrl)
    {
        if (Uri.TryCreate(repositoryUrl, UriKind.Absolute, out Uri? uri))
        {
            string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 1 && !string.IsNullOrWhiteSpace(segments[0]))
            {
                return segments[0];
            }
        }

        return "github-user";
    }

    private static string EscapeCredentialArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Contains(' ') && !value.Contains('"'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static async Task<ProcessCommandResult> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            Arguments = arguments,
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
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrEmpty(eventArgs.Data))
            {
                errorBuilder.AppendLine(eventArgs.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessCommandResult(-1, string.Empty, $"Không thể chạy lệnh: {fileName}");
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
            return new ProcessCommandResult(-1, outputBuilder.ToString().Trim(), exception.Message);
        }
    }
}
