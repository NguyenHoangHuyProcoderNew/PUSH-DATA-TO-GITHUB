using PushDataToGitHub.App.Models;

namespace PushDataToGitHub.App.Services.Push;

public interface IGitPushService
{
    Task<string?> GetExistingOriginUrlAsync(string sourceFolderPath, CancellationToken cancellationToken = default);

    Task<PushResult> PushAsync(
        PushRequest request,
        Action<string, string> log,
        Action<string>? gitHubLog = null,
        CancellationToken cancellationToken = default);
}
