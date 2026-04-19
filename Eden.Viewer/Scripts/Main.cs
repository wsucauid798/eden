using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Eden.Client;
using Eden.Launcher;
using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Transport;

// `Vector3` in this file means Godot.Vector3. Eden's wire-format math types
// live in Eden.Shared.Math and are fully-qualified at the send/receive boundary.

namespace Eden.Viewer;

/// <summary>
/// Root scene built entirely in code — no .tscn scene content needed.
/// <para>
/// On launch a simple mode menu is shown. Press:
/// <list type="bullet">
///   <item><c>1</c> — solo (private, in-process world)</item>
///   <item><c>2</c> — host (opens a QUIC listener on port 5001)</item>
///   <item><c>3</c> — join (connects to localhost:5001)</item>
/// </list>
/// Env vars <c>EDEN_MODE</c>, <c>EDEN_HOST</c>, <c>EDEN_PORT</c> auto-select
/// and skip the menu if present.
/// </para>
/// </summary>
public partial class Main : Node3D
{
    private const float  MoveSpeed    = 5f;
    private const double SendHz       = 20.0;
    private const int    DefaultPort  = 5001;
    private readonly double _sendInterval = 1.0 / SendHz;

    private SoloHandle?   _solo;
    private HostHandle?   _host;
    private ViewerClient? _client;
    private MeshInstance3D? _playerCube;
    private Label? _menuLabel;

    private readonly Dictionary<EdenId<UserTag>, MeshInstance3D> _remoteCubes    = new();
    private readonly ConcurrentQueue<AvatarState>                _pendingUpdates = new();
    private readonly ConcurrentQueue<EdenId<UserTag>>            _pendingLeaves  = new();

    private double _sendAccumulator;
    private bool   _started;

    public override void _Ready()
    {
        // If env vars pre-select a mode, start immediately and skip the menu.
        var envMode = (OS.GetEnvironment("EDEN_MODE") ?? "").ToLowerInvariant();
        if (envMode is "solo" or "host" or "join")
        {
            _ = StartAsync(envMode);
            return;
        }

        ShowMenu();
    }

    public override async void _Input(InputEvent @event)
    {
        if (_started || @event is not InputEventKey { Pressed: true } key) return;

        var mode = key.Keycode switch
        {
            Key.Key1 or Key.Kp1 => "solo",
            Key.Key2 or Key.Kp2 => "host",
            Key.Key3 or Key.Kp3 => "join",
            _ => null,
        };
        if (mode is null) return;

        await StartAsync(mode);
    }

    private void ShowMenu()
    {
        _menuLabel = new Label
        {
            Text = "Pick mode:\n\n  [1] Solo\n  [2] Host\n  [3] Join (localhost:5001)",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        _menuLabel.AddThemeFontSizeOverride("font_size", 32);
        AddChild(_menuLabel);
        // Must call this after AddChild — it's a method, not a settable property.
        _menuLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    private async Task StartAsync(string mode)
    {
        if (_started) return;
        _started = true;

        _menuLabel?.QueueFree();
        _menuLabel = null;

        BuildScene();

        var transport = await ResolveTransportAsync(mode);

        _client = new ViewerClient(transport);
        _client.AvatarUpdated += state  => _pendingUpdates.Enqueue(state);
        _client.AvatarLeft    += userId => _pendingLeaves.Enqueue(userId);

        await _client.ConnectAsync(OS.GetEnvironment("USERNAME") ?? "Player");
        GD.Print($"[Eden] connected. My UserId = {_client.MyUserId}");

        await SendCurrentPoseAsync();
    }

    private async Task<ITransport> ResolveTransportAsync(string mode)
    {
        var port = int.TryParse(OS.GetEnvironment("EDEN_PORT"), out var p) ? p : DefaultPort;
        var host = !string.IsNullOrWhiteSpace(OS.GetEnvironment("EDEN_HOST"))
                    ? OS.GetEnvironment("EDEN_HOST")
                    : "localhost";

        switch (mode)
        {
            case "host":
                _host = await EdenLauncher.StartHostAsync(port);
                GD.Print($"[Eden] hosting on port {_host.LocalEndPoint.Port}");
                return _host.Transport;

            case "join":
                GD.Print($"[Eden] joining {host}:{port}");
                return await EdenLauncher.ConnectAsync(host, port);

            default:
                _solo = EdenLauncher.StartSolo();
                GD.Print("[Eden] solo mode");
                return _solo.Transport;
        }
    }

    // ---- scene construction ----

    private void BuildScene()
    {
        var camera = new Camera3D { Position = new Vector3(0f, 4f, 8f) };
        AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up);

        var light = new DirectionalLight3D
        {
            Rotation = new Vector3(-Mathf.Pi / 4f, -Mathf.Pi / 6f, 0f),
        };
        AddChild(light);

        var floor = new MeshInstance3D
        {
            Mesh     = new PlaneMesh { Size = new Vector2(40f, 40f) },
            Position = Vector3.Zero,
        };
        floor.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.22f, 0.22f, 0.25f),
        });
        AddChild(floor);

        _playerCube = new MeshInstance3D
        {
            Mesh     = new BoxMesh { Size = Vector3.One },
            Position = new Vector3(0f, 0.5f, 0f),
        };
        _playerCube.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.30f, 0.60f, 0.95f),
        });
        AddChild(_playerCube);
    }

    // ---- per-frame ----

    public override void _Process(double delta)
    {
        if (!_started) return;
        HandleMovement(delta);
        ApplyPendingRemoteEvents();
        MaybeSendPose(delta);
    }

    private void HandleMovement(double delta)
    {
        if (_playerCube is null) return;

        var dir = new Vector3(
            x: (Input.IsKeyPressed(Key.D) ? 1f : 0f) - (Input.IsKeyPressed(Key.A) ? 1f : 0f),
            y: 0f,
            z: (Input.IsKeyPressed(Key.S) ? 1f : 0f) - (Input.IsKeyPressed(Key.W) ? 1f : 0f));

        if (dir != Vector3.Zero)
            _playerCube.Position += dir.Normalized() * MoveSpeed * (float)delta;
    }

    private void ApplyPendingRemoteEvents()
    {
        while (_pendingUpdates.TryDequeue(out var state))  ApplyRemoteAvatar(state);
        while (_pendingLeaves.TryDequeue(out var userId))  RemoveRemoteAvatar(userId);
    }

    private void MaybeSendPose(double delta)
    {
        _sendAccumulator += delta;
        if (_sendAccumulator < _sendInterval) return;
        _sendAccumulator = 0;
        _ = SendCurrentPoseAsync();
    }

    private async Task SendCurrentPoseAsync()
    {
        if (_client?.Session is null || _playerCube is null) return;

        var pos = _playerCube.Position;
        var edenPos = new Eden.Shared.Math.Vector3(pos.X, pos.Y, pos.Z);

        await _client.SendAvatarUpdateAsync(new AvatarState(
            UserId:         _client.MyUserId,
            SessionId:      _client.Session.Value.SessionId,
            DisplayName:    "Player",
            Transform:      new Eden.Shared.Math.Transform(edenPos, Eden.Shared.Math.Quaternion.Identity),
            Velocity:       Eden.Shared.Math.Vector3.Zero,
            AppearanceHash: 0));
    }

    // ---- remote avatars ----

    private void ApplyRemoteAvatar(AvatarState state)
    {
        if (!_remoteCubes.TryGetValue(state.UserId, out var cube))
        {
            cube = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One } };
            cube.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
            {
                AlbedoColor = new Color(0.95f, 0.55f, 0.20f),
            });
            AddChild(cube);
            _remoteCubes[state.UserId] = cube;
        }

        var p = state.Transform.Position;
        cube.Position = new Vector3(p.X, p.Y + 0.5f, p.Z);
    }

    private void RemoveRemoteAvatar(EdenId<UserTag> userId)
    {
        if (_remoteCubes.Remove(userId, out var cube))
            cube.QueueFree();
    }

    // ---- teardown ----

    public override async void _ExitTree()
    {
        if (_client is not null) await _client.DisposeAsync();
        if (_solo   is not null) await _solo.DisposeAsync();
        if (_host   is not null) await _host.DisposeAsync();
    }
}
