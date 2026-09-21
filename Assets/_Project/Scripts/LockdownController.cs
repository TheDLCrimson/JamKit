using JamKit;
using TMPro;
using UnityEngine;

// LockdownController: Lockdown mechanic. Drives the per-level countdown and win/lose flow, then opens the
// WinLoseBlocker. Plain Assembly-CSharp, no namespace (jam-code convention).
public class LockdownController : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private LevelLoader _levelLoader;
    [SerializeField] private UIBlockerBase _winLoseBlockerPrefab;
    [SerializeField] private LockdownDoor _door;
    [SerializeField] private KeyPickup _keyPickup;
    [Tooltip("HUD label - shows the level number/subtitle and countdown during play. Without this, nothing on screen indicates which level you're on or how much time is left.")]
    [SerializeField] private TMP_Text _hudText;

    [Header("Tuning (per level index; human-tunable, not code)")]
    [SerializeField] private float[] _timerDurationsByLevel = { 10f, 7f, 5f };

    private float _timeRemaining;
    private bool _isResolved;

    private void OnEnable()
    {
        EventBus.Subscribe<LockdownWonEvent>(OnWon);
        EventBus.Subscribe<ContinueRequestedEvent>(OnContinueRequested);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<LockdownWonEvent>(OnWon);
        EventBus.Unsubscribe<ContinueRequestedEvent>(OnContinueRequested);
    }

    private void Start()
    {
        _timeRemaining = CurrentLevelDuration();
        UpdateHud();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        JamKit.DebugOverlay.AddButton("Lockdown: Force Win", () => OnWon(new LockdownWonEvent()));
        JamKit.DebugOverlay.AddButton("Lockdown: Force Lose", ForceLose);
#endif
    }

    private void Update()
    {
        if (_isResolved)
        {
            return;
        }

        _timeRemaining -= Time.deltaTime;
        UpdateHud();

        if (_timeRemaining <= 0f)
        {
            OnLost();
        }
    }

    private void UpdateHud()
    {
        if (_hudText == null)
        {
            return;
        }

        LevelData level = _levelLoader != null ? _levelLoader.CurrentLevel : null;
        string levelLabel = level != null ? $"Level {level.LevelNumber}: {level.Subtitle}" : "Level ?";
        _hudText.text = $"{levelLabel}\n{Mathf.Max(_timeRemaining, 0f):F1}s";
    }

    private float CurrentLevelDuration()
    {
        int index = _levelLoader != null ? _levelLoader.CurrentIndex : 0;
        return index >= 0 && index < _timerDurationsByLevel.Length ? _timerDurationsByLevel[index] : _timerDurationsByLevel[0];
    }

    private void OnWon(LockdownWonEvent e)
    {
        if (_isResolved)
        {
            return;
        }

        _isResolved = true;
        Resolve(won: true);
    }

    private void OnLost()
    {
        _isResolved = true;
        EventBus.Fire(new LockdownLostEvent());
        Resolve(won: false);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void ForceLose()
    {
        if (!_isResolved)
        {
            OnLost();
        }
    }
#endif

    private void Resolve(bool won)
    {
        float elapsed = CurrentLevelDuration() - Mathf.Max(_timeRemaining, 0f);
        SaveResult(won, elapsed);

        if (won)
        {
            UIScreenEffects.Instance?.HealFlash();
        }
        else
        {
            UIScreenEffects.Instance?.WarningFlash();
        }

        OpenResultBlocker();
    }

    private void SaveResult(bool won, float elapsed)
    {
        if (SaveService.Instance == null)
        {
            return;
        }

        SaveService.Instance.SetBool(GameConstants.PrefsKeyLockdownLastWon, won);
        SaveService.Instance.SetFloat(GameConstants.PrefsKeyLockdownLastElapsed, elapsed);

        if (!won)
        {
            return;
        }

        int index = _levelLoader != null ? _levelLoader.CurrentIndex : 0;
        string bestKey = GameConstants.PrefsKeyLockdownBestTimePrefix + index;
        float best = SaveService.Instance.GetFloat(bestKey, float.MaxValue);
        if (elapsed < best)
        {
            SaveService.Instance.SetFloat(bestKey, elapsed);
        }
    }

    private void OpenResultBlocker()
    {
        if (_winLoseBlockerPrefab == null || UIBlockerStack.Instance == null)
        {
            return;
        }

        // UIBlockerStack.OpenBlocker does not return the instantiated instance, so WinLoseBlocker reads its
        // own result data back out of SaveService in OnOpen rather than receiving it as a parameter here.
        UIBlockerStack.Instance.OpenBlocker(_winLoseBlockerPrefab);
    }

    /// <summary>
    /// Handles WinLoseBlocker's Continue button, via EventBus rather than a direct reference (WinLoseBlocker
    /// is a prefab asset and cannot hold one). Advances the level index on a win, then resets the room in
    /// place (no scene reload - keeps LevelLoader's in-memory index intact).
    /// </summary>
    private void OnContinueRequested(ContinueRequestedEvent e) => AdvanceOrRetry(e.Won);

    private void AdvanceOrRetry(bool won)
    {
        if (won && _levelLoader != null)
        {
            _levelLoader.Next();
        }

        _isResolved = false;
        _timeRemaining = CurrentLevelDuration();

        if (_door != null)
        {
            _door.ReLock();
        }

        if (_keyPickup != null)
        {
            _keyPickup.ResetPickup();
        }

        UpdateHud();
    }
}
