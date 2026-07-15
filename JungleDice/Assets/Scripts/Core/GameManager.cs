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
            { GameState.Logo,     new() { GameState.Login } },
            { GameState.Login,    new() { GameState.MainMenu } },
            { GameState.MainMenu, new() { GameState.InGame } },
            { GameState.InGame,   new() { GameState.Pause, GameState.GameOver } },
            { GameState.Pause,    new() { GameState.InGame, GameState.MainMenu } },
            { GameState.GameOver, new() { GameState.MainMenu, GameState.InGame } },
        };

        protected override void OnAwake()
        {
            EventBus.Subscribe<LogoSceneReady>(_ => ChangeState(GameState.Login));
            EventBus.Subscribe<LoginSceneReady>(_ => ChangeState(GameState.MainMenu));
            StartCoroutine(LogoSequence());
        }

        private IEnumerator LogoSequence()
        {
            // 코어 시스템이 Awake 완료될 때까지 1프레임 대기
            yield return null;

            // SaveSystem에서 설정 로드
            // (SaveSystem 구현 후 연결)

            // 초기화 완료 → Logo 상태 진입
            // Logo → Login 전이는 LogoSceneManager의 LogoSceneReady 수신 시 처리
            ChangeState(GameState.Logo);
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
            // None → 어디든 허용 (초기 Logo 진입)
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
