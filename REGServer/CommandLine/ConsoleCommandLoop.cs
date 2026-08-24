using REGServer.Logging;
using REGServer.Server;

namespace REGServer.CommandLine;

/// <summary>Tương đương CommandLine/*.cs + CommandManager.StartReadingKeys() cũ, thu gọn về 1 file.</summary>
public static class ConsoleCommandLoop
{
    public static void Start(Action shutdown)
    {
        var thread = new Thread(() => Run(shutdown))
        {
            IsBackground = true,
            Name = "CONSOLE COMMAND THREAD",
        };
        thread.Start();
    }

    private static void Run(Action shutdown)
    {
        while (true)
        {
            string? line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            switch (line.Trim().ToLowerInvariant())
            {
                case "help":
                    Log.Info("Lệnh khả dụng: help, online, shutdown");
                    break;
                case "online":
                    Log.Info($"Đang online: {PlayerManager.Instance.OnlineCount}");
                    break;
                case "shutdown":
                    Log.Info("Đang tắt server...");
                    shutdown();
                    return;
                default:
                    Log.Warning($"Lệnh không hợp lệ: {line}");
                    break;
            }
        }
    }
}
