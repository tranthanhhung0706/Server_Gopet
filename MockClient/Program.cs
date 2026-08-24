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

    private static NetworkStream? _stream;

    private static async Task<int> Main(string[] args)
    {
        string host = args.Length > 0 ? args[0] : "127.0.0.1";
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
        SendClientInfo(_stream, languageCode: "vi", appVersion: "1.4.2");
        Console.WriteLine("[MockClient] Da gui CLIENT_INFO, cho server phan hoi...");

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
                        SendClientInfo(_stream, "vi", "1.4.2");
                        break;
                    case "login" when parts.Length >= 3:
                        {
                            var up = parts[2].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                            SendLogin(_stream, parts[1], up.Length > 0 ? up[0] : "", "1.4.2");
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
                    case "raw" when parts.Length >= 2:
                        {
                            var rawParts = parts[1].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                            sbyte opcode = (sbyte)sbyte.Parse(rawParts[0]);
                            byte[] body = rawParts.Length > 1 ? HexToBytes(rawParts[1]) : Array.Empty<byte>();
                            SendPacket(_stream, opcode, body);
                            Console.WriteLine($"[MockClient] Da gui raw opcode={opcode} body={body.Length} byte");
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
              raw <opcode> [hexBytes]  - gui goi tuy y, vi du: raw 1 00047573657200047061737300053"
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
        w.WriteUTF(appVersion);          // ApplicationVersion (phải >= 1.4.2, xem GopetManager.VERSION_142)
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

    private static void SendRegister(NetworkStream stream, string username, string password)
    {
        var w = new PacketWriter();
        w.WriteUTF(username);
        w.WriteUTF(password);
        SendPacket(stream, REGISTER, w.ToArray());
        Console.WriteLine($"[MockClient] Da gui REGISTER user={username}");
    }

    private static void SendPacket(NetworkStream stream, sbyte opcode, byte[] body)
    {
        var payload = new byte[1 + body.Length];
        payload[0] = unchecked((byte)opcode);
        Array.Copy(body, 0, payload, 1, body.Length);

        WriteInt32BE(stream, payload.Length + 1);
        stream.WriteByte(0); // cờ mã hoá = 0 (server hiện không gửi/nhận gói mã hoá TEA)
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
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
