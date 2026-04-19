using System.Collections.Concurrent;
using System.Collections.Generic;
using Godot;
using Eden.Client;
using Eden.Shared.Entities;
using Eden.Shared.Ids;

namespace Eden.Viewer;

/// <summary>
/// Mirrors <see cref="ViewerClient.RemotePrims"/> into the Godot scene as
/// <see cref="MeshInstance3D"/> boxes. Each unique <c>PrimId</c> becomes
/// one mesh; subsequent <see cref="ViewerClient.PrimUpdated"/> events
/// mutate transform / scale / colour in place.
/// </summary>
/// <remarks>
/// <see cref="ViewerClient.PrimUpdated"/> fires on the receive-loop thread.
/// States are queued and drained on the main thread in <see cref="_Process"/>.
/// </remarks>
public partial class PrimRenderer : Node3D
{
    private readonly Dictionary<EdenId<PrimTag>, MeshInstance3D> _meshes = new();
    private readonly ConcurrentQueue<PrimState> _pending = new();

    /// <summary>Live view of the prim meshes keyed by prim id. Exposed for
    /// tests and for inspector nodes (minimap, pick cursors, etc.).</summary>
    public IReadOnlyDictionary<EdenId<PrimTag>, MeshInstance3D> Meshes => _meshes;

    public void Bind(ViewerClient client)
    {
        client.PrimUpdated += state => _pending.Enqueue(state);
        foreach (var snapshot in client.RemotePrims.Values)
            _pending.Enqueue(snapshot);
    }

    public override void _Process(double delta)
    {
        while (_pending.TryDequeue(out var state))
            ApplyPrimState(state);
    }

    /// <summary>Create-or-update the mesh for this prim. Called on the main
    /// thread by <see cref="_Process"/>, or directly by tests.</summary>
    public void ApplyPrimState(PrimState state)
    {
        if (!_meshes.TryGetValue(state.Id, out var mesh))
        {
            mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One } };
            AddChild(mesh);
            _meshes[state.Id] = mesh;
        }

        var pos = state.Transform.Position;
        var rot = state.Transform.Rotation;
        mesh.Position = new Vector3(pos.X, pos.Y, pos.Z);
        mesh.Quaternion = new Quaternion(rot.X, rot.Y, rot.Z, rot.W);
        mesh.Scale    = new Vector3(state.Scale.X, state.Scale.Y, state.Scale.Z);

        mesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(
                state.TintColor.R / 255f,
                state.TintColor.G / 255f,
                state.TintColor.B / 255f,
                state.TintColor.A / 255f),
        });
    }
}
