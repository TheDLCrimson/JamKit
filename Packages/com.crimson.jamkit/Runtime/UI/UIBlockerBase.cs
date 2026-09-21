using UnityEngine;
using UnityEngine.Serialization;

namespace JamKit
{
    /// <summary>
    /// Base class for a modal panel managed by <see cref="UIBlockerStack"/>. Each blocker declares whether it
    /// pauses gameplay, hides the HUD, and can be dismissed with the UI Cancel action, and receives lifecycle
    /// callbacks as it moves through the stack.
    /// </summary>
    /// <remarks>
    /// Extracted from NineTailsProject's <c>UIBlockerBase</c>. The audit quirk where a blocker becoming the top
    /// again (after the one above it closed) re-fired <see cref="OnOpen"/> is fixed here by a distinct
    /// <see cref="OnResume"/> callback that <see cref="UIBlockerStack"/> raises in that case.
    /// </remarks>
    public abstract class UIBlockerBase : MonoBehaviour
    {
        [Header("Base Configurations")]
        [Tooltip("Pause gameplay while this blocker is active.")]
        [FormerlySerializedAs("pauseGameplay")]
        [SerializeField] private bool _pauseGameplay = true;

        [Tooltip("Hide the HUD layer while this blocker is active.")]
        [FormerlySerializedAs("hideHUD")]
        [SerializeField] private bool _hideHUD = true;

        [Tooltip("Allow this blocker to be closed via the UI Cancel action.")]
        [FormerlySerializedAs("respondToEscape")]
        [SerializeField] private bool _respondToEscape = true;

        /// <summary>Pause gameplay while this blocker is active.</summary>
        public bool PauseGameplay => _pauseGameplay;

        /// <summary>Hide the HUD layer while this blocker is active.</summary>
        public bool HideHUD => _hideHUD;

        /// <summary>Allow this blocker to be closed via the UI Cancel action.</summary>
        public bool RespondToEscape => _respondToEscape;

        /// <summary>Called when the blocker first becomes the active top element of the stack.</summary>
        public virtual void OnOpen() { }

        /// <summary>Called when another blocker is pushed above this one in the stack.</summary>
        public virtual void OnPause() { }

        /// <summary>
        /// Called when this blocker becomes the top again after the blocker above it was closed. Distinct from
        /// <see cref="OnOpen"/> so a panel can tell "opened fresh" from "revealed again".
        /// </summary>
        public virtual void OnResume() { }

        /// <summary>Called when the blocker is removed from the stack.</summary>
        public virtual void OnClose() { }

        /// <summary>Called each frame while this blocker is the topmost active element.</summary>
        public virtual void OnUpdate() { }

        /// <summary>Request dismissal of this blocker (for example from a close button).</summary>
        public void RequestClose()
        {
            if (UIBlockerStack.Instance) UIBlockerStack.Instance.CloseTopBlocker();
        }
    }
}
