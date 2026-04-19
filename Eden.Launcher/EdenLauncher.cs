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
    /// Start an Eden server in the current process and return a transport
    /// the caller can use to talk to it. No sockets, no serialisation hop —
    /// payloads move across two in-memory channels.
    /// </summary>
    /// <returns>
    /// <c>Transport</c> is the viewer-facing end; await the server's
    /// lifetime via <c>Dispose</c> on the returned <see cref="SoloHandle"/>
    /// to shut it down cleanly.
    /// </returns>
    public static SoloHandle StartSolo(CancellationToken ct = default)
    {
        var (viewerSide, serverSide) = InMemoryTransport.CreatePair();

        var server = new EdenServer(EdenId<WorldTag>.New());
        var cts    = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var serverTask = Task.Run(() => server.RunAsync(serverSide, cts.Token), cts.Token);

        return new SoloHandle(viewerSide, serverSide, serverTask, cts);
    }
}

/// <summary>
/// Handle to a solo-mode Eden server running in the current process. Dispose
/// to stop the server; the <see cref="Transport"/> becomes invalid after that.
/// </summary>
public sealed class SoloHandle : IAsyncDisposable
{
    private readonly ITransport _serverSide;
    private readonly Task _serverTask;
    private readonly CancellationTokenSource _cts;

    public ITransport Transport { get; }

    internal SoloHandle(
        ITransport transport,
        ITransport serverSide,
        Task serverTask,
        CancellationTokenSource cts)
    {
        Transport   = transport;
        _serverSide = serverSide;
        _serverTask = serverTask;
        _cts        = cts;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await Transport.DisposeAsync().ConfigureAwait(false);
        await _serverSide.DisposeAsync().ConfigureAwait(false);
        try { await _serverTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { /* expected */ }
        _cts.Dispose();
    }
}
