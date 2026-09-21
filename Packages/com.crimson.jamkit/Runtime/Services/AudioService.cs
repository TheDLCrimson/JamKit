using PrimeTween;
using UnityEngine;
using UnityEngine.Audio;

namespace JamKit
{
    /// <summary>
    /// SFX playback over a small pooled <see cref="AudioSource"/> ring, music crossfade over two
    /// dedicated <see cref="AudioSource"/>s, and mixer volume get/set persisted via <see cref="SaveService"/>.
    /// </summary>
    /// <remarks>
    /// The SFX ring and the two music sources are separate concerns and never share an AudioSource: SFX
    /// are short one-shots that can overlap freely, music is a single long-running crossfaded loop. Mixing
    /// the two onto shared sources would let an SFX cut off a crossfade or vice versa.
    /// </remarks>
    public class AudioService : Singleton<AudioService>
    {
        private const string PrefsKeyMusicVolume = "JamKit.AudioService.MusicVolume";
        private const string PrefsKeySfxVolume = "JamKit.AudioService.SfxVolume";
        private const float MusicCrossfadeSeconds = 1f;
        private const float MinDecibel = -80f;

        [SerializeField] private AudioMixer _mixer;
        [SerializeField] private string _musicVolumeParam = "MusicVolume";
        [SerializeField] private string _sfxVolumeParam = "SfxVolume";

        [Tooltip("Pooled ring of sources for PlaySfx. Never used for music.")]
        [SerializeField] private AudioSource[] _sfxSources;

        [Tooltip("Two dedicated sources for PlayMusic's A/B crossfade. Never used for SFX.")]
        [SerializeField] private AudioSource _musicSourceA;
        [SerializeField] private AudioSource _musicSourceB;

        private int _nextSfxIndex;
        private AudioSource _activeMusicSource;

        public void Initialize()
        {
            _activeMusicSource = _musicSourceA;

            if (_mixer == null)
            {
                Debug.LogError("AudioService.Initialize: _mixer is not assigned. Assign an AudioMixer asset on " +
                                "the AudioService component (Bootstrap prefab) - music/SFX volume control will not " +
                                "function until this is fixed. Volume preferences still persist via SaveService.");
            }

            // Never throw here: Bootstrap.Awake() runs service Initialize() calls in a fixed order with no
            // isolation between them, so an unguarded NullReferenceException on a missing _mixer would
            // silently abort GameState/UIScreenEffects initialization too. SetMusicVolume/SetSfxVolume are
            // themselves null-safe against _mixer, so this proceeds normally either way.
            SetMusicVolume(SaveService.Instance.GetFloat(PrefsKeyMusicVolume, 1f));
            SetSfxVolume(SaveService.Instance.GetFloat(PrefsKeySfxVolume, 1f));
        }

        /// <summary>Plays a one-shot clip on the next source in the SFX ring.</summary>
        public void PlaySfx(AudioClip clip, float volume = 1f)
        {
            if (clip == null || _sfxSources == null || _sfxSources.Length == 0) return;

            AudioSource source = _sfxSources[_nextSfxIndex];
            _nextSfxIndex = (_nextSfxIndex + 1) % _sfxSources.Length;
            source.PlayOneShot(clip, volume);
        }

        /// <summary>Crossfades from the current music source to <paramref name="clip"/> over the other one.</summary>
        /// <remarks>
        /// Any in-flight volume tweens on both dedicated music sources are cancelled before the new
        /// crossfade starts, so rapid consecutive calls cannot leave competing tweens on one source or let
        /// a stale fade-out <c>OnComplete</c> stop the newly active source.
        /// </remarks>
        public void PlayMusic(AudioClip clip, bool loop = true)
        {
            if (clip == null) return;

            AudioSource outgoing = _activeMusicSource;
            AudioSource incoming = outgoing == _musicSourceA ? _musicSourceB : _musicSourceA;

            // Cancel prior crossfade tweens (and their fade-out OnComplete stops) on both sources first.
            Tween.StopAll(_musicSourceA);
            Tween.StopAll(_musicSourceB);

            incoming.clip = clip;
            incoming.loop = loop;
            incoming.volume = 0f;
            incoming.Play();
            Tween.AudioVolume(incoming, 1f, MusicCrossfadeSeconds);

            if (outgoing != null && outgoing != incoming && outgoing.isPlaying)
            {
                Tween.AudioVolume(outgoing, 0f, MusicCrossfadeSeconds).OnComplete(outgoing, target => target.Stop());
            }

            _activeMusicSource = incoming;
        }

        public void SetMusicVolume(float linear01)
        {
            linear01 = Mathf.Clamp01(linear01);
            if (_mixer != null) _mixer.SetFloat(_musicVolumeParam, LinearToDecibel(linear01));
            SaveService.Instance.SetFloat(PrefsKeyMusicVolume, linear01);
        }

        public float GetMusicVolume() => SaveService.Instance.GetFloat(PrefsKeyMusicVolume, 1f);

        public void SetSfxVolume(float linear01)
        {
            linear01 = Mathf.Clamp01(linear01);
            if (_mixer != null) _mixer.SetFloat(_sfxVolumeParam, LinearToDecibel(linear01));
            SaveService.Instance.SetFloat(PrefsKeySfxVolume, linear01);
        }

        public float GetSfxVolume() => SaveService.Instance.GetFloat(PrefsKeySfxVolume, 1f);

        private static float LinearToDecibel(float linear01)
        {
            return linear01 <= 0.0001f ? MinDecibel : Mathf.Log10(linear01) * 20f;
        }
    }
}
