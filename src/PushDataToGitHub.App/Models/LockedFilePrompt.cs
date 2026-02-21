namespace PushDataToGitHub.App.Models;

public sealed class LockedFilePrompt
{
    public required string RelativePath { get; init; }

    public required string FullPath { get; init; }

    public required string FailureReason { get; init; }

    public required IReadOnlyList<LockingProcessInfo> LockingProcesses { get; init; }

    public required int Attempt { get; init; }

    public required int MaxAttempts { get; init; }
}
