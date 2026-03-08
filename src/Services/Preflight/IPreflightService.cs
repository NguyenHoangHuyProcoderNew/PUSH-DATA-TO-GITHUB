namespace PushDataToGitHub.App.Services.Preflight;

public interface IPreflightService
{
    Task<PreflightResult> CheckAsync(CancellationToken cancellationToken = default);
}
