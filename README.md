# 🚀 Push Data To GitHub

**Ứng dụng Windows Desktop** giúp đẩy toàn bộ dữ liệu từ thư mục local lên GitHub một cách an toàn, tự động xử lý Git LFS cho file lớn, hỗ trợ nhiều phương thức xác thực, và có cơ chế retry thông minh khi gặp lỗi.

![Platform](https://img.shields.io/badge/Platform-Windows-blue?logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet)
![UI](https://img.shields.io/badge/UI-WPF-green)
![License](https://img.shields.io/badge/License-Private-lightgrey)

---

## 📋 Mục Lục

- [Tổng Quan](#-tổng-quan)
- [Tính Năng Chính](#-tính-năng-chính)
- [Yêu Cầu Hệ Thống](#-yêu-cầu-hệ-thống)
- [Cài Đặt & Chạy](#-cài-đặt--chạy)
- [Hướng Dẫn Sử Dụng](#-hướng-dẫn-sử-dụng)
- [Kiến Trúc Dự Án](#-kiến-trúc-dự-án)
- [Chi Tiết Kỹ Thuật](#-chi-tiết-kỹ-thuật)

---

## 🌟 Tổng Quan

**Push Data To GitHub** là ứng dụng WPF được xây dựng trên .NET 8, cho phép người dùng push nội dung từ bất kỳ thư mục nào lên GitHub repository mà **không cần biết lệnh Git**. Ứng dụng tự động xử lý toàn bộ quy trình: khởi tạo repository, cấu hình remote, đồng bộ dữ liệu theo chế độ mirror, commit, và push — tất cả chỉ với một cú click.

### Vấn đề giải quyết

- Push dữ liệu/backup lên GitHub mà **không cần dùng terminal**
- Tự động xử lý **file lớn** (>100MB) bằng Git LFS
- Tự động **bỏ qua file bị khóa** hoặc file vượt giới hạn GitHub (>2GB)
- Xử lý **conflict tự động** bằng rebase retry
- An toàn: **chặn force push** vào branch `main`/`master`

---

## ✨ Tính Năng Chính

### 🔐 Xác Thực Linh Hoạt
- **Browser/Device Flow** — Sử dụng Git Credential Manager, mở trình duyệt để đăng nhập GitHub
- **Personal Access Token (PAT)** — Nhập token trực tiếp, tự động lưu vào Windows Credential Manager

### 📦 Mirror Push Thông Minh
- Tạo workspace tạm để push, **không ảnh hưởng thư mục gốc**
- Đồng bộ dữ liệu theo chế độ mirror (xóa file cũ, thêm file mới)
- Tự động clone branch đã có hoặc tạo branch mới nếu chưa tồn tại

### 🗂️ Git LFS Tự Động
- Quét và detect file lớn (>100MB) tự động
- Track bằng Git LFS theo batch để tối ưu hiệu năng
- Bỏ qua file vượt giới hạn push (>2GB)
- Hiển thị tiến trình track **realtime** (% hoàn thành)

### 🛡️ An Toàn & Bảo Vệ
- **Safe Mode** — Chặn force push vào branch `main`/`master`
- Hỏi xác nhận khi remote origin thay đổi
- Xử lý file bị khóa: phát hiện tiến trình khóa, hỏi user trước khi kill process
- Retry tự động lên đến 12 lần khi `git add` gặp lỗi file lock

### 🔄 Rebase Retry
- Khi push bị reject do remote có commit mới, tự động fetch + rebase
- Phát hiện và báo cáo conflict files
- Retry push sau rebase thành công

### 📊 Nhật Ký Chi Tiết
- **Nhật Ký Thực Thi** — Log chi tiết từng bước (INFO, OK, WARN, ERROR) với timestamp
- **Nhật Ký GitHub** — Raw output từ lệnh git/GitHub
- **File Bỏ Qua** — Danh sách file không thể push (quá lớn, bị khóa...)
- **File Conflict** — Danh sách file bị conflict sau rebase
- **Heartbeat** — Hiển thị trạng thái realtime khi push đang chạy

### 🖱️ Trải Nghiệm Người Dùng
- Kéo thả thư mục (**drag & drop**) vào ô nhập liệu
- Giao diện card-based hiện đại với tone xanh dương
- Commit message tự động hoặc thủ công
- Kiểm tra môi trường (Git, Git LFS) trước khi push
- Hiển thị thời gian đã chạy theo giây

---

## 💻 Yêu Cầu Hệ Thống

| Thành phần       | Yêu cầu                        |
|-------------------|---------------------------------|
| **Hệ điều hành** | Windows 10/11 (x64)             |
| **.NET Runtime**  | .NET 8.0 Desktop Runtime        |
| **Git**           | Git for Windows (bắt buộc)      |
| **Git LFS**       | Git LFS (bắt buộc)              |
| **Git Credential Manager** | Được cài kèm Git for Windows |

---

## 🔧 Cài Đặt & Chạy

### Cách 1: Chạy từ file build sẵn

```bash
# File thực thi đã có sẵn trong thư mục App/
.\App\PushDataToGitHub.App.exe
```

### Cách 2: Build từ mã nguồn

```bash
# Clone repository
git clone <repository-url>
cd PUSH-DATA-TO-GITHUB

# Build
dotnet build PushDataToGitHubApp.slnx -c Release

# Chạy
dotnet run --project src/PushDataToGitHub.App/PushDataToGitHub.App.csproj
```

### Kiểm tra môi trường

Trước khi push, nhấn nút **"Kiểm tra Git/Git LFS"** trong ứng dụng để đảm bảo Git và Git LFS đã được cài đặt đúng.

---

## 📖 Hướng Dẫn Sử Dụng

### Bước 1: Chọn thư mục
- Nhấn **"Chọn thư mục"** hoặc **kéo thả** thư mục vào ô Thư mục local

### Bước 2: Nhập thông tin repository
- **URL GitHub repo** — VD: `https://github.com/username/repo-name`
- **Branch đích** — Mặc định: `main`

### Bước 3: Chọn phương thức xác thực
- **Browser/Device** — Mở trình duyệt để đăng nhập (khuyên dùng)
- **PAT** — Nhập Personal Access Token nếu không muốn dùng trình duyệt

### Bước 4: Tùy chỉnh (tuỳ chọn)
- Bật/tắt **Safe Mode** (chặn force push vào main/master)
- Chọn chế độ commit message: **Tự động** hoặc **Thủ công**

### Bước 5: Push
- Nhấn **"Push Ngay"** và theo dõi tiến trình tại panel Nhật Ký

---

## 🏗️ Kiến Trúc Dự Án

```
PUSH-DATA-TO-GITHUB/
├── PushDataToGitHubApp.slnx          # Solution file
├── App/                               # Build output (executable)
│   ├── PushDataToGitHub.App.exe
│   └── ...
└── src/                               # Mã nguồn chính
    ├── PushDataToGitHub.App.csproj     # Project file (.NET 8, WPF)
    ├── App.xaml / App.xaml.cs          # Entry point
    ├── MainWindow.xaml / .cs           # Giao diện chính
    ├── Assets/
    │   └── AppIcon.ico                 # Icon ứng dụng
    ├── Commands/
    │   ├── RelayCommand.cs             # ICommand đồng bộ
    │   └── AsyncRelayCommand.cs        # ICommand bất đồng bộ
    ├── Models/
    │   ├── AuthenticationMode.cs       # Enum: Browser/PAT
    │   ├── CommitMessageMode.cs        # Enum: Auto/Manual
    │   ├── PushRequest.cs              # Input cho push
    │   ├── PushResult.cs               # Kết quả push
    │   ├── LogEntry.cs                 # Dòng log
    │   ├── LockedFilePrompt.cs         # Prompt xử lý file khóa
    │   ├── LockingProcessInfo.cs       # Thông tin process khóa file
    │   └── OptionItem.cs               # Item cho ComboBox
    ├── ViewModels/
    │   ├── ViewModelBase.cs            # Base class (INotifyPropertyChanged)
    │   └── MainViewModel.cs            # ViewModel chính (MVVM)
    └── Services/
        ├── Dialogs/
        │   ├── DialogService.cs        # MessageBox wrapper
        │   └── FolderPickerService.cs  # Chọn thư mục
        ├── Preflight/
        │   ├── GitCommandRunner.cs     # Thực thi lệnh git
        │   └── PreflightService.cs     # Kiểm tra Git/Git LFS
        ├── Push/
        │   ├── GitPushService.cs       # Logic push chính (~1500 dòng)
        │   └── RepositoryUrlComparer.cs # So sánh URL repo
        ├── Safety/
        │   └── ForcePushGuard.cs       # Chặn force push branch chính
        └── Security/
            └── CredentialService.cs    # Xác thực GitHub
```

### Design Pattern: **MVVM** (Model-View-ViewModel)

| Layer | Thành phần | Vai trò |
|-------|-----------|---------|
| **View** | `MainWindow.xaml` | Giao diện WPF, data binding |
| **ViewModel** | `MainViewModel.cs` | Logic UI, commands, state |
| **Model** | `Models/` | Data objects (PushRequest, PushResult...) |
| **Service** | `Services/` | Business logic (Git operations, auth...) |

---

## 🔬 Chi Tiết Kỹ Thuật

### Quy trình Push (Pipeline)

```
1. Validate input (folder, URL, branch, auth)
2. Preflight check (Git + Git LFS installed?)
3. Authenticate (Browser/Device hoặc PAT)
4. Kiểm tra remote origin cũ vs mới
5. Clone/Init workspace tạm
6. Mirror sync: source → temp workspace
7. Git LFS track file >100MB (batch mode)
8. git add -A (resilient, tối đa 12 lần retry)
9. git commit
10. git push (rebase retry nếu bị reject)
11. Sync branch về source folder
12. Cleanup workspace tạm
```

### Xử lý file bị khóa

Khi `git add` thất bại do file bị process khác chiếm:
1. Phát hiện đường dẫn file lỗi từ git output
2. Dùng **Restart Manager API** (Win32) để xác định tiến trình đang khóa
3. Hiển thị dialog cho user: kill process hoặc bỏ qua file
4. Retry tự động sau khi xử lý — tối đa **5 lần** mỗi file, **12 lần** tổng

### Cấu hình Git tự động

Mỗi phiên push tự động cấu hình:
- `core.longpaths true` — Hỗ trợ đường dẫn dài trên Windows
- `core.autocrlf false` — Không chuyển đổi line endings
- `core.safecrlf false` — Tắt cảnh báo CRLF

---

## 📝 Ghi Chú

- Ứng dụng tạo workspace tạm tại `%TEMP%\PushDataToGitHubApp\sessions\` và tự dọn dẹp sau mỗi phiên
- File >2GB sẽ **luôn bị bỏ qua** (giới hạn GitHub LFS)
- File 100MB–2GB sẽ được tự động **track bằng Git LFS**
- Nên sử dụng **Browser/Device** auth cho trải nghiệm tốt nhất
- Safe Mode được **bật mặc định** để bảo vệ branch `main`/`master`
