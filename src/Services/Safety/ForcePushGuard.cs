using System.Text.RegularExpressions;

namespace PushDataToGitHub.App.Services.Safety;

public sealed class ForcePushGuard : IForcePushGuard
{
    private static readonly Regex ForceFlagRegex = new(
        @"(^|\s)(--force-with-lease|--force|-f)(\s|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void EnsureAllowed(string gitArguments, string branchName, bool safeModeEnabled)
    {
        if (!safeModeEnabled)
        {
            return;
        }

        if (!IsProtectedBranch(branchName))
        {
            return;
        }

        if (!ForceFlagRegex.IsMatch(gitArguments))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Safe mode đã chặn lệnh force push vào nhánh bảo vệ '{branchName}'.");
    }

    private static bool IsProtectedBranch(string branchName)
    {
        return branchName.Equals("main", StringComparison.OrdinalIgnoreCase)
               || branchName.Equals("master", StringComparison.OrdinalIgnoreCase);
    }
}
