namespace PushDataToGitHub.App.Services.Preflight;

public sealed class PreflightService : IPreflightService
{
    private readonly IGitCommandRunner _gitCommandRunner;

    public PreflightService(IGitCommandRunner gitCommandRunner)
    {
        _gitCommandRunner = gitCommandRunner;
    }

    public async Task<PreflightResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        ProcessCommandResult gitVersionResult = await _gitCommandRunner.RunAsync("--version", cancellationToken: cancellationToken);
        ProcessCommandResult lfsVersionResult = await _gitCommandRunner.RunAsync("lfs version", cancellationToken: cancellationToken);

        bool gitInstalled = gitVersionResult.IsSuccess && gitVersionResult.StandardOutput.Contains("git version", StringComparison.OrdinalIgnoreCase);
        bool lfsInstalled = lfsVersionResult.IsSuccess && lfsVersionResult.StandardOutput.Contains("git-lfs", StringComparison.OrdinalIgnoreCase);

        return new PreflightResult
        {
            GitInstalled = gitInstalled,
            GitVersion = gitVersionResult.IsSuccess ? gitVersionResult.StandardOutput : gitVersionResult.StandardError,
            GitLfsInstalled = lfsInstalled,
            GitLfsVersion = lfsVersionResult.IsSuccess ? lfsVersionResult.StandardOutput : lfsVersionResult.StandardError
        };
    }
}
