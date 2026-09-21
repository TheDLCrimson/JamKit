using System;
using System.Collections.Generic;
using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// One level's design-time definition in the "one scene, many levels" pattern: a single Game scene
    /// is reused and re-parameterized per level from this data instead of authoring a scene per level.
    /// </summary>
    /// <remarks>
    /// Kept deliberately generic - number, subtitle, and optional starting items. It carries no
    /// game-specific fields (the CarGoesAround donor coupled a grid-layout SO and an inventory-preset SO;
    /// both are dropped). Add game-specific level fields in game code, not here.
    /// </remarks>
    [Serializable]
    public class LevelData
    {
        [SerializeField] private int _levelNumber;
        [SerializeField] private string _subtitle;
        [SerializeField] private ItemDefinition[] _startingItems = Array.Empty<ItemDefinition>();

        /// <summary>1-based level index.</summary>
        public int LevelNumber => _levelNumber;

        /// <summary>Optional display subtitle for the level.</summary>
        public string Subtitle => _subtitle;

        /// <summary>Items the player begins the level holding (may be empty).</summary>
        public IReadOnlyList<ItemDefinition> StartingItems => _startingItems;
    }

    /// <summary>
    /// An ordered collection of <see cref="LevelData"/> entries driving a single reused Game scene.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelData", menuName = "JamKit/Level Data")]
    public class LevelDataSO : ScriptableObject
    {
        [SerializeField] private List<LevelData> _levels = new List<LevelData>();

        /// <summary>All authored levels, in order.</summary>
        public IReadOnlyList<LevelData> Levels => _levels;

        /// <summary>Number of authored levels.</summary>
        public int Count => _levels.Count;

        /// <summary>Returns the level at <paramref name="index"/>, or null if out of range.</summary>
        public LevelData GetLevel(int index)
        {
            if (index < 0 || index >= _levels.Count)
            {
                return null;
            }

            return _levels[index];
        }
    }
}
