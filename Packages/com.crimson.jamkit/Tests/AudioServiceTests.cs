using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace JamKit.Tests
{
    public class AudioServiceTests
    {
        private const string MusicVolumeKey = "JamKit.AudioService.MusicVolume";
        private const string SfxVolumeKey = "JamKit.AudioService.SfxVolume";

        private GameObject _saveHost;
        private GameObject _audioHost;
        private SaveService _saveService;
        private AudioService _audioService;

        [SetUp]
        public void SetUp()
        {
            ResetSingletonInstance<SaveService>();
            ResetSingletonInstance<AudioService>();

            _saveHost = new GameObject(nameof(SaveService));
            _saveService = _saveHost.AddComponent<SaveService>();
            InvokeAwake(_saveService); // see remarks below - AudioService.Initialize() reads SaveService.Instance

            _audioHost = new GameObject(nameof(AudioService));
            _audioService = _audioHost.AddComponent<AudioService>(); // _mixer left unassigned on purpose
            InvokeAwake(_audioService);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_audioHost);
            Object.DestroyImmediate(_saveHost);
            PlayerPrefs.DeleteKey(MusicVolumeKey);
            PlayerPrefs.DeleteKey(SfxVolumeKey);

            ResetSingletonInstance<SaveService>();
            ResetSingletonInstance<AudioService>();
        }

        // AddComponent's automatic Awake dispatch is not reliably synchronous in this EditMode test
        // environment (M6 friction log Issue #15) - confirmed here to extend beyond Awake side effects to
        // FindObjectsByType-based Singleton<T>.Instance resolution too: AudioService.Initialize() reading
        // SaveService.Instance immediately after SetUp created it intermittently NRE'd because Instance's
        // FindFirstObjectByType fallback didn't see the just-created object yet. Awake is invoked explicitly
        // for determinism; Singleton<T>.Awake is idempotent-safe to call once per instance.
        private static void InvokeAwake<T>(T component) where T : MonoBehaviour
        {
            MethodInfo method = typeof(Singleton<T>).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(component, null);
        }

        private static void ResetSingletonInstance<T>() where T : MonoBehaviour
        {
            FieldInfo field = typeof(Singleton<T>).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            field.SetValue(null, null);
        }

        [Test]
        public void Initialize_WithUnassignedMixer_LogsDiagnosticErrorAndDoesNotThrow()
        {
            LogAssert.Expect(LogType.Error, new Regex("_mixer is not assigned"));

            Assert.DoesNotThrow(() => _audioService.Initialize());
        }

        [Test]
        public void Initialize_WithUnassignedMixer_StillPersistsVolumePreferences()
        {
            LogAssert.Expect(LogType.Error, new Regex("_mixer is not assigned"));
            _audioService.Initialize();

            _audioService.SetMusicVolume(0.4f);
            _audioService.SetSfxVolume(0.7f);

            Assert.AreEqual(0.4f, _audioService.GetMusicVolume(), 0.0001f);
            Assert.AreEqual(0.7f, _audioService.GetSfxVolume(), 0.0001f);
        }
    }
}
