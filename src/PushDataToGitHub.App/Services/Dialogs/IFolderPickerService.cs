namespace PushDataToGitHub.App.Services.Dialogs;

public interface IFolderPickerService
{
    string? PickFolder(string? initialFolderPath);
}
