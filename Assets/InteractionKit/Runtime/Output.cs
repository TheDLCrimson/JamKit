using System;
using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>How a target node reacts to the source node's state.</summary>
    public enum OutputMode
    {
        /// <summary>Target mirrors the source. Source off means target off.</summary>
        Follow = 0,

        /// <summary>Target is set true when the source turns on, and left alone when it turns off.</summary>
        Pulse = 1,
    }

    /// <summary>
    /// One authored connection from a source node to a target node.
    /// This is the whole wiring model: a target and how it should react. There is deliberately no
    /// action name and no delay - both were cut because nothing in the jam's rooms needs them and
    /// both cost editor tooling to author well.
    /// </summary>
    [Serializable]
    public struct Output
    {
        [Tooltip("The node this connection drives. Empty rows are ignored.")]
        public SignalNode Target;

        [Tooltip("Follow mirrors this node's state. Pulse only sets the target true on the rising edge.")]
        public OutputMode Mode;
    }

    /// <summary>
    /// Global scene-view toggle for the kit's wiring gizmos, driven by the
    /// <c>JamKit &gt; Interaction &gt; Show Wiring</c> menu item.
    /// </summary>
    /// <remarks>
    /// A plain static rather than a setting asset: it is a per-developer view preference with no
    /// effect on a build, and the editor side persists it in EditorPrefs.
    /// </remarks>
    public static class InteractionGizmos
    {
        /// <summary>
        /// When true, EVERY node draws its wiring without being selected. Selecting a node always
        /// draws its own wiring regardless. Defaults to off so a scene is not covered in lines.
        /// </summary>
        public static bool ShowWiring;
    }
}
