using REGServer.Api;
using REGServer.CommandLine;
using REGServer.Config;
using REGServer.Database;
using REGServer.Logging;
using REGServer.Server;

Console.Title = "REGServer - Gopet base mới";
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

ServerSettings serverSettings = AppConfig.LoadServerSettings();
DatabaseSettings dbSettings = AppConfig.LoadDatabaseSettings();

var dbManager = new DbManager(dbSettings);
var playerRepository = new PlayerRepository(dbManager);
var accountRepository = new AccountRepository(dbManager);

// TODO: đăng ký handler opcode thật ở đây khi port tính năng từ GServer, ví dụ:
// OpcodeRouter.Register(Opcodes.Login, LoginHandler.HandleAsync);

var gameServer = new GameServer(serverSettings.TcpPort);
var apiServer = new ApiServer(serverSettings.HttpPort, playerRepository, accountRepository, dbManager);

gameServer.Start();
_ = apiServer.StartAsync();

Log.Info($"REGServer đã sẵn sàng. TCP={serverSettings.TcpPort} HTTP={serverSettings.HttpPort}");

ConsoleCommandLoop.Start(shutdown: () =>
{
    gameServer.Stop();
    _ = apiServer.StopAsync();
    Environment.Exit(0);
});

await Task.Delay(Timeout.Infinite);
