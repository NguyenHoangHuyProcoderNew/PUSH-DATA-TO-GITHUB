namespace PushDataToGitHub.App.Services.Dialogs;

public interface IDialogService
{
    bool Confirm(string title, string message);

    void ShowInfo(string title, string message);

    void ShowWarning(string title, string message);

    void ShowError(string title, string message);
}
