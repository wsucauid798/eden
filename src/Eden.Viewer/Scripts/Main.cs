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
[Tool]
public partial class Main : Node3D
{
    private const string GeneratedRootName = "__GeneratedScene";
    private const string EditorPreviewWorldName = "Editor Preview";
    private enum SceneBuildMode
    {
        Runtime,
        EditorPreview,
    }

    private const float  MoveSpeed        = 4f;
    private const float  SprintMultiplier = 2.5f;
    private const float  TurnSpeed        = 10f;   // rad/s — how fast the avatar yaws toward the movement direction
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
    private Label?          _debugHudLabel;
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
    private Node3D? _generatedRoot;
    private bool  _editorPreviewEnabled = true;
    // Match the server's default solo-world start time so editor preview
    // and runtime are comparable without extra tweaking.
    private float _previewTimeOfDayHours = 8f;
    private Weather _previewWeather = Weather.Clear;
    private bool _editorPreviewRefreshQueued;
    private readonly Queue<string> _debugMessages = new();
    private bool _debugHudEnabled = true;
    private bool _debugOutputEnabled = true;
    private int _lastLoggedWorldHour = -1;
    private Weather? _lastLoggedWeather;
    private bool? _lastLoggedNightSkyEnabled;
    private const int MaxDebugMessages = 8;

    [Export]
    public bool EditorPreviewEnabled
    {
        get => _editorPreviewEnabled;
        set
        {
            if (_editorPreviewEnabled == value) return;
            _editorPreviewEnabled = value;
            QueueEditorPreviewRefresh();
        }
    }

    [Export(PropertyHint.Range, "0,24,0.1")]
    public float PreviewTimeOfDayHours
    {
        get => _previewTimeOfDayHours;
        set
        {
            var wrapped = SunMath.WrapHours(value);
            if (Mathf.IsEqualApprox(_previewTimeOfDayHours, wrapped)) return;
            _previewTimeOfDayHours = wrapped;
            QueueEditorPreviewRefresh();
        }
    }

    [Export]
    public Weather PreviewWeather
    {
        get => _previewWeather;
        set
        {
            if (_previewWeather == value) return;
            _previewWeather = value;
            QueueEditorPreviewRefresh();
        }
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    public override void _Ready()
    {
        _displayName = OS.GetEnvironment("USERNAME") is { Length: > 0 } u ? u : "Player";

        if (Engine.IsEditorHint())
        {
            QueueEditorPreviewRefresh();
            return;
        }

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
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 }:
                _debugHudEnabled = !_debugHudEnabled;
                LogDebug($"debug HUD {(_debugHudEnabled ? "enabled" : "disabled")}");
                break;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.F4 }:
                _debugOutputEnabled = !_debugOutputEnabled;
                LogDebug(
                    $"Godot output logging {(_debugOutputEnabled ? "enabled" : "disabled")}",
                    forceOutput: true);
                break;

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
        if (Engine.IsEditorHint())
            return;

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

        EnsureSceneBuilt(SceneBuildMode.Runtime);
        BuildHud();
        LogDebug($"starting {mode} mode");

        var transport = await ResolveTransportAsync(mode);

        _client = new ViewerClient(transport);
        _client.AvatarUpdated += state  => _pendingUpdates.Enqueue(state);
        _client.AvatarLeft    += userId => _pendingLeaves.Enqueue(userId);
        if (_world is not null) _world.Bind(_client);
        if (_prims is not null) _prims.Bind(_client);

        await _client.ConnectAsync(_displayName);
        LogDebug($"connected. My UserId = {_client.MyUserId}", forceOutput: true);

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
                LogDebug($"hosting on port {_host.LocalEndPoint.Port}", forceOutput: true);
                return _host.Transport;

            case "join":
                LogDebug($"joining {host}:{port}", forceOutput: true);
                return await EdenLauncher.ConnectAsync(host, port);

            default:
                _solo = EdenLauncher.StartSolo();
                LogDebug("solo mode", forceOutput: true);
                return _solo.Transport;
        }
    }

    // ------------------------------------------------------------------
    // World construction
    // ------------------------------------------------------------------

    private void QueueEditorPreviewRefresh()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree()) return;

        if (_editorPreviewRefreshQueued) return;
        _editorPreviewRefreshQueued = true;
        CallDeferred(nameof(RefreshEditorPreview));
    }

    private void RefreshEditorPreview()
    {
        if (!Engine.IsEditorHint()) return;
        _editorPreviewRefreshQueued = false;

        ClearGeneratedContent();
        if (!EditorPreviewEnabled) return;

        EnsureSceneBuilt(SceneBuildMode.EditorPreview);
        ApplyEditorPreviewWorld();
    }

    private void EnsureSceneBuilt(SceneBuildMode mode)
    {
        if (_generatedRoot is not null) return;
        BuildScene(mode);
    }

    private void ApplyEditorPreviewWorld()
    {
        if (_world is null) return;

        _world.EnsureBuilt();
        _world.ApplyWorldState(new WorldState(
            WorldId:         EdenId<WorldTag>.Empty,
            Name:            EditorPreviewWorldName,
            TimeOfDayHours:  _previewTimeOfDayHours,
            Wind:            Eden.Shared.Math.Vector3.Zero,
            Weather:         _previewWeather,
            Gravity:         9.81f));
    }

    private void ClearGeneratedContent()
    {
        if (_generatedRoot is null && GetNodeOrNull<Node3D>(GeneratedRootName) is { } existingRoot)
            _generatedRoot = existingRoot;

        if (_generatedRoot is not null)
        {
            _generatedRoot.QueueFree();
            _generatedRoot = null;
        }

        _playerCube = null;
        _playerMesh = null;
        _playerLabel = null;
        _cameraRig = null;
        _yawPivot = null;
        _pitchPivot = null;
        _camera = null;
        if (_world is not null)
            _world.DebugStateChanged -= OnWorldDebugStateChanged;
        _world = null;
        _prims = null;
    }

    private void BuildScene(SceneBuildMode mode)
    {
        _generatedRoot = new Node3D { Name = GeneratedRootName };
        AddChild(_generatedRoot);

        var sceneRoot = _generatedRoot;

        // WorldRenderer owns the sun + environment (sky, fog, ambient).
        // Its state is driven by the server's world clock once we bind it
        // to the ViewerClient in StartAsync.
        _world = new WorldRenderer();
        sceneRoot.AddChild(_world);
        _world.EnsureBuilt();
        if (mode == SceneBuildMode.Runtime)
            _world.DebugStateChanged += OnWorldDebugStateChanged;

        if (mode == SceneBuildMode.Runtime)
        {
            // PrimRenderer mirrors RemotePrims into the scene as MeshInstance3Ds.
            // Wired to the client after ConnectAsync.
            _prims = new PrimRenderer();
            sceneRoot.AddChild(_prims);
        }

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
        sceneRoot.AddChild(floor);

        if (mode == SceneBuildMode.Runtime)
        {
            // Physics collider for the floor — an infinite Y=0 plane so the
            // player's CharacterBody3D has something to stand and land on.
            var floorBody = new StaticBody3D();
            floorBody.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
            sceneRoot.AddChild(floorBody);
        }

        // A few reference props so the world isn't two cubes in a void.
        BuildReferenceProps(sceneRoot, includeCollision: mode == SceneBuildMode.Runtime);

        if (mode == SceneBuildMode.EditorPreview)
        {
            var previewAvatar = BuildAvatarMesh(new Color(0.55f, 0.55f, 0.60f));
            previewAvatar.Position = new Vector3(0f, GroundY, 0f);
            sceneRoot.AddChild(previewAvatar);

            var previewLabel = BuildNameLabel("Preview Spawn");
            previewAvatar.AddChild(previewLabel);
            return;
        }

        // Player body — CharacterBody3D so movement, jumping, gravity, and
        // collisions all go through Godot physics instead of hand-rolled
        // position math. Visible cube and box collider are both children;
        // MoveAndSlide is driven from _PhysicsProcess.
        _playerCube = new CharacterBody3D { Position = new Vector3(0f, GroundY, 0f) };
        _playerMesh = BuildAvatarMesh(new Color(0.55f, 0.55f, 0.60f));
        _playerCube.AddChild(_playerMesh);
        _playerCube.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One } });
        sceneRoot.AddChild(_playerCube);

        _playerLabel = BuildNameLabel(_displayName);
        _playerCube.AddChild(_playerLabel);

        // Camera rig — separate node that tracks player's position (not
        // rotation). Yaw and pitch come from the mouse.
        _cameraRig  = new Node3D();
        _yawPivot   = new Node3D();
        _pitchPivot = new Node3D { Rotation = new Vector3(_pitch, 0, 0) };
        _camera     = new Camera3D { Position = new Vector3(0f, CameraHeight, _cameraDistance) };

        sceneRoot.AddChild(_cameraRig);
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
    private void BuildReferenceProps(Node3D parent, bool includeCollision)
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
        if (includeCollision)
            AttachStaticBody(mono, new BoxShape3D { Size = new Vector3(2f, 8f, 2f) });
        parent.AddChild(mono);

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
            if (includeCollision)
                AttachStaticBody(pillar, new CylinderShape3D { Height = 5f, Radius = 1.0f });
            parent.AddChild(pillar);
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
        if (includeCollision)
            AttachStaticBody(sphereA, new SphereShape3D { Radius = 1.2f });
        parent.AddChild(sphereA);

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
        if (includeCollision)
            AttachStaticBody(sphereB, new SphereShape3D { Radius = 0.9f });
        parent.AddChild(sphereB);
    }

    /// <summary>Attach a <see cref="StaticBody3D"/> child with the given
    /// shape so the player's CharacterBody3D can collide with this prop.</summary>
    private static void AttachStaticBody(Node3D parent, Shape3D shape)
    {
        var body = new StaticBody3D();
        body.AddChild(new CollisionShape3D { Shape = shape });
        parent.AddChild(body);
    }

    /// <summary>Body mesh + a small white "nose" on the front face so
    /// which way the avatar is pointing is visible at a glance (a plain
    /// cube looks identical from four sides).</summary>
    private static MeshInstance3D BuildAvatarMesh(Color bodyColor)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One } };
        mesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = bodyColor });

        var nose = new MeshInstance3D
        {
            Mesh     = new BoxMesh { Size = new Vector3(0.3f, 0.3f, 0.3f) },
            Position = new Vector3(0f, 0.2f, -0.6f),   // local -Z is forward
        };
        nose.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f),
        });
        mesh.AddChild(nose);
        return mesh;
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
        if (_hudLabel is not null) return;

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

        _debugHudLabel = new Label
        {
            Text = "",
            Position = new Vector2(16f, 40f),
            Size = new Vector2(960f, 260f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _debugHudLabel.AddThemeFontSizeOverride("font_size", 13);
        _debugHudLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.90f, 1.00f));
        hud.AddChild(_debugHudLabel);
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

            // Smoothly turn toward the movement direction instead of
            // snapping. LerpAngle handles the -pi..pi wrap so there's no
            // flip when crossing 180°.
            var targetYaw  = Mathf.Atan2(-worldDir.X, -worldDir.Z);
            var currentYaw = _playerCube.Rotation.Y;
            var t          = Mathf.Min(1f, TurnSpeed * dt);
            _playerCube.Rotation = new Vector3(0f, Mathf.LerpAngle(currentYaw, targetYaw, t), 0f);
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
        var q   = _playerCube.Quaternion;
        var edenPos = new Eden.Shared.Math.Vector3(pos.X, pos.Y, pos.Z);
        var edenRot = new Eden.Shared.Math.Quaternion(q.X, q.Y, q.Z, q.W);

        await _client.SendAvatarUpdateAsync(new AvatarState(
            UserId:         _client.MyUserId,
            SessionId:      _client.Session.Value.SessionId,
            DisplayName:    _displayName,
            Transform:      new Eden.Shared.Math.Transform(edenPos, edenRot),
            Velocity:       Eden.Shared.Math.Vector3.Zero,
            AppearanceHash: 0));
    }

    private void UpdateHud()
    {
        if (_hudLabel is null || _playerCube is null) return;
        var pos = _playerCube.Position;
        var motion = _isFlying ? "fly" : _isSprinting ? "sprint" : "walk";
        var worldStatus = _world?.LastApplied is { } worldState
            ? $" · {worldState.TimeOfDayHours:F1}h · {worldState.Weather}"
            : "";
        _hudLabel.Text = $"Eden · {ModeLabel()} · {_displayName} · {motion}{worldStatus} · " +
                         $"({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}) · {_remote.Count} other(s)";

        if (_debugHudLabel is not null)
        {
            _debugHudLabel.Visible = _debugHudEnabled;
            _debugHudLabel.Text = _debugHudEnabled ? BuildDebugHudText() : "";
        }
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
            var cube = BuildAvatarMesh(ColorForUser(state.UserId));
            (_generatedRoot ?? this).AddChild(cube);

            var label = BuildNameLabel(state.DisplayName);
            cube.AddChild(label);

            avatar = new RemoteAvatar(cube, label);
            _remote[state.UserId] = avatar;
        }

        var p = state.Transform.Position;
        var r = state.Transform.Rotation;
        avatar.Cube.Position   = new Vector3(p.X, p.Y + 0.5f, p.Z);
        avatar.Cube.Quaternion = new Quaternion(r.X, r.Y, r.Z, r.W);
        if (!string.IsNullOrEmpty(state.DisplayName))
            avatar.Label.Text = state.DisplayName;
    }

    private void RemoveRemoteAvatar(EdenId<UserTag> userId)
    {
        if (_remote.Remove(userId, out var avatar))
            avatar.Cube.QueueFree();
    }

    private void OnWorldDebugStateChanged(WorldRenderer.RenderDebugState state)
    {
        var hourBucket = Mathf.FloorToInt(state.TimeOfDayHours);
        if (hourBucket == _lastLoggedWorldHour &&
            _lastLoggedWeather == state.Weather &&
            _lastLoggedNightSkyEnabled == state.NightSkyEnabled)
        {
            return;
        }

        _lastLoggedWorldHour = hourBucket;
        _lastLoggedWeather = state.Weather;
        _lastLoggedNightSkyEnabled = state.NightSkyEnabled;

        LogDebug(
            $"world {state.TimeOfDayHours:F1}h {state.Weather} daylight={state.DaylightFactor:F2} " +
            $"sun={state.SunEnergy:F2} sky={state.SkyEnergy:F2} exposure={state.Exposure:F2} " +
            $"fog={state.FogDensity:F4} ambientSky={state.AmbientSkyContribution:F2} " +
            $"clouds={state.CloudCover:F2} moon={state.MoonVisibility:F2} " +
            $"stars={state.StarVisibility:F2} nightSky={(state.NightSkyEnabled ? "on" : "off")}");
    }

    private string BuildDebugHudText()
    {
        var lines = new List<string>
        {
            $"debug F3=HUD({(_debugHudEnabled ? "on" : "off")}) F4=output({(_debugOutputEnabled ? "on" : "off")})",
        };

        if (_world?.LastDebugState is { } render)
        {
            lines.Add(
                $"render daylight={render.DaylightFactor:F2} sun={render.SunEnergy:F2} " +
                $"sky={render.SkyEnergy:F2} nightSky={(render.NightSkyEnabled ? "on" : "off")}");
            lines.Add(
                $"env exposure={render.Exposure:F2} fog={render.FogDensity:F4} " +
                $"ambientSky={render.AmbientSkyContribution:F2} ambient={render.AmbientEnergy:F2}");
            lines.Add(
                $"sky clouds={render.CloudCover:F2} moon={render.MoonVisibility:F2} " +
                $"stars={render.StarVisibility:F2}");
        }
        else
        {
            lines.Add("render waiting for world state...");
        }

        if (_debugMessages.Count > 0)
        {
            lines.Add("log:");
            foreach (var line in _debugMessages)
                lines.Add(line);
        }

        return string.Join("\n", lines);
    }

    private void LogDebug(string message, bool forceOutput = false)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {message}";
        if (_debugMessages.Count >= MaxDebugMessages)
            _debugMessages.Dequeue();
        _debugMessages.Enqueue(line);

        if (forceOutput || _debugOutputEnabled)
            GD.Print($"[Eden][Debug] {line}");
    }

    private readonly record struct RemoteAvatar(MeshInstance3D Cube, Label3D Label);
}
