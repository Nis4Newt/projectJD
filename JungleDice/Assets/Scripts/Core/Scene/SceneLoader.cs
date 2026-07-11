using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using JungleDice.Core.Event;

namespace JungleDice.Core.Scene
{
    public class SceneLoader : Singleton<SceneLoader>
    {
        public bool IsLoading { get; private set; }

        private static readonly Dictionary<GameState, string> _stateSceneMap = new()
        {
            { GameState.Boot,     "BootStrap" },
            { GameState.Login,    "Login" },
            { GameState.MainMenu, "MainMenu" },
            { GameState.InGame,   "InGame" },
        };

        protected override void OnAwake()
        {
            EventBus.Subscribe<GameStateChanged>(OnGameStateChanged);
        }

        private void OnGameStateChanged(GameStateChanged e)
        {
            if (!_stateSceneMap.TryGetValue(e.Next, out var sceneName))
                return; // 씬 전환이 필요 없는 상태 (Pause, GameOver 등)

            if (SceneManager.GetActiveScene().name == sceneName)
                return; // 이미 해당 씬

            LoadScene(sceneName);
        }

        public void LoadScene(string sceneName)
        {
            if (IsLoading)
            {
                Debug.LogWarning($"[SceneLoader] 로딩 중 중복 요청 무시: {sceneName}");
                return;
            }

            StartCoroutine(LoadSceneRoutine(sceneName));
        }

        private IEnumerator LoadSceneRoutine(string sceneName)
        {
            IsLoading = true;
            EventBus.Publish(new SceneLoadRequested(sceneName));

            var op = SceneManager.LoadSceneAsync(sceneName);

            try
            {
                yield return op;
            }
            finally
            {
                IsLoading = false;
            }

            EventBus.Publish(new SceneLoadCompleted(sceneName));
        }
    }
}
