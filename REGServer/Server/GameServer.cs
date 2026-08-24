using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using REGServer.Logging;

namespace REGServer.Server;

/// <summary>
/// Tương đương Server/Server.cs (Gopet.MServer.Server) cũ: TcpListener accept loop + chống spam
/// kết nối theo IP. Khác biệt: dùng AcceptTcpClientAsync + Task.Run thay vì thread chặn + ThreadPool.
/// </summary>
public sealed class GameServer
{
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<string, DateTime> _connectionWait = new();
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }

    public GameServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
        Log.Info($"GameServer TCP đang lắng nghe port {((IPEndPoint)_listener.LocalEndpoint).Port}");
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }
        IsRunning = false;
        _cts?.Cancel();
        _listener.Stop();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error("Lỗi accept kết nối", ex);
                continue;
            }

            string ip = client.Client.RemoteEndPoint is IPEndPoint ep ? ep.Address.ToString() : "unknown";
            if (_connectionWait.TryGetValue(ip, out DateTime until) && until > DateTime.Now)
            {
                client.Close();
                continue;
            }
            _connectionWait[ip] = DateTime.Now.AddSeconds(2);
            CleanupConnectionWait();

            _ = HandleClientAsync(client, ct);
        }
    }

    private static async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var session = new Session(client);
        bool handshakeOk = await session.HandshakeAsync(ct).ConfigureAwait(false);
        if (!handshakeOk)
        {
            client.Close();
            return;
        }

        var player = new Player(session);
        PlayerManager.Instance.Add(player);
        session.SendClientOk(true);

        await session.RunAsync().ConfigureAwait(false);
    }

    private void CleanupConnectionWait()
    {
        DateTime now = DateTime.Now;
        foreach (string key in _connectionWait.Keys)
        {
            if (_connectionWait.TryGetValue(key, out DateTime until) && until <= now)
            {
                _connectionWait.TryRemove(key, out _);
            }
        }
    }
}
