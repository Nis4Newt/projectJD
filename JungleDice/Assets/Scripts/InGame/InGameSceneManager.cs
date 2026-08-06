using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
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

        [SerializeField] private FieldSlot[] _fieldSlots; // 필드 6칸, 배열 인덱스 0~5 = 절대 번호 1~6 (1/2/3 컴퓨터, 4/5/6 유저)
        [SerializeField] private BaseStone _userBase;
        [SerializeField] private BaseStone _computerBase;
        [SerializeField] private float _selectPunchScale = 0.05f;
        [SerializeField] private float _selectPunchDuration = 0.2f;
        [SerializeField] private float _attackerPunchScale = 0.15f;
        [SerializeField] private float _attackerPunchDuration = 1f;
        [SerializeField] private float _moveToTargetDuration = 0.3f;
        [SerializeField] private float _moveBackDuration = 0.3f;

        private readonly CompositeDisposable _subs = new();

        private List<int> _userDeck;
        private List<int> _computerDeck;

        private TurnOwner _currentOwner;
        private TurnPhase _currentPhase;
        private FieldSlot _attackerSlot; // 이번 턴에 뽑힌 공격자 슬롯, 비어있으면 null

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
                {
                    int attackerRoll = Random.Range(1, 7);
                    Debug.Log($"[InGame] {_currentOwner} 턴 - 공격 주사위: {attackerRoll}");

                    var attackerSlot = GetFieldSlot(attackerRoll);
                    _attackerSlot = attackerSlot.IsOccupied ? attackerSlot : null;

                    if (_attackerSlot == null)
                    {
                        // 공격자가 없으면 RollTarget으로 넘어가지 않고 곧바로 턴 종료
                        Debug.Log($"[InGame] {_currentOwner} 턴 - 공격자 없음, 턴 종료");
                        _actionButtonText.text = "상대 턴";
                        _actionButton.interactable = false;
                        StartCoroutine(SwitchTurnAfterDelay());
                        return; // 아래의 컴퓨터 자동 진행도 걸지 않음 — 이미 턴 종료 코루틴을 시작함
                    }

                    var attacker = _attackerSlot.GetComponentInChildren<Friend>();
                    attacker.SetHighlight(true, Color.red);
                    attacker.PunchScale(_selectPunchScale, _selectPunchDuration);

                    _actionButtonText.text = "roll target";
                    _actionButton.interactable = _currentOwner == TurnOwner.User;
                    break;
                }

                case TurnPhase.RollTarget:
                {
                    int targetRoll = Random.Range(1, 7);
                    Debug.Log($"[InGame] {_currentOwner} 턴 - 타겟 주사위: {targetRoll}");

                    _actionButtonText.text = "상대 턴";
                    _actionButton.interactable = false;
                    StartCoroutine(ResolveAttackRoutine(GetFieldSlot(targetRoll)));
                    break;
                }
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

        private FieldSlot GetFieldSlot(int rollValue) => _fieldSlots[rollValue - 1];

        private BaseStone GetBase(int slotIndex) => slotIndex <= 3 ? _computerBase : _userBase;

        // RollAttacker에서 공격자가 없으면 이 코루틴 자체가 시작되지 않으므로, _attackerSlot은 항상 점유된 슬롯이다.
        private IEnumerator ResolveAttackRoutine(FieldSlot targetSlot)
        {
            var attacker = _attackerSlot.GetComponentInChildren<Friend>();
            var targetFriend = targetSlot.IsOccupied ? targetSlot.GetComponentInChildren<Friend>() : null;

            if (targetFriend != null)
            {
                targetFriend.SetHighlight(true, Color.blue);
                targetFriend.PunchScale(_selectPunchScale, _selectPunchDuration);
                yield return new WaitForSeconds(_selectPunchDuration);
            }

            attacker.PunchScale(_attackerPunchScale, _attackerPunchDuration);
            yield return new WaitForSeconds(_attackerPunchDuration);

            Vector3 originalPosition = attacker.transform.position;
            Vector3 targetPosition = targetFriend != null
                ? targetFriend.transform.position
                : GetBase(targetSlot.Index).transform.position;

            attacker.MoveTo(targetPosition, _moveToTargetDuration, Ease.InQuad); // 서서히 → 빠르게
            yield return new WaitForSeconds(_moveToTargetDuration);

            // 타격음, 타격 이펙트 재생 지점

            if (targetFriend == null)
            {
                int damage = CardTable.Instance.GetAtt(attacker.Key);
                GetBase(targetSlot.Index).TakeDamage(damage);
            }
            // targetFriend != null인 경우 카드 대 카드 피해 판정은 범위 밖 — 연출만 재생

            attacker.MoveTo(originalPosition, _moveBackDuration, Ease.Linear); // 등속 복귀
            yield return new WaitForSeconds(_moveBackDuration);

            attacker.SetHighlight(false, Color.clear);
            if (targetFriend != null) targetFriend.SetHighlight(false, Color.clear);

            if (targetFriend == null && GetBase(targetSlot.Index).CurrentHp <= 0)
            {
                Debug.Log($"[InGame] {(targetSlot.Index <= 3 ? "Computer" : "User")} 본체 파괴 — 패배");
                GameManager.Instance.ChangeState(GameState.GameOver);
                yield break; // 턴 교대 없이 종료
            }

            yield return SwitchTurnAfterDelay();
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
