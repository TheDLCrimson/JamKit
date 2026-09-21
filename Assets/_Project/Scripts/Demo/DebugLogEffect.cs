using JamKit;
using UnityEngine;

/// <summary>
/// Demo leaf effect: logs a message when applied. Concrete <see cref="ItemEffect"/> subclasses are
/// game-side (the toolkit ships only the composable base and <c>RandomChanceEffect</c>); this one exists
/// so the M4 demo can compose and observe polymorphic effects, including nested RandomChance branches.
/// </summary>
[System.Serializable]
public class DebugLogEffect : ItemEffect
{
    // Exists only where it's actually read (below) - a release build has no code path that touches this
    // field, so it must not exist there either, or the compiler correctly flags it as CS0414 dead weight.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [SerializeField] private string _message = "Effect applied";
#endif

    /// <inheritdoc />
    public override void ApplyEffect(GameObject caster = null, GameObject target = null)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[DebugLogEffect] {_message}");
#endif
    }
}
