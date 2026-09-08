using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using REGServer.Database;
using REGServer.Server;

namespace REGServer.Api;

/// <summary>
/// Tương đương APIs/HttpServer.cs cũ nhưng dùng Minimal API thay vì MVC Controllers +
/// Swashbuckle — gọn hơn cho vài endpoint quản trị/health-check. Muốn thêm endpoint thật (như
/// APIs/DataController.cs, ServerController.cs cũ) thì thêm app.MapGet/MapPost trong Start().
/// </summary>
public sealed class ApiServer
{
    private readonly WebApplication _app;
    private readonly int _port;

    public ApiServer(int port, PlayerRepository playerRepository, AccountRepository accountRepository, DbManager dbManager)
    {
        _port = port;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        _app = builder.Build();

        _app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

        _app.MapGet("/players/online", () => Results.Ok(new { online = PlayerManager.Instance.OnlineCount }));

        _app.MapGet("/players/count", async (CancellationToken ct) =>
        {
            int total = await playerRepository.CountAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { total });
        });

        _app.MapGet("/users/count", async (CancellationToken ct) =>
        {
            int total = await accountRepository.CountAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { total });
        });

        _app.MapGet("/health/db", async (CancellationToken ct) =>
        {
            var (gameOk, gameMsg) = await dbManager.TestConnectionAsync(dbManager.CreateGameConnectionAsync, ct).ConfigureAwait(false);
            var (webOk, webMsg) = await dbManager.TestConnectionAsync(dbManager.CreateWebConnectionAsync, ct).ConfigureAwait(false);

            var result = new
            {
                game = new { ok = gameOk, message = gameMsg },
                web = new { ok = webOk, message = webMsg },
            };
            return (gameOk && webOk) ? Results.Ok(result) : Results.Json(result, statusCode: 503);
        });
    }

    public Task StartAsync() => _app.RunAsync($"http://0.0.0.0:{_port}");

    public Task StopAsync() => _app.StopAsync();
}
