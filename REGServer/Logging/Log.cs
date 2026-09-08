namespace REGServer.Logging;

/// <summary>
/// Log console tối giản (thay cho Logging/Monitor.cs cũ). Có thể thay bằng Serilog/NLog sau này
/// mà không phải sửa chỗ gọi, vì toàn bộ codebase chỉ gọi qua class này.
/// </summary>
public static class Log
{
    private static readonly object SyncRoot = new();

    public static void Info(string message) => Write(ConsoleColor.Gray, "INFO", message);

    public static void Warning(string message) => Write(ConsoleColor.Yellow, "WARN", message);

    public static void Error(string message) => Write(ConsoleColor.Red, "ERROR", message);

    public static void Error(string message, Exception ex) => Write(ConsoleColor.Red, "ERROR", $"{message}: {ex}");

    private static void Write(ConsoleColor color, string level, string message)
    {
        lock (SyncRoot)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
            Console.ResetColor();
        }
    }
}
