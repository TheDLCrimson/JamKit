using System;
using System.IO;
using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Centralizes all persistent writes: typed PlayerPrefs helpers for trivial flags, and a generic JSON
    /// blob for structured data. Every Set writes through immediately (PlayerPrefs.Save() / File.WriteAllText)
    /// rather than deferring to OnApplicationQuit, so a crash or Alt-F4 cannot lose progress.
    /// </summary>
    public class SaveService : Singleton<SaveService>
    {
        public void Initialize() { }

        public void SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
        }

        public int GetInt(string key, int defaultValue = 0) => PlayerPrefs.GetInt(key, defaultValue);

        public void SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
        }

        public float GetFloat(string key, float defaultValue = 0f) => PlayerPrefs.GetFloat(key, defaultValue);

        public void SetString(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
        }

        public string GetString(string key, string defaultValue = "") => PlayerPrefs.GetString(key, defaultValue);

        public void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        public bool GetBool(string key, bool defaultValue = false) =>
            PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) != 0;

        /// <summary>
        /// Serializes <paramref name="data"/> with <see cref="JsonUtility"/> and writes it to
        /// <c>persistentDataPath/{fileName}.json</c>.
        /// </summary>
        /// <remarks>
        /// <see cref="JsonUtility"/> only serializes public or <c>[SerializeField]</c> fields of a
        /// <c>[Serializable]</c> type - not properties, not <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>,
        /// and not polymorphic references (<c>[SerializeReference]</c> base types serialize as their
        /// declared type, not the runtime type). It also cannot serialize a type at the document root
        /// that isn't a plain class/struct (no top-level primitives, arrays, or lists). Shape save data
        /// as a flat <c>[Serializable]</c> class with fields only; flatten dictionaries into a
        /// serializable list of key/value entries instead. Do not add a third-party JSON library to work
        /// around these gaps - the intended shapes (level progress, settings, simple counters) all fit
        /// within them.
        /// </remarks>
        public void SaveJson<T>(string fileName, T data)
        {
            string json = JsonUtility.ToJson(data);
            File.WriteAllText(GetJsonPath(fileName), json);
        }

        /// <summary>
        /// Reads and deserializes <c>persistentDataPath/{fileName}.json</c>, or returns
        /// <paramref name="defaultValue"/> if the file does not exist. See <see cref="SaveJson{T}"/>
        /// for the serialization limitations that apply to <typeparamref name="T"/>.
        /// </summary>
        public T LoadJson<T>(string fileName, T defaultValue)
        {
            string path = GetJsonPath(fileName);

            if (!File.Exists(path)) return defaultValue;

            try
            {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception ex)
            {
                // A truncated or malformed file (e.g. a crash mid-write) must not hard-crash load;
                // fall back to the caller's default so the game keeps running with fresh state.
                Debug.LogError($"SaveService.LoadJson: failed to parse '{path}', returning default. {ex.Message}");
                return defaultValue;
            }
        }

        private static string GetJsonPath(string fileName)
        {
            return Path.Combine(Application.persistentDataPath, fileName + ".json");
        }
    }
}
