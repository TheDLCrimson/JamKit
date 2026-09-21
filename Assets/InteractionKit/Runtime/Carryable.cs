using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// Marks a physics prop as something the player can pick up, and optionally as something a
    /// <see cref="Socket"/> can recognise by id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v2 note: in v1 this was an id and nothing else, because picking an object up needed no marker
    /// at all - a prop was grabbable purely because it had a light enough dynamic rigidbody on the
    /// right layer. That meant there was nothing to tick, nothing to see, and no way to tell a
    /// grabbable crate from scenery by looking at it in the inspector. <see cref="CanBeCarried"/>
    /// makes the intent visible and authorable.
    /// </para>
    /// <para>
    /// It defaults to ON so adding the component makes a prop work immediately; the box is there to
    /// disable a specific prop, not to enable every one. A prop with no Carryable at all is still
    /// grabbable if its physics qualify, so existing scenes do not break and scattered debris still
    /// behaves - only an explicit unticked Carryable refuses.
    /// </para>
    /// </remarks>
    [AddComponentMenu("JamKit/Interaction/Carryable")]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Carryable : MonoBehaviour
    {
        [Tooltip("Whether the player can pick this prop up. Untick for a prop that should be physics-active scenery, or one that is only ever moved by a socket.")]
        [SerializeField] private bool _canBeCarried = true;

        [Tooltip("Identifier a Socket matches against, e.g. \"battery\" or \"key_vault\". Leave empty for a prop no socket accepts.")]
        [SerializeField] private string _id = "";

        /// <summary>Whether the player may pick this prop up.</summary>
        public bool CanBeCarried => _canBeCarried;

        /// <summary>Identifier a socket matches against.</summary>
        public string Id => _id;

        /// <summary>The socket currently holding this prop, or null when free. Set by the socket.</summary>
        public Socket Holder { get; set; }

        /// <summary>Code-built setup (demos, tests and runtime-authored rooms); in the editor set the fields.</summary>
        public void Configure(string id, bool canBeCarried = true)
        {
            _id = id;
            _canBeCarried = canBeCarried;
        }

        // A prop at the default mass of 1 with no collider is the shape of a thing that silently
        // will not be grabbable, so give a new one working physics rather than a plausible-looking
        // set of defaults that does nothing.
        private void Reset()
        {
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.mass = 2f;
                body.isKinematic = false;
                body.useGravity = true;
            }

            if (GetComponent<Collider>() == null)
                gameObject.AddComponent<BoxCollider>();
        }
    }
}
