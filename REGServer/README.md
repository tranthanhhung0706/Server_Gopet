# REGServer

Base backend mới cho goPet, dựng lại từ kiến trúc của `GServer` cũ nhưng gọn hơn và dùng
kỹ thuật .NET 8 hiện đại. **Giao thức mạng (TCP framing + mã hoá TEA) được giữ bit-for-bit
để tương thích với client goPet hiện tại** — phần còn lại (logic game, DB, HTTP) được viết lại
theo hướng module hoá, dễ mở rộng dần.

## Chạy thử

```bash
cd REGServer
dotnet run
```

Mặc định: TCP `19180`, HTTP `8082` (health check tại `http://localhost:8082/health`).
Sửa `config/server.json` và `config/database.json` cho đúng môi trường của bạn.

## Đối chiếu với GServer cũ

| REGServer | Vai trò | Tương đương trong GServer |
|---|---|---|
| `Program.cs` | Composition root, khởi động mọi thứ | `Program.cs` + `App/Main.cs` |
| `Config/*` | Đọc `config/*.json` bằng `System.Text.Json` | `App.config` + `settings/*.cs` (System.Configuration) |
| `Networking/Tea.cs` | Mã hoá TEA — **giữ nguyên thuật toán** | `Server/IO/TEA.cs` |
| `Networking/Packet*.cs` | Đọc/ghi gói tin nhị phân | `Server/IO/Message.cs`, `DataInputStream.cs`, `DataOutputStream.cs` |
| `Networking/PacketStream.cs` | Đóng/mở khung gói tin trên stream | `Server/IO/MsgReader.cs` + `MsgSender.cs` |
| `Server/GameServer.cs` | TCP accept loop | `Server/Server.cs` |
| `Server/Session.cs` | 1 kết nối: bắt tay khoá, đọc/gửi async | `Server/IO/Session.cs` |
| `Server/Player.cs` | State của người chơi đang kết nối | `Server/Player.cs` (đã bỏ phần logic game khổng lồ) |
| `Server/OpcodeRouter.cs` + `Handlers/Opcodes.cs` | Định tuyến opcode -> handler | `Server/GameController.cs`, `MenuController.*.cs` (if/switch khổng lồ) |
| `Server/PlayerManager.cs` | Danh sách người chơi online | `Manager/PlayerManager.cs` |
| `Zone/*` | Zone/map instance chứa danh sách người chơi | `Place/Place.cs`, `Manager/MapManager.cs` |
| `Database/DbManager.cs` | Kết nối MySQL (Dapper) | `Manager/MYSQLManager.cs` |
| `Database/*Repository.cs` | Truy vấn bảng `player`/`user` | rải rác trong `GameController.cs`, `Data/User/*` |
| `Api/ApiServer.cs` | HTTP API tối giản (Minimal API) | `APIs/HttpServer.cs` (MVC Controllers + Swashbuckle) |
| `CommandLine/ConsoleCommandLoop.cs` | Lệnh console cho admin | `CommandLine/*.cs` + `Manager/CommandManager.cs` |

## Những gì đã lược bỏ có chủ đích

Đây là **base**, không phải bản port đầy đủ. Toàn bộ nghiệp vụ game thật (pet, item, clan, task,
battle, chợ, sự kiện...) trong `GameController.cs`/`Data/*` của GServer **chưa** được port sang.
Khi cần thêm 1 tính năng:

1. Tìm opcode/luồng xử lý tương ứng trong GServer (thường ở `GameController.cs` hoặc
   `MenuController.*.cs`).
2. Thêm hằng opcode vào `Server/Handlers/Opcodes.cs`.
3. Viết 1 handler mới (khuyến khích 1 file/tính năng thay vì nhồi vào 1 class), đăng ký qua
   `OpcodeRouter.Register(...)` trong `Program.cs`.
4. Nếu cần dữ liệu DB, thêm field vào `Database/Models/*Record.cs` hoặc tạo repository mới —
   schema MySQL đã có sẵn ở `../MariaDB_SQL/server_db.sql` và `web_db.sql`, không cần đổi.

## Lưu ý quan trọng

- **Không sửa** `Networking/Tea.cs` (phần Brew/Unbrew/Pack/Unpack) nếu không chắc chắn — sai 1
  bit là client cũ sẽ không giải mã được gói tin.
- Opcode trong `Packet.Id` là `sbyte` (giữ đúng kiểu Java gốc `byte`, có thể âm) để copy trực tiếp
  giá trị số từ code Java/C# cũ mà không phải quy đổi.
- `PacketStream` dùng framing y hệt bản gốc: `[Int32 BE length][Byte isEncrypted][payload]`.
