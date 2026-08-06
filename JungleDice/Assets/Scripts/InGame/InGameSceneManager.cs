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

        [SerializeField] private FriendCard _friendCardPrefab;
        [SerializeField] private Friend _friendPrefab;
        [SerializeField] private Transform _deckOrigin;
        [SerializeField] private HandSlot[] _handSlots; // hand의 고정 슬롯 4개, 인덱스 0~3(왼쪽→오른쪽)
        [SerializeField] private Transform _dragLayer;
        [SerializeField] private float _drawInterval = 0.15f;
        [SerializeField] private float _drawDuration = 0.3f;
        [SerializeField] private float _compactDuration = 0.25f;

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
                    if (_currentOwner == TurnOwner.User) DrawHandCards();
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
                    CompactHand(); // hand를 앞으로 당겨 정리한 뒤 다음 단계로
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

        private void DrawHandCards()
        {
            var emptySlots = new List<HandSlot>();
            foreach (var slot in _handSlots)
                if (!slot.IsOccupied) emptySlots.Add(slot);

            int needed = Mathf.Min(emptySlots.Count, _userDeck.Count);
            if (needed <= 0) return;

            StartCoroutine(DrawHandCardsRoutine(emptySlots, needed));
        }

        private IEnumerator DrawHandCardsRoutine(List<HandSlot> emptySlots, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int key = _userDeck[0];
                _userDeck.RemoveAt(0); // 이미 셔플된 순서 그대로 앞에서부터 소비

                SpawnFriendCard(key, emptySlots[i]);

                yield return new WaitForSeconds(_drawInterval);
            }
        }

        private void SpawnFriendCard(int key, HandSlot slot)
        {
            var card = Instantiate(_friendCardPrefab, _dragLayer);
            card.transform.position = _deckOrigin.position; // 덱 오브젝트의 위치에서 생성
            card.SetKey(key);
            card.Initialize(_dragLayer);

            card.MoveToSlot(slot, _drawDuration); // 빈 슬롯 위치로 이동 후 도착하면 그 슬롯의 자식으로
        }

        // 유저가 "roll attacker"를 눌러 PlayFriend를 끝낼 때, hand의 빈 슬롯(드래그로 필드에 낸 카드 자리)을 앞으로 당겨 채운다.
        private void CompactHand()
        {
            var cards = new List<FriendCard>();
            foreach (var slot in _handSlots)
            {
                if (slot.IsOccupied)
                    cards.Add(slot.GetComponentInChildren<FriendCard>());
            }

            for (int i = 0; i < cards.Count; i++)
            {
                var targetSlot = _handSlots[i];
                var card = cards[i];

                if (card.HomeSlot == targetSlot) continue; // 이미 제자리

                card.MoveToSlot(targetSlot, _compactDuration);
            }
        }

        public void TryPlaceFriendCard(FieldSlot slot, FriendCard card)
        {
            if (slot.IsOccupied) return; // 이미 친구카드가 있다면 놓을 수 없음

            var friend = Instantiate(_friendPrefab, slot.transform);
            friend.SetKey(card.Key);

            card.NotifyPlaced();
            Destroy(card.gameObject);
        }

        protected override void OnDestroy()
        {
            _subs.Dispose();
            base.OnDestroy();
        }
    }
}
