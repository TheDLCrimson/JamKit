using UnityEngine;
using UnityEngine.SceneManagement;

namespace JamKit
{
    /// <summary>Coarse phase of the running game, used for pause gating and debug display.</summary>
    public enum GamePhase
    {
        Menu,
        Playing,
        Paused
    }

    /// <summary>Fired by <see cref="GameState.Pause"/>.</summary>
    public struct GamePausedEvent : IGameEvent { }

    /// <summary>Fired by <see cref="GameState.Resume"/>.</summary>
    public struct GameResumedEvent : IGameEvent { }

    /// <summary>Fired whenever <see cref="GameState.CurrentPhase"/> changes.</summary>
    public struct GamePhaseChangedEvent : IGameEvent
    {
        public GamePhase Phase;
    }

    /// <summary>
    /// Pause/resume, current-phase tracking, and a restart helper. Replaces the audited projects'
    /// per-project GameManagers, each of which carried its own defect.
    /// </summary>
    public class GameState : Singleton<GameState>
    {
        public GamePhase CurrentPhase { get; private set; } = GamePhase.Menu;

        // The phase Pause() transitioned out of, so Resume() can restore it instead of assuming Playing.
        // Only written by Pause() itself, and only on the actual Playing/Menu -> Paused transition (the
        // early-return guard means a redundant Pause() call while already paused can never clobber this
        // with GamePhase.Paused).
        private GamePhase _phaseBeforePause = GamePhase.Playing;

        public void Initialize() { }

        public void Pause()
        {
            if (CurrentPhase == GamePhase.Paused) return;

            _phaseBeforePause = CurrentPhase;
            Time.timeScale = 0f;
            SetPhase(GamePhase.Paused);
            EventBus.Fire(new GamePausedEvent());
        }

        public void Resume()
        {
            if (CurrentPhase != GamePhase.Paused) return;

            Time.timeScale = 1f;
            SetPhase(_phaseBeforePause);
            EventBus.Fire(new GameResumedEvent());
        }

        public void SetPhase(GamePhase phase)
        {
            CurrentPhase = phase;
            EventBus.Fire(new GamePhaseChangedEvent { Phase = phase });
        }

        /// <summary>Resets time scale and reloads the active scene through <see cref="SceneLoader"/>.</summary>
        public void Restart()
        {
            Time.timeScale = 1f;
            SceneLoader.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}
