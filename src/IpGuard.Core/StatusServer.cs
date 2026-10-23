using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IpGuard;

public sealed class StatusServer : IDisposable
{
    private const int MaxRequestBytes = 8192;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);

    private readonly Func<DateTime> now;
    private readonly object sync = new();
    private TcpListener? listener;
    private CancellationTokenSource? cancel;
    private StatusSnapshot? snapshot;

    public StatusServer(Func<DateTime>? now = null)
    {
        this.now = now ?? (() => DateTime.UtcNow);
    }

    public int Port { get; private set; }

    public bool Running => listener is not null;

    public bool Start(int port, out string? error)
    {
        Stop();
        try
        {
            var created = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
            created.Start();
            listener = created;
            Port = ((IPEndPoint)created.LocalEndpoint).Port;
            cancel = new CancellationTokenSource();
            _ = Task.Run(() => LoopAsync(created, cancel.Token));
            error = null;
            return true;
        }
        catch (SocketException e)
        {
            listener = null;
            Port = 0;
            error = e.Message;
            return false;
        }
    }

    public void Stop()
    {
        cancel?.Cancel();
        cancel?.Dispose();
        cancel = null;
        try
        {
            listener?.Stop();
        }
        catch (SocketException)
        {
        }

        listener = null;
        Port = 0;
    }

    public void Update(StatusSnapshot value)
    {
        lock (sync)
        {
            snapshot = value;
        }
    }

    public void Dispose() => Stop();

    public (int Code, string Type, string Body) Handle(string method, string path, string? host)
    {
        if (host is null || (host != $"127.0.0.1:{Port}" && host != $"localhost:{Port}"))
        {
            return (403, "text/plain", "forbidden");
        }

        if (method is not ("GET" or "HEAD"))
        {
            return (405, "text/plain", "method not allowed");
        }

        StatusSnapshot? current;
        lock (sync)
        {
            current = snapshot;
        }

        var route = path.Split('?')[0];
        switch (route)
        {
            case "/status":
                return current is null
                    ? (503, "application/json; charset=utf-8", "{\"ok\":false,\"state\":\"starting\"}")
                    : (200, "application/json; charset=utf-8", StatusMapper.ToJson(current, now()));
            case "/ok":
                return current is { Ok: true }
                    ? (200, "text/plain; charset=utf-8", "ok")
                    : (503, "text/plain; charset=utf-8", current is null ? "starting" : current.State);
            case "/":
                return (200, "text/plain; charset=utf-8", "IpGuard status: /status (JSON), /ok (200 when the IP matches a reference, otherwise 503)");
            default:
                return (404, "text/plain", "not found");
        }
    }

    private async Task LoopAsync(TcpListener active, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await active.AcceptTcpClientAsync(token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client, token), token);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(ReadTimeout);
                var stream = client.GetStream();
                var buffer = new byte[MaxRequestBytes];
                var length = 0;
                string text;
                while (true)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token);
                    if (read == 0)
                    {
                        return;
                    }

                    length += read;
                    text = Encoding.ASCII.GetString(buffer, 0, length);
                    if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        break;
                    }

                    if (length >= MaxRequestBytes)
                    {
                        await WriteAsync(stream, (431, "text/plain", "headers too large"), true, token);
                        await DrainAsync(client, stream, token);
                        return;
                    }
                }

                var lines = text.Split("\r\n");
                var request = lines[0].Split(' ');
                var host = lines.Skip(1)
                    .Select(line => line.Split(':', 2))
                    .Where(parts => parts.Length == 2 && parts[0].Trim().Equals("Host", StringComparison.OrdinalIgnoreCase))
                    .Select(parts => parts[1].Trim())
                    .FirstOrDefault();

                var response = request.Length >= 2 ? Handle(request[0], request[1], host) : (400, "text/plain", "bad request");
                await WriteAsync(stream, response, request.Length >= 1 && request[0] == "HEAD", token);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
            }
        }
    }

    private static async Task DrainAsync(TcpClient client, NetworkStream stream, CancellationToken token)
    {
        try
        {
            client.Client.Shutdown(SocketShutdown.Send);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(TimeSpan.FromMilliseconds(500));
            var sink = new byte[4096];
            while (await stream.ReadAsync(sink, limit.Token) > 0)
            {
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }
    }

    private static async Task WriteAsync(NetworkStream stream, (int Code, string Type, string Body) response, bool headOnly, CancellationToken token)
    {
        var body = Encoding.UTF8.GetBytes(response.Body);
        var reason = response.Code switch
        {
            200 => "OK",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            431 => "Request Header Fields Too Large",
            _ => "Service Unavailable"
        };
        var head = $"HTTP/1.1 {response.Code} {reason}\r\nContent-Type: {response.Type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), token);
        if (!headOnly)
        {
            await stream.WriteAsync(body, token);
        }

        await stream.FlushAsync(token);
    }
}
