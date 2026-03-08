using PushDataToGitHub.App.Models;

namespace PushDataToGitHub.App.Services.Security;

public interface ICredentialService
{
    Task<AuthenticationResult> AuthenticateAsync(
        AuthenticationMode mode,
        string repositoryUrl,
        string? personalAccessToken,
        Action<string, string> log,
        CancellationToken cancellationToken = default);
}
