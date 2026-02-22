namespace PushDataToGitHub.App.Models;

public sealed class PushRequest
{
    public required string SourceFolderPath { get; init; }

    public required string RepositoryUrl { get; init; }

    public required string BranchName { get; init; }

    public required AuthenticationMode AuthenticationMode { get; init; }

    public string? PersonalAccessToken { get; init; }

    public required CommitMessageMode CommitMessageMode { get; init; }

    public string? ManualCommitMessage { get; init; }

    public bool IsSafeModeEnabled { get; init; } = true;

    public long LfsTrackThresholdBytes { get; init; } = 100L * 1024 * 1024;

    public long GitHubLfsMaxFileSizeBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    public Func<LockedFilePrompt, bool>? ConfirmKillLockedFileProcesses { get; init; }
}
