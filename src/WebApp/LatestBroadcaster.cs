using System.Text.Json;
using System.Threading.Channels;

namespace WebApp;

/// Consulta la base una vez por segundo y reparte el mismo snapshot
/// a todos los clientes SSE conectados.
public sealed class LatestBroadcaster(ReadingRepository repo, ILogger<LatestBroadcaster> log)
    : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly List<Channel<string>> subscribers = [];

    /// Ultimo snapshot, para que un cliente que recien llega no espere un segundo.
    public string? Last { get; private set; }

    /// Buzon de capacidad 1: si un cliente se atrasa, se le pisa el valor viejo
    /// con el nuevo en vez de frenar al publicador.
    public Channel<string> Subscribe()
    {
        var channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (subscribers) subscribers.Add(channel);
        return channel;
    }

    public void Unsubscribe(Channel<string> channel)
    {
        lock (subscribers) subscribers.Remove(channel);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                try
                {
                    var readings = await repo.GetLatestAsync(ct);
                    var json = JsonSerializer.Serialize(readings, JsonOptions);
                    Last = json;

                    Channel<string>[] current;
                    lock (subscribers) current = [.. subscribers];
                    foreach (var c in current) c.Writer.TryWrite(json);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Si la base se cae un rato, el stream no se muere: reintenta al siguiente tick.
                    log.LogWarning(ex, "Fallo la consulta de ultimos valores");
                }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { /* cierre ordenado de la app */ }
    }
}