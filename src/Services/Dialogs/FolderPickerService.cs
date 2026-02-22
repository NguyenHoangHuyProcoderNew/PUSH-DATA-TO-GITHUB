using System.IO;
using System.Windows.Forms;

namespace PushDataToGitHub.App.Services.Dialogs;

public sealed class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string? initialFolderPath)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Chọn thư mục cần đẩy lên GitHub",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (!string.IsNullOrWhiteSpace(initialFolderPath) && Directory.Exists(initialFolderPath))
        {
            dialog.SelectedPath = initialFolderPath;
        }

        DialogResult result = dialog.ShowDialog();
        if (result != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return null;
        }

        return dialog.SelectedPath;
    }
}
