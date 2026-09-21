using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JamKit.Editor
{
    /// <summary>
    /// Bulk-generates one ScriptableObject content asset per source prefab in a folder, deriving the
    /// asset's display name from the prefab name and linking a matching icon by filename. Load-or-create,
    /// so re-running updates existing assets rather than duplicating them.
    /// </summary>
    /// <remarks>
    /// Generalized from FurnishMyHome's hardcoded FurnitureDataGenerator: the folders and the target SO
    /// type are configurable here. It keeps the donor's prefab-as-source model - prefabs are naming seeds,
    /// one output SO per prefab. Fields are assigned by convention (via <see cref="SerializedObject"/>, so
    /// <c>_camelCase</c> private serialized fields work) only where the chosen SO type actually exposes
    /// them: <c>displayName</c> and <c>icon</c> always; <c>prefab</c> only if the type has such a field
    /// (e.g. <see cref="ItemDefinition"/> has no prefab field, so that assignment is skipped).
    /// </remarks>
    public class DataAssetGenerator : EditorWindow
    {
        /// <summary>Outcome counts from a generation run.</summary>
        public struct Result
        {
            /// <summary>Number of new assets created.</summary>
            public int Created;

            /// <summary>Number of existing assets loaded and updated.</summary>
            public int Updated;
        }

        private static readonly string[] DisplayNameFields = { "_displayName", "displayName", "_itemName", "itemName" };
        private static readonly string[] IconFields = { "_icon", "icon", "_itemIcon", "itemIcon" };
        private static readonly string[] PrefabFields = { "_prefab", "prefab" };

        private string _prefabFolder = "Assets/_Project/";
        private string _iconFolder = "Assets/_Project/";
        private string _outputFolder = "Assets/_Project/Data/";
        private List<Type> _soTypes;
        private string[] _soTypeNames;
        private int _selectedTypeIndex;

        [MenuItem("JamKit/Data Asset Generator")]
        private static void Open()
        {
            GetWindow<DataAssetGenerator>(true, "Data Asset Generator");
        }

        private void OnEnable()
        {
            _soTypes = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && typeof(ScriptableObject).IsAssignableFrom(t))
                .Where(t => t.Namespace == null || (!t.Namespace.StartsWith("UnityEditor") && !t.Namespace.StartsWith("UnityEngine")))
                .OrderBy(t => t.Name)
                .ToList();
            _soTypeNames = _soTypes.Select(t => t.FullName).ToArray();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            _prefabFolder = FolderField("Prefab folder", _prefabFolder);
            _iconFolder = FolderField("Icon folder", _iconFolder);
            _outputFolder = FolderField("Output folder", _outputFolder);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
            if (_soTypeNames == null || _soTypeNames.Length == 0)
            {
                EditorGUILayout.HelpBox("No ScriptableObject types found.", MessageType.Warning);
                return;
            }
            _selectedTypeIndex = EditorGUILayout.Popup("SO type", _selectedTypeIndex, _soTypeNames);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_prefabFolder) || string.IsNullOrEmpty(_outputFolder)))
            {
                if (GUILayout.Button("Generate", GUILayout.Height(28)))
                {
                    Type soType = _soTypes[Mathf.Clamp(_selectedTypeIndex, 0, _soTypes.Count - 1)];
                    Result result = Generate(_prefabFolder, _iconFolder, _outputFolder, soType);
                    ShowNotification(new GUIContent($"Created {result.Created}, updated {result.Updated}"));
                }
            }
        }

        private static string FolderField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            value = EditorGUILayout.TextField(label, value);
            if (GUILayout.Button("...", GUILayout.Width(28)))
            {
                string picked = EditorUtility.OpenFolderPanel(label, string.IsNullOrEmpty(value) ? Application.dataPath : value, string.Empty);
                string rel = ToProjectRelative(picked);
                if (!string.IsNullOrEmpty(rel))
                {
                    value = rel;
                }
            }
            EditorGUILayout.EndHorizontal();
            return value;
        }

        /// <summary>
        /// Generates/updates one <paramref name="soType"/> asset in <paramref name="outputFolder"/> per
        /// prefab found in <paramref name="prefabFolder"/>, setting its display name from the prefab name
        /// and its icon from <paramref name="iconFolder"/>/{name}.png when found.
        /// </summary>
        public static Result Generate(string prefabFolder, string iconFolder, string outputFolder, Type soType)
        {
            Result result = default;
            if (soType == null || !typeof(ScriptableObject).IsAssignableFrom(soType))
            {
                Debug.LogError("[JamKit DataAssetGenerator] Invalid target ScriptableObject type.");
                return result;
            }

            if (!AssetDatabase.IsValidFolder(prefabFolder))
            {
                Debug.LogError($"[JamKit DataAssetGenerator] Prefab folder not found: {prefabFolder}");
                return result;
            }

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                AssetDatabase.Refresh();
            }

            string normalizedOutput = outputFolder.TrimEnd('/');
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabFolder });

            // FindAssets is recursive, so two prefabs named the same in different sub-folders both map
            // to one "{name}_Data.asset". Detect those collisions up front and skip them rather than
            // silently letting the last one overwrite the first.
            HashSet<string> collidingBaseNames = FindCollidingBaseNames(prefabGuids);

            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    continue;
                }

                string baseName = prefab.name;
                if (collidingBaseNames.Contains(baseName))
                {
                    Debug.LogWarning($"[JamKit DataAssetGenerator] Skipping '{baseName}': multiple source prefabs share this base name and would all map to '{normalizedOutput}/{baseName}_Data.asset'. Rename one of them to disambiguate.");
                    continue;
                }

                string assetPath = $"{normalizedOutput}/{baseName}_Data.asset";

                // Guard against clobbering a foreign-typed asset already at this path: LoadAssetAtPath
                // with a type filter returns null for a mismatched type, and CreateAsset would then
                // replace the existing file. Inspect the main asset first and skip on incompatibility.
                UnityEngine.Object existingMain = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (existingMain != null && !soType.IsInstanceOfType(existingMain))
                {
                    Debug.LogError($"[JamKit DataAssetGenerator] Skipping '{baseName}': an asset already exists at '{assetPath}' of type {existingMain.GetType().Name}, expected {soType.Name}. The existing asset was left untouched.");
                    continue;
                }

                ScriptableObject asset = AssetDatabase.LoadAssetAtPath(assetPath, soType) as ScriptableObject;

                if (asset == null)
                {
                    asset = CreateInstance(soType);
                    AssetDatabase.CreateAsset(asset, assetPath);
                    result.Created++;
                }
                else
                {
                    result.Updated++;
                }

                SerializedObject so = new SerializedObject(asset);
                TrySetString(so, DisplayNameFields, baseName.Replace("_", " "));

                Sprite icon = LoadIcon(iconFolder, baseName);
                if (icon == null)
                {
                    Debug.LogWarning($"[JamKit DataAssetGenerator] No icon found for '{baseName}' in {iconFolder}");
                }
                else
                {
                    TrySetObject(so, IconFields, icon);
                }

                // prefab link only if the chosen type actually has such a field.
                TrySetObject(so, PrefabFields, prefab);

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[JamKit DataAssetGenerator] Done. Created {result.Created}, updated {result.Updated} ({soType.Name}).");
            return result;
        }

        // Returns the set of prefab base names that appear more than once across the discovered prefabs
        // (they would all resolve to the same "{name}_Data.asset" output path).
        private static HashSet<string> FindCollidingBaseNames(string[] prefabGuids)
        {
            HashSet<string> seen = new HashSet<string>();
            HashSet<string> colliding = new HashSet<string>();

            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    continue;
                }

                if (!seen.Add(prefab.name))
                {
                    colliding.Add(prefab.name);
                }
            }

            return colliding;
        }

        private static Sprite LoadIcon(string iconFolder, string baseName)
        {
            if (string.IsNullOrEmpty(iconFolder))
            {
                return null;
            }

            string folder = iconFolder.TrimEnd('/');
            return AssetDatabase.LoadAssetAtPath<Sprite>($"{folder}/{baseName}.png");
        }

        private static void TrySetString(SerializedObject so, string[] candidateNames, string value)
        {
            SerializedProperty prop = FindFirst(so, candidateNames);
            if (prop != null && prop.propertyType == SerializedPropertyType.String)
            {
                prop.stringValue = value;
            }
        }

        private static void TrySetObject(SerializedObject so, string[] candidateNames, UnityEngine.Object value)
        {
            SerializedProperty prop = FindFirst(so, candidateNames);
            if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference)
            {
                prop.objectReferenceValue = value;
            }
        }

        private static SerializedProperty FindFirst(SerializedObject so, string[] candidateNames)
        {
            foreach (string name in candidateNames)
            {
                SerializedProperty prop = so.FindProperty(name);
                if (prop != null)
                {
                    return prop;
                }
            }

            return null;
        }

        private static string ToProjectRelative(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return null;
            }

            string dataPath = Application.dataPath.Replace('\\', '/');
            string picked = absolutePath.Replace('\\', '/');
            if (picked == dataPath)
            {
                return "Assets";
            }

            return picked.StartsWith(dataPath + "/") ? "Assets" + picked.Substring(dataPath.Length) : null;
        }
    }
}
