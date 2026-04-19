using Eden.Shared.Entities;

namespace Eden.Shared.Wire.Messages;

/// <summary>
/// Server → viewer: the authoritative <see cref="WorldState"/> has changed
/// (or a new viewer just connected). Viewers mirror this and drive their
/// lighting / sky / weather / wind animation from it.
/// </summary>
public readonly record struct WorldStateUpdate(WorldState State);
