namespace PushDataToGitHub.App.Models;

public sealed class AuthenticationResult
{
    public bool IsSuccess { get; init; }

    public string Message { get; init; } = string.Empty;
}
