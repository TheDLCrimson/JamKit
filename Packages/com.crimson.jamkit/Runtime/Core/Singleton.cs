using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Find-only MonoBehaviour singleton base. A duplicate instance destroys itself in <see cref="Awake"/>.
    /// </summary>
    /// <remarks>
    /// Leave <c>Dont Destroy On Load</c> unchecked for a component parented under the <see cref="Bootstrap"/>
    /// prefab - Bootstrap calls <c>DontDestroyOnLoad</c> once on its own root, which carries its whole
    /// child hierarchy across scene loads. Check it only for a <see cref="Singleton{T}"/> used standalone,
    /// outside Bootstrap (a demo scene, a module, another project reusing the package).
    /// <para/>
    /// <b>Never fabricates.</b> <see cref="Instance"/> resolves an already-present, fully-wired instance or
    /// returns <c>null</c>; it does not <c>AddComponent</c> a bare stand-in. The four services are provided
    /// wired by the <see cref="Bootstrap"/> prefab, and a fabricated instance would (a) carry null serialized
    /// dependencies and (b) occupy the static slot so the real wired prefab child destroys itself as a
    /// duplicate in its own <see cref="Awake"/> - a silent, non-functional state. Reading <see cref="Instance"/>
    /// before Bootstrap has run <see cref="Awake"/> on the service (for example from a scene object's own
    /// <c>Awake</c>, which runs before <see cref="Bootstrap.EnsureBootstrapExists"/> at
    /// <see cref="RuntimeInitializeLoadType.AfterSceneLoad"/>) therefore returns <c>null</c> by design: read
    /// services from <c>Start</c> or later, so the failure is a loud null rather than a silent broken instance.
    /// <para/>
    /// <c>_quitting</c> is reset at the top of every <see cref="Awake"/> rather than only in a domain-reload
    /// static initializer: with Enter Play Mode's "Reload Domain" disabled, static fields are not reset between
    /// Play sessions, so a stale <c>true</c> left over from the previous session's <see cref="OnApplicationQuit"/>
    /// would otherwise make <see cref="Instance"/> return null forever after the first Play/Stop cycle.
    /// </remarks>
    public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        [Tooltip("Persist this instance across scene loads. Leave off for components parented under Bootstrap.")]
        [SerializeField] private bool _dontDestroyOnLoad;

        protected static T _instance;
        protected static bool _quitting;

        public static T Instance
        {
            get
            {
                if (_quitting) return null;

                if (!_instance)
                {
                    _instance = FindFirstObjectByType<T>();
                }

                // Deliberately never fabricates a bare instance (see remarks): null here means the wired
                // Bootstrap instance has not Awoken yet - read services from Start, not Awake.
                return _instance;
            }
        }

        protected virtual void Awake()
        {
            _quitting = false;

            if (_instance == null)
            {
                _instance = this as T;

                if (_dontDestroyOnLoad)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        protected virtual void OnApplicationQuit()
        {
            _quitting = true;
        }
    }
}
