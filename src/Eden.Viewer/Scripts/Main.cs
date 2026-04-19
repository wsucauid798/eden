using System;
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
    private const float  SprintMultiplier = 1.8f;
    private const float  JumpVelocity     = 6f;
    private const float  Gravity          = 18f;
    private const float  FlySpeed         = 6f;
    private const float  GroundY          = 0.5f;
    private const float  MouseSensitivity = 0.003f;
    private const float  CameraDistance   = 6f;
    private const float  CameraHeight     = 2.2f;
    private const float  MinCameraDistance = 1.5f;
    private const float  MaxCameraDistance = 20f;
    private const float  ZoomStep         = 0.6f;
    private const double SendHz           = 20.0;
    private const int    DefaultPort      = 5001;
    private readonly double _sendInterval = 1.0 / SendHz;

    private SoloHandle?   _solo;
    private HostHandle?   _host;
    private ViewerClient? _client;

    private CharacterBody3D? _playerCube;
    private MeshInstance3D?  _playerMesh;
    private Label3D?         _playerLabel;
    private Node3D?         _cameraRig;
    private Node3D?         _yawPivot;
    private Node3D?         _pitchPivot;
    private Camera3D?       _camera;
    private Label?          _menuLabel;
    private Label?          _hudLabel;
    private WorldRenderer?  _world;
    private PrimRenderer?   _prims;

    private float  _cameraDistance = CameraDistance;
    private bool   _isFlying;
    private bool   _isSprinting;
    private double _lastForwardReleaseTime = -1;
    private const float DoubleTapWindowSeconds = 0.3f;

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
            // Double-tap forward (W or Up) to start sprinting. Releasing the
            // key ends the sprint.
            case InputEventKey ek when ek.Keycode == Key.W || ek.Keycode == Key.Up:
                var now = Time.GetTicksMsec() / 1000.0;
                if (ek is { Pressed: true, Echo: false })
                {
                    if (now - _lastForwardReleaseTime <= DoubleTapWindowSeconds)
                        _isSprinting = true;
                }
                else if (!ek.Pressed)
                {
                    _lastForwardReleaseTime = now;
                    _isSprinting = false;
                }
                break;

            // Hold right mouse button to rotate the camera. Mouse is never
            // captured — the cursor stays free for clicking and for the OS.
            case InputEventMouseMotion mm when Input.IsMouseButtonPressed(MouseButton.Right):
                _yaw   -= mm.Relative.X * MouseSensitivity;
                _pitch  = Mathf.Clamp(_pitch - mm.Relative.Y * MouseSensitivity,
                                      -Mathf.Pi / 2 + 0.1f, Mathf.Pi / 4);
                if (_yawPivot   is not null) _yawPivot.Rotation   = new Vector3(0, _yaw, 0);
                if (_pitchPivot is not null) _pitchPivot.Rotation = new Vector3(_pitch, 0, 0);
                break;

            case InputEventMouseButton { Pressed: true } mb:
                switch (mb.ButtonIndex)
                {
                    case MouseButton.WheelUp:
                        _cameraDistance = Mathf.Max(MinCameraDistance, _cameraDistance - ZoomStep);
                        UpdateCameraOffset();
                        break;
                    case MouseButton.WheelDown:
                        _cameraDistance = Mathf.Min(MaxCameraDistance, _cameraDistance + ZoomStep);
                        UpdateCameraOffset();
                        break;
                    case MouseButton.Left:
                        await TryClickTouchAsync();
                        break;
                }
                break;
        }
    }

    private void UpdateCameraOffset()
    {
        if (_camera is not null)
            _camera.Position = new Vector3(0f, CameraHeight, _cameraDistance);
    }

    private async Task TryClickTouchAsync()
    {
        if (_client is null || _camera is null) return;

        var vp = GetViewport();
        var center = vp.GetVisibleRect().Size / 2f;
        var rayOrigin = _camera.ProjectRayOrigin(center);
        var rayEnd    = rayOrigin + _camera.ProjectRayNormal(center) * 200f;

        var space = _camera.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
        var hit = space.IntersectRay(query);
        if (hit.Count == 0) return;

        if (hit["collider"].AsGodotObject() is not Node body) return;
        var primIdStr = body.GetMeta("prim_id", "").AsString();
        if (string.IsNullOrEmpty(primIdStr) || !Guid.TryParse(primIdStr, out var g)) return;

        await _client.TouchPrimAsync(new EdenId<PrimTag>(g));
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_started) return;
        HandleMovement(delta);
    }

    public override void _Process(double delta)
    {
        if (!_started) return;
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
        if (_prims is not null) _prims.Bind(_client);

        await _client.ConnectAsync(_displayName);
        GD.Print($"[Eden] connected. My UserId = {_client.MyUserId}");

        // Own label displays the name immediately; movement fires real updates.
        if (_playerLabel is not null) _playerLabel.Text = _displayName;

        // Tint the local cube with the colour derived from our UserId so it
        // matches what every other viewer will see us as.
        if (_playerMesh is not null)
        {
            _playerMesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
            {
                AlbedoColor = ColorForUser(_client.MyUserId),
            });
        }

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

        // PrimRenderer mirrors RemotePrims into the scene as MeshInstance3Ds.
        // Wired to the client after ConnectAsync.
        _prims = new PrimRenderer();
        AddChild(_prims);

        // Checkered floor — 200 m, bakes the check pattern into a
        // 128x128 image at 4 tiles across, then repeats that 25x across
        // the floor (~5 m per check).
        var floor = new MeshInstance3D
        {
            Mesh     = new PlaneMesh { Size = new Vector2(200f, 200f) },
            Position = Vector3.Zero,
        };
        floor.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoTexture = BuildCheckerTexture(128, tilesPerSide: 4,
                light: new Color(0.70f, 0.70f, 0.73f),
                dark:  new Color(0.52f, 0.52f, 0.55f)),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Scale      = new Vector3(25f, 25f, 1f),
            Roughness     = 0.85f,
            Metallic      = 0.0f,
        });
        AddChild(floor);

        // Physics collider for the floor — an infinite Y=0 plane so the
        // player's CharacterBody3D has something to stand and land on.
        var floorBody = new StaticBody3D();
        floorBody.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        AddChild(floorBody);

        // A few reference props so the world isn't two cubes in a void.
        BuildReferenceProps();

        // Player body — CharacterBody3D so movement, jumping, gravity, and
        // collisions all go through Godot physics instead of hand-rolled
        // position math. Visible cube and box collider are both children;
        // MoveAndSlide is driven from _PhysicsProcess.
        _playerCube = new CharacterBody3D { Position = new Vector3(0f, GroundY, 0f) };
        _playerMesh = new MeshInstance3D  { Mesh = new BoxMesh { Size = Vector3.One } };
        _playerMesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.55f, 0.60f),
        });
        _playerCube.AddChild(_playerMesh);
        _playerCube.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One } });
        AddChild(_playerCube);

        _playerLabel = BuildNameLabel(_displayName);
        _playerCube.AddChild(_playerLabel);

        // Camera rig — separate node that tracks player's position (not
        // rotation). Yaw and pitch come from the mouse.
        _cameraRig  = new Node3D();
        _yawPivot   = new Node3D();
        _pitchPivot = new Node3D { Rotation = new Vector3(_pitch, 0, 0) };
        _camera     = new Camera3D { Position = new Vector3(0f, CameraHeight, _cameraDistance) };

        AddChild(_cameraRig);
        _cameraRig.AddChild(_yawPivot);
        _yawPivot.AddChild(_pitchPivot);
        _pitchPivot.AddChild(_camera);
        _camera.LookAt(_cameraRig.GlobalPosition + Vector3.Up * 0.8f, Vector3.Up);
    }

    /// <summary>Bake a checker-pattern texture at runtime. `size` is pixels
    /// per side; `tilesPerSide` is how many checks fit across the image.</summary>
    private static ImageTexture BuildCheckerTexture(int size, int tilesPerSide, Color light, Color dark)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        var tilePx = size / tilesPerSide;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var isDark = ((x / tilePx) + (y / tilePx)) % 2 == 0;
            img.SetPixel(x, y, isDark ? dark : light);
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Quick decor: a ring of pillars + a central monolith so the
    /// world has scale cues and shadow casters. Pure presentation; these
    /// live only in the viewer, not on the server.</summary>
    private void BuildReferenceProps()
    {
        // Central monolith.
        var mono = new MeshInstance3D
        {
            Mesh     = new BoxMesh { Size = new Vector3(2f, 8f, 2f) },
            Position = new Vector3(15f, 4f, -18f),
        };
        mono.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.20f, 0.22f, 0.28f),
            Roughness   = 0.6f,
            Metallic    = 0.1f,
        });
        AttachStaticBody(mono, new BoxShape3D { Size = new Vector3(2f, 8f, 2f) });
        AddChild(mono);

        // Ring of 8 pillars at radius 25.
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Mathf.Tau / 8f;
            var pillar = new MeshInstance3D
            {
                Mesh = new CylinderMesh
                {
                    TopRadius    = 0.8f,
                    BottomRadius = 1.0f,
                    Height       = 5f,
                },
                Position = new Vector3(
                    Mathf.Cos(angle) * 25f,
                    2.5f,
                    Mathf.Sin(angle) * 25f),
            };
            pillar.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
            {
                AlbedoColor = new Color(0.80f, 0.78f, 0.72f),
                Roughness   = 0.7f,
            });
            AttachStaticBody(pillar, new CylinderShape3D { Height = 5f, Radius = 1.0f });
            AddChild(pillar);
        }

        // A couple of coloured spheres near the spawn for near-field interest.
        var sphereA = new MeshInstance3D
        {
            Mesh     = new SphereMesh { Radius = 1.2f, Height = 2.4f },
            Position = new Vector3(-4f, 1.2f, -6f),
        };
        sphereA.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.90f, 0.40f, 0.35f),
            Roughness   = 0.4f,
        });
        AttachStaticBody(sphereA, new SphereShape3D { Radius = 1.2f });
        AddChild(sphereA);

        var sphereB = new MeshInstance3D
        {
            Mesh     = new SphereMesh { Radius = 0.9f, Height = 1.8f },
            Position = new Vector3(5f, 0.9f, -7f),
        };
        sphereB.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.35f, 0.75f, 0.50f),
            Roughness   = 0.4f,
        });
        AttachStaticBody(sphereB, new SphereShape3D { Radius = 0.9f });
        AddChild(sphereB);
    }

    /// <summary>Attach a <see cref="StaticBody3D"/> child with the given
    /// shape so the player's CharacterBody3D can collide with this prop.</summary>
    private static void AttachStaticBody(Node3D parent, Shape3D shape)
    {
        var body = new StaticBody3D();
        body.AddChild(new CollisionShape3D { Shape = shape });
        parent.AddChild(body);
    }

    /// <summary>Deterministic colour from a <see cref="EdenId{UserTag}"/>.
    /// Same user id → same colour on every viewer, so an avatar's
    /// appearance is a property of who you are, not of whose screen
    /// you're rendered on.</summary>
    private static Color ColorForUser(EdenId<UserTag> userId)
    {
        // 16 hash bytes of the Guid → stable hue; fixed S/V for readability.
        Span<byte> bytes = stackalloc byte[16];
        userId.Value.TryWriteBytes(bytes);
        var h = 0u;
        foreach (var b in bytes) h = h * 31u + b;
        var hue = (h & 0xFFFFu) / (float)0xFFFFu;
        return Color.FromHsv(hue, 0.65f, 0.88f);
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
        var dt = (float)delta;

        // Horizontal — WASD and arrow keys both move, yaw-relative.
        var fwd   = (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))    ? 1f : 0f;
        var back  = (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))  ? 1f : 0f;
        var right = (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) ? 1f : 0f;
        var left  = (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))  ? 1f : 0f;
        var input = new Vector3(right - left, 0f, back - fwd);

        var speed = MoveSpeed;
        if (_isSprinting) speed *= SprintMultiplier;

        var worldDir = Vector3.Zero;
        if (input != Vector3.Zero)
        {
            var yawBasis = Basis.FromEuler(new Vector3(0f, _yaw, 0f));
            worldDir = (yawBasis * input.Normalized()).Normalized();
            _playerCube.Rotation = new Vector3(0f, Mathf.Atan2(-worldDir.X, -worldDir.Z), 0f);
        }

        var vel = _playerCube.Velocity;
        vel.X = worldDir.X * speed;
        vel.Z = worldDir.Z * speed;

        // Vertical — fly while Page Up/Down held, otherwise gravity + Space jump.
        var pageUp   = Input.IsKeyPressed(Key.Pageup);
        var pageDown = Input.IsKeyPressed(Key.Pagedown);

        if (pageUp)
        {
            _isFlying = true;
            vel.Y = FlySpeed;
        }
        else if (_isFlying && pageDown)
        {
            vel.Y = -FlySpeed;
        }
        else if (_isFlying)
        {
            vel.Y = 0f;                 // hover
        }
        else
        {
            vel.Y = _playerCube.IsOnFloor() && Input.IsKeyPressed(Key.Space)
                ? JumpVelocity
                : vel.Y - Gravity * dt;
        }

        _playerCube.Velocity = vel;
        _playerCube.MoveAndSlide();

        // Exit fly mode when Page Down lands us back on the floor.
        if (_isFlying && pageDown && _playerCube.IsOnFloor())
            _isFlying = false;
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
                AlbedoColor = ColorForUser(state.UserId),
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
