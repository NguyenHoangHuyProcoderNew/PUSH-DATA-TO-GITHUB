namespace PushDataToGitHub.App.Models;

public sealed class LogEntry
{
    public LogEntry(string level, string message)
    {
        Timestamp = DateTime.Now.ToString("HH:mm:ss");
        Level = level;
        Message = message;
    }

    public string Timestamp { get; }

    public string Level { get; }

    public string Message { get; }
}
