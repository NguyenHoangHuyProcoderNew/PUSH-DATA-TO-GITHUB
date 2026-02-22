using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using PushDataToGitHub.App.Commands;
using PushDataToGitHub.App.Models;
using PushDataToGitHub.App.Services.Dialogs;
using PushDataToGitHub.App.Services.Preflight;
using PushDataToGitHub.App.Services.Push;
using PushDataToGitHub.App.Services.Security;

namespace PushDataToGitHub.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private const int MaxLogEntries = 3000;
    private const int MaxGitHubLogEntries = 6000;

    private readonly IPreflightService _preflightService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IDialogService _dialogService;
    private readonly ICredentialService _credentialService;
    private readonly IGitPushService _gitPushService;

    private readonly AsyncRelayCommand _runPreflightCommand;
    private readonly AsyncRelayCommand _pushCommand;
    private readonly RelayCommand _browseFolderCommand;
    private readonly RelayCommand _clearLogsCommand;

    private string _folderPath = string.Empty;
    private string _repositoryUrl = string.Empty;
    private string _branchName = "main";
    private bool _isSafeModeEnabled = true;
    private bool _isBusy;
    private string _statusMessage = "Sẵn sàng.";
    private OptionItem<AuthenticationMode> _selectedAuthenticationModeOption = default!;
    private OptionItem<CommitMessageMode> _selectedCommitMessageModeOption = default!;
    private string _personalAccessToken = string.Empty;
    private string _manualCommitMessage = string.Empty;

    public MainViewModel(
        IPreflightService preflightService,
        IFolderPickerService folderPickerService,
        IDialogService dialogService,
        ICredentialService credentialService,
        IGitPushService gitPushService)
    {
        _preflightService = preflightService;
        _folderPickerService = folderPickerService;
        _dialogService = dialogService;
        _credentialService = credentialService;
        _gitPushService = gitPushService;

        _browseFolderCommand = new RelayCommand(BrowseFolder, () => IsNotBusy);
        _runPreflightCommand = new AsyncRelayCommand(RunPreflightAsync, () => IsNotBusy);
        _pushCommand = new AsyncRelayCommand(RunPushAsync, CanRunPush);
        _clearLogsCommand = new RelayCommand(ClearLogs);

        Logs = new ObservableCollection<LogEntry>();
        GitHubLogs = new ObservableCollection<string>();
        SkippedFiles = new ObservableCollection<string>();
        ConflictFiles = new ObservableCollection<string>();

        AuthenticationModeOptions = new ObservableCollection<OptionItem<AuthenticationMode>>
        {
            new() { Value = AuthenticationMode.BrowserDevice, DisplayName = "Browser/Device (Git Credential Manager)" },
            new() { Value = AuthenticationMode.PersonalAccessToken, DisplayName = "Personal Access Token (PAT)" }
        };
        SelectedAuthenticationModeOption = AuthenticationModeOptions[0];

        CommitMessageModeOptions = new ObservableCollection<OptionItem<CommitMessageMode>>
        {
            new() { Value = CommitMessageMode.Automatic, DisplayName = "Tự động (Sao Lưu Dữ Liệu - hh:mm:ss dd/MM/yyyy)" },
            new() { Value = CommitMessageMode.Manual, DisplayName = "Thủ công (tự nhập commit message)" }
        };
        SelectedCommitMessageModeOption = CommitMessageModeOptions[0];

        AddLog("INFO", "Ứng dụng đã sẵn sàng. Có thể kiểm tra môi trường và push.");
    }

    public ObservableCollection<OptionItem<AuthenticationMode>> AuthenticationModeOptions { get; }

    public ObservableCollection<OptionItem<CommitMessageMode>> CommitMessageModeOptions { get; }

    public ObservableCollection<LogEntry> Logs { get; }

    public ObservableCollection<string> GitHubLogs { get; }

    public ObservableCollection<string> SkippedFiles { get; }

    public ObservableCollection<string> ConflictFiles { get; }

    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (SetProperty(ref _folderPath, value))
            {
                RaiseActionStateChanged();
            }
        }
    }

    public string RepositoryUrl
    {
        get => _repositoryUrl;
        set
        {
            if (SetProperty(ref _repositoryUrl, value))
            {
                RaiseActionStateChanged();
            }
        }
    }

    public string BranchName
    {
        get => _branchName;
        set
        {
            if (SetProperty(ref _branchName, value))
            {
                RaiseActionStateChanged();
            }
        }
    }

    public bool IsSafeModeEnabled
    {
        get => _isSafeModeEnabled;
        set => SetProperty(ref _isSafeModeEnabled, value);
    }

    public OptionItem<AuthenticationMode> SelectedAuthenticationModeOption
    {
        get => _selectedAuthenticationModeOption;
        set
        {
            if (SetProperty(ref _selectedAuthenticationModeOption, value))
            {
                OnPropertyChanged(nameof(IsPatMode));
                RaiseActionStateChanged();
            }
        }
    }

    public OptionItem<CommitMessageMode> SelectedCommitMessageModeOption
    {
        get => _selectedCommitMessageModeOption;
        set
        {
            if (SetProperty(ref _selectedCommitMessageModeOption, value))
            {
                OnPropertyChanged(nameof(IsManualCommitMode));
                RaiseActionStateChanged();
            }
        }
    }

    public string PersonalAccessToken
    {
        get => _personalAccessToken;
        private set
        {
            if (SetProperty(ref _personalAccessToken, value))
            {
                RaiseActionStateChanged();
            }
        }
    }

    public string ManualCommitMessage
    {
        get => _manualCommitMessage;
        set
        {
            if (SetProperty(ref _manualCommitMessage, value))
            {
                RaiseActionStateChanged();
            }
        }
    }

    public bool IsPatMode => SelectedAuthenticationModeOption.Value == AuthenticationMode.PersonalAccessToken;

    public bool IsManualCommitMode => SelectedCommitMessageModeOption.Value == CommitMessageMode.Manual;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                RaiseActionStateChanged();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand BrowseFolderCommand => _browseFolderCommand;

    public ICommand RunPreflightCommand => _runPreflightCommand;

    public ICommand PushCommand => _pushCommand;

    public ICommand ClearLogsCommand => _clearLogsCommand;

    public void ApplyDroppedFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            AddLog("WARN", $"Bỏ qua đường dẫn không hợp lệ: {folderPath}");
            return;
        }

        FolderPath = folderPath;
        AddLog("INFO", $"Đã nhận thư mục từ kéo-thả: {folderPath}");
    }

    public void UpdatePersonalAccessToken(string token)
    {
        PersonalAccessToken = token;
    }

    private void BrowseFolder()
    {
        string? selectedFolder = _folderPickerService.PickFolder(FolderPath);
        if (string.IsNullOrWhiteSpace(selectedFolder))
        {
            return;
        }

        FolderPath = selectedFolder;
        AddLog("INFO", $"Đã chọn thư mục: {selectedFolder}");
    }

    private async Task RunPreflightAsync()
    {
        IsBusy = true;
        StatusMessage = "Đang kiểm tra môi trường...";
        AddLog("INFO", "Bắt đầu kiểm tra Git/Git LFS.");

        try
        {
            PreflightResult result = await _preflightService.CheckAsync();

            if (result.GitInstalled)
            {
                AddLog("OK", $"Git sẵn sàng: {result.GitVersion}");
            }
            else
            {
                AddLog("ERROR", $"Git chưa cài hoặc lỗi: {Fallback(result.GitVersion)}");
            }

            if (result.GitLfsInstalled)
            {
                AddLog("OK", $"Git LFS sẵn sàng: {result.GitLfsVersion}");
            }
            else
            {
                AddLog("ERROR", $"Git LFS chưa cài hoặc lỗi: {Fallback(result.GitLfsVersion)}");
            }

            StatusMessage = result.IsSuccess
                ? "Môi trường sẵn sàng."
                : "Thiếu công cụ bắt buộc (Git/Git LFS).";
        }
        catch (Exception exception)
        {
            AddLog("ERROR", $"Lỗi kiểm tra môi trường: {exception.Message}");
            StatusMessage = "Kiểm tra thất bại.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunPushAsync()
    {
        ClearResultCollections();
        GitHubLogs.Clear();
        IsBusy = true;
        StatusMessage = "Đang chuẩn bị push...";
        AddLog("INFO", "Bắt đầu luồng push.");

        try
        {
            string? validationError = ValidateInput();
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                AddLog("ERROR", validationError);
                _dialogService.ShowError("Thiếu thông tin", validationError);
                StatusMessage = "Dữ liệu đầu vào chưa hợp lệ.";
                return;
            }

            PreflightResult preflightResult = await _preflightService.CheckAsync();
            if (!preflightResult.IsSuccess)
            {
                AddLog("ERROR", "Môi trường chưa sẵn sàng. Vui lòng cài Git và Git LFS trước.");
                _dialogService.ShowError(
                    "Thiếu công cụ bắt buộc",
                    "Máy hiện tại chưa có Git hoặc Git LFS. Vui lòng cài đặt rồi thử lại.");
                StatusMessage = "Thiếu Git/Git LFS.";
                return;
            }

            AddLog("OK", "Preflight thành công.");

            AuthenticationResult authenticationResult = await _credentialService.AuthenticateAsync(
                SelectedAuthenticationModeOption.Value,
                RepositoryUrl.Trim(),
                PersonalAccessToken,
                AddLog);

            if (!authenticationResult.IsSuccess)
            {
                AddLog("ERROR", authenticationResult.Message);
                _dialogService.ShowError("Xác thực thất bại", authenticationResult.Message);
                StatusMessage = "Xác thực thất bại.";
                return;
            }

            AddLog("OK", authenticationResult.Message);

            string? existingOrigin = await _gitPushService.GetExistingOriginUrlAsync(FolderPath.Trim());
            if (!string.IsNullOrWhiteSpace(existingOrigin)
                && !RepositoryUrlComparer.IsEquivalent(existingOrigin, RepositoryUrl.Trim()))
            {
                bool accepted = _dialogService.Confirm(
                    "Xác nhận đổi remote origin",
                    $"Origin hiện tại: {existingOrigin}\nOrigin mới: {RepositoryUrl.Trim()}\n\nBạn có đồng ý tiếp tục push lên URL mới không?");

                if (!accepted)
                {
                    AddLog("WARN", "Người dùng đã hủy do origin cũ khác origin mới.");
                    StatusMessage = "Đã hủy theo yêu cầu người dùng.";
                    return;
                }
            }

            PushRequest request = new()
            {
                SourceFolderPath = FolderPath.Trim(),
                RepositoryUrl = RepositoryUrl.Trim(),
                BranchName = BranchName.Trim(),
                AuthenticationMode = SelectedAuthenticationModeOption.Value,
                PersonalAccessToken = PersonalAccessToken,
                CommitMessageMode = SelectedCommitMessageModeOption.Value,
                ManualCommitMessage = ManualCommitMessage,
                IsSafeModeEnabled = IsSafeModeEnabled,
                LfsTrackThresholdBytes = 100L * 1024 * 1024,
                GitHubLfsMaxFileSizeBytes = 2L * 1024 * 1024 * 1024,
                ConfirmKillLockedFileProcesses = ConfirmKillLockedFileProcesses
            };

            StatusMessage = "Đang push dữ liệu...";
            PushResult pushResult = await PushWithHeartbeatAsync(request);

            foreach (string skippedFile in pushResult.SkippedFiles)
            {
                SkippedFiles.Add(skippedFile);
            }

            foreach (string conflictFile in pushResult.ConflictFiles)
            {
                ConflictFiles.Add(conflictFile);
            }

            if (pushResult.IsSuccess)
            {
                StatusMessage = "Push hoàn tất.";
                AddLog("OK", pushResult.Summary);

                if (SkippedFiles.Count > 0)
                {
                    _dialogService.ShowWarning(
                        "Push thành công có bỏ qua file",
                        $"Push thành công nhưng có {SkippedFiles.Count} file bị bỏ qua. Xem danh sách ở panel kết quả.");
                }
                else
                {
                    _dialogService.ShowInfo("Hoàn tất", pushResult.Summary);
                }

                return;
            }

            StatusMessage = "Push thất bại.";
            AddLog("ERROR", pushResult.Summary);
            if (!string.IsNullOrWhiteSpace(pushResult.ErrorDetails))
            {
                AddLog("ERROR", pushResult.ErrorDetails);
            }

            if (ConflictFiles.Count > 0)
            {
                _dialogService.ShowWarning(
                    "Rebase conflict",
                    $"Phát hiện {ConflictFiles.Count} file conflict. Vui lòng xử lý thủ công.");
                return;
            }

            _dialogService.ShowError("Push thất bại", pushResult.Summary);
        }
        catch (Exception exception)
        {
            StatusMessage = "Push thất bại.";
            AddLog("ERROR", $"Lỗi không mong muốn: {exception.Message}");
            _dialogService.ShowError("Lỗi hệ thống", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRunPush()
    {
        if (IsBusy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(FolderPath)
            || string.IsNullOrWhiteSpace(RepositoryUrl)
            || string.IsNullOrWhiteSpace(BranchName))
        {
            return false;
        }

        if (IsPatMode && string.IsNullOrWhiteSpace(PersonalAccessToken))
        {
            return false;
        }

        if (IsManualCommitMode && string.IsNullOrWhiteSpace(ManualCommitMessage))
        {
            return false;
        }

        return true;
    }

    private string? ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            return "Thư mục local không tồn tại.";
        }

        if (!RepositoryUrlComparer.IsGitHubHttpUrl(RepositoryUrl))
        {
            return "URL repository không hợp lệ hoặc không thuộc github.com.";
        }

        if (string.IsNullOrWhiteSpace(BranchName))
        {
            return "Branch không được để trống.";
        }

        if (IsPatMode && string.IsNullOrWhiteSpace(PersonalAccessToken))
        {
            return "Bạn đã chọn PAT nhưng chưa nhập token.";
        }

        if (IsManualCommitMode && string.IsNullOrWhiteSpace(ManualCommitMessage))
        {
            return "Bạn đã chọn commit thủ công nhưng chưa nhập commit message.";
        }

        return null;
    }

    private bool ConfirmKillLockedFileProcesses(LockedFilePrompt prompt)
    {
        string processList = prompt.LockingProcesses.Count == 0
            ? "(Không xác định được tiến trình)"
            : string.Join(
                "\n",
                prompt.LockingProcesses.Select(process =>
                    $"- {process.ProcessName} (PID {process.ProcessId})"));

        string message =
            $"File đang bị khóa và chưa thể push:\n{prompt.RelativePath}\n\n" +
            $"Nguyên nhân hiện tại:\n{prompt.FailureReason}\n\n" +
            $"Tiến trình đang sử dụng file:\n{processList}\n\n" +
            $"Bạn có muốn đóng (kill) các tiến trình trên để thử lại không?\n" +
            $"Lần thử: {prompt.Attempt}/{prompt.MaxAttempts}\n\n" +
            "Chọn No để bỏ qua file này và tiếp tục push các file còn lại.";

        return _dialogService.Confirm("Xử lý file bị khóa", message);
    }

    private void ClearLogs()
    {
        Logs.Clear();
        GitHubLogs.Clear();
        AddLog("INFO", "Đã xóa log.");
    }

    private void ClearResultCollections()
    {
        SkippedFiles.Clear();
        ConflictFiles.Clear();
    }

    private async Task<PushResult> PushWithHeartbeatAsync(PushRequest request)
    {
        string currentStep = "Chuẩn bị push";
        string currentLevel = "INFO";
        DateTimeOffset lastRealLogAt = DateTimeOffset.UtcNow;
        int lastHeartbeatLoggedAtSecond = -10;
        string lastHeartbeatStep = string.Empty;
        object sync = new();

        void PushLog(string level, string message)
        {
            lock (sync)
            {
                currentLevel = level;
                currentStep = message;
                lastRealLogAt = DateTimeOffset.UtcNow;
            }

            AddLog(level, message);
        }

        void PushGitHubRawLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lock (sync)
            {
                lastRealLogAt = DateTimeOffset.UtcNow;
            }

            AddGitHubLog(message);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        Task<PushResult> pushTask = _gitPushService.PushAsync(request, PushLog, PushGitHubRawLog);

        while (!pushTask.IsCompleted)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            if (pushTask.IsCompleted)
            {
                break;
            }

            int elapsedSeconds = (int)stopwatch.Elapsed.TotalSeconds;
            string stepSnapshot;
            string levelSnapshot;
            DateTimeOffset lastLogSnapshot;
            lock (sync)
            {
                levelSnapshot = currentLevel;
                stepSnapshot = currentStep;
                lastLogSnapshot = lastRealLogAt;
            }

            StatusMessage = $"Đang push dữ liệu... ({elapsedSeconds}s)";
            if ((DateTimeOffset.UtcNow - lastLogSnapshot).TotalSeconds >= 1)
            {
                string heartbeatStep = BuildHeartbeatStepSummary(levelSnapshot, stepSnapshot);
                bool shouldLogHeartbeat = elapsedSeconds - lastHeartbeatLoggedAtSecond >= 10
                                          || !string.Equals(lastHeartbeatStep, heartbeatStep, StringComparison.Ordinal);
                if (shouldLogHeartbeat)
                {
                    AddLog("INFO", $"[Đang xử lý {elapsedSeconds}s] {heartbeatStep}");
                    lastHeartbeatLoggedAtSecond = elapsedSeconds;
                    lastHeartbeatStep = heartbeatStep;
                }
            }
        }

        return await pushTask;
    }

    private static string BuildHeartbeatStepSummary(string level, string step)
    {
        if (string.IsNullOrWhiteSpace(step))
        {
            return "Đang xử lý...";
        }

        if (step.StartsWith("Đã bỏ qua file bị khóa:", StringComparison.OrdinalIgnoreCase)
            || step.StartsWith("Đã bỏ qua thư mục bị khóa:", StringComparison.OrdinalIgnoreCase)
            || step.StartsWith("Bỏ qua file quá lớn:", StringComparison.OrdinalIgnoreCase))
        {
            return "Đang tiếp tục push sau khi bỏ qua file không thể xử lý.";
        }

        if (step.StartsWith("LFS track", StringComparison.OrdinalIgnoreCase))
        {
            if (step.StartsWith("LFS track 100", StringComparison.OrdinalIgnoreCase))
            {
                return "Đã track 100% file lớn. Đang chuyển sang bước stage/commit...";
            }

            return step;
        }

        if (step.StartsWith("Hoàn tất xử lý Git LFS", StringComparison.OrdinalIgnoreCase))
        {
            return "Đã xong bước Git LFS, đang chuẩn bị stage thay đổi...";
        }

        if (step.Contains("stage", StringComparison.OrdinalIgnoreCase))
        {
            return "Đang chạy git add -A, chờ Git trả kết quả...";
        }

        if (step.Contains("push", StringComparison.OrdinalIgnoreCase))
        {
            return "Đang chạy git push, chờ phản hồi từ GitHub...";
        }

        return step;
    }

    private void AddLog(string level, string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add(new LogEntry(level, message));

            while (Logs.Count > MaxLogEntries)
            {
                Logs.RemoveAt(0);
            }
        });
    }

    private void AddGitHubLog(string message)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            GitHubLogs.Add(message);

            while (GitHubLogs.Count > MaxGitHubLogEntries)
            {
                GitHubLogs.RemoveAt(0);
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RaiseActionStateChanged()
    {
        _browseFolderCommand.RaiseCanExecuteChanged();
        _runPreflightCommand.RaiseCanExecuteChanged();
        _pushCommand.RaiseCanExecuteChanged();
    }

    private static string Fallback(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? "(không có chi tiết lỗi)" : text;
    }
}

