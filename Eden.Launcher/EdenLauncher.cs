using Eden.Server.Core;
using Eden.Shared.Ids;
using Eden.Shared.Transport;

namespace Eden.Launcher;

/// <summary>
/// Entry points for bringing Eden up in each of its modes. Callers (the Godot
/// viewer, a CLI harness, or a test) hand a mode description to the launcher
/// and get back an <see cref="ITransport"/> they can talk to — it doesn't
/// matter to them whether the server is in-process, on localhost QUIC, or
/// across the internet.
/// </summary>
public static class EdenLauncher
{
    /// <summary>
    /// Start an Eden server in the current process and return a transport the
    /// caller can use to talk to it. No sockets, no serialisation hop —
    /// payloads move across two in-memory channels.
    /// </summary>
    public static SoloHandle StartSolo(CancellationToken ct = default)
    {
        var server = new EdenServer(EdenId<WorldTag>.New());
        var handle = new SoloHandle(server, ct);
        handle.Connect(); // first client — the caller-facing transport
        return handle;
    }
}

/// <summary>
/// Handle to a solo-mode Eden server running in the current process. Exposes
/// the initial viewer-facing transport. Additional clients (useful for tests)
/// can be attached via <see cref="Connect"/>. Dispose to stop the server.
/// </summary>
public sealed class SoloHandle : IAsyncDisposable
{
    private readonly EdenServer _server;
    private readonly CancellationTokenSource _cts;
    private readonly List<(ITransport serverSide, Task loop)> _attached = new();

    private ITransport? _primaryViewerSide;

    /// <summary>
    /// The first viewer-side transport, handed back by
    /// <see cref="EdenLauncher.StartSolo"/>.
    /// </summary>
    public ITransport Transport
        => _primaryViewerSide ?? throw new InvalidOperationException(
            "StartSolo must be invoked through EdenLauncher; don't construct SoloHandle directly.");

    /// <summary>Number of currently-attached client transports.</summary>
    public int ClientCount => _attached.Count;

    internal SoloHandle(EdenServer server, CancellationToken ct)
    {
        _server = server;
        _cts    = CancellationTokenSource.CreateLinkedTokenSource(ct);
    }

    /// <summary>
    /// Create and attach a new in-memory client transport pair to the running
    /// solo server. Returns the viewer-side transport.
    /// </summary>
    public ITransport Connect()
    {
        var (viewerSide, serverSide) = InMemoryTransport.CreatePair();
        var loop = Task.Run(() => _server.HandleClientAsync(serverSide, _cts.Token), _cts.Token);
        _attached.Add((serverSide, loop));
        _primaryViewerSide ??= viewerSide;
        return viewerSide;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        foreach (var (serverSide, _) in _attached)
        {
            try { await serverSide.DisposeAsync().ConfigureAwait(false); } catch { }
        }
        if (_primaryViewerSide is not null)
            try { await _primaryViewerSide.DisposeAsync().ConfigureAwait(false); } catch { }

        foreach (var (_, loop) in _attached)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }

        _cts.Dispose();
    }
}
