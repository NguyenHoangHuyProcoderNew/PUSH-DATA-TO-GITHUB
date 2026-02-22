namespace PushDataToGitHub.App.Models;

public sealed class OptionItem<T>
{
    public required T Value { get; init; }

    public required string DisplayName { get; init; }
}
