using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JungleDice.Core.Event;

namespace JungleDice.Core
{
    public class GameManager : Singleton<GameManager>
    {
        public GameState CurrentState { get; private set; } = GameState.None;

        private static readonly Dictionary<GameState, HashSet<GameState>> _validTransitions = new()
        {
            { GameState.Boot,     new() { GameState.Login } },
            { GameState.Login,    new() { GameState.MainMenu } },
            { GameState.MainMenu, new() { GameState.InGame } },
            { GameState.InGame,   new() { GameState.Pause, GameState.GameOver } },
            { GameState.Pause,    new() { GameState.InGame, GameState.MainMenu } },
            { GameState.GameOver, new() { GameState.MainMenu, GameState.InGame } },
        };

        protected override void OnAwake()
        {
            StartCoroutine(BootSequence());
        }

        private IEnumerator BootSequence()
        {
            // 코어 시스템이 Awake 완료될 때까지 1프레임 대기
            yield return null;

            // SaveSystem에서 설정 로드
            // (SaveSystem 구현 후 연결)

            // 초기화 완료 → Boot 상태 진입
            // SceneLoader가 GameStateChanged 구독 후 Login 씬 로드 처리
            ChangeState(GameState.Boot);
        }

        public void ChangeState(GameState next)
        {
            if (CurrentState == next) return;

            if (!IsValidTransition(CurrentState, next))
            {
                Debug.LogWarning($"[GameManager] Invalid transition: {CurrentState} → {next}");
                return;
            }

            GameState previous = CurrentState;
            CurrentState = next;

            EventBus.Publish(new GameStateChanged(previous, next));
        }

        private bool IsValidTransition(GameState from, GameState to)
        {
            // None → 어디든 허용 (초기 Boot 진입)
            if (from == GameState.None) return true;
            return _validTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
        }

        void OnApplicationPause(bool paused)
        {
            EventBus.Publish(new AppPauseChanged(paused));
        }

        void OnApplicationFocus(bool hasFocus)
        {
            EventBus.Publish(new AppFocusChanged(hasFocus));
        }

        void OnApplicationQuit()
        {
            EventBus.Publish(new AppQuitRequested());
        }
    }
}
