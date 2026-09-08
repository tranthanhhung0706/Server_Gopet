using System.Net.Sockets;
using System.Text;
using System.Linq;

namespace MockClient;

// Mock client tối giản để test GServer qua TCP.
// Giao thức (xem GServer/Server/IO/Session.cs, MsgReader.cs, MsgSender.cs, Message.cs):
//   Handshake: client gửi 9 byte đầu tiên (byte[0] bỏ qua, byte[1..8] = key TEA dạng long big-endian).
//              Server không xác thực giá trị này, chỉ dùng để tạo TEA cho việc mã hoá (hiện tại
//              không có gói tin nào server gửi đi bị mã hoá nên client không cần cài TEA để đọc).
//   Frame:     [Int32 BE: payloadLength + 1][1 byte: cờ mã hoá (0/1)][payload]
//   Payload:   byte đầu = opcode (sbyte), phần còn lại là dữ liệu theo từng lệnh (xem GopetCMD.cs, Player.cs).
internal static class Program
{
    private const sbyte CLIENT_INFO = -36;
    private const sbyte LOGIN = 1;
    private const sbyte REGISTER = 35;
    private const sbyte LOGIN_SUCCES = 3;
    private const sbyte LOGIN_FAILED = 4;
    private const sbyte COMMAND_IMAGE = 96;
    private const string ImageOutputDir = "received_images";
    private const string AppVersion = "1.4.3";
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(60);
    // 160.191.214.66
    private static NetworkStream? _stream;
    private static readonly object _writeLock = new();

    // Gom cac path anh (chuoi ket thuc .png/.jpg/.jpeg/.gif) da thay server nhac toi trong bat ky goi tin
    // that nao (khong phai do MockClient tu doan) - xem DecodeAndPrint. Day la nguon du lieu 100% chinh xac
    // voi server dang chay vi chinh server la ben gui chuoi nay, khong can biet truoc repo/DB.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _knownImagePaths = new();
    private static volatile bool _autoHarvestFetch = false;

    // 14.225.198.250
    private static async Task<int> Main(string[] args)
    {
        string host = args.Length > 0 ? args[0] : "14.225.198.250";
        int port = args.Length > 1 ? int.Parse(args[1]) : 19180;

        Console.WriteLine($"[MockClient] Connecting to {host}:{port} ...");
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MockClient] KHONG the ket noi: {ex.Message}");
            return 1;
        }
        Console.WriteLine("[MockClient] Da ket noi TCP thanh cong.");

        _stream = client.GetStream();

        SendHandshake(_stream);
        Console.WriteLine("[MockClient] Da gui handshake (9 byte key).");

        var readerThread = new Thread(() => ReadLoop(_stream)) { IsBackground = true };
        readerThread.Start();

        // Server yêu cầu CLIENT_INFO là gói đầu tiên, nếu không sẽ đóng kết nối ngay (Player.cs:96).
        SendClientInfo(_stream, languageCode: "vi", appVersion: AppVersion);
        Console.WriteLine("[MockClient] Da gui CLIENT_INFO, cho server phan hoi...");

        // Server (Session.cs/MsgReader.cs) khong tu ngat ket noi ranh, nhung ha tang mang (firewall/NAT/
        // load-balancer) phia truoc server that thuong tu dong dong cac ket noi TCP khong co traffic sau
        // vai phut. Gui lai CLIENT_INFO dinh ky nhu 1 goi "nhip tim" vo hai (server chi re-ack, khong reset
        // trang thai dang nhap - xem Player.cs:104-130) de giu ket noi song khi de MockClient chay lau (vd
        // luc dang bat "harvest on").
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(KeepAliveInterval);
                try
                {
                    if (_stream == null) break;
                    SendClientInfo(_stream, "vi", AppVersion);
                }
                catch
                {
                    break; // da mat ket noi, ReadLoop se tu bao loi rieng
                }
            }
        });

        PrintHelp();
        while (true)
        {
            Console.Write("> ");
            string? line = Console.ReadLine();
            if (line == null) break;
            line = line.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    case "quit":
                    case "exit":
                        return 0;
                    case "help":
                        PrintHelp();
                        break;
                    case "info":
                        SendClientInfo(_stream, "vi", AppVersion);
                        break;
                    case "login" when parts.Length >= 3:
                        {
                            var up = parts[2].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                            SendLogin(_stream, parts[1], up.Length > 0 ? up[0] : "", AppVersion);
                            break;
                        }
                    case "register" when parts.Length >= 3:
                        SendRegister(_stream, parts[1], parts[2]);
                        break;
                    case "img" when parts.Length >= 2:
                        {
                            string path = parts.Length >= 3 ? $"{parts[1]} {parts[2]}" : parts[1];
                            SendImageRequest(_stream, path);
                            break;
                        }
                    case "imgrange" when parts.Length >= 3:
                        {
                            var rangeArgs = parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (rangeArgs.Length < 2 || !int.TryParse(rangeArgs[0], out int start) || !int.TryParse(rangeArgs[1], out int end))
                            {
                                Console.WriteLine("[MockClient] Cu phap: imgrange <template co {n}> <start> <end> [delayMs]");
                                break;
                            }
                            int delayMs = rangeArgs.Length >= 3 && int.TryParse(rangeArgs[2], out int d2) ? d2 : 150;
                            await RequestImgRangeAsync(_stream, parts[1], start, end, delayMs);
                            break;
                        }
                    case "raw" when parts.Length >= 2:
                        {
                            var rawParts = parts[1].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                            sbyte opcode = (sbyte)sbyte.Parse(rawParts[0]);
                            byte[] body = rawParts.Length > 1 ? HexToBytes(rawParts[1]) : Array.Empty<byte>();
                            SendPacket(_stream, opcode, body);
                            Console.WriteLine($"[MockClient] Da gui raw opcode={opcode} body={body.Length} byte");
                            break;
                        }
                    case "frameall":
                        {
                            int delayMs = 150;
                            string? assetsDir = null;
                            if (parts.Length >= 2 && int.TryParse(parts[1], out int d)) delayMs = d;
                            if (parts.Length >= 3) assetsDir = parts[2];
                            await RequestAllAssetImgsAsync(_stream, "petFrame3", assetsDir, delayMs);
                            break;
                        }
                    case "animall":
                        {
                            int delayMs = 150;
                            string? assetsDir = null;
                            if (parts.Length >= 2 && int.TryParse(parts[1], out int d)) delayMs = d;
                            if (parts.Length >= 3) assetsDir = parts[2];
                            await RequestAllAssetImgsAsync(_stream, "anim_characters", assetsDir, delayMs);
                            break;
                        }
                    case "harvest":
                        {
                            string sub = parts.Length >= 2 ? parts[1].ToLowerInvariant() : "list";
                            switch (sub)
                            {
                                case "on":
                                    _autoHarvestFetch = true;
                                    Console.WriteLine("[MockClient] Harvest ON: moi path anh moi thay tu server se tu dong duoc xin anh that.");
                                    break;
                                case "off":
                                    _autoHarvestFetch = false;
                                    Console.WriteLine("[MockClient] Harvest OFF.");
                                    break;
                                case "save" when parts.Length >= 3:
                                    File.WriteAllLines(parts[2], _knownImagePaths.Keys.OrderBy(x => x));
                                    Console.WriteLine($"[MockClient] Da luu {_knownImagePaths.Count} path vao {parts[2]}");
                                    break;
                                default:
                                    Console.WriteLine($"[MockClient] Da gom duoc {_knownImagePaths.Count} path anh tu server (harvest {(_autoHarvestFetch ? "ON" : "OFF")}):");
                                    foreach (var p in _knownImagePaths.Keys.OrderBy(x => x))
                                    {
                                        Console.WriteLine($"    {p}");
                                    }
                                    break;
                            }
                            break;
                        }
                    default:
                        Console.WriteLine("Lenh khong hop le. Go 'help' de xem huong dan.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MockClient] Loi khi gui lenh: {ex.Message}");
            }
        }
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Cac lenh:
              info                     - gui lai goi CLIENT_INFO (bat buoc truoc khi lam gi khac)
              login <user> <pass>      - gui goi LOGIN
              register <user> <pass>   - gui goi REGISTER
              img <path>               - yeu cau anh that (vd: img npcs/tran_tran.png), luu vao received_images/
              imgrange <template co {n}> <start> <end> [delayMs]
                                       - lap gui img tu <start> den <end>, thay {n} bang tung so
                                         vd: imgrange anim_characters/{n}.png 61 62
                                         -> gui img anim_characters/61.png roi img anim_characters/62.png
                                         (delay mac dinh 150ms giua cac lan gui)
              raw <opcode> [hexBytes]  - gui goi tuy y, vi du: raw 1 00047573657200047061737300053"
              frameall [delayMs] [assetsDir] - quet thu muc petFrame3 cuc bo (mac dinh tu tim GServer/assets/petFrame3
                                         quanh vi tri chay MockClient, hoac truyen duong dan rieng), gui lan luot
                                         COMMAND_IMAGE cho tung file tim thay (delay mac dinh 150ms giua cac lan gui
                                         de tranh flood), anh luu vao received_images/
              animall [delayMs] [assetsDir] - giong frameall nhung quet thu muc GServer/assets/anim_characters
                                         (anh animation nhan vat) thay vi petFrame3
              harvest [on|off|save <file>] - gom cac path anh (.png/.jpg/.jpeg/.gif) server DA THAT SU nhac toi
                                         trong goi tin that (khong doan mo) - chinh xac 100% voi server dang
                                         connect vi server tu gui, khong can biet truoc local repo/DB.
                                         "harvest on": tu dong xin anh that (COMMAND_IMAGE) ngay khi thay path moi
                                         (can login/di lai/mo man hinh pet de server gui cac goi co path that).
                                         "harvest" (khong tham so): liet ke path da gom duoc.
                                         "harvest save <file>": luu danh sach path da gom ra file text.
              help                     - hien lai huong dan
              quit                     - thoat
            """);
    }

    private static byte[] HexToBytes(string hex)
    {
        hex = hex.Replace(" ", "");
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    private static void SendHandshake(NetworkStream stream)
    {
        var rnd = new Random();
        var keyBytes = new byte[9];
        rnd.NextBytes(keyBytes); // giá trị không được server xác thực, chỉ dùng để seed TEA phía server
        stream.Write(keyBytes, 0, keyBytes.Length);
        stream.Flush();
    }

    private static void SendClientInfo(NetworkStream stream, string languageCode, string appVersion)
    {
        var w = new PacketWriter();
        w.WriteSByte(0);                 // CLIENT_TYPE
        w.WriteInt(0);                   // PROVIDER
        w.WriteUTF(appVersion);          // ApplicationVersion (phải >= 1.4.3, xem GopetManager.VERSION_143)
        w.WriteUTF("MockClient");        // info
        w.WriteInt(240);                 // displayWidth
        w.WriteInt(320);                 // displayHeight
        w.WriteUTF(languageCode);        // phải là "vi" hoặc "en" (GopetManager.Language)
        w.WriteUTF("");                  // Refcode
        SendPacket(stream, CLIENT_INFO, w.ToArray());
    }

    private static void SendLogin(NetworkStream stream, string username, string password, string version)
    {
        var w = new PacketWriter();
        w.WriteUTF(username);
        w.WriteUTF(password);
        w.WriteUTF(version);
        SendPacket(stream, LOGIN, w.ToArray());
        Console.WriteLine($"[MockClient] Da gui LOGIN user={username}");
    }

    // Yêu cầu ảnh thật: opcode COMMAND_IMAGE (96), server đọc trong GameController.cs:337
    // (readsbyte gameType, readsbyte type, readUTF path) rồi trả lại ảnh nếu file tồn tại
    // trong GServer/assets/<path> (xem GameController.requestImg, PlatformHelper.loadAssets).
    private static void SendImageRequest(NetworkStream stream, string path)
    {
        var w = new PacketWriter();
        w.WriteSByte(0); // gameType = 0
        w.WriteSByte(0); // type: phải khác 10 và 11 (xem GameController.cs:891)
        w.WriteUTF(path);
        SendPacket(stream, COMMAND_IMAGE, w.ToArray());
        Console.WriteLine($"[MockClient] Da gui yeu cau anh: \"{path}\"");
    }

    // Lap gui SendImageRequest tu start den end (ho tro ca giam dan neu start > end), thay {n} trong
    // template bang tung so. Vi du template "anim_characters/{n}.png" voi start=61 end=62 se gui lan luot
    // "anim_characters/61.png" roi "anim_characters/62.png", co delay giua cac lan de khong flood server.
    private static async Task RequestImgRangeAsync(NetworkStream stream, string template, int start, int end, int delayMs)
    {
        int step = start <= end ? 1 : -1;
        int count = Math.Abs(end - start) + 1;
        int i = start;
        int sent = 0;
        while (true)
        {
            string path = template.Replace("{n}", i.ToString());
            SendImageRequest(stream, path);
            sent++;
            Console.WriteLine($"[MockClient] ({sent}/{count}) da gui: {path}");
            if (i == end) break;
            i += step;
            if (delayMs > 0) await Task.Delay(delayMs);
        }
    }

    private static void SendRegister(NetworkStream stream, string username, string password)
    {
        var w = new PacketWriter();
        w.WriteUTF(username);
        w.WriteUTF(password);
        SendPacket(stream, REGISTER, w.ToArray());
        Console.WriteLine($"[MockClient] Da gui REGISTER user={username}");
    }

    // Khong co opcode "gui 1 lan nhan nhieu anh" trong giao thuc goc (COMMAND_IMAGE / REQUEST_PET_IMG
    // deu la 1 request <-> 1 anh, xem GameController.requestImg/requestPetImg). Ham nay mo phong hanh vi
    // "tai het" bang cach quet truc tiep 1 thu muc con trong GServer/assets tren dia (khong can DB/mat khau)
    // roi gui lan luot tung COMMAND_IMAGE (co delay giua cac lan de khong flood server) - moi phan hoi
    // da duoc HandleCommandImage tu luu vao received_images/ theo dung path server echo lai.
    // subDir: ten thu muc con trong GServer/assets (vd "petFrame3", "anim_characters").
    private static async Task RequestAllAssetImgsAsync(NetworkStream stream, string subDir, string? assetsDirArg, int delayMs)
    {
        string? assetsDir = assetsDirArg != null ? Path.GetFullPath(assetsDirArg) : FindAssetsSubDir(subDir);
        if (assetsDir == null || !Directory.Exists(assetsDir))
        {
            Console.WriteLine($"[MockClient] Khong tim thay thu muc {subDir}. Truyen duong dan cu the: " +
                               $"<lenh> [delayMs] <duong-dan-toi-GServer/assets/{subDir}>");
            return;
        }

        string[] files = Directory.GetFiles(assetsDir)
            .Where(f => ImageExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        string[] paths = files.Select(f => $"{subDir}/{Path.GetFileName(f)}").ToArray();

        Console.WriteLine($"[MockClient] Tim thay {paths.Length} file anh trong \"{assetsDir}\". " +
                           $"Bat dau gui yeu cau anh (delay {delayMs}ms)...");
        for (int i = 0; i < paths.Length; i++)
        {
            SendImageRequest(stream, paths[i]);
            Console.WriteLine($"[MockClient] ({i + 1}/{paths.Length}) da gui yeu cau: {paths[i]}");
            if (delayMs > 0) await Task.Delay(delayMs);
        }
        Console.WriteLine("[MockClient] Da gui xong tat ca yeu cau. Cho server tra ve anh (xem log [SERVER] COMMAND_IMAGE ben duoi).");
    }

    // Doan MockClient nam canh GServer trong cung repo (SRCGOPETGOC/MockClient va SRCGOPETGOC/GServer),
    // nen tu do tim GServer/assets/<subDir> tu vi tri file .exe dang chay hoac thu muc lam viec hien tai.
    private static string? FindAssetsSubDir(string subDir)
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "GServer", "assets", subDir),
            Path.Combine(Environment.CurrentDirectory, "GServer", "assets", subDir),
            Path.Combine(Environment.CurrentDirectory, "..", "GServer", "assets", subDir),
            Path.Combine(Environment.CurrentDirectory, "..", "..", "GServer", "assets", subDir),
        };
        foreach (var candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);
            if (Directory.Exists(full)) return full;
        }
        return null;
    }

    // Co lock vi harvest tu-dong (chay tren ReadLoop thread) va cac lenh nguoi dung go (main thread)
    // co the cung goi SendPacket/SendImageRequest cung luc - khong lock se bi ghi chong cheo, hong frame.
    private static void SendPacket(NetworkStream stream, sbyte opcode, byte[] body)
    {
        var payload = new byte[1 + body.Length];
        payload[0] = unchecked((byte)opcode);
        Array.Copy(body, 0, payload, 1, body.Length);

        lock (_writeLock)
        {
            WriteInt32BE(stream, payload.Length + 1);
            stream.WriteByte(0); // cờ mã hoá = 0 (server hiện không gửi/nhận gói mã hoá TEA)
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }
    }

    private static void WriteInt32BE(Stream s, int value)
    {
        Span<byte> b = stackalloc byte[4];
        b[0] = (byte)(value >> 24);
        b[1] = (byte)(value >> 16);
        b[2] = (byte)(value >> 8);
        b[3] = (byte)value;
        s.Write(b);
    }

    private static void ReadLoop(NetworkStream stream)
    {
        try
        {
            while (true)
            {
                int hi = ReadInt32BE(stream);
                if (hi == -1)
                {
                    Console.WriteLine("[MockClient] Server bao dong ket noi (marker -1).");
                    return;
                }
                int length = hi - 1;
                int flag = stream.ReadByte();
                if (flag == -1)
                {
                    Console.WriteLine("[MockClient] Server dong socket.");
                    return;
                }
                if (length <= 0) continue;

                byte[] data = ReadExact(stream, length);
                sbyte opcode = unchecked((sbyte)data[0]);
                byte[] body = data[1..];
                DecodeAndPrint(opcode, flag == 1, body);
            }
        }
        catch (IOException)
        {
            Console.WriteLine("[MockClient] Mat ket noi voi server.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MockClient] Loi doc du lieu: {ex.Message}");
        }
    }

    private static int ReadInt32BE(Stream s)
    {
        Span<byte> b = stackalloc byte[4];
        int read = 0;
        while (read < 4)
        {
            int n = s.Read(b.Slice(read));
            if (n == 0) throw new IOException("EOF");
            read += n;
        }
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }

    private static byte[] ReadExact(Stream s, int count)
    {
        var buf = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = s.Read(buf, read, count - read);
            if (n == 0) throw new IOException("EOF");
            read += n;
        }
        return buf;
    }

    private static void DecodeAndPrint(sbyte opcode, bool encrypted, byte[] body)
    {
        switch (opcode)
        {
            case CLIENT_INFO:
                bool ok = body.Length > 0 && body[0] == 1;
                Console.WriteLine(ok
                    ? "[SERVER] CLIENT_INFO ACK -> OK, co the login/register."
                    : "[SERVER] CLIENT_INFO ACK -> TU CHOI (sai ngon ngu hoac version cu). Server se dong ket noi.");
                break;
            case LOGIN_FAILED:
                Console.WriteLine($"[SERVER] LOGIN_FAILED: \"{TryReadUTF(body)}\"");
                break;
            case LOGIN_SUCCES:
                Console.WriteLine("[SERVER] LOGIN_SUCCES nhan duoc (dang nhap thanh cong hoac buoc tiep theo cua luong login).");
                break;
            case COMMAND_IMAGE:
                HandleCommandImage(body);
                return; // đã tự in log riêng, không chạy tiếp scanner chuỗi bên dưới trên dữ liệu ảnh nhị phân
            default:
                Console.WriteLine($"[SERVER] opcode={opcode} encrypted={encrypted} len={body.Length} hex={ToHex(body, 32)}");
                break;
        }

        // Các gói đồng bộ trạng thái (GAME_OBJECT, PET_SERVICE...) chỉ gửi TÊN/ĐƯỜNG DẪN ảnh
        // dạng chuỗi (writeUTF: 2 byte độ dài BE + UTF-8) xen giữa dữ liệu nhị phân khác — muốn lấy
        // ảnh thật phải dùng lệnh "img <path>" (gửi COMMAND_IMAGE) để server đọc file trong assets/ và trả byte thật.
        // Quét toàn bộ payload để tìm các chuỗi tên/đường dẫn đó, không phụ thuộc opcode.
        var strings = ExtractStrings(body);
        foreach (var s in strings)
        {
            bool looksLikeImage = ImageExtensions.Any(ext => s.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            string tag = looksLikeImage ? "[ANH]" : "[TXT]";
            Console.WriteLine($"    {tag} \"{s}\"");
            if (looksLikeImage && _knownImagePaths.TryAdd(s, 0))
            {
                Console.WriteLine($"    [HARVEST] Path moi tu server: \"{s}\"" + (_autoHarvestFetch ? " -> dang xin anh that..." : ""));
                if (_autoHarvestFetch && _stream != null)
                {
                    SendImageRequest(_stream, s);
                }
            }
        }
    }

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif" };

    // Quét brute-force: tại mỗi vị trí, thử đọc 2 byte làm độ dài chuỗi UTF (giống DataOutputStream.writeUTF
    // phía server), nếu đoạn theo sau decode ra được text hợp lệ thì coi là 1 chuỗi và nhảy qua, ngược lại lùi 1 byte.
    private static List<string> ExtractStrings(byte[] body)
    {
        var result = new List<string>();
        int i = 0;
        while (i + 2 <= body.Length)
        {
            int len = (body[i] << 8) | body[i + 1];
            if (len >= 2 && len <= 200 && i + 2 + len <= body.Length && IsLikelyText(body, i + 2, len))
            {
                result.Add(Encoding.UTF8.GetString(body, i + 2, len));
                i += 2 + len;
            }
            else
            {
                i++;
            }
        }
        return result;
    }

    private static bool IsLikelyText(byte[] data, int offset, int length)
    {
        string s;
        try
        {
            var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
            s = utf8.GetString(data, offset, length);
        }
        catch
        {
            return false;
        }
        foreach (char c in s)
        {
            if (char.IsControl(c)) return false;
        }
        return true;
    }

    private static string? TryReadUTF(byte[] body)
    {
        try
        {
            if (body.Length < 2) return null;
            int len = (body[0] << 8) | body[1];
            if (len < 0 || len + 2 > body.Length) return null;
            string s = Encoding.UTF8.GetString(body, 2, len);
            return s.Length > 0 ? s : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ToHex(byte[] data, int max)
    {
        int n = Math.Min(data.Length, max);
        var sb = new StringBuilder(n * 2);
        for (int i = 0; i < n; i++) sb.Append(data[i].ToString("x2"));
        if (data.Length > max) sb.Append("...");
        return sb.ToString();
    }

    // Khớp với cách server đóng gói phản hồi COMMAND_IMAGE trong GameController.requestImg (GameController.cs:918-923):
    // sbyte gameType, sbyte type, UTF originPath, int bufferLength, rồi đúng bufferLength byte ảnh thô (PNG...).
    private static void HandleCommandImage(byte[] body)
    {
        try
        {
            var r = new BodyReader(body);
            sbyte gameType = r.ReadSByte();
            sbyte type = r.ReadSByte();
            string path = r.ReadUTF();
            int length = r.ReadInt();
            byte[] imgBytes = r.ReadBytes(length);

            Directory.CreateDirectory(ImageOutputDir);
            string safeName = path.Replace('/', '_').Replace('\\', '_');
            if (safeName.Length == 0) safeName = "unnamed.bin";
            string outPath = Path.Combine(ImageOutputDir, safeName);
            File.WriteAllBytes(outPath, imgBytes);

            Console.WriteLine($"[SERVER] COMMAND_IMAGE: path=\"{path}\" gameType={gameType} type={type} size={imgBytes.Length} byte -> da luu {outPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MockClient] Khong parse duoc COMMAND_IMAGE: {ex.Message} (co the server tra ve rong vi khong tim thay file)");
        }
    }
}

internal sealed class BodyReader
{
    private readonly byte[] _data;
    private int _pos;

    public BodyReader(byte[] data) => _data = data;

    public sbyte ReadSByte() => unchecked((sbyte)_data[_pos++]);

    public int ReadInt()
    {
        int v = (_data[_pos] << 24) | (_data[_pos + 1] << 16) | (_data[_pos + 2] << 8) | _data[_pos + 3];
        _pos += 4;
        return v;
    }

    public string ReadUTF()
    {
        int len = (_data[_pos] << 8) | _data[_pos + 1];
        _pos += 2;
        string s = Encoding.UTF8.GetString(_data, _pos, len);
        _pos += len;
        return s;
    }

    public byte[] ReadBytes(int count)
    {
        var buf = new byte[count];
        Array.Copy(_data, _pos, buf, 0, count);
        _pos += count;
        return buf;
    }
}

internal sealed class PacketWriter
{
    private readonly MemoryStream _ms = new();

    public void WriteSByte(sbyte value) => _ms.WriteByte(unchecked((byte)value));

    public void WriteInt(int value)
    {
        _ms.WriteByte((byte)(value >> 24));
        _ms.WriteByte((byte)(value >> 16));
        _ms.WriteByte((byte)(value >> 8));
        _ms.WriteByte((byte)value);
    }

    public void WriteShort(short value)
    {
        _ms.WriteByte((byte)(value >> 8));
        _ms.WriteByte((byte)value);
    }

    // Tương ứng DataOutputStream.writeUTF trong GServer: 2 byte độ dài (BE) + UTF-8 bytes.
    public void WriteUTF(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteShort((short)bytes.Length);
        _ms.Write(bytes, 0, bytes.Length);
    }

    public byte[] ToArray() => _ms.ToArray();
}
