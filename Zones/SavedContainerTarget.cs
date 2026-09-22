using UnityEngine;

namespace SpiritHelper.Zones;

public readonly struct SavedContainerTarget
{
    public SavedContainerTarget(long userId, uint id)
    {
        UserId = userId;
        Id = id;
    }

    public long UserId { get; }
    public uint Id { get; }
    public bool IsBound => Id != 0;

    public static SavedContainerTarget From(Container? container)
    {
        if (!container) return default;
        var view = container.GetComponent<ZNetView>();
        if (!view || !view.IsValid()) return default;
        var id = view.GetZDO().m_uid;
        return new SavedContainerTarget(id.UserID, id.ID);
    }

    public Container? Resolve()
    {
        if (!IsBound || !ZNetScene.instance) return null;
        var instance = ZNetScene.instance.FindInstance(new ZDOID(UserId, Id));
        return instance ? instance.GetComponent<Container>() : null;
    }
}
