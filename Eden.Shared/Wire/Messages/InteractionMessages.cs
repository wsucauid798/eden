using Eden.Shared.Ids;

namespace Eden.Shared.Wire.Messages;

/// <summary>
/// Client → server: the connected avatar touched the given prim. The server
/// dispatches this to the prim's attached behavior's <c>[OnTouch]</c> handler(s).
/// </summary>
public readonly record struct ClientTouchPrim(EdenId<PrimTag> PrimId);
