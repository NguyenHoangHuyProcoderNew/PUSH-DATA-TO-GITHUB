namespace PushDataToGitHub.App.Services.Preflight;

public interface IGitCommandRunner
{
    Task<ProcessCommandResult> RunAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        Action<string>? standardOutputCallback = null,
        Action<string>? standardErrorCallback = null);
}
