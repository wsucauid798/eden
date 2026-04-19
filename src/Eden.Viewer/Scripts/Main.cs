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
/// Root scene built entirely in code.
/// <para>
/// Modes (picked at startup via keys <c>1</c>/<c>2</c>/<c>3</c>, or the
/// <c>EDEN_MODE</c> env var): solo / host / join.
/// </para>
/// Controls once in the world:
/// <list type="bullet">
///   <item>WASD — move relative to camera yaw</item>
///   <item>Mouse — orbit camera around the avatar</item>
///   <item>Esc  — release / recapture mouse</item>
/// </list>
/// </summary>
public partial class Main : Node3D
{
    private const float  MoveSpeed        = 5f;
    private const float  MouseSensitivity = 0.003f;
    private const float  CameraDistance   = 6f;
    private const float  CameraHeight     = 2.2f;
    private const double SendHz           = 20.0;
    private const int    DefaultPort      = 5001;
    private readonly double _sendInterval = 1.0 / SendHz;

    private SoloHandle?   _solo;
    private HostHandle?   _host;
    private ViewerClient? _client;

    private MeshInstance3D? _playerCube;
    private Label3D?        _playerLabel;
    private Node3D?         _cameraRig;
    private Node3D?         _yawPivot;
    private Node3D?         _pitchPivot;
    private Label?          _menuLabel;
    private Label?          _hudLabel;
    private WorldRenderer?  _world;

    private readonly Dictionary<EdenId<UserTag>, RemoteAvatar> _remote          = new();
    private readonly ConcurrentQueue<AvatarState>              _pendingUpdates  = new();
    private readonly ConcurrentQueue<EdenId<UserTag>>          _pendingLeaves   = new();

    private double _sendAccumulator;
    private float  _yaw;
    private float  _pitch = -0.25f;
    private bool   _started;
    private string _displayName = "Player";

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    public override void _Ready()
    {
        _displayName = OS.GetEnvironment("USERNAME") is { Length: > 0 } u ? u : "Player";

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
        if (!_started)
        {
            if (@event is InputEventKey { Pressed: true } menuKey)
            {
                var mode = menuKey.Keycode switch
                {
                    Key.Key1 or Key.Kp1 => "solo",
                    Key.Key2 or Key.Kp2 => "host",
                    Key.Key3 or Key.Kp3 => "join",
                    _ => null,
                };
                if (mode is not null) await StartAsync(mode);
            }
            return;
        }

        switch (@event)
        {
            case InputEventMouseMotion mm when Input.MouseMode == Input.MouseModeEnum.Captured:
                _yaw   -= mm.Relative.X * MouseSensitivity;
                _pitch  = Mathf.Clamp(_pitch - mm.Relative.Y * MouseSensitivity,
                                      -Mathf.Pi / 2 + 0.1f, Mathf.Pi / 4);
                if (_yawPivot   is not null) _yawPivot.Rotation   = new Vector3(0, _yaw, 0);
                if (_pitchPivot is not null) _pitchPivot.Rotation = new Vector3(_pitch, 0, 0);
                break;

            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                    ? Input.MouseModeEnum.Visible
                    : Input.MouseModeEnum.Captured;
                break;

            case InputEventMouseButton { Pressed: true } when Input.MouseMode == Input.MouseModeEnum.Visible:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!_started) return;
        HandleMovement(delta);
        UpdateCameraRig();
        ApplyPendingRemoteEvents();
        UpdateHud();
        MaybeSendPose(delta);
    }

    public override async void _ExitTree()
    {
        if (_client is not null) await _client.DisposeAsync();
        if (_solo   is not null) await _solo.DisposeAsync();
        if (_host   is not null) await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------
    // Mode menu
    // ------------------------------------------------------------------

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
        _menuLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    private async Task StartAsync(string mode)
    {
        if (_started) return;
        _started = true;

        _menuLabel?.QueueFree();
        _menuLabel = null;

        BuildScene();
        BuildHud();

        var transport = await ResolveTransportAsync(mode);

        _client = new ViewerClient(transport);
        _client.AvatarUpdated += state  => _pendingUpdates.Enqueue(state);
        _client.AvatarLeft    += userId => _pendingLeaves.Enqueue(userId);
        if (_world is not null) _world.Bind(_client);

        await _client.ConnectAsync(_displayName);
        GD.Print($"[Eden] connected. My UserId = {_client.MyUserId}");

        // Own label displays the name immediately; movement fires real updates.
        if (_playerLabel is not null) _playerLabel.Text = _displayName;

        await SendCurrentPoseAsync();
        Input.MouseMode = Input.MouseModeEnum.Captured;
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

    // ------------------------------------------------------------------
    // World construction
    // ------------------------------------------------------------------

    private void BuildScene()
    {
        // WorldRenderer owns the sun + environment (sky, fog, ambient).
        // Its state is driven by the server's world clock once we bind it
        // to the ViewerClient in StartAsync.
        _world = new WorldRenderer();
        AddChild(_world);

        var floor = new MeshInstance3D
        {
            Mesh     = new PlaneMesh { Size = new Vector2(80f, 80f) },
            Position = Vector3.Zero,
        };
        floor.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.22f, 0.22f, 0.25f),
        });
        AddChild(floor);

        // Player — blue cube + billboarded name label.
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

        _playerLabel = BuildNameLabel(_displayName);
        _playerCube.AddChild(_playerLabel);

        // Camera rig — separate node that tracks player's position (not
        // rotation). Yaw and pitch come from the mouse.
        _cameraRig  = new Node3D();
        _yawPivot   = new Node3D();
        _pitchPivot = new Node3D { Rotation = new Vector3(_pitch, 0, 0) };
        var camera  = new Camera3D { Position = new Vector3(0f, CameraHeight, CameraDistance) };

        AddChild(_cameraRig);
        _cameraRig.AddChild(_yawPivot);
        _yawPivot.AddChild(_pitchPivot);
        _pitchPivot.AddChild(camera);
        camera.LookAt(_cameraRig.GlobalPosition + Vector3.Up * 0.8f, Vector3.Up);
    }

    private static Label3D BuildNameLabel(string text) => new Label3D
    {
        Text       = text,
        Position   = new Vector3(0f, 1.2f, 0f),
        Billboard  = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        PixelSize  = 0.004f,
        Modulate   = Colors.White,
        OutlineSize = 6,
    };

    private void BuildHud()
    {
        var hud = new CanvasLayer();
        AddChild(hud);

        _hudLabel = new Label
        {
            Text     = "",
            Position = new Vector2(16f, 14f),
        };
        _hudLabel.AddThemeFontSizeOverride("font_size", 16);
        _hudLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
        hud.AddChild(_hudLabel);
    }

    // ------------------------------------------------------------------
    // Per-frame
    // ------------------------------------------------------------------

    private void HandleMovement(double delta)
    {
        if (_playerCube is null) return;

        var input = new Vector3(
            x: (Input.IsKeyPressed(Key.D) ? 1f : 0f) - (Input.IsKeyPressed(Key.A) ? 1f : 0f),
            y: 0f,
            z: (Input.IsKeyPressed(Key.S) ? 1f : 0f) - (Input.IsKeyPressed(Key.W) ? 1f : 0f));

        if (input == Vector3.Zero) return;

        // Move in the plane, relative to camera yaw.
        var yawBasis = Basis.FromEuler(new Vector3(0f, _yaw, 0f));
        var worldDir = (yawBasis * input.Normalized()).Normalized();
        _playerCube.Position += worldDir * MoveSpeed * (float)delta;

        // Face movement direction.
        _playerCube.Rotation = new Vector3(
            0f, Mathf.Atan2(-worldDir.X, -worldDir.Z), 0f);
    }

    private void UpdateCameraRig()
    {
        if (_cameraRig is null || _playerCube is null) return;
        _cameraRig.Position = _playerCube.Position;
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
            DisplayName:    _displayName,
            Transform:      new Eden.Shared.Math.Transform(edenPos, Eden.Shared.Math.Quaternion.Identity),
            Velocity:       Eden.Shared.Math.Vector3.Zero,
            AppearanceHash: 0));
    }

    private void UpdateHud()
    {
        if (_hudLabel is null || _playerCube is null) return;
        var pos = _playerCube.Position;
        _hudLabel.Text = $"Eden · {ModeLabel()} · {_displayName} · " +
                         $"({pos.X:F1}, {pos.Z:F1}) · {_remote.Count} other(s)";
    }

    private string ModeLabel() =>
        _host is not null ? "hosting" :
        _solo is not null ? "solo"    :
                            "joined";

    // ------------------------------------------------------------------
    // Remote avatars
    // ------------------------------------------------------------------

    private void ApplyRemoteAvatar(AvatarState state)
    {
        if (!_remote.TryGetValue(state.UserId, out var avatar))
        {
            var cube = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One } };
            cube.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
            {
                AlbedoColor = new Color(0.95f, 0.55f, 0.20f),
            });
            AddChild(cube);

            var label = BuildNameLabel(state.DisplayName);
            cube.AddChild(label);

            avatar = new RemoteAvatar(cube, label);
            _remote[state.UserId] = avatar;
        }

        var p = state.Transform.Position;
        avatar.Cube.Position = new Vector3(p.X, p.Y + 0.5f, p.Z);
        if (!string.IsNullOrEmpty(state.DisplayName))
            avatar.Label.Text = state.DisplayName;
    }

    private void RemoveRemoteAvatar(EdenId<UserTag> userId)
    {
        if (_remote.Remove(userId, out var avatar))
            avatar.Cube.QueueFree();
    }

    private readonly record struct RemoteAvatar(MeshInstance3D Cube, Label3D Label);
}
