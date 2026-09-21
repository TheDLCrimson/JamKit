using JamKit;
using TMPro;
using UnityEngine;

// WinLoseBlocker: Lockdown's result screen. Reads the last result out of SaveService rather than a
// parameter, because UIBlockerStack.OpenBlocker does not return the instantiated instance. Continue is
// routed through EventBus rather than a direct LockdownController reference, because this is a prefab asset
// instantiated at runtime and cannot hold a pre-wired reference to a specific scene's controller instance.
// Plain Assembly-CSharp, no namespace.
public class WinLoseBlocker : UIBlockerBase
{
    [Header("Wiring")]
    [SerializeField] private TMP_Text _resultText;

    public override void OnOpen()
    {
        base.OnOpen();
        Display();
    }

    private void Display()
    {
        if (_resultText == null || SaveService.Instance == null)
        {
            return;
        }

        bool won = SaveService.Instance.GetBool(GameConstants.PrefsKeyLockdownLastWon, false);
        float elapsed = SaveService.Instance.GetFloat(GameConstants.PrefsKeyLockdownLastElapsed, 0f);

        _resultText.text = won ? $"UNLOCKED - {elapsed:F1}s" : "LOCKED OUT - try again";
    }

    /// <summary>Wired to the blocker's Continue button in the Inspector.</summary>
    public void OnContinuePressed()
    {
        bool won = SaveService.Instance != null && SaveService.Instance.GetBool(GameConstants.PrefsKeyLockdownLastWon, false);

        RequestClose();
        EventBus.Fire(new ContinueRequestedEvent { Won = won });
    }
}
