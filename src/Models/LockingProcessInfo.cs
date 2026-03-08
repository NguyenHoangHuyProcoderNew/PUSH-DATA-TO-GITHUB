namespace PushDataToGitHub.App.Models;

public sealed class LockingProcessInfo
{
    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public string? AppName { get; init; }
}
