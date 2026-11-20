# MySocialApp — Backend API

Backend nền tảng cho ứng dụng mạng xã hội lai **TikTok + Zalo + Shopee + Chatbot**, xây dựng trên **ASP.NET Core (.NET 10)** theo kiến trúc Clean Architecture 4 lớp, phục vụ đa nền tảng (Mobile App, Web App, Desktop App).

---

## 1. Yêu cầu hệ thống (Prerequisites)

- **.NET SDK**: [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- **Database**: Chọn 1 trong các cách sau:
  - **SQL Server LocalDB** (có sẵn khi cài Visual Studio trên Windows — khuyên dùng cho dev)
  - **SQL Server Express / Developer**
  - **Docker SQL Server** (khuyên dùng cho macOS / Linux hoặc muốn chạy nhanh qua container)
- **Tùy chọn (Optional)**:
  - **FFmpeg & ffprobe**: Dùng để nén video, trích xuất thumbnail bài viết/chat và kiểm tra thời lượng video/voice. *Mặc định `RequireFFmpeg = false`, nên nếu chưa cài FFmpeg thì backend vẫn chạy bình thường (chỉ bỏ qua bước nén video/tạo thumbnail).*
  - **Ollama**: Dùng cho tính năng Chatbot AI (`http://localhost:11434`, model `qwen3.5:4b`). Không cần cài nếu bạn không test Chatbot.

---

## 2. Cài đặt & Chạy nhanh (Quick Start)

### Bước 1: Clone repo
```bash
git clone https://github.com/QuangDuy-dev/projectzola-server.git
cd projectzola-server
```

### Bước 2: Tạo file cấu hình local (`appsettings.Development.json`)
File `appsettings.Development.json` đã được đưa vào `.gitignore` để mỗi người giữ cấu hình máy cá nhân và key bí mật riêng.

Copy từ file mẫu:
```powershell
# Windows PowerShell
Copy-Item src/MySocialApp.Api/appsettings.Example.json src/MySocialApp.Api/appsettings.Development.json
```
```bash
# macOS / Linux
cp src/MySocialApp.Api/appsettings.Example.json src/MySocialApp.Api/appsettings.Development.json
```

Mở `src/MySocialApp.Api/appsettings.Development.json` và chỉnh lại `ConnectionStrings.DefaultConnection` cho khớp SQL Server trên máy bạn (xem Mục 3 bên dưới).

### Bước 3: Chạy Backend API
```bash
dotnet run --project src/MySocialApp.Api
```
Khi chạy lần đầu, hệ thống sẽ **tự động**:
1. Áp dụng toàn bộ EF Core Migrations vào Database (`context.Database.Migrate()`).
2. Seed sẵn dữ liệu & tài khoản mẫu (`SeedData.Initialize()`).
3. Tự động tạo thư mục `storage/` cùng cấp với thư mục repo (xem Mục 4).

Truy cập **Swagger UI** tại:
- `http://localhost:5114/` (hoặc `http://localhost:5114/swagger`)

---

## 3. Hướng dẫn đầy đủ về Database (SQL Server)

### Cách A: Dùng SQL Server LocalDB (Windows — Khuyên dùng)
Kiểm tra và khởi động LocalDB:
```powershell
sqllocaldb info
sqllocaldb start MSSQLLocalDB
```
Chuỗi kết nối trong `appsettings.Development.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=MySocialAppDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
}
```

### Cách B: Dùng SQL Server Express (Windows)
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=.\\SQLEXPRESS;Database=MySocialAppDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
}
```

### Cách C: Dùng Docker SQL Server (Windows / macOS / Linux)
```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong@Passw0rd" -p 1433:1433 --name mysocialapp-sql -d mcr.microsoft.com/mssql/server:2022-latest
```
Chuỗi kết nối:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost,1433;Database=MySocialAppDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

### Quản lý EF Core Migrations
Cài EF CLI (nếu chưa có):
```bash
dotnet tool install --global dotnet-ef
```
Chạy migration thủ công (tùy chọn, vì `Program.cs` đã tự chạy lúc khởi động):
```bash
dotnet ef database update --project src/MySocialApp.Infrastructure --startup-project src/MySocialApp.Api
```
Khi bạn sửa Entity/DbContext và cần tạo Migration mới:
```bash
dotnet ef migrations add <TenMigration> --project src/MySocialApp.Infrastructure --startup-project src/MySocialApp.Api
```
> **Lưu ý khi làm việc nhóm:** Luôn `git pull` trước khi tạo Migration mới để tránh trùng hoặc lệch thứ tự snapshot (`MySocialAppDbContextModelSnapshot.cs`).

### Tài khoản Seed Data mặc định
| Role | Username | Password | Email |
|---|---|---|---|
| **Admin** | `admin` | `Admin@123` | `admin@mysocialapp.com` |
| **User** | `user1` | `User@123` | `user1@mysocialapp.com` |
| **ShopOwner** | `shopowner1` | `Shop@123` | `shop@mysocialapp.com` |
| **Shipper** | `shipper1` | `Shipper@123` | `shipper@mysocialapp.com` |

---

## 4. Cơ chế lưu Media (`storage/`) & Xử lý Ảnh/Video

### Đường dẫn `storage` tự động (Không cần sửa tay)
- Cấu hình mặc định là `"RootPath": "../storage"` trong `MediaStorageOptions`.
- Hàm `MediaStorageOptions.ResolveRootPath(...)` sẽ tự động tìm thư mục gốc repo (chứa `MySocialApp.slnx`) và tạo thư mục `storage/` **nằm cùng cấp với thư mục repo** (ví dụ bạn clone repo vào `C:\Projects\projectzola-server` thì ảnh/video sẽ tự lưu vào `C:\Projects\storage`).
- Nếu deploy bản publish độc lập (không có file `.slnx`), hệ thống tự fallback về thư mục `./storage` ngay cạnh file chạy.
- Nếu muốn lưu ở ổ đĩa khác trên máy cá nhân, bạn có thể ghi đè `MediaStorage.RootPath` trong `appsettings.Development.json`.

### Database có lưu đường dẫn cứng không?
**Không.** Database chỉ lưu **URL tương đối** dạng `/media/posts/videos/<file>.mp4`, `/media/avatars/<file>.jpg`, `/media/thumbnails/<file>.jpg`... thông qua `LocalMediaStorageService.GetUrl(storageKey)`.
Nhờ vậy, bạn bè clone code về máy nào cũng chạy được ngay mà không bị lỗi sai ổ đĩa `D:\` hay `C:\`.

### Cài đặt FFmpeg (Để xử lý Video & Voice đầy đủ)
- **Ảnh**: Dùng `SixLabors.ImageSharp` (thuần .NET, không cần cài thêm gì).
- **Video / Voice**: `MediaProcessingService` gọi `ffmpeg` và `ffprobe` từ `PATH` hệ thống.
  - **Windows**: `winget install Gyan.FFmpeg` (sau đó mở lại terminal).
  - **macOS**: `brew install ffmpeg`.
  - Nếu muốn trỏ tới file thực thi cụ thể, cấu hình `MediaStorage:FFmpegPath` và `MediaStorage:FFprobePath` trong `appsettings.Development.json`.

---

## 5. Hướng dẫn kết nối với Repo Mobile App (`projectzola-mobile`)

Repo Mobile App (React Native / Expo): [`QuangDuy-dev/projectzola-mobile`](https://github.com/QuangDuy-dev/projectzola-mobile.git)

### Trường hợp 1: Cả 2 chạy local trên cùng máy (Android Emulator)
1. Chạy Backend ở `http://localhost:5114`.
2. Trong repo `projectzola-mobile`, tạo file `.env` từ `.env.example`:
   ```env
   EXPO_PUBLIC_LOCAL_API_URL=http://10.0.2.2:5114
   ```
   *(Android Emulator dùng `10.0.2.2` để trỏ về `localhost` của máy tính).*

### Trường hợp 2: Test trên điện thoại thật cùng mạng Wi-Fi (LAN)
1. Chạy Backend lắng nghe trên tất cả card mạng (mở port `5114` trong Firewall nếu cần):
   ```bash
   dotnet run --project src/MySocialApp.Api --urls "http://0.0.0.0:5114"
   ```
2. Kiểm tra IP LAN máy chạy Backend (ví dụ `192.168.1.15`), rồi đặt vào `.env` của `projectzola-mobile`:
   ```env
   EXPO_PUBLIC_LOCAL_API_URL=http://192.168.1.15:5114
   ```

### Trường hợp 3: Bạn làm Mobile App từ xa gọi vào Backend máy của bạn
Nếu bạn của bạn không muốn cài SQL Server / FFmpeg trên máy họ mà chỉ muốn code Mobile App gọi thẳng vào máy bạn:
1. Chạy Backend trên máy bạn ở port `5114`.
2. Mở tunnel bằng **ngrok** hoặc **Cloudflare Tunnel**:
   ```bash
   ngrok http 5114
   ```
3. Gửi URL HTTPS của tunnel cho bạn của bạn điền vào `.env` của `projectzola-mobile`:
   ```env
   EXPO_PUBLIC_API_BASE_URL=https://<your-tunnel-domain>
   ```

---

## 6. Kiểm thử (Tests)

Chạy toàn bộ bộ test (xUnit + InMemory/Integration):
```bash
dotnet test MySocialApp.slnx
```

---

## 7. Cấu trúc Project

```text
sever/
├── MySocialApp.slnx
├── .gitignore
├── README.md
├── src/
│   ├── MySocialApp.Domain/           # Entities, Enums, Domain Interfaces
│   ├── MySocialApp.Application/      # DTOs, Service Interfaces, Business Logic, Options
│   ├── MySocialApp.Infrastructure/   # EF Core DbContext, Migrations, SeedData, Media & AI Services
│   └── MySocialApp.Api/              # Controllers, SignalR Hub (/hubs/chat), Middleware, Program.cs
└── tests/
    └── MySocialApp.Tests/            # Unit & Integration Tests (xUnit)
```

---

## 8. Quy ước làm việc nhóm (Git Workflow)

1. **Không commit trực tiếp lên `main`:** Tạo nhánh theo tính năng/bugfix, ví dụ `feat/chat-reaction` hoặc `fix/video-upload`.
2. **Không commit file chứa secrets:** Kiểm tra kỹ `git status` trước khi push, tuyệt đối không đưa `appsettings.Development.json` hay `storage/` lên repo.
3. **Thay đổi API Contract:** Khi thêm/sửa DTO hoặc Endpoint ảnh hưởng tới Mobile/Web App, ghi rõ vào PR description hoặc thông báo để đồng đội cập nhật client tương ứng.
