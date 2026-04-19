using Godot;
using Eden.Client;
using Eden.Launcher;
using Eden.Shared.Entities;
using Eden.Shared.Math;

namespace Eden.Viewer;

/// <summary>
/// Root scene node. For the scaffold, brings Eden up in solo mode and logs
/// world-state events to Godot's output. Replace / expand with real
/// rendering and input as the viewer grows.
/// </summary>
public partial class Main : Node
{
    private SoloHandle? _solo;
    private ViewerClient? _client;

    public override async void _Ready()
    {
        _solo   = EdenLauncher.StartSolo();
        _client = new ViewerClient(_solo.Transport);

        _client.AvatarUpdated += s => GD.Print($"[Eden] avatar {s.UserId} at {s.Transform.Position}");
        _client.AvatarLeft    += u => GD.Print($"[Eden] avatar {u} left");

        await _client.ConnectAsync(OS.GetEnvironment("USERNAME") ?? "Player");
        GD.Print($"[Eden] connected. My UserId = {_client.MyUserId}");

        // Fire an initial avatar update so we appear in the world.
        await _client.SendAvatarUpdateAsync(new AvatarState(
            UserId:         _client.MyUserId,
            SessionId:      _client.Session!.Value.SessionId,
            DisplayName:    "Player",
            Transform:      Transform.Identity,
            Velocity:       Vector3.Zero,
            AppearanceHash: 0));
    }

    public override async void _ExitTree()
    {
        if (_client is not null) await _client.DisposeAsync();
        if (_solo   is not null) await _solo.DisposeAsync();
    }
}
