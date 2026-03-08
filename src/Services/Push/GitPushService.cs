using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using PushDataToGitHub.App.Models;
using PushDataToGitHub.App.Services.Preflight;
using PushDataToGitHub.App.Services.Safety;

namespace PushDataToGitHub.App.Services.Push;

public sealed class GitPushService : IGitPushService
{
    private const int LockRetryLimit = 5;
    private const int MaxAddAttempts = 12;
    private readonly IGitCommandRunner _gitCommandRunner;
    private readonly IForcePushGuard _forcePushGuard;
    private Action<string>? _gitHubRawLog;

    public GitPushService(IGitCommandRunner gitCommandRunner, IForcePushGuard forcePushGuard)
    {
        _gitCommandRunner = gitCommandRunner;
        _forcePushGuard = forcePushGuard;
    }

    public async Task<string?> GetExistingOriginUrlAsync(string sourceFolderPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Path.Combine(sourceFolderPath, ".git")))
        {
            return null;
        }

        ProcessCommandResult originResult = await RunGitAsync(
            "remote get-url origin",
            sourceFolderPath,
            cancellationToken);

        if (!originResult.IsSuccess)
        {
            return null;
        }

        return FirstLine(originResult.StandardOutput);
    }

    public async Task<PushResult> PushAsync(
        PushRequest request,
        Action<string, string> log,
        Action<string>? gitHubLog = null,
        CancellationToken cancellationToken = default)
    {
        _gitHubRawLog = gitHubLog;
        _gitHubRawLog?.Invoke($"===== Bắt đầu phiên push ({DateTime.Now:HH:mm:ss dd/MM/yyyy}) =====");
        List<string> skippedFiles = [];
        List<string> conflictFiles = [];

        string tempSessionRoot = Path.Combine(Path.GetTempPath(), "PushDataToGitHubApp", "sessions");
        Directory.CreateDirectory(tempSessionRoot);

        string tempRepositoryPath = Path.Combine(
            tempSessionRoot,
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");

        bool branchExistsOnRemote = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            ProcessCommandResult sourcePrepareResult = await PrepareSourceRepositoryAsync(request, log, cancellationToken);
            if (!sourcePrepareResult.IsSuccess)
            {
                return CreateFailure(
                    "Không thể chuẩn bị repository tại folder nguồn.",
                    sourcePrepareResult,
                    skippedFiles,
                    conflictFiles);
            }

            log("INFO", $"Tạo workspace tạm: {tempRepositoryPath}");
            branchExistsOnRemote = await RemoteBranchExistsAsync(
                request.RepositoryUrl,
                request.BranchName,
                log,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (branchExistsOnRemote)
            {
                log("INFO", $"Đang clone branch '{request.BranchName}' từ remote...");
                ProcessCommandResult cloneResult = await RunGitAsync(
                    $"-c core.longpaths=true -c core.autocrlf=false -c core.safecrlf=false clone --single-branch --branch {Quote(request.BranchName)} {Quote(request.RepositoryUrl)} {Quote(tempRepositoryPath)}",
                    cancellationToken: cancellationToken);
                LogGitCommandResult(log, $"clone branch '{request.BranchName}'", cloneResult);

                if (!cloneResult.IsSuccess)
                {
                    return CreateFailure("Clone repository thất bại.", cloneResult, skippedFiles, conflictFiles);
                }
            }
            else
            {
                log("INFO", $"Branch '{request.BranchName}' chưa tồn tại trên remote. Tạo repository tạm mới...");
                Directory.CreateDirectory(tempRepositoryPath);

                ProcessCommandResult initResult = await RunGitAsync("init", tempRepositoryPath, cancellationToken);
                LogGitCommandResult(log, "git init workspace tạm", initResult);
                if (!initResult.IsSuccess)
                {
                    return CreateFailure("Khởi tạo repository tạm thất bại.", initResult, skippedFiles, conflictFiles);
                }

                ProcessCommandResult checkoutResult = await RunGitAsync(
                    $"checkout -b {Quote(request.BranchName)}",
                    tempRepositoryPath,
                    cancellationToken);
                LogGitCommandResult(log, $"tạo branch '{request.BranchName}'", checkoutResult);
                if (!checkoutResult.IsSuccess)
                {
                    return CreateFailure("Không thể tạo branch đích.", checkoutResult, skippedFiles, conflictFiles);
                }

                ProcessCommandResult remoteResult = await RunGitAsync(
                    $"remote add origin {Quote(request.RepositoryUrl)}",
                    tempRepositoryPath,
                    cancellationToken);
                LogGitCommandResult(log, "thêm remote origin", remoteResult);
                if (!remoteResult.IsSuccess)
                {
                    return CreateFailure("Không thể gán remote origin.", remoteResult, skippedFiles, conflictFiles);
                }
            }

            ProcessCommandResult tempRepoConfigResult = await ConfigureRepositoryForBackupAsync(tempRepositoryPath, cancellationToken);
            if (!tempRepoConfigResult.IsSuccess)
            {
                return CreateFailure(
                    "Không thể cấu hình repository tạm cho push nhiều file.",
                    tempRepoConfigResult,
                    skippedFiles,
                    conflictFiles);
            }

            cancellationToken.ThrowIfCancellationRequested();
            log("INFO", "Đang đồng bộ thư mục nguồn theo chế độ mirror...");

            MirrorSourceToRepository(
                request.SourceFolderPath,
                tempRepositoryPath,
                request.GitHubLfsMaxFileSizeBytes,
                skippedFiles,
                log,
                cancellationToken);

            ProcessCommandResult lfsInstallResult = await RunGitAsync(
                "lfs install --local",
                tempRepositoryPath,
                cancellationToken);
            LogGitCommandResult(log, "git lfs install --local", lfsInstallResult);
            if (!lfsInstallResult.IsSuccess)
            {
                return CreateFailure("Không thể kích hoạt Git LFS trong workspace tạm.", lfsInstallResult, skippedFiles, conflictFiles);
            }

            log("INFO", "Bắt đầu xử lý Git LFS cho file lớn...");
            await TrackLargeFilesWithLfsAsync(
                tempRepositoryPath,
                request.LfsTrackThresholdBytes,
                request.GitHubLfsMaxFileSizeBytes,
                log,
                cancellationToken);
            log("OK", "Hoàn tất xử lý Git LFS.");

            log("INFO", "Đang stage thay đổi...");
            ProcessCommandResult addResult = await StageAllChangesResilientAsync(
                request,
                tempRepositoryPath,
                skippedFiles,
                log,
                cancellationToken);
            if (!addResult.IsSuccess)
            {
                string summary = LooksLikePathLengthIssue(addResult)
                    ? "Không thể stage thay đổi vì có đường dẫn quá dài trên Windows."
                    : "Không thể stage thay đổi.";
                return CreateFailure(summary, addResult, skippedFiles, conflictFiles);
            }
            log("OK", "Stage thay đổi thành công.");

            ProcessCommandResult statusResult = await RunGitAsync("status --porcelain", tempRepositoryPath, cancellationToken);
            LogGitCommandResult(log, "git status --porcelain", statusResult);
            if (!statusResult.IsSuccess)
            {
                return CreateFailure("Không thể kiểm tra trạng thái git.", statusResult, skippedFiles, conflictFiles);
            }

            bool hasChanges = !string.IsNullOrWhiteSpace(statusResult.StandardOutput);
            if (!hasChanges && branchExistsOnRemote)
            {
                return new PushResult
                {
                    IsSuccess = true,
                    Summary = "Không có thay đổi để push.",
                    SkippedFiles = skippedFiles
                };
            }

            ProcessCommandResult identityResult = await EnsureGitIdentityAsync(cancellationToken);
            if (!identityResult.IsSuccess)
            {
                return CreateFailure(
                    "Thiếu user.name hoặc user.email trong global git config.",
                    identityResult,
                    skippedFiles,
                    conflictFiles);
            }

            string commitMessage = BuildCommitMessage(request);
            string commitArguments = hasChanges
                ? $"commit -m {Quote(commitMessage)}"
                : $"commit --allow-empty -m {Quote(commitMessage)}";

            log("INFO", "Đang tạo commit...");
            ProcessCommandResult commitResult = await RunGitAsync(commitArguments, tempRepositoryPath, cancellationToken);
            if (!ContainsNothingToCommit(commitResult))
            {
                LogGitCommandResult(log, "git commit", commitResult);
            }
            if (!commitResult.IsSuccess && !ContainsNothingToCommit(commitResult))
            {
                return CreateFailure("Commit thất bại.", commitResult, skippedFiles, conflictFiles);
            }
            log("OK", "Tạo commit thành công.");

            log("INFO", "Đang push lên remote...");
            PushResult pushResult = await PushWithRebaseRetryAsync(
                request,
                tempRepositoryPath,
                skippedFiles,
                log,
                cancellationToken);

            if (pushResult.IsSuccess)
            {
                await SyncSourceRepoBranchAsync(
                    request.SourceFolderPath,
                    request.BranchName,
                    log,
                    cancellationToken);
            }

            return pushResult;
        }
        catch (OperationCanceledException)
        {
            return new PushResult
            {
                IsCancelled = true,
                Summary = "Tác vụ đã bị hủy.",
                SkippedFiles = skippedFiles,
                ConflictFiles = conflictFiles
            };
        }
        finally
        {
            _gitHubRawLog?.Invoke("===== Kết thúc phiên push =====");
            _gitHubRawLog = null;
            TryDeleteTemporaryRepository(tempRepositoryPath, log);
        }
    }

    private async Task<ProcessCommandResult> PrepareSourceRepositoryAsync(
        PushRequest request,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        string sourceGitPath = Path.Combine(request.SourceFolderPath, ".git");
        if (!Directory.Exists(sourceGitPath))
        {
            log("INFO", "Folder nguồn chưa có .git, đang tự khởi tạo git init...");
            ProcessCommandResult initResult = await RunGitAsync("init", request.SourceFolderPath, cancellationToken);
            LogGitCommandResult(log, "git init folder nguồn", initResult);
            if (!initResult.IsSuccess)
            {
                return initResult;
            }
        }

        ProcessCommandResult originResult = await RunGitAsync(
            "remote get-url origin",
            request.SourceFolderPath,
            cancellationToken);
        if (originResult.IsSuccess)
        {
            LogGitCommandResult(log, "remote get-url origin", originResult);
        }
        else
        {
            log("WARN", $"[GIT] remote get-url origin: chưa có origin cũ. {SummarizeResultForLog(originResult)}");
        }

        if (originResult.IsSuccess && !string.IsNullOrWhiteSpace(originResult.StandardOutput))
        {
            ProcessCommandResult setOriginResult = await RunGitAsync(
                $"remote set-url origin {Quote(request.RepositoryUrl)}",
                request.SourceFolderPath,
                cancellationToken);
            LogGitCommandResult(log, "remote set-url origin", setOriginResult);
            if (!setOriginResult.IsSuccess)
            {
                return setOriginResult;
            }

            return await ConfigureRepositoryForBackupAsync(request.SourceFolderPath, cancellationToken);
        }

        ProcessCommandResult addOriginResult = await RunGitAsync(
            $"remote add origin {Quote(request.RepositoryUrl)}",
            request.SourceFolderPath,
            cancellationToken);
        LogGitCommandResult(log, "remote add origin", addOriginResult);
        if (!addOriginResult.IsSuccess)
        {
            return addOriginResult;
        }

        return await ConfigureRepositoryForBackupAsync(request.SourceFolderPath, cancellationToken);
    }

    private async Task<ProcessCommandResult> ConfigureRepositoryForBackupAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        string[] commands =
        [
            "config core.longpaths true",
            "config core.autocrlf false",
            "config core.safecrlf false"
        ];

        foreach (string command in commands)
        {
            ProcessCommandResult result = await RunGitAsync(
                command,
                repositoryPath,
                cancellationToken);

            if (!result.IsSuccess)
            {
                return result;
            }
        }

        return new ProcessCommandResult(0, "configured", string.Empty);
    }

    private async Task<bool> RemoteBranchExistsAsync(
        string repositoryUrl,
        string branchName,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        ProcessCommandResult lsRemoteResult = await RunGitAsync(
            $"ls-remote --heads {Quote(repositoryUrl)} {Quote(branchName)}",
            cancellationToken: cancellationToken);

        if (!lsRemoteResult.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Không thể truy vấn remote branch. Chi tiết: {Fallback(lsRemoteResult)}");
        }

        bool branchExists = !string.IsNullOrWhiteSpace(lsRemoteResult.StandardOutput);
        log("INFO", branchExists
            ? $"Remote đã có branch '{branchName}'."
            : $"Remote chưa có branch '{branchName}'.");

        return branchExists;
    }

    private static void MirrorSourceToRepository(
        string sourceFolderPath,
        string tempRepositoryPath,
        long maxPushableFileSizeBytes,
        List<string> skippedFiles,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        ClearWorkingTreeExceptGit(tempRepositoryPath);

        foreach (string sourceFilePath in Directory.EnumerateFiles(sourceFolderPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativePath = Path.GetRelativePath(sourceFolderPath, sourceFilePath);
            if (IsGitMetadataPath(relativePath))
            {
                continue;
            }

            try
            {
                FileInfo fileInfo = new(sourceFilePath);
                if (fileInfo.Length > maxPushableFileSizeBytes)
                {
                    skippedFiles.Add($"{relativePath} (vượt giới hạn {ToMegabytes(maxPushableFileSizeBytes)}MB)");
                    log("WARN", $"Bỏ qua file quá lớn: {relativePath}");
                    continue;
                }

                string destinationPath = Path.Combine(tempRepositoryPath, relativePath);
                string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(sourceFilePath, destinationPath, overwrite: true);
            }
            catch (Exception exception)
            {
                skippedFiles.Add($"{relativePath} ({exception.Message})");
                log("WARN", $"Không thể copy file, đã bỏ qua: {relativePath}");
            }
        }
    }

    private static void ClearWorkingTreeExceptGit(string repositoryPath)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(repositoryPath))
        {
            string name = Path.GetFileName(entry);
            if (name.Equals(".git", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                ResetAttributesRecursive(entry);
                Directory.Delete(entry, recursive: true);
                continue;
            }

            File.SetAttributes(entry, FileAttributes.Normal);
            File.Delete(entry);
        }
    }

    private async Task TrackLargeFilesWithLfsAsync(
        string repositoryPath,
        long lfsTrackThresholdBytes,
        long maxPushableFileSizeBytes,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        string gitFolder = Path.Combine(repositoryPath, ".git");
        HashSet<string> trackedPaths = new(StringComparer.OrdinalIgnoreCase);
        List<LfsTrackCandidate> lfsCandidates = [];
        int scannedFiles = 0;

        foreach (string filePath in Directory.EnumerateFiles(repositoryPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (filePath.StartsWith(gitFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            scannedFiles++;
            if (scannedFiles % 3000 == 0)
            {
                log("INFO", $"Đang quét file để xác định LFS... ({scannedFiles} files)");
            }

            FileInfo fileInfo = new(filePath);
            if (fileInfo.Length <= lfsTrackThresholdBytes || fileInfo.Length > maxPushableFileSizeBytes)
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(repositoryPath, filePath).Replace('\\', '/');
            if (!trackedPaths.Add(relativePath))
            {
                continue;
            }

            lfsCandidates.Add(new LfsTrackCandidate(relativePath, fileInfo.Length));
        }

        if (lfsCandidates.Count == 0)
        {
            return;
        }

        long totalCandidateBytes = lfsCandidates.Sum(candidate => candidate.SizeBytes);
        log("INFO", $"Phát hiện {lfsCandidates.Count} file cần track LFS (>{ToMegabytes(lfsTrackThresholdBytes)}MB), tổng {FormatMegabytes(totalCandidateBytes)}.");
        await TrackLfsCandidatesInBatchesAsync(repositoryPath, lfsCandidates, log, cancellationToken);
    }

    private async Task TrackLfsCandidatesInBatchesAsync(
        string repositoryPath,
        IReadOnlyList<LfsTrackCandidate> lfsCandidates,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        const int maxCommandLength = 7000;
        const string prefix = "lfs track -- ";
        List<LfsTrackCandidate> currentBatch = [];
        int trackedCount = 0;
        long trackedBytes = 0;
        long totalBytes = lfsCandidates.Sum(candidate => candidate.SizeBytes);

        async Task FlushBatchAsync()
        {
            if (currentBatch.Count == 0)
            {
                return;
            }

            string command = $"{prefix}{string.Join(" ", currentBatch.Select(candidate => Quote(candidate.Path)))}";
            ProcessCommandResult trackResult = await RunGitAsync(command, repositoryPath, cancellationToken);
            if (!trackResult.IsSuccess)
            {
                string failedPath = currentBatch[0].Path;
                throw new InvalidOperationException($"Không thể track file LFS: {failedPath}. {Fallback(trackResult)}");
            }

            trackedCount += currentBatch.Count;
            trackedBytes += currentBatch.Sum(candidate => candidate.SizeBytes);
            string latestFile = currentBatch[^1].Path;
            double progressPercent = totalBytes <= 0
                ? 100d
                : Math.Min(100d, trackedBytes * 100d / totalBytes);
            log("INFO", $"LFS track {progressPercent:0.0}% | {FormatMegabytes(trackedBytes)}/{FormatMegabytes(totalBytes)} | {trackedCount}/{lfsCandidates.Count} file | {latestFile}");
            currentBatch.Clear();
        }

        int currentLength = prefix.Length;
        foreach (LfsTrackCandidate candidate in lfsCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int quotedLength = Quote(candidate.Path).Length + 1;
            if (currentBatch.Count > 0 && currentLength + quotedLength > maxCommandLength)
            {
                await FlushBatchAsync();
                currentLength = prefix.Length;
            }

            currentBatch.Add(candidate);
            currentLength += quotedLength;
        }

        await FlushBatchAsync();
    }

    private async Task<ProcessCommandResult> EnsureGitIdentityAsync(CancellationToken cancellationToken)
    {
        ProcessCommandResult nameResult = await RunGitAsync("config --global user.name", cancellationToken: cancellationToken);
        ProcessCommandResult emailResult = await RunGitAsync("config --global user.email", cancellationToken: cancellationToken);

        bool hasName = nameResult.IsSuccess && !string.IsNullOrWhiteSpace(nameResult.StandardOutput);
        bool hasEmail = emailResult.IsSuccess && !string.IsNullOrWhiteSpace(emailResult.StandardOutput);
        if (hasName && hasEmail)
        {
            return new ProcessCommandResult(0, "identity ok", string.Empty);
        }

        string message = $"user.name: {Fallback(nameResult)} | user.email: {Fallback(emailResult)}";
        return new ProcessCommandResult(-1, string.Empty, message);
    }

    private async Task<ProcessCommandResult> StageAllChangesResilientAsync(
        PushRequest request,
        string repositoryPath,
        List<string> skippedFiles,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        ProcessCommandResult latestResult = new(-1, string.Empty, "Không thể stage thay đổi.");
        HashSet<string> skipSet = new(
            skippedFiles.Select(ExtractSkipPath).Select(NormalizePath),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> excludedPaths = new(skipSet, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> lockAttemptsByPath = new(StringComparer.OrdinalIgnoreCase);

        for (int attempt = 1; attempt <= MaxAddAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string addArguments = BuildResilientAddArguments(excludedPaths);
            log("INFO", $"[GIT] stage attempt {attempt}/{MaxAddAttempts}: git add -A (exclude {excludedPaths.Count} đường dẫn)");
            latestResult = await RunGitAsync(addArguments, repositoryPath, cancellationToken);
            if (latestResult.IsSuccess)
            {
                LogGitCommandResult(log, $"git add -A lần {attempt}/{MaxAddAttempts}", latestResult);
                return latestResult;
            }

            LogGitCommandResult(log, $"git add -A lần {attempt}/{MaxAddAttempts}", latestResult);

            string failureText = $"{latestResult.StandardOutput}\n{latestResult.StandardError}";
            bool permissionIssue = failureText.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
                                   || failureText.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);

            if (permissionIssue)
            {
                log("WARN", $"git add thất bại do quyền truy cập (lần {attempt}/{MaxAddAttempts}). Đang khắc phục và thử lại...");
                RelaxGitMetadataPermissions(repositoryPath, log);
                ProcessCommandResult gcResult = await RunGitAsync("gc --auto", repositoryPath, cancellationToken);
                LogGitCommandResult(log, "git gc --auto", gcResult);
            }

            List<string> badPaths = ExtractUnstageablePaths(latestResult);
            if (badPaths.Count > 0)
            {
                bool resolvedAnyPath = false;

                foreach (string badPath in badPaths)
                {
                    string normalized = NormalizeForRepository(repositoryPath, badPath);
                    if (string.IsNullOrWhiteSpace(normalized) || IsGitMetadataPath(normalized))
                    {
                        continue;
                    }

                    if (excludedPaths.Contains(normalized))
                    {
                        continue;
                    }

                    int lockAttempt = lockAttemptsByPath.TryGetValue(normalized, out int existing)
                        ? existing + 1
                        : 1;
                    lockAttemptsByPath[normalized] = lockAttempt;

                    LockedPathResolution resolution = await HandleLockedPathAsync(
                        request,
                        repositoryPath,
                        normalized,
                        latestResult,
                        lockAttempt,
                        log,
                        cancellationToken);

                    if (resolution.Action == LockedPathAction.Resolved)
                    {
                        resolvedAnyPath = true;
                        continue;
                    }

                    if (resolution.Action == LockedPathAction.Retry)
                    {
                        resolvedAnyPath = true;
                        log("WARN", $"Chưa xử lý xong file bị khóa: {normalized}. {resolution.Reason}");
                        continue;
                    }

                    resolvedAnyPath = true;
                    excludedPaths.Add(normalized);
                    if (skipSet.Add(normalized))
                    {
                        skippedFiles.Add($"{normalized} ({resolution.Reason})");
                    }

                    RemoveProblematicPath(repositoryPath, normalized, log);
                }

                if (resolvedAnyPath && attempt < MaxAddAttempts)
                {
                    await Task.Delay(500 * attempt, cancellationToken);
                    continue;
                }
            }

            if (permissionIssue && badPaths.Count == 0 && attempt < MaxAddAttempts)
            {
                log("WARN", $"Chưa xác định được file lỗi cụ thể (lần {attempt}/{MaxAddAttempts}), đang thử lại git add...");
                await Task.Delay(600 * attempt, cancellationToken);
                continue;
            }

            if (attempt < MaxAddAttempts)
            {
                await Task.Delay(300 * attempt, cancellationToken);
            }
        }

        return latestResult;
    }

    private async Task<LockedPathResolution> HandleLockedPathAsync(
        PushRequest request,
        string repositoryPath,
        string relativePath,
        ProcessCommandResult addFailureResult,
        int attempt,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = BuildFullPath(repositoryPath, relativePath);
        IReadOnlyList<LockingProcessInfo> lockingProcesses = GetLockingProcesses(fullPath, log);
        string reasonFromGit = Fallback(addFailureResult);

        if (lockingProcesses.Count == 0)
        {
            if (attempt >= LockRetryLimit)
            {
                return LockedPathResolution.Skip($"đã thử {LockRetryLimit} lần nhưng không xác định được tiến trình đang khóa file. Git báo: {reasonFromGit}");
            }

            return LockedPathResolution.Retry("chưa xác định được tiến trình đang khóa file");
        }

        bool shouldKill = request.ConfirmKillLockedFileProcesses?.Invoke(new LockedFilePrompt
        {
            RelativePath = relativePath,
            FullPath = fullPath,
            FailureReason = reasonFromGit,
            LockingProcesses = lockingProcesses,
            Attempt = Math.Min(attempt, LockRetryLimit),
            MaxAttempts = LockRetryLimit
        }) ?? false;

        if (!shouldKill)
        {
            return LockedPathResolution.Skip("người dùng chọn bỏ qua vì file đang bị tiến trình khác sử dụng");
        }

        KillProcessResult killResult = TryKillProcesses(lockingProcesses, log);
        await Task.Delay(600, cancellationToken);

        IReadOnlyList<LockingProcessInfo> remainingProcesses = GetLockingProcesses(fullPath, log);
        if (remainingProcesses.Count == 0)
        {
            log("OK", $"Đã giải phóng file bị khóa: {relativePath}");
            return LockedPathResolution.Resolved();
        }

        string remaining = string.Join(", ", remainingProcesses.Select(process => $"{process.ProcessName} (PID {process.ProcessId})"));
        string killError = string.IsNullOrWhiteSpace(killResult.ErrorMessage)
            ? string.Empty
            : $" Lỗi khi kill: {killResult.ErrorMessage}.";

        if (attempt >= LockRetryLimit)
        {
            return LockedPathResolution.Skip($"đã thử {LockRetryLimit} lần nhưng vẫn bị khóa bởi: {remaining}.{killError}");
        }

        return LockedPathResolution.Retry($"file vẫn đang bị khóa bởi: {remaining}.{killError}");
    }

    private static string BuildFullPath(string repositoryPath, string relativePath)
    {
        string candidatePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(candidatePath)
            ? candidatePath
            : Path.Combine(repositoryPath, candidatePath);
    }

    private static KillProcessResult TryKillProcesses(IReadOnlyList<LockingProcessInfo> processes, Action<string, string> log)
    {
        List<string> errors = [];

        foreach (LockingProcessInfo processInfo in processes.GroupBy(process => process.ProcessId).Select(group => group.First()))
        {
            if (processInfo.ProcessId == Environment.ProcessId)
            {
                errors.Add($"PID {processInfo.ProcessId} là chính ứng dụng hiện tại nên không thể tự kill");
                continue;
            }

            try
            {
                Process process = Process.GetProcessById(processInfo.ProcessId);
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000))
                {
                    errors.Add($"PID {processInfo.ProcessId} không thoát trong thời gian chờ");
                    continue;
                }

                log("WARN", $"Đã đóng tiến trình khóa file: {processInfo.ProcessName} (PID {processInfo.ProcessId})");
            }
            catch (Exception exception)
            {
                errors.Add($"PID {processInfo.ProcessId}: {exception.Message}");
            }
        }

        return new KillProcessResult(string.Join(" | ", errors));
    }

    private static IReadOnlyList<LockingProcessInfo> GetLockingProcesses(string filePath, Action<string, string> log)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<LockingProcessInfo>();
        }

        uint sessionHandle = 0;
        StringBuilder sessionKey = new(Guid.NewGuid().ToString("N"));

        int startResult = RmStartSession(out sessionHandle, 0, sessionKey);
        if (startResult != 0)
        {
            log("WARN", $"Không thể khởi tạo Restart Manager để dò lock file (mã {startResult}).");
            return Array.Empty<LockingProcessInfo>();
        }

        try
        {
            string[] resources = [filePath];
            int registerResult = RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, null, 0, null);
            if (registerResult != 0)
            {
                log("WARN", $"Không thể đăng ký file với Restart Manager (mã {registerResult}).");
                return Array.Empty<LockingProcessInfo>();
            }

            uint processInfoNeeded = 0;
            uint processInfo = 0;
            uint rebootReasons = 0;

            int listResult = RmGetList(sessionHandle, out processInfoNeeded, ref processInfo, null, ref rebootReasons);
            if (listResult == ErrorMoreData)
            {
                RM_PROCESS_INFO[] processInfos = new RM_PROCESS_INFO[processInfoNeeded];
                processInfo = processInfoNeeded;

                listResult = RmGetList(sessionHandle, out processInfoNeeded, ref processInfo, processInfos, ref rebootReasons);
                if (listResult != 0)
                {
                    log("WARN", $"Không thể lấy danh sách process khóa file (mã {listResult}).");
                    return Array.Empty<LockingProcessInfo>();
                }

                List<LockingProcessInfo> lockingProcesses = [];
                for (int index = 0; index < processInfo; index++)
                {
                    RM_PROCESS_INFO processInfoItem = processInfos[index];
                    int processId = processInfoItem.Process.dwProcessId;
                    string processName = processInfoItem.strAppName;

                    if (string.IsNullOrWhiteSpace(processName))
                    {
                        try
                        {
                            processName = Process.GetProcessById(processId).ProcessName;
                        }
                        catch
                        {
                            processName = "Unknown process";
                        }
                    }

                    lockingProcesses.Add(new LockingProcessInfo
                    {
                        ProcessId = processId,
                        ProcessName = processName,
                        AppName = processInfoItem.strServiceShortName
                    });
                }

                return lockingProcesses
                    .GroupBy(process => process.ProcessId)
                    .Select(group => group.First())
                    .ToList();
            }

            if (listResult != 0)
            {
                log("WARN", $"Restart Manager trả về mã {listResult} khi kiểm tra lock file.");
            }

            return Array.Empty<LockingProcessInfo>();
        }
        finally
        {
            RmEndSession(sessionHandle);
        }
    }

    private static string BuildResilientAddArguments(IEnumerable<string> excludedPaths)
    {
        List<string> excludes = excludedPaths
            .Select(NormalizePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !IsGitMetadataPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (excludes.Count == 0)
        {
            return "add -A";
        }

        StringBuilder builder = new("add -A -- .");
        foreach (string path in excludes)
        {
            builder.Append(' ');
            builder.Append(Quote($":(exclude,literal){path}"));
        }

        return builder.ToString();
    }

    private static void RelaxGitMetadataPermissions(string repositoryPath, Action<string, string> log)
    {
        string gitPath = Path.Combine(repositoryPath, ".git");
        if (!Directory.Exists(gitPath))
        {
            return;
        }

        try
        {
            ResetAttributesRecursive(gitPath);
        }
        catch (Exception exception)
        {
            log("WARN", $"Không thể reset quyền thư mục .git: {exception.Message}");
        }
    }

    private static List<string> ExtractUnstageablePaths(ProcessCommandResult result)
    {
        string text = $"{result.StandardOutput}\n{result.StandardError}";
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        Regex unableIndex = new(@"unable to index file '([^']+)'", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        foreach (Match match in unableIndex.Matches(text))
        {
            if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                paths.Add(match.Groups[1].Value.Trim());
            }
        }

        Regex failedInsert = new(@"error:\s+(.+?):\s+failed to insert into database", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        foreach (Match match in failedInsert.Matches(text))
        {
            if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                paths.Add(match.Groups[1].Value.Trim());
            }
        }

        Regex openDenied = new(@"error:\s+open\(""?(.+?)""?\):\s+(permission denied|access is denied)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        foreach (Match match in openDenied.Matches(text))
        {
            if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                paths.Add(match.Groups[1].Value.Trim());
            }
        }

        Regex openDirectoryDenied = new(@"warning:\s+could not open directory ['""]?([^'""]+)['""]?:\s+(permission denied|access is denied)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        foreach (Match match in openDirectoryDenied.Matches(text))
        {
            if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                paths.Add(match.Groups[1].Value.Trim());
            }
        }

        return paths.ToList();
    }

    private static void RemoveProblematicPath(string repositoryPath, string relativePath, Action<string, string> log)
    {
        string candidatePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.IsPathRooted(candidatePath)
            ? candidatePath
            : Path.Combine(repositoryPath, candidatePath);

        string repositoryRoot = Path.GetFullPath(repositoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string resolvedPath = Path.GetFullPath(fullPath);
        if (!resolvedPath.StartsWith(repositoryRoot, StringComparison.OrdinalIgnoreCase))
        {
            log("WARN", $"Bỏ qua đường dẫn nằm ngoài repository: {relativePath}");
            return;
        }

        try
        {
            if (File.Exists(resolvedPath))
            {
                File.SetAttributes(resolvedPath, FileAttributes.Normal);
                File.Delete(resolvedPath);
                log("WARN", $"Đã bỏ qua file bị khóa: {relativePath}");
                return;
            }

            if (Directory.Exists(resolvedPath))
            {
                ResetAttributesRecursive(resolvedPath);
                Directory.Delete(resolvedPath, recursive: true);
                log("WARN", $"Đã bỏ qua thư mục bị khóa: {relativePath}");
            }
        }
        catch (Exception exception)
        {
            log("WARN", $"Không thể loại bỏ đường dẫn bị lỗi '{relativePath}': {exception.Message}");
        }
    }

    private async Task<PushResult> PushWithRebaseRetryAsync(
        PushRequest request,
        string repositoryPath,
        IReadOnlyList<string> skippedFiles,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        string pushArguments = $"push -u origin {Quote(request.BranchName)}";
        _forcePushGuard.EnsureAllowed(pushArguments, request.BranchName, request.IsSafeModeEnabled);

        log("INFO", $"[GIT] chạy lệnh push lần 1: git {pushArguments}");
        ProcessCommandResult pushResult = await RunGitAsync(pushArguments, repositoryPath, cancellationToken);
        LogGitCommandResult(log, "git push lần 1", pushResult);
        if (pushResult.IsSuccess)
        {
            return new PushResult
            {
                IsSuccess = true,
                Summary = "Push thành công.",
                SkippedFiles = skippedFiles
            };
        }

        log("WARN", $"Push lần 1 thất bại: {Fallback(pushResult)}");
        log("INFO", "Thử pull --rebase rồi push lại...");

        ProcessCommandResult rebaseResult = await RunGitAsync(
            $"pull origin {Quote(request.BranchName)} --rebase",
            repositoryPath,
            cancellationToken);
        LogGitCommandResult(log, "git pull --rebase", rebaseResult);

        if (!rebaseResult.IsSuccess)
        {
            IReadOnlyList<string> conflictFiles = await ReadConflictFilesAsync(repositoryPath, cancellationToken);
            return new PushResult
            {
                IsSuccess = false,
                Summary = conflictFiles.Count > 0
                    ? "Rebase bị conflict. Cần xử lý thủ công."
                    : "Rebase thất bại.",
                SkippedFiles = skippedFiles,
                ConflictFiles = conflictFiles,
                ErrorDetails = Fallback(rebaseResult)
            };
        }

        ProcessCommandResult pushRetryResult = await RunGitAsync(pushArguments, repositoryPath, cancellationToken);
        LogGitCommandResult(log, "git push lần 2", pushRetryResult);
        if (pushRetryResult.IsSuccess)
        {
            return new PushResult
            {
                IsSuccess = true,
                Summary = "Push thành công sau khi rebase.",
                SkippedFiles = skippedFiles
            };
        }

        return new PushResult
        {
            IsSuccess = false,
            Summary = "Push thất bại sau khi retry.",
            SkippedFiles = skippedFiles,
            ErrorDetails = Fallback(pushRetryResult)
        };
    }

    private async Task<IReadOnlyList<string>> ReadConflictFilesAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        ProcessCommandResult diffResult = await RunGitAsync(
            "diff --name-only --diff-filter=U",
            repositoryPath,
            cancellationToken);

        if (!diffResult.IsSuccess || string.IsNullOrWhiteSpace(diffResult.StandardOutput))
        {
            return Array.Empty<string>();
        }

        return diffResult.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsGitMetadataPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        if (normalized.Equals(".git", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCommitMessage(PushRequest request)
    {
        return request.CommitMessageMode switch
        {
            CommitMessageMode.Automatic => $"Sao Lưu Dữ Liệu - {DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture)}",
            CommitMessageMode.Manual when !string.IsNullOrWhiteSpace(request.ManualCommitMessage) => request.ManualCommitMessage.Trim(),
            CommitMessageMode.Manual => throw new InvalidOperationException("Commit message thủ công đang rỗng."),
            _ => throw new InvalidOperationException("Commit mode không hợp lệ.")
        };
    }

    private static PushResult CreateFailure(
        string summary,
        ProcessCommandResult commandResult,
        IReadOnlyList<string> skippedFiles,
        IReadOnlyList<string> conflictFiles)
    {
        return new PushResult
        {
            IsSuccess = false,
            Summary = summary,
            SkippedFiles = skippedFiles,
            ConflictFiles = conflictFiles,
            ErrorDetails = Fallback(commandResult)
        };
    }

    private static bool ContainsNothingToCommit(ProcessCommandResult commandResult)
    {
        string text = $"{commandResult.StandardOutput}\n{commandResult.StandardError}";
        return text.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase)
               || text.Contains("no changes added", StringComparison.OrdinalIgnoreCase);
    }

    private async Task SyncSourceRepoBranchAsync(
        string sourceFolderPath,
        string branchName,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        log("INFO", $"Đang đồng bộ branch '{branchName}' về folder nguồn...");

        ProcessCommandResult fetchResult = await RunGitAsync(
            "fetch origin",
            sourceFolderPath,
            cancellationToken);
        LogGitCommandResult(log, "git fetch origin (source)", fetchResult);

        if (!fetchResult.IsSuccess)
        {
            log("WARN", $"Không thể fetch về folder nguồn: {Fallback(fetchResult)}");
            return;
        }

        // -B: tạo mới nếu chưa có, hoặc reset về remote ref nếu đã có
        ProcessCommandResult checkoutResult = await RunGitAsync(
            $"checkout -B {Quote(branchName)} origin/{Quote(branchName)}",
            sourceFolderPath,
            cancellationToken);
        LogGitCommandResult(log, $"git checkout -B {branchName} (source)", checkoutResult);

        if (!checkoutResult.IsSuccess)
        {
            log("WARN", $"Không thể tạo branch local '{branchName}' ở folder nguồn. {Fallback(checkoutResult)}");
            return;
        }

        log("OK", $"Đã tạo branch local '{branchName}' tracking origin/{branchName} tại folder nguồn.");
    }

    private static void TryDeleteTemporaryRepository(string tempRepositoryPath, Action<string, string> log)
    {
        if (!Directory.Exists(tempRepositoryPath))
        {
            return;
        }

        try
        {
            ResetAttributesRecursive(tempRepositoryPath);
        }
        catch
        {
            // Best effort before delete retries.
        }

        Exception? lastException = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(tempRepositoryPath))
                {
                    return;
                }

                Directory.Delete(tempRepositoryPath, recursive: true);
                return;
            }
            catch (Exception exception)
            {
                lastException = exception;
                System.Threading.Thread.Sleep(150 * attempt);
            }
        }

        if (lastException is not null)
        {
            log("WARN", $"Không thể xóa thư mục tạm: {tempRepositoryPath}. {lastException.Message}");
        }
    }

    private async Task<ProcessCommandResult> RunGitAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        Action<string>? gitHubRawLog = _gitHubRawLog;
        if (gitHubRawLog is null)
        {
            return await _gitCommandRunner.RunAsync(arguments, workingDirectory, cancellationToken);
        }

        gitHubRawLog.Invoke($"$ git {arguments}");
        ProcessCommandResult result = await _gitCommandRunner.RunAsync(
            arguments,
            workingDirectory,
            cancellationToken,
            standardOutputCallback: gitHubRawLog,
            standardErrorCallback: gitHubRawLog);
        gitHubRawLog.Invoke($"[exit {result.ExitCode}] git {arguments}");
        return result;
    }

    private static void ResetAttributesRecursive(string rootDirectory)
    {
        foreach (string filePath in Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }

        foreach (string directoryPath in Directory.EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directoryPath, FileAttributes.Normal);
        }

        File.SetAttributes(rootDirectory, FileAttributes.Normal);
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static string FirstLine(string value)
    {
        return value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string Fallback(ProcessCommandResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            return result.StandardError;
        }

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return result.StandardOutput;
        }

        return "không có chi tiết";
    }

    private static void LogGitCommandResult(Action<string, string> log, string action, ProcessCommandResult result)
    {
        string summary = SummarizeResultForLog(result);
        if (result.IsSuccess)
        {
            log("OK", $"[GIT] {action}: thành công (exit 0). {summary}");
            return;
        }

        log("ERROR", $"[GIT] {action}: thất bại (exit {result.ExitCode}). {summary}");
    }

    private static string SummarizeResultForLog(ProcessCommandResult result)
    {
        string source = !string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardError
            : result.StandardOutput;

        if (string.IsNullOrWhiteSpace(source))
        {
            return "(git không trả về output)";
        }

        string summary = source.Replace("\r", " ").Replace("\n", " ").Trim();
        summary = Regex.Replace(summary, @"\s{2,}", " ");

        const int maxLength = 260;
        if (summary.Length > maxLength)
        {
            summary = $"{summary[..maxLength]}...";
        }

        return summary;
    }

    private static bool LooksLikePathLengthIssue(ProcessCommandResult result)
    {
        string text = $"{result.StandardOutput}\n{result.StandardError}";
        return text.Contains("Filename too long", StringComparison.OrdinalIgnoreCase)
               || text.Contains("File name too long", StringComparison.OrdinalIgnoreCase)
               || text.Contains("path too long", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        string normalized = path.Trim().Trim('\'', '"').Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    private static string NormalizeForRepository(string repositoryPath, string path)
    {
        string normalized = NormalizePath(path);
        if (!Path.IsPathRooted(normalized))
        {
            return normalized;
        }

        string repositoryRoot = Path.GetFullPath(repositoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(normalized);
        if (!fullPath.StartsWith(repositoryRoot, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return NormalizePath(Path.GetRelativePath(repositoryPath, fullPath));
    }

    private static string ExtractSkipPath(string skippedEntry)
    {
        int reasonIndex = skippedEntry.IndexOf(" (", StringComparison.Ordinal);
        if (reasonIndex <= 0)
        {
            return skippedEntry;
        }

        return skippedEntry[..reasonIndex];
    }

    private sealed record LfsTrackCandidate(string Path, long SizeBytes);

    private readonly record struct KillProcessResult(string ErrorMessage);

    private readonly record struct LockedPathResolution(LockedPathAction Action, string Reason)
    {
        public static LockedPathResolution Resolved()
        {
            return new LockedPathResolution(LockedPathAction.Resolved, string.Empty);
        }

        public static LockedPathResolution Retry(string reason)
        {
            return new LockedPathResolution(LockedPathAction.Retry, reason);
        }

        public static LockedPathResolution Skip(string reason)
        {
            return new LockedPathResolution(LockedPathAction.Skip, reason);
        }
    }

    private enum LockedPathAction
    {
        Resolved,
        Retry,
        Skip
    }

    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;
    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;

        public FILETIME ProcessStartTime;
    }

    private enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string strServiceShortName;

        public RM_APP_TYPE ApplicationType;

        public uint AppStatus;

        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[] rgsFileNames,
        uint nApplications,
        [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);
    private static int ToMegabytes(long bytes)
    {
        return (int)Math.Round(bytes / 1024d / 1024d, MidpointRounding.AwayFromZero);
    }

    private static string FormatMegabytes(long bytes)
    {
        double megabytes = bytes / 1024d / 1024d;
        return $"{megabytes:0.0}MB";
    }
}


