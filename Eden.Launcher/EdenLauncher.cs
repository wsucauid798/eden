using System.Net;
using System.Net.Quic;
using System.Net.Security;
using Eden.Launcher.Quic;
using Eden.Server.Core;
using Eden.Shared.Ids;
using Eden.Shared.Transport;

namespace Eden.Launcher;

/// <summary>
/// Entry points for bringing Eden up in each of its modes. Callers (the
/// Godot viewer, a CLI harness, or a test) hand a mode description to the
/// launcher and get back an <see cref="ITransport"/> they can talk to.
/// The launcher handles the network-transport difference between solo,
/// host, and connect modes.
/// </summary>
public static class EdenLauncher
{
    /// <summary>The ALPN identifier used for Eden's QUIC protocol.</summary>
    internal const string Alpn = "eden/1";

    /// <summary>
    /// Start an Eden server in the current process and return a handle
    /// whose <see cref="SoloHandle.Transport"/> is the viewer-facing end.
    /// No sockets, no serialisation hop — payloads move across two in-memory
    /// channels. Use this for the &quot;Just me&quot; mode of the launcher.
    /// </summary>
    public static SoloHandle StartSolo(CancellationToken ct = default)
    {
        var server = new EdenServer(EdenId<WorldTag>.New());
        var handle = new SoloHandle(server, ct);
        handle.Connect();
        return handle;
    }

    /// <summary>
    /// Start an Eden server that accepts QUIC connections from viewers over
    /// the network. Use this for &quot;Host&quot; mode — opening your world to
    /// friends or to the public internet. Returns when the listener is ready.
    /// </summary>
    public static async Task<HostHandle> StartHostAsync(
        int port,
        CancellationToken ct = default)
    {
        var cert = DevCert.CreateSelfSigned();
        var server = new EdenServer(EdenId<WorldTag>.New());
        var hostCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var listenerOptions = new QuicListenerOptions
        {
            ListenEndPoint        = new IPEndPoint(IPAddress.IPv6Any, port),
            ApplicationProtocols  = [new SslApplicationProtocol(Alpn)],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(
                new QuicServerConnectionOptions
                {
                    DefaultStreamErrorCode = 0,
                    DefaultCloseErrorCode  = 0,
                    ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ApplicationProtocols = [new SslApplicationProtocol(Alpn)],
                        ServerCertificate    = cert,
                    },
                }),
        };

        var listener = await QuicListener.ListenAsync(listenerOptions, ct).ConfigureAwait(false);

        var acceptTask = Task.Run(() => AcceptLoopAsync(listener, server, hostCts.Token), hostCts.Token);

        // Attach an in-process client so the host itself has a seat in the
        // world (no wasted QUIC loopback). Remote clients use QuicTransport;
        // the host uses InMemoryTransport.
        var (hostViewerSide, hostServerSide) = InMemoryTransport.CreatePair();
        var hostLoop = Task.Run(
            () => server.HandleClientAsync(hostServerSide, hostCts.Token),
            hostCts.Token);

        return new HostHandle(
            listener, server, hostCts, acceptTask, cert,
            hostViewerSide, hostServerSide, hostLoop);
    }

    /// <summary>
    /// Connect to a remote Eden server over QUIC and return a transport the
    /// viewer can talk to. For use in &quot;Join&quot; mode.
    /// </summary>
    public static async Task<ITransport> ConnectAsync(
        string host,
        int port,
        CancellationToken ct = default)
    {
        var options = new QuicClientConnectionOptions
        {
            RemoteEndPoint          = new DnsEndPoint(host, port),
            DefaultStreamErrorCode  = 0,
            DefaultCloseErrorCode   = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [new SslApplicationProtocol(Alpn)],
                TargetHost           = host,
                // Dev: trust the server's self-signed cert. Production use
                // will need a proper CA / pinned-cert policy.
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        };

        var connection = await QuicConnection.ConnectAsync(options, ct).ConfigureAwait(false);
        var stream     = await connection
            .OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct)
            .ConfigureAwait(false);

        return new QuicTransport(stream, ownedConnection: connection);
    }

    private static async Task AcceptLoopAsync(
        QuicListener listener,
        EdenServer server,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            QuicConnection connection;
            try
            {
                connection = await listener.AcceptConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (QuicException)               { continue; }

            _ = Task.Run(() => HandleConnectionAsync(connection, server, ct), ct);
        }
    }

    private static async Task HandleConnectionAsync(
        QuicConnection connection,
        EdenServer server,
        CancellationToken ct)
    {
        try
        {
            var stream = await connection.AcceptInboundStreamAsync(ct).ConfigureAwait(false);
            await using var transport = new QuicTransport(stream);
            await server.HandleClientAsync(transport, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (QuicException)               { /* peer dropped */ }
        finally
        {
            try { await connection.CloseAsync(0, ct).ConfigureAwait(false); } catch { }
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Handle to a solo-mode Eden server running in the current process.
/// </summary>
public sealed class SoloHandle : IAsyncDisposable
{
    private readonly EdenServer _server;
    private readonly CancellationTokenSource _cts;
    private readonly List<(ITransport serverSide, Task loop)> _attached = new();

    private ITransport? _primaryViewerSide;

    public ITransport Transport
        => _primaryViewerSide ?? throw new InvalidOperationException(
            "StartSolo must be invoked through EdenLauncher; don't construct SoloHandle directly.");

    public int ClientCount => _attached.Count;

    internal SoloHandle(EdenServer server, CancellationToken ct)
    {
        _server = server;
        _cts    = CancellationTokenSource.CreateLinkedTokenSource(ct);
    }

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
            try { await serverSide.DisposeAsync().ConfigureAwait(false); } catch { }
        if (_primaryViewerSide is not null)
            try { await _primaryViewerSide.DisposeAsync().ConfigureAwait(false); } catch { }
        foreach (var (_, loop) in _attached)
            try { await loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}

/// <summary>
/// Handle to an Eden server listening for network connections. Dispose to
/// stop accepting and tear down all in-flight sessions.
/// </summary>
public sealed class HostHandle : IAsyncDisposable
{
    private readonly QuicListener _listener;
    private readonly CancellationTokenSource _cts;
    private readonly Task _acceptTask;
    private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _cert;

    private readonly ITransport _hostServerSide;
    private readonly Task _hostLoop;

    /// <summary>The server instance backing this host.</summary>
    public EdenServer Server { get; }

    /// <summary>The endpoint the listener is bound to.</summary>
    public IPEndPoint LocalEndPoint => _listener.LocalEndPoint;

    /// <summary>
    /// The host's own viewer-facing transport. Wired in-process to the
    /// server — the host is &quot;a player too,&quot; with no QUIC loopback.
    /// </summary>
    public ITransport Transport { get; }

    internal HostHandle(
        QuicListener listener,
        EdenServer server,
        CancellationTokenSource cts,
        Task acceptTask,
        System.Security.Cryptography.X509Certificates.X509Certificate2 cert,
        ITransport hostViewerSide,
        ITransport hostServerSide,
        Task hostLoop)
    {
        _listener       = listener;
        Server          = server;
        _cts            = cts;
        _acceptTask     = acceptTask;
        _cert           = cert;
        Transport       = hostViewerSide;
        _hostServerSide = hostServerSide;
        _hostLoop       = hostLoop;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _listener.DisposeAsync().ConfigureAwait(false); } catch { }
        try { await Transport.DisposeAsync().ConfigureAwait(false); } catch { }
        try { await _hostServerSide.DisposeAsync().ConfigureAwait(false); } catch { }
        try { await _acceptTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        try { await _hostLoop.ConfigureAwait(false); }    catch (OperationCanceledException) { }
        _cert.Dispose();
        _cts.Dispose();
    }
}
