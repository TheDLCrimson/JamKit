using NUnit.Framework;
using UnityEngine;

namespace JamKit.Tests
{
    public class GameStateTests
    {
        private GameObject _host;
        private GameState _gameState;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject(nameof(GameState));
            _gameState = _host.AddComponent<GameState>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            Time.timeScale = 1f;
        }

        [Test]
        public void PauseThenResume_FromPlaying_ReturnsToPlaying()
        {
            _gameState.SetPhase(GamePhase.Playing);

            _gameState.Pause();
            Assert.AreEqual(GamePhase.Paused, _gameState.CurrentPhase);

            _gameState.Resume();
            Assert.AreEqual(GamePhase.Playing, _gameState.CurrentPhase);
        }

        [Test]
        public void PauseThenResume_FromMenu_ReturnsToMenuNotPlaying()
        {
            _gameState.SetPhase(GamePhase.Menu);

            _gameState.Pause();
            Assert.AreEqual(GamePhase.Paused, _gameState.CurrentPhase);

            _gameState.Resume();
            Assert.AreEqual(GamePhase.Menu, _gameState.CurrentPhase,
                "Resume() must restore the phase active before Pause(), not hardcode Playing.");
        }

        [Test]
        public void Pause_WhenAlreadyPaused_IsIdempotentAndDoesNotOverwriteThePrePauseState()
        {
            _gameState.SetPhase(GamePhase.Playing);
            _gameState.Pause();
            _gameState.Pause(); // redundant call while already paused must not record Paused as "before pause"

            _gameState.Resume();

            Assert.AreEqual(GamePhase.Playing, _gameState.CurrentPhase);
        }

        [Test]
        public void Resume_WhenNotPaused_IsNoOp()
        {
            _gameState.SetPhase(GamePhase.Playing);

            _gameState.Resume();

            Assert.AreEqual(GamePhase.Playing, _gameState.CurrentPhase);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void Pause_SetsTimeScaleToZero_ResumeRestoresIt()
        {
            _gameState.SetPhase(GamePhase.Playing);

            _gameState.Pause();
            Assert.AreEqual(0f, Time.timeScale);

            _gameState.Resume();
            Assert.AreEqual(1f, Time.timeScale);
        }
    }
}
