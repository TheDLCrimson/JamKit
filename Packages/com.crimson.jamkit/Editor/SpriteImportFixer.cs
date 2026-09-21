using UnityEditor;
using UnityEngine;

namespace JamKit.Editor
{
    /// <summary>
    /// Batch-forces every texture in a chosen folder to import as a single Sprite - the settings icons
    /// need but rarely have on import. Only reimports textures that actually changed.
    /// </summary>
    /// <remarks>
    /// Generalized from FurnishMyHome/CarGoesAround's identical hardcoded-path fixers (the duplication
    /// across two donor projects is the proof of demand): the target folder is picked at run time instead
    /// of being hardcoded.
    /// </remarks>
    public static class SpriteImportFixer
    {
        [MenuItem("JamKit/Fix Sprite Import Settings")]
        private static void FixViaPicker()
        {
            string absolute = EditorUtility.OpenFolderPanel("Select icon folder", Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(absolute))
            {
                return;
            }

            string relative = ToProjectRelative(absolute);
            if (relative == null)
            {
                Debug.LogError("[JamKit SpriteImportFixer] Folder must be inside this project's Assets/.");
                return;
            }

            int fixedCount = FixFolder(relative);
            Debug.Log($"[JamKit SpriteImportFixer] Done. Updated {fixedCount} texture(s) to Sprite in {relative}.");
        }

        /// <summary>
        /// Forces every Texture2D under <paramref name="folder"/> to <see cref="TextureImporterType.Sprite"/>
        /// / <see cref="SpriteImportMode.Single"/>, reimporting only those that changed. Returns the count changed.
        /// </summary>
        public static int FixFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogError($"[JamKit SpriteImportFixer] Folder not found: {folder}");
                return 0;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            int fixedCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                {
                    continue;
                }

                bool dirty = false;

                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    dirty = true;
                }

                if (importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    dirty = true;
                }

                if (dirty)
                {
                    importer.SaveAndReimport();
                    fixedCount++;
                }
            }

            return fixedCount;
        }

        private static string ToProjectRelative(string absolutePath)
        {
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
