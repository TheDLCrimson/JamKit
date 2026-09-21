using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JamKit
{
    /// <summary>
    /// Async scene loading with fade hook points and a guaranteed <see cref="EventBus.Clear"/> so static
    /// subscribers from the outgoing scene never leak into the next one.
    /// </summary>
    /// <remarks>
    /// Every <see cref="LoadScene"/> call runs this fixed lifecycle: <c>Time.timeScale</c> is reset to 1
    /// (every scene begins unpaused); then, if a <see cref="BeforeSceneLoad"/> cover hook is set, the screen
    /// is covered to completion before the load starts; then <see cref="EventBus.Clear"/>, then
    /// <c>SceneManager.LoadSceneAsync</c>, then <see cref="AfterSceneLoad"/> once the new scene has finished
    /// loading. M3's UIScreenEffects owns <see cref="BeforeSceneLoad"/> (fade to opaque) and subscribes to
    /// <see cref="AfterSceneLoad"/> (fade back in); SceneLoader itself has no dependency on UI.
    /// <para/>
    /// <b>Fade is completion-aware.</b> <see cref="BeforeSceneLoad"/> is a single-owner delegate, not a plain
    /// event, precisely so the transition can wait for the screen to be fully covered before the scene
    /// activates. The owner receives a continuation and must invoke it once the cover (fade-to-opaque) is
    /// complete; SceneLoader only then begins the async load, so the brief load and activation happen under
    /// full cover and the hard cut is never visible. When <see cref="BeforeSceneLoad"/> is <c>null</c> the
    /// load proceeds immediately (a hard cut). The <i>in</i> direction (<see cref="AfterSceneLoad"/>) stays a
    /// plain multicast event because fading in after activation needs no gating.
    /// <para/>
    /// A <see cref="LoadScene"/> call made while a transition is already in progress is rejected (logged,
    /// no-op) rather than queued - overlapping scene loads indicate a bug at the call site, and a silent
    /// queue would hide it. An invalid or unregistered scene name is rejected up front (via
    /// <c>Application.CanStreamedLevelBeLoaded</c>) before any transition side effect runs, so a mistyped
    /// scene neither covers/fades the still-live current scene nor blocks later valid loads. The
    /// null-<c>AsyncOperation</c> guard and try/catch remain as defense in depth so the transition flag can
    /// never stay stuck.
    /// <para/>
    /// The static transition flag and the two hooks are reset at
    /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/> so a stale value cannot survive a
    /// Play session when Enter Play Mode has "Reload Domain" disabled.
    /// </remarks>
    public static class SceneLoader
    {
        /// <summary>
        /// Optional single-owner cover hook (owned by UIScreenEffects). When set, SceneLoader invokes it with
        /// a continuation that the owner MUST call once the screen is fully covered (fade-to-opaque complete);
        /// the load then begins under full cover. When null, the load proceeds immediately (hard cut). This is
        /// a delegate rather than a multicast event because completion-awareness needs exactly one owner and a
        /// continuation, which an event cannot express.
        /// </summary>
        public static Action<Action> BeforeSceneLoad;

        /// <summary>Fired after the new scene has finished loading (UIScreenEffects fades back in on this).</summary>
        public static event Action AfterSceneLoad;

        private static bool _isTransitioning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _isTransitioning = false;
            BeforeSceneLoad = null;
            AfterSceneLoad = null;
        }

        public static void LoadScene(string sceneName)
        {
            if (_isTransitioning)
            {
                Debug.LogWarning($"SceneLoader.LoadScene(\"{sceneName}\") rejected - a transition is already in progress.");
                return;
            }

            // Reject an invalid/unregistered scene before running ANY transition side effect, so a mistyped
            // name does not cover/fade (or clear the EventBus of) the current scene that is staying put.
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"SceneLoader.LoadScene(\"{sceneName}\") aborted - the scene is not in the build list. No transition ran.");
                return;
            }

            _isTransitioning = true;
            Time.timeScale = 1f;

            // Cover the screen to completion first (if an owner is set), then begin the actual load so the
            // fade reaches opaque before the scene activates. No cover owner => immediate hard-cut load.
            if (BeforeSceneLoad != null)
            {
                BeforeSceneLoad(() => BeginLoad(sceneName));
            }
            else
            {
                BeginLoad(sceneName);
            }
        }

        private static void BeginLoad(string sceneName)
        {
            try
            {
                EventBus.Clear();

                AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
                if (operation == null)
                {
                    // Defense in depth: CanStreamedLevelBeLoaded already screened the name, but never leave
                    // the loader permanently busy if the load still fails to start.
                    _isTransitioning = false;
                    Debug.LogError($"SceneLoader.LoadScene(\"{sceneName}\") failed to start unexpectedly.");
                    return;
                }

                operation.completed += _ =>
                {
                    _isTransitioning = false;
                    AfterSceneLoad?.Invoke();
                };
            }
            catch (Exception)
            {
                // Never leave the loader permanently busy if starting the transition threw.
                _isTransitioning = false;
                throw;
            }
        }
    }
}
