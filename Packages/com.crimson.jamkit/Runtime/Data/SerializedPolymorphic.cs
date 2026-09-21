using System;

namespace JamKit
{
    /// <summary>
    /// Marker base for any type authored through a <c>[SerializeReference]</c> polymorphic list
    /// that should get JamKit's <c>PolymorphicListDrawer</c> (type dropdown, per-element delete,
    /// open-script button) in the inspector.
    /// </summary>
    /// <remarks>
    /// This class is intentionally empty. Its only job is to give the editor drawer a single
    /// JamKit-owned type to register against (<c>[CustomPropertyDrawer(typeof(SerializedPolymorphic), true)]</c>),
    /// so the drawer is a reusable data-authoring primitive rather than being bound to one concrete
    /// hierarchy. Derive your own polymorphic base (e.g. <see cref="ItemEffect"/>) from this, mark the
    /// field <c>[SerializeReference]</c>, and the drawer applies automatically.
    /// </remarks>
    [Serializable]
    public abstract class SerializedPolymorphic
    {
    }
}
