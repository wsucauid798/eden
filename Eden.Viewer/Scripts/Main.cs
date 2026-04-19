using System.Collections.Concurrent;
using System.Threading.Tasks;
using Godot;
using Eden.Client;
using Eden.Launcher;
using Eden.Shared.Entities;
using Eden.Shared.Ids;

// `Vector3` in this file means Godot.Vector3. Eden's wire-format math types
// live in Eden.Shared.Math and are fully-qualified at the send/receive boundary.

namespace Eden.Viewer;

/// <summary>
/// Root scene built entirely in code — no .tscn scene content needed. On
/// start, brings up Eden in solo mode, wires the player cube to WASD input,
/// and renders other avatars as orange cubes driven by the server's
/// broadcast stream.
/// </summary>
public partial class Main : Node3D
{
    private const float  MoveSpeed    = 5f;
    private const double SendHz       = 20.0;
    private readonly double _sendInterval = 1.0 / SendHz;

    private SoloHandle?   _solo;
    private ViewerClient? _client;
    private MeshInstance3D? _playerCube;

    private readonly Dictionary<EdenId<UserTag>, MeshInstance3D> _remoteCubes = new();
    private readonly ConcurrentQueue<AvatarState>           _pendingUpdates = new();
    private readonly ConcurrentQueue<EdenId<UserTag>>        _pendingLeaves  = new();

    private double _sendAccumulator;

    public override async void _Ready()
    {
        BuildScene();
        await StartEdenAsync();
    }

    // ---- scene construction ----

    private void BuildScene()
    {
        // Camera pulled back and up, looking at origin.
        var camera = new Camera3D { Position = new Vector3(0f, 4f, 8f) };
        camera.LookAt(Vector3.Zero, Vector3.Up);
        AddChild(camera);

        // Sun-like directional light.
        var light = new DirectionalLight3D
        {
            Rotation = new Vector3(-Mathf.Pi / 4f, -Mathf.Pi / 6f, 0f),
        };
        AddChild(light);

        // A simple floor grid.
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

        // Player cube — blue, sits on the floor at origin.
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

    // ---- Eden wiring ----

    private async Task StartEdenAsync()
    {
        _solo   = EdenLauncher.StartSolo();
        _client = new ViewerClient(_solo.Transport);

        // ViewerClient events fire from a background task; push into queues
        // and apply on the main thread in _Process.
        _client.AvatarUpdated += state  => _pendingUpdates.Enqueue(state);
        _client.AvatarLeft    += userId => _pendingLeaves.Enqueue(userId);

        await _client.ConnectAsync(OS.GetEnvironment("USERNAME") ?? "Player");
        GD.Print($"[Eden] connected. My UserId = {_client.MyUserId}");

        await SendCurrentPoseAsync();
    }

    // ---- per-frame ----

    public override void _Process(double delta)
    {
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
    }
}
