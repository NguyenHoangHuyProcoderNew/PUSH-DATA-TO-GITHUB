# ============================================================
# SCRIPT TỰ ĐỘNG CÀI ĐẶT GIT + GIT LFS VÀ PUSH LÊN GITHUB
# Hỗ trợ file lớn (>100MB) với Git LFS
# Right-click -> Run with PowerShell
# ============================================================

# Cấu hình UTF-8 để hiển thị tiếng Việt đúng
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
chcp 65001 > $null

# QUAN TRỌNG: Không dùng Stop để script tiếp tục chạy dù có lỗi nhỏ
$ErrorActionPreference = "Continue"
$Host.UI.RawUI.WindowTitle = "Git Setup & Push (with LFS)"

# ============================================================
# CẤU HÌNH - THAY ĐỔI THÔNG TIN GITHUB CỦA BẠN Ở ĐÂY
# ============================================================
$GITHUB_REPO = "https://github.com/NguyenHoangHuyProcoderNew/DU-LIEU-MAY-BAN.git"
$GITHUB_USERNAME = "NguyenHoangHuyProcoderNew"
$GITHUB_EMAIL = "nguyenhoanghuyprocoder@gmail.com"
$BRANCH = "main"
# ============================================================

# Các loại file lớn cần dùng Git LFS (file >50MB)
$LFS_FILE_TYPES = @("*.exe", "*.dll", "*.pyd", "*.so", "*.dylib", "*.zip", "*.rar", "*.7z", "*.tar", "*.gz", "*.pt", "*.pth", "*.onnx", "*.bin", "*.pkl", "*.model", "*.weights", "*.h5", "*.pb")

# Thư mục hiện tại (TỰ ĐỘNG LẤY - KHÔNG CẦN SỬA)
$WORK_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path

# Nếu WORK_DIR rỗng (chạy từ console), dùng thư mục hiện tại
if ([string]::IsNullOrEmpty($WORK_DIR)) {
    $WORK_DIR = Get-Location
}

# Biến đếm tiến độ
$script:StepCounter = 0
$script:TotalSteps = 10

function Write-Step { 
    param($text) 
    $script:StepCounter++
    Write-Host "`n[$script:StepCounter/$script:TotalSteps] $text" -ForegroundColor Cyan 
}
function Write-OK { param($text) Write-Host "[OK] $text" -ForegroundColor Green }
function Write-Warn { param($text) Write-Host "[CANH BAO] $text" -ForegroundColor Yellow }
function Write-Err { param($text) Write-Host "[LOI] $text" -ForegroundColor Red }

Write-Host "`n" -NoNewline
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "    SCRIPT SAO LUU DU LIEU LEN GITHUB" -ForegroundColor Green
Write-Host "    Ho tro file lon voi Git LFS" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Cyan

Write-Host "Thu muc: $WORK_DIR" -ForegroundColor Yellow
Write-Host "Kho luu tru: $GITHUB_REPO" -ForegroundColor Yellow

# Kiểm tra Admin
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
if (-NOT $isAdmin) {
    Write-Warn "Can quyen Admin de cai dat. Dang yeu cau..."
    # Chuyển đổi đường dẫn an toàn
    $escapedPath = $WORK_DIR -replace "'", "''"
    $escapedScript = $PSCommandPath -replace "'", "''"
    Start-Process PowerShell -Verb RunAs -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "Set-Location -LiteralPath '$escapedPath'; & '$escapedScript'"
    exit
}

# Di chuyển đến thư mục làm việc
try {
    Set-Location -LiteralPath $WORK_DIR
    Write-Host "Da chuyen den: $(Get-Location)" -ForegroundColor Gray
}
catch {
    Write-Err "Khong the chuyen den thu muc: $WORK_DIR"
    Write-Host "Loi: $_" -ForegroundColor Red
    Read-Host "Nhan Enter de dong..."
    exit 1
}

# ============================================================
# 1. Kiểm tra và cài đặt Git
# ============================================================
Write-Step "Kiem tra Git..."
$gitInstalled = $false
$gitExe = Get-Command git -ErrorAction SilentlyContinue
if ($gitExe) {
    $gitVersion = & git --version 2>&1
    if ($gitVersion -match "git version") {
        Write-OK "Git da cai dat: $gitVersion"
        $gitInstalled = $true
    }
}

if (-not $gitInstalled) {
    Write-Warn "Git chua cai dat. Dang tai Git..."
    $gitInstaller = "$env:TEMP\git_installer.exe"
    
    try {
        Invoke-WebRequest -Uri "https://github.com/git-for-windows/git/releases/download/v2.43.0.windows.1/Git-2.43.0-64-bit.exe" -OutFile $gitInstaller
        Write-Host "Dang cai dat Git..."
        Start-Process -FilePath $gitInstaller -ArgumentList "/VERYSILENT /NORESTART /NOCANCEL /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /COMPONENTS=icons,ext\reg\shellhere,assoc,assoc_sh" -Wait
        
        # Cập nhật PATH
        $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")
        $env:PATH += ";C:\Program Files\Git\bin;C:\Program Files\Git\cmd"
        Write-OK "Da cai dat Git!"
    }
    catch {
        Write-Err "Khong the cai dat Git: $_"
        Read-Host "Nhan Enter de dong..."
        exit 1
    }
}

# ============================================================
# 2. Kiểm tra và cài đặt Git LFS
# ============================================================
Write-Step "Kiem tra Git LFS (ho tro file lon)..."
$lfsInstalled = $false
$lfsCheck = & git lfs version 2>&1
if ($lfsCheck -match "git-lfs") {
    Write-OK "Git LFS da cai dat: $lfsCheck"
    $lfsInstalled = $true
}

if (-not $lfsInstalled) {
    Write-Warn "Git LFS chua cai dat. Dang tai..."
    $lfsInstaller = "$env:TEMP\git-lfs-installer.exe"
    
    try {
        Invoke-WebRequest -Uri "https://github.com/git-lfs/git-lfs/releases/download/v3.4.1/git-lfs-windows-amd64-v3.4.1.exe" -OutFile $lfsInstaller
        Write-Host "Dang cai dat Git LFS..."
        Start-Process -FilePath $lfsInstaller -ArgumentList "/VERYSILENT /NORESTART" -Wait
        Write-OK "Da cai dat Git LFS!"
    }
    catch {
        Write-Err "Khong the cai dat Git LFS: $_"
    }
}

# ============================================================
# 3. Cấu hình Git global
# ============================================================
Write-Step "Cau hinh Git..."
& git config --global user.name $GITHUB_USERNAME 2>&1 | Out-Null
& git config --global user.email $GITHUB_EMAIL 2>&1 | Out-Null
& git config --global init.defaultBranch main 2>&1 | Out-Null
& git config --global core.autocrlf true 2>&1 | Out-Null
& git config --global core.quotepath false 2>&1 | Out-Null
& git config --global i18n.logoutputencoding utf-8 2>&1 | Out-Null
& git config --global i18n.commitencoding utf-8 2>&1 | Out-Null

# QUAN TRONG: Cho phep Git hoat dong trong thu muc nay
# (Fix loi "dubious ownership" khi cai lai Windows hoac copy folder tu may khac)

# Chuyen doi duong dan sang forward slash (Git can forward slash)
$safeDir = $WORK_DIR -replace '\\', '/'

# Them ca 2 cach de dam bao hoat dong
& git config --global --add safe.directory "$safeDir" 2>&1 | Out-Null
& git config --global --add safe.directory "*" 2>&1 | Out-Null
Write-Host "Da them safe.directory: $safeDir (va wildcard *)" -ForegroundColor Gray
Write-OK "Da cau hinh Git!"

# ============================================================
# 4. Khởi tạo repository (QUAN TRỌNG: PHẢI LÀM TRƯỚC)
# ============================================================
Write-Step "Khoi tao Git repository..."
$gitFolder = Join-Path $WORK_DIR ".git"

if (-not (Test-Path $gitFolder)) {
    Write-Warn "Chua co Git repository. Dang khoi tao moi..."
    $initResult = & git init 2>&1
    Write-Host $initResult -ForegroundColor Gray
    
    # Kiểm tra lại xem .git đã được tạo chưa
    if (Test-Path $gitFolder) {
        Write-OK "Da khoi tao Git repository moi!"
    }
    else {
        Write-Err "KHONG THE KHOI TAO GIT REPOSITORY!"
        Write-Host "Thu muc hien tai: $(Get-Location)" -ForegroundColor Red
        Read-Host "Nhan Enter de dong..."
        exit 1
    }
}
else {
    Write-OK "Git repository da ton tai!"
}

# ============================================================
# 5. Kích hoạt Git LFS (SAU KHI ĐÃ CÓ .git)
# ============================================================
Write-Step "Kich hoat Git LFS trong repository..."
$lfsResult = & git lfs install 2>&1
if ($lfsResult -match "error" -or $lfsResult -match "fatal") {
    Write-Err "Loi khi kich hoat LFS: $lfsResult"
}
else {
    Write-OK "Da kich hoat Git LFS!"
}

# ============================================================
# 6. Cấu hình Git LFS cho các loại file lớn
# ============================================================
Write-Step "Cau hinh Git LFS cho file lon..."
foreach ($fileType in $LFS_FILE_TYPES) {
    & git lfs track $fileType 2>&1 | Out-Null
}

# Quét file lớn
Write-Host "Dang quet file lon (>50MB)..." -ForegroundColor Yellow
$largeFiles = Get-ChildItem -Recurse -File -ErrorAction SilentlyContinue | 
Where-Object { 
    $_.Length -gt 50MB -and 
    $_.DirectoryName -notlike "*\.venv*" -and 
    $_.DirectoryName -notlike "*\.git*" 
}

if ($largeFiles) {
    Write-Warn "Tim thay $($largeFiles.Count) file lon:"
    foreach ($file in $largeFiles | Select-Object -First 10) {
        $sizeMB = [math]::Round($file.Length / 1MB, 2)
        Write-Host "  - $($file.Name) ($sizeMB MB)" -ForegroundColor Yellow
        $ext = $file.Extension
        if ($ext) {
            & git lfs track "*$ext" 2>&1 | Out-Null
        }
    }
}
Write-OK "Da cau hinh Git LFS!"

# ============================================================
# 7. Thêm remote
# ============================================================
Write-Step "Cau hinh remote..."
$remoteCheck = & git remote -v 2>&1
if ($remoteCheck -match "origin") {
    & git remote set-url origin $GITHUB_REPO 2>&1 | Out-Null
}
else {
    & git remote add origin $GITHUB_REPO 2>&1 | Out-Null
}
Write-OK "Kho luu tru: $GITHUB_REPO"

# ============================================================
# 8. Tạo/Cập nhật .gitignore
# ============================================================
Write-Step "Cap nhat .gitignore..."
$gitignoreContent = @"
# Virtual environment
.venv/
venv/
env/
__pycache__/
*.pyc

# IDE
.idea/
.vscode/
*.swp

# Temp files
~`$*
*.tmp
*.temp
*.log
.DS_Store
Thumbs.db
"@
$gitignoreContent | Out-File -FilePath ".gitignore" -Encoding UTF8 -Force
Write-OK "Da cap nhat .gitignore!"

# ============================================================
# 9. Stage và Commit
# ============================================================
Write-Step "Stage va Commit..."
& git add .gitattributes 2>&1 | Out-Null
& git add -A 2>&1 | Out-Null

$commitMessage = "SAO LƯU DỮ LIỆU - $(Get-Date -Format 'HH:mm:ss dd-MM-yyyy')"
$commitResult = & git commit -m $commitMessage 2>&1
if ($commitResult -match "nothing to commit") {
    Write-Warn "Khong co thay doi de commit."
}
else {
    Write-OK "Da commit: $commitMessage"
}

# ============================================================
# 10. Push lên GitHub
# ============================================================
Write-Step "Push len GitHub..."
Write-Host "Luu y: Neu day la lan dau, trinh duyet se mo de xac thuc GitHub." -ForegroundColor Yellow

$pushResult = & git push -u origin $BRANCH --force 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-OK "Da push thanh cong len GitHub!"
}
else {
    Write-Err "Loi khi push: $pushResult"
    Write-Host "Dang thu pull va push lai..." -ForegroundColor Yellow
    
    & git pull origin $BRANCH --rebase --allow-unrelated-histories 2>&1 | Out-Null
    $pushRetry = & git push -u origin $BRANCH 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-OK "Da push thanh cong!"
    }
    else {
        Write-Err "Van khong the push. Vui long kiem tra lai."
        Write-Host "Chi tiet loi: $pushRetry" -ForegroundColor Red
    }
}

# ============================================================
# HOÀN TẤT
# ============================================================
Write-Host "`n" -NoNewline
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "              HOAN TAT" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Kho luu tru: $GITHUB_REPO" -ForegroundColor Yellow
Write-Host "Nhanh: $BRANCH" -ForegroundColor Yellow
Write-Host "Git LFS: Da kich hoat cho file lon" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Cyan

# Hiển thị thông tin LFS
Write-Host "`nFile duoc quan ly boi Git LFS:" -ForegroundColor Cyan
& git lfs ls-files 2>&1 | Select-Object -First 10

Write-Host "`nNhan Enter de dong..." -ForegroundColor Yellow
Read-Host