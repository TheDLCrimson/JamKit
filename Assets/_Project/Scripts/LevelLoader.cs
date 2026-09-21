using JamKit;
using UnityEngine;

/// <summary>
/// Game-side consumer of the JamKit "one scene, many levels" pattern: holds a <see cref="LevelDataSO"/>
/// and a current index, and exposes the active <see cref="LevelData"/> so the Game scene re-parameterizes
/// itself per level instead of loading a scene per level.
/// </summary>
public class LevelLoader : MonoBehaviour
{
    [SerializeField] private LevelDataSO _levels;
    [SerializeField] private int _currentIndex;

    /// <summary>The currently selected level, or null if the index is out of range.</summary>
    public LevelData CurrentLevel => _levels != null ? _levels.GetLevel(_currentIndex) : null;

    /// <summary>Current 0-based level index.</summary>
    public int CurrentIndex => _currentIndex;

    /// <summary>Number of levels available.</summary>
    public int LevelCount => _levels != null ? _levels.Count : 0;

    /// <summary>Selects a level by index (clamped) and returns it.</summary>
    public LevelData Load(int index)
    {
        if (_levels == null || _levels.Count == 0)
        {
            return null;
        }

        _currentIndex = Mathf.Clamp(index, 0, _levels.Count - 1);
        return CurrentLevel;
    }

    /// <summary>Advances to the next level if one exists; returns true if it advanced.</summary>
    public bool Next()
    {
        if (_levels == null || _currentIndex >= _levels.Count - 1)
        {
            return false;
        }

        _currentIndex++;
        return true;
    }
}
