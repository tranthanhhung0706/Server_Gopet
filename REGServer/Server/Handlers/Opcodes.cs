namespace REGServer.Server;

/// <summary>
/// Bảng opcode dùng chung giữa server &amp; client. Đây chỉ là điểm khởi đầu — khi port thêm
/// tính năng từ GServer (GameController.cs, MenuController.*.cs, Player.cs...), hãy copy nguyên
/// giá trị số opcode tương ứng vào đây để không phá vỡ giao thức với client cũ.
/// </summary>
public static class Opcodes
{
    /// <summary>Server -> Client: xác nhận bắt tay xong, cho phép client gửi lệnh (Session.setClientOK cũ).</summary>
    public const sbyte ClientOk = -36;
}
