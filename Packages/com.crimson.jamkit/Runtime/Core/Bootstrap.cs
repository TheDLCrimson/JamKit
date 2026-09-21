using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Holds the persistent services (<see cref="SaveService"/>, <see cref="AudioService"/>,
    /// <see cref="GameState"/>, <see cref="UIScreenEffects"/>) under one DontDestroyOnLoad root, initialized in
    /// explicit order, and guarantees exactly one instance exists regardless of which scene Play is pressed in.
    /// </summary>
    /// <remarks>
    /// <para><b>Idempotency:</b> Bootstrap is not a <see cref="Singleton{T}"/> - it isn't one of the four
    /// services - so it guards its own duplicate with a plain static field compared via Unity's overloaded
    /// <c>==</c>/<c>!=</c>, the same pattern <see cref="Singleton{T}"/> uses. Whichever Bootstrap runs
    /// <c>Awake</c> second in a given Play session (the scene-authored one or the fallback-spawned one,
    /// whichever loses the race) destroys itself before any service is initialized, so nothing is
    /// initialized twice.</para>
    /// <para><b>Fallback timing:</b> <see cref="EnsureBootstrapExists"/> runs at
    /// <see cref="RuntimeInitializeLoadType.AfterSceneLoad"/>, which Unity guarantees fires after every
    /// GameObject in the first-loaded scene has run Awake/OnEnable but before any of them run Start. If
    /// Boot is the first scene loaded, its scene-placed Bootstrap instance has therefore already registered
    /// itself by the time this callback checks for one, so the fallback correctly finds it and does not
    /// spawn a duplicate. If Menu or Game is entered directly instead (no Bootstrap placed in those
    /// scenes), the fallback spawns one from Resources at this point - still before Start, but after
    /// Awake/OnEnable. Consequently, no scene script may read a JamKit service from its own <c>Awake</c>;
    /// read from <c>Start</c> or later.</para>
    /// <para><b>Disabled Domain Reload:</b> the duplicate-guard's static field and each service's
    /// <see cref="Singleton{T}"/> instance field are compared with Unity's null-safe <c>==</c>/<c>!=</c>,
    /// which correctly reports a destroyed object as equal to null - so a stale reference left over from a
    /// previous Play session (when Reload Domain is off) self-heals without a manual reset. The fallback
    /// spawn check itself queries the live scene (<c>FindFirstObjectByType</c>) rather than a cached static
    /// bool, for the same reason: a cached bool is not reset across a Stop/Play cycle and could wrongly
    /// skip spawning after the first session.</para>
    /// </remarks>
    public class Bootstrap : MonoBehaviour
    {
        private const string ResourcePath = "Bootstrap";

        private static Bootstrap _instance;

        [SerializeField] private SaveService _saveService;
        [SerializeField] private AudioService _audioService;
        [SerializeField] private GameState _gameState;
        [SerializeField] private UIScreenEffects _uiScreenEffects;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Explicit order: SaveService first (the others read persisted values on init), then
            // AudioService (reads persisted volume), then GameState, then UIScreenEffects (overlay setup,
            // no cross-service dependency).
            _saveService.Initialize();
            _audioService.Initialize();
            _gameState.Initialize();
            _uiScreenEffects.Initialize();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrapExists()
        {
            if (FindFirstObjectByType<Bootstrap>() != null) return;

            GameObject prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"Bootstrap.EnsureBootstrapExists: no prefab found at Resources/{ResourcePath}.");
                return;
            }

            Instantiate(prefab);
        }
    }
}
