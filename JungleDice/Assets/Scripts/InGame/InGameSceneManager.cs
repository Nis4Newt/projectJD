using System.Collections;
using System.Collections.Generic;
using JungleDice.Core;
using JungleDice.Core.Event;
using JungleDice.Core.User;
using JungleDice.Data.Table;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.InGame
{
    public enum TurnOwner
    {
        User,
        Computer,
    }

    public enum TurnPhase
    {
        PlayFriend,
        RollAttacker,
        RollTarget,
    }

    public class InGameSceneManager : SceneSingleton<InGameSceneManager>
    {
        [SerializeField] private Button _actionButton;
        [SerializeField] private TextMeshProUGUI _actionButtonText;

        private readonly CompositeDisposable _subs = new();

        private List<int> _userDeck;
        private List<int> _computerDeck;

        private TurnOwner _currentOwner;
        private TurnPhase _currentPhase;

        protected override void OnAwake()
        {
            _subs.Add(EventBus.Subscribe<GameStateChanged>(OnGameStateChanged));

            if (GameSession.CurrentGameType != GameType.Solo) return; // Battle 모드는 범위 밖

            SetupDecks();

            _actionButton.onClick.AddListener(OnActionButtonClicked);
            StartMatch();
        }

        private void OnGameStateChanged(GameStateChanged e)
        {
            // InGame ↔ Pause는 SceneLoader의 _stateSceneMap에 없어 씬 전환이 일어나지 않음
            // → 오버레이 표시/숨김은 이 씬 매니저가 전담
            if (e.Next == GameState.Pause)
            {
                // ShowPauseOverlay();
            }
            else if (e.Previous == GameState.Pause && e.Next == GameState.InGame)
            {
                // HidePauseOverlay();
            }
        }

        private void SetupDecks()
        {
            var stageFriends = StageTable.Instance.GetFriends(UserManager.Current.NextStage);

            _userDeck = DeckBuilder.Build(UserManager.Current.Friends);
            _computerDeck = DeckBuilder.Build(stageFriends);

            Debug.Log($"[InGame] 유저 덱: {string.Join(", ", _userDeck)}");
            Debug.Log($"[InGame] 컴퓨터 덱: {string.Join(", ", _computerDeck)}");
        }

        private void StartMatch()
        {
            _currentOwner = TurnOwner.User;
            EnterPhase(TurnPhase.PlayFriend);
        }

        private void EnterPhase(TurnPhase phase)
        {
            _currentPhase = phase;

            switch (phase)
            {
                case TurnPhase.PlayFriend:
                    Debug.Log($"[InGame] {_currentOwner} 턴 - 친구카드 플레이");
                    _actionButtonText.text = "roll attacker";
                    _actionButton.interactable = _currentOwner == TurnOwner.User;
                    break;

                case TurnPhase.RollAttacker:
                    Debug.Log($"[InGame] {_currentOwner} 턴 - 공격 주사위: {Random.Range(1, 7)}");
                    _actionButtonText.text = "roll target";
                    _actionButton.interactable = _currentOwner == TurnOwner.User;
                    break;

                case TurnPhase.RollTarget:
                    Debug.Log($"[InGame] {_currentOwner} 턴 - 타겟 주사위: {Random.Range(1, 7)}");
                    _actionButtonText.text = "상대 턴";
                    _actionButton.interactable = false;
                    StartCoroutine(SwitchTurnAfterDelay());
                    break;
            }

            if (_currentOwner == TurnOwner.Computer && phase != TurnPhase.RollTarget)
                StartCoroutine(ComputerAdvanceAfterDelay(phase));
        }

        private void OnActionButtonClicked()
        {
            if (_currentOwner != TurnOwner.User) return; // 컴퓨터 턴에는 버튼이 이미 비활성화되어 있지만 이중 방어

            switch (_currentPhase)
            {
                case TurnPhase.PlayFriend:
                    EnterPhase(TurnPhase.RollAttacker);
                    break;
                case TurnPhase.RollAttacker:
                    EnterPhase(TurnPhase.RollTarget);
                    break;
                // RollTarget 단계에서는 버튼이 비활성화되어 있어 호출되지 않음
            }
        }

        private IEnumerator ComputerAdvanceAfterDelay(TurnPhase enteredPhase)
        {
            yield return new WaitForSeconds(2f);

            if (_currentPhase != enteredPhase) yield break;

            EnterPhase(enteredPhase == TurnPhase.PlayFriend ? TurnPhase.RollAttacker : TurnPhase.RollTarget);
        }

        private IEnumerator SwitchTurnAfterDelay()
        {
            yield return new WaitForSeconds(2f);

            _currentOwner = _currentOwner == TurnOwner.User ? TurnOwner.Computer : TurnOwner.User;
            EnterPhase(TurnPhase.PlayFriend);
        }

        protected override void OnDestroy()
        {
            _subs.Dispose();
            base.OnDestroy();
        }
    }
}
