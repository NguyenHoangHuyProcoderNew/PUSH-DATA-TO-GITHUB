namespace PushDataToGitHub.App.Services.Safety;

public interface IForcePushGuard
{
    void EnsureAllowed(string gitArguments, string branchName, bool safeModeEnabled);
}
