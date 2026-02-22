namespace PushDataToGitHub.App.Services.Preflight;

public sealed class PreflightResult
{
    public bool GitInstalled { get; init; }

    public string GitVersion { get; init; } = string.Empty;

    public bool GitLfsInstalled { get; init; }

    public string GitLfsVersion { get; init; } = string.Empty;

    public bool IsSuccess => GitInstalled && GitLfsInstalled;
}
