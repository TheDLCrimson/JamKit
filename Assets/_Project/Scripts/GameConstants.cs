// Convention: every tag, layer, PlayerPrefs key, and scene name for this game lives in this file, and only this file.
// Reference these constants everywhere instead of repeating string/layer literals.
public static class GameConstants
{
    public const string SceneBoot = "Boot";
    public const string SceneMenu = "Menu";
    public const string SceneGame = "Game";

    // AudioService owns its own volume-pref keys internally (JamKit must never depend on game-side
    // constants) - only game-specific keys belong here.

    // Lockdown (sample game): per-level best time, keyed as PrefsKeyLockdownBestTimePrefix + level index.
    public const string PrefsKeyLockdownBestTimePrefix = "LockdownBestTime_";
    public const string PrefsKeyLockdownLastElapsed = "LockdownLastElapsed";
    public const string PrefsKeyLockdownLastWon = "LockdownLastWon";

    public const string TagPlayer = "Player";
}
