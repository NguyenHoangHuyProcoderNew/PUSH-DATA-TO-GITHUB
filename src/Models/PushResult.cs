namespace PushDataToGitHub.App.Models;

public sealed class PushResult
{
    public bool IsSuccess { get; init; }

    public bool IsCancelled { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<string> SkippedFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ConflictFiles { get; init; } = Array.Empty<string>();

    public string? ErrorDetails { get; init; }
}
