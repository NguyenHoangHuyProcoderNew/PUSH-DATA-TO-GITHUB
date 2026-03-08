using PushDataToGitHub.App.Services.Dialogs;
using PushDataToGitHub.App.Services.Preflight;
using PushDataToGitHub.App.Services.Push;
using PushDataToGitHub.App.Services.Safety;
using PushDataToGitHub.App.Services.Security;
using PushDataToGitHub.App.ViewModels;

namespace PushDataToGitHub.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        IGitCommandRunner gitCommandRunner = new GitCommandRunner();
        IPreflightService preflightService = new PreflightService(gitCommandRunner);
        IFolderPickerService folderPickerService = new FolderPickerService();
        IDialogService dialogService = new DialogService();
        ICredentialService credentialService = new CredentialService(gitCommandRunner);
        IForcePushGuard forcePushGuard = new ForcePushGuard();
        IGitPushService gitPushService = new GitPushService(gitCommandRunner, forcePushGuard);

        MainViewModel viewModel = new(
            preflightService,
            folderPickerService,
            dialogService,
            credentialService,
            gitPushService);
        MainWindow window = new()
        {
            DataContext = viewModel
        };

        window.Show();
    }
}
