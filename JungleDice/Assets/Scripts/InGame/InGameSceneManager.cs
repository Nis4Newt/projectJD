using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using JungleDice.Core;
using JungleDice.Core.Event;
using JungleDice.Core.Settings;
using JungleDice.Core.UI;
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
        [SerializeField] private Transform _attackLayer; // 공격 연출 도중 attacker가 다른 슬롯/카드 위로 그려지도록 임시로 옮겨가는 레이어(_dragLayer와 같은 역할)
        [SerializeField] private BaseStone _userBase;
        [SerializeField] private BaseStone _computerBase;
        [SerializeField] private float _selectPunchScale = 0.05f;
        [SerializeField] private float _selectPunchDuration = 0.2f;
        [SerializeField] private float _attackerPunchScale = 0.15f;
        [SerializeField] private float _attackerPunchDuration = 1f;
        [SerializeField] private float _moveToTargetDuration = 0.3f;
        [SerializeField] private float _moveBackDuration = 0.3f;
        [SerializeField] private float _mergePunchScale = 0.1f;
        [SerializeField] private float _mergePunchDuration = 0.25f;

        [SerializeField] private ResultPanel _resultPanel;

        [SerializeField] private Button _settingsButton;
        [SerializeField] private Transform _canvasTransform;

        private OptionPanel _optionPanel;
        private readonly CompositeDisposable _subs = new();

        private List<int> _userDeck;
        private List<int> _computerDeck;
        private int _computerInitialDeckSize;

        private const int ComputerHandSize = 4;
        private const float ComputerActionInterval = 0.5f; // RunComputerTurnRoutine이 카드를 한 장씩 낼 때마다 두는 텀
        private readonly List<int> _computerHand = new(); // UI 없음, PlayFriend 진입마다 ComputerHandSize까지 채움

        private TurnOwner _currentOwner;
        private TurnPhase _currentPhase;
        private FieldSlot _attackerSlot; // 이번 턴에 뽑힌 공격자 슬롯, 비어있으면 null
        private bool _userWon; // GameOver 직전에 세팅, OnGameStateChanged(GameOver)에서 읽음

        private readonly List<int> _userGraveyard = new();
        private readonly List<int> _computerGraveyard = new();

        public bool CanPlayFriend => _currentOwner == TurnOwner.User && _currentPhase == TurnPhase.PlayFriend;

        protected override void OnAwake()
        {
            _subs.Add(EventBus.Subscribe<GameStateChanged>(OnGameStateChanged));

            if (GameSession.CurrentGameType != GameType.Solo) return; // Battle 모드는 범위 밖

            SetupDecks();

            _optionPanel = UIManager.Load<OptionPanel>(_canvasTransform, p => p.Configure(OptionPanelMode.InGame));

            _actionButton.onClick.AddListener(OnActionButtonClicked);
            _settingsButton.onClick.AddListener(_optionPanel.Show);
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
            else if (e.Next == GameState.GameOver)
            {
                _settingsButton.interactable = false;
                _resultPanel.ShowResult(_userWon);
            }
        }

        private void SetupDecks()
        {
            var stageFriends = StageTable.Instance.GetFriends(UserManager.Current.NextStage);

            _userDeck = DeckBuilder.Build(UserManager.Current.Friends);
            _computerDeck = DeckBuilder.Build(stageFriends);
            _computerInitialDeckSize = _computerDeck.Count; // ComputerAI의 "예상 잔여 턴수 가중치" 정규화 기준

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
                    if (_currentOwner == TurnOwner.User)
                    {
                        DrawHandCards();
                        _resultPanel.PlayMyTurnAlert();
                    }
                    else
                    {
                        RefillComputerHand();
                        StartCoroutine(RunComputerTurnRoutine());
                    }
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

            // PlayFriend는 RunComputerTurnRoutine이 카드를 다 낸 뒤 스스로 ComputerAdvanceAfterDelay를 시작한다(행동 수에 따라 소요 시간이 달라지므로 여기서 고정 지연으로 겹쳐 걸면 안 됨)
            if (_currentOwner == TurnOwner.Computer && phase == TurnPhase.RollAttacker)
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

        // slotIndex(필드 절대 번호 1~6)로 소유 진영을 판정해 그 진영의 무덤에 key를 저장한다 — GetBase와 동일한 기준(1~3 컴퓨터, 4~6 유저)
        private void AddToGraveyard(int slotIndex, int key)
        {
            var graveyard = slotIndex <= 3 ? _computerGraveyard : _userGraveyard;
            graveyard.Add(key);
        }

        public IReadOnlyList<int> GetGraveyard(TurnOwner owner) => owner == TurnOwner.User ? _userGraveyard : _computerGraveyard;

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

            attacker.SetParent(_attackLayer); // 공격 연출 도중 다른 슬롯/카드 위로 그려지도록 임시로 옮김
            attacker.MoveTo(targetPosition, _moveToTargetDuration, Ease.InQuad); // 서서히 → 빠르게
            yield return new WaitForSeconds(_moveToTargetDuration);

            // 타격음, 타격 이펙트 재생 지점

            bool attackerDied = false;
            bool targetDied = false;

            if (targetFriend != null)
            {
                if (targetFriend == attacker)
                {
                    // 공격 주사위/타겟 주사위가 같은 슬롯을 가리켜 attacker와 target이 동일한 카드인 경우 — 서로 공격하는 대신 공격력이 2배로 오르는 어드밴티지
                    attacker.DoubleAtt();
                }
                else
                {
                    // 친구카드끼리는 무조건 쌍방 피해 — attacker는 target의 공격력만큼, target은 attacker의 공격력만큼
                    targetFriend.TakeDamage(attacker.Att);
                    attacker.TakeDamage(targetFriend.Att);
                }

                targetDied = targetFriend.IsDead;
                attackerDied = attacker.IsDead;
            }
            else
            {
                GetBase(targetSlot.Index).TakeDamage(attacker.Att); // base의 공격력은 0으로 고정 — 되돌아오는 피해 없음
            }

            // 죽더라도 공격자가 제자리로 돌아오는 연출까지는 재생 — 그 뒤에 죽은 쪽을 한번에 제거
            attacker.MoveTo(originalPosition, _moveBackDuration, Ease.Linear); // 등속 복귀
            yield return new WaitForSeconds(_moveBackDuration);

            if (attackerDied)
            {
                bool revived = TryHandleDeath(attacker, _attackerSlot.transform);
                if (revived)
                {
                    attacker.SetParent(_attackerSlot.transform);
                    attacker.SetHighlight(false, Color.clear);
                }
            }
            else
            {
                attacker.SetParent(_attackerSlot.transform); // 공격 레이어에서 원래 슬롯으로 복귀
                attacker.SetHighlight(false, Color.clear);
            }

            if (targetFriend != null)
            {
                if (targetDied)
                {
                    bool revived = TryHandleDeath(targetFriend, targetSlot.transform);
                    if (revived) targetFriend.SetHighlight(false, Color.clear);
                }
                else
                {
                    targetFriend.SetHighlight(false, Color.clear);
                }
            }

            if (targetFriend == null && GetBase(targetSlot.Index).CurrentHp <= 0)
            {
                _userWon = targetSlot.Index <= 3; // 컴퓨터(1~3) 본체가 파괴되면 유저 승리
                Debug.Log($"[InGame] {(targetSlot.Index <= 3 ? "Computer" : "User")} 본체 파괴 — {(_userWon ? "승리" : "패배")}");
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
            if (slot.IsOccupied)
            {
                var existing = slot.GetComponentInChildren<Friend>();
                if (!CanMerge(existing, card.Key)) return; // 병합 불가 — 배치 거부, OnEndDrag가 원래 슬롯으로 복귀시킴

                MergeCardIntoSlot(existing, card.Key, slot.Index);

                card.NotifyPlaced();
                Destroy(card.gameObject);
                return;
            }

            var friend = Instantiate(_friendPrefab, slot.transform);
            friend.SetKey(card.Key);

            card.NotifyPlaced();
            Destroy(card.gameObject);
        }

        // 병합 가능 판정 자체는 ComputerAI.CanMerge(key 기반, Unity 비의존)에 위임한다 — 판정 로직은 하나만 존재해야
        // 컴퓨터 AI의 후보 탐색과 유저 드래그 배치가 항상 같은 결과를 보장한다.
        private bool CanMerge(Friend existing, int mergeKey) => ComputerAI.CanMerge(existing.Key, mergeKey);

        // existing에 mergeKey 카드의 기본 스탯을 합산 + 연출 + 발동 효과. 호출 전 CanMerge로 이미 통과된 조합이라고 가정한다.
        // slotIndex는 existing이 실제로 놓인 필드 절대 번호(1~6) — "내 필드"/"상대 필드"를 이 위치 기준으로 판정한다(고정된 유저=Ally 아님, 치트로 컴퓨터 필드에서 병합해도 정확히 동작해야 함)
        private void MergeCardIntoSlot(Friend existing, int mergeKey, int slotIndex)
        {
            var existingData = CardTable.Instance.Get(existing.Key);
            var data = CardTable.Instance.Get(mergeKey);

            int addAtt = data.att;
            int addHp = data.hp;
            if (existingData.EffectClauses.Any(c => c.Keyword == "MultiplierMerge"))
            {
                var (ownStart, ownEnd) = OwnFieldRange(slotIndex);
                int sameCount = GetFieldFriends(ownStart, ownEnd).Count(f => f.Key == existing.Key);
                addAtt *= sameCount;
                addHp *= sameCount;
            }

            existing.MergeWith(addAtt, addHp);
            existing.PunchScale(_mergePunchScale, _mergePunchDuration);
            TriggerMergeAbility(existing, slotIndex);
        }

        // 컴퓨터 손패를 ComputerHandSize까지 채운다 — 유저의 DrawHandCardsRoutine과 동일하게 덱 앞에서부터 소비(이미 셔플됨), 화면에 그리지 않으므로 연출 없이 즉시 채움
        private void RefillComputerHand()
        {
            while (_computerHand.Count < ComputerHandSize && _computerDeck.Count > 0)
            {
                _computerHand.Add(_computerDeck[0]);
                _computerDeck.RemoveAt(0);
            }
        }

        // 컴퓨터 턴 한 번에 손패 전체(0~ComputerHandSize장)를 순서대로 낸다.
        // UrgencyState(위급 상태)는 턴 시작 시 한 번만 확인해 고정하고, 카드를 낼 때마다 달라지는 필드/손패는
        // BuildObservation()으로 매번 새로 스냅샷을 떠 DecideNextAction에 넘긴다 — 더 이상 낼 카드가 없으면 종료.
        // 행동 사이마다 ComputerActionInterval만큼 텀을 둬 카드가 한 장씩 나오는 게 보이게 하고,
        // 다 낸 뒤에는 기존 PlayFriend 흐름과 동일하게 ComputerAdvanceAfterDelay로 다음 단계로 넘어간다.
        private IEnumerator RunComputerTurnRoutine()
        {
            var (aiState, playerState) = ComputerAI.DetermineUrgencyStates(BuildObservation());
            int actionCount = 0;

            while (true)
            {
                var action = ComputerAI.DecideNextAction(BuildObservation(), aiState, playerState);
                if (!action.HasValue) break;

                Debug.Log($"[InGame] Computer 행동: key={action.Value.Key}, slot={action.Value.SlotIndex}, merge={action.Value.IsMerge}");
                ExecuteComputerAction(action.Value);
                actionCount++;

                yield return new WaitForSeconds(ComputerActionInterval);
            }

            Debug.Log($"[InGame] Computer 턴 종료 — 이번 턴 행동 횟수: {actionCount}");
            StartCoroutine(ComputerAdvanceAfterDelay(TurnPhase.PlayFriend));
        }

        // ComputerAI가 판단에 쓸 보드 상태 스냅샷 — 상대 손패는 개수만 세고 내용(FriendCard.Key)은 읽지 않는다(정보 제한)
        private ComputerObservation BuildObservation()
        {
            var aiField = SnapshotFieldRange(ComputerFieldStart, ComputerFieldEnd);
            var playerField = SnapshotFieldRange(UserFieldStart, UserFieldEnd);

            var aiEmptySlots = new List<int>();
            for (int i = ComputerFieldStart; i <= ComputerFieldEnd; i++)
                if (!GetFieldSlot(i).IsOccupied) aiEmptySlots.Add(i);

            int playerHandCount = _handSlots.Count(s => s.IsOccupied);

            return new ComputerObservation(
                aiHp: _computerBase.CurrentHp, aiMaxHp: _computerBase.MaxHp,
                aiField: aiField, aiEmptySlotIndices: aiEmptySlots,
                aiHand: new List<int>(_computerHand), aiDeckRemainingCount: _computerDeck.Count,
                playerHp: _userBase.CurrentHp, playerField: playerField,
                playerHandCount: playerHandCount, playerDeckRemainingCount: _userDeck.Count,
                initialComputerDeckSize: _computerInitialDeckSize);
        }

        private List<FriendSnapshot> SnapshotFieldRange(int fromIndex, int toIndex)
        {
            var result = new List<FriendSnapshot>();
            for (int i = fromIndex; i <= toIndex; i++)
            {
                var slot = GetFieldSlot(i);
                if (!slot.IsOccupied) continue;
                var friend = slot.GetComponentInChildren<Friend>();
                result.Add(new FriendSnapshot(i, friend.Key, friend.Att, friend.CurrentHp, friend.MaxHp));
            }
            return result;
        }

        // ComputerAI가 결정한 행동을 실제로 반영 — 기존 배치/병합 경로(MergeCardIntoSlot, TryPlaceFriendCard의 신규 배치 분기)를 그대로 재사용
        private void ExecuteComputerAction(ComputerAction action)
        {
            _computerHand.Remove(action.Key);

            var slot = GetFieldSlot(action.SlotIndex);
            if (action.IsMerge)
            {
                var existing = slot.GetComponentInChildren<Friend>();
                MergeCardIntoSlot(existing, action.Key, action.SlotIndex);
            }
            else
            {
                var friend = Instantiate(_friendPrefab, slot.transform);
                friend.SetKey(action.Key);
            }
        }

        // 슬롯을 강제로 비운다 — 점유돼 있지 않으면 아무 것도 하지 않는다(치트 전용, TryHandleDeath를 거치지 않아 부활/포자감염을 트리거하지 않음)
        public void CheatClearSlot(int slotIndex)
        {
            var slot = GetFieldSlot(slotIndex);
            if (!slot.IsOccupied) return;

            Destroy(slot.GetComponentInChildren<Friend>().gameObject);
        }

        // 슬롯에 key를 강제로 채운다 — 점유돼 있으면 기존 카드를 먼저 제거하고 새로 채운다(치트 전용, 병합 규칙을 타지 않고 항상 덮어씀)
        public void CheatSetSlot(int slotIndex, int key)
        {
            CheatClearSlot(slotIndex);

            var slot = GetFieldSlot(slotIndex);
            var friend = Instantiate(_friendPrefab, slot.transform);
            friend.SetKey(key);
        }

        // 점유된 슬롯에 key를 합친다 — TryPlaceFriendCard와 동일한 CanMerge 판정을 통과해야 실제로 합쳐진다(치트 전용이지만 정상 합체 규칙은 유지)
        public void CheatMergeIntoSlot(int slotIndex, int mergeKey)
        {
            var slot = GetFieldSlot(slotIndex);
            if (!slot.IsOccupied)
            {
                Debug.LogWarning($"[Cheat] 슬롯 {slotIndex}이 비어 있어 병합할 수 없습니다.");
                return;
            }

            var existing = slot.GetComponentInChildren<Friend>();
            if (!CanMerge(existing, mergeKey))
            {
                Debug.LogWarning($"[Cheat] 슬롯 {slotIndex}(key={existing.Key})에 key={mergeKey}를 합칠 수 없습니다 — 같은 종류가 아니고 target(All/Any) 조건도 만족하지 않습니다.");
                return;
            }

            MergeCardIntoSlot(existing, mergeKey, slotIndex);
        }

        // 슬롯의 카드에 데미지를 강제로 입힌다 — Friend.TakeDamage를 그대로 재사용(방어막 소모 포함), 죽으면 TryHandleDeath로 정상 사망 처리(부활/포자감염 포함)와 동일하게 처리
        public void CheatDamageSlot(int slotIndex, int amount)
        {
            var slot = GetFieldSlot(slotIndex);
            if (!slot.IsOccupied)
            {
                Debug.LogWarning($"[Cheat] 슬롯 {slotIndex}이 비어 있어 데미지를 적용할 수 없습니다.");
                return;
            }

            var friend = slot.GetComponentInChildren<Friend>();
            friend.TakeDamage(amount);

            if (friend.IsDead) TryHandleDeath(friend, slot.transform);
        }

        // 유저가 핸드 카드를 드래그하는 동안 병합 가능한 유저 필드 슬롯을 초록으로 미리 보여준다.
        // 판정식은 TryPlaceFriendCard와 반드시 동일하게 유지한다.
        public void ShowMergePreview(int draggedKey)
        {
            foreach (var slot in _fieldSlots)
            {
                if (slot.Index < UserFieldStart || !slot.IsOccupied) continue;
                var friend = slot.GetComponentInChildren<Friend>();
                if (CanMerge(friend, draggedKey)) friend.SetHighlight(true, Color.green);
            }
        }

        public void HideMergePreview()
        {
            foreach (var slot in _fieldSlots)
            {
                if (slot.Index < UserFieldStart || !slot.IsOccupied) continue;
                slot.GetComponentInChildren<Friend>().SetHighlight(false, Color.clear);
            }
        }

        private const int ComputerFieldStart = 1, ComputerFieldEnd = 3;
        private const int UserFieldStart = 4, UserFieldEnd = 6;

        // slotIndex가 속한 진영의 필드 범위("내 필드") — 유저 고정이 아니라 실제 슬롯 위치 기준(치트로 컴퓨터 필드에서 병합해도 그 진영 기준으로 판정)
        private (int Start, int End) OwnFieldRange(int slotIndex) =>
            slotIndex <= ComputerFieldEnd ? (ComputerFieldStart, ComputerFieldEnd) : (UserFieldStart, UserFieldEnd);

        // slotIndex 반대 진영의 필드 범위("상대 필드")
        private (int Start, int End) OpponentFieldRange(int slotIndex) =>
            slotIndex <= ComputerFieldEnd ? (UserFieldStart, UserFieldEnd) : (ComputerFieldStart, ComputerFieldEnd);

        private List<Friend> GetFieldFriends(int fromIndex, int toIndex)
        {
            var result = new List<Friend>();
            for (int i = fromIndex; i <= toIndex; i++)
            {
                var slot = GetFieldSlot(i);
                if (slot.IsOccupied) result.Add(slot.GetComponentInChildren<Friend>());
            }
            return result;
        }

        private Friend PickRandomTargetable(List<Friend> candidates)
        {
            candidates.RemoveAll(f => CardTable.Instance.GetCond(f.Key) == CardCondition.Except);
            return candidates.Count == 0 ? null : candidates[Random.Range(0, candidates.Count)];
        }

        // 병합 직후 발동 효과 — scope로 대상을, EffectClauses로 동작을 결정한다(카드 key로 분기하지 않음).
        // slotIndex는 existing이 실제로 놓인 필드 절대 번호 — Ally/Enemy는 이 위치를 기준으로 판정한다(유저=Ally 고정 아님)
        private void TriggerMergeAbility(Friend existing, int slotIndex)
        {
            var data = CardTable.Instance.Get(existing.Key);
            if (data.cond != CardCondition.Merge) return;
            if (data.EffectClauses.Any(c => c.Keyword == "MultiplierMerge")) return; // 병합 공식 자체(TryPlaceFriendCard)에서 이미 처리됨

            var (ownStart, ownEnd) = OwnFieldRange(slotIndex);
            var (oppStart, oppEnd) = OpponentFieldRange(slotIndex);

            switch (data.scope)
            {
                case CardAbilityScope.Self:
                    ApplyClausesToFriend(data.EffectClauses, existing);
                    break;
                case CardAbilityScope.AllyRandom:
                {
                    var candidates = GetFieldFriends(ownStart, ownEnd);
                    candidates.Remove(existing); // 무작위 단일 대상에서는 자기 자신 제외(AllyAll처럼 전체 적용일 때는 포함됨)
                    ApplyClausesToFriend(data.EffectClauses, PickRandomTargetable(candidates));
                    break;
                }
                case CardAbilityScope.EnemyRandom:
                    ApplyClausesToFriend(data.EffectClauses, PickRandomTargetable(GetFieldFriends(oppStart, oppEnd)));
                    break;
                case CardAbilityScope.AllyAll:
                    foreach (var f in GetFieldFriends(ownStart, ownEnd)) ApplyClausesToFriend(data.EffectClauses, f);
                    break;
                case CardAbilityScope.EnemyAll:
                    foreach (var f in GetFieldFriends(oppStart, oppEnd)) ApplyClausesToFriend(data.EffectClauses, f);
                    break;
                case CardAbilityScope.AllyBase:
                    ApplyClausesToBase(data.EffectClauses, GetBase(slotIndex));
                    break;
                case CardAbilityScope.EnemyBase:
                    ApplyClausesToBase(data.EffectClauses, slotIndex <= ComputerFieldEnd ? _userBase : _computerBase);
                    break;
            }
        }

        // Friend 대상 — 조각의 종류(Kind)별로 적용. 적용 후 죽었으면(능력엔 복귀 연출이 없으므로) 그 자리에서 즉시 제거
        private void ApplyClausesToFriend(List<CardEffectClause> clauses, Friend target)
        {
            if (target == null) return;

            foreach (var clause in clauses)
            {
                switch (clause.Kind)
                {
                    case CardEffectClauseKind.Stat:
                        switch (clause.Stat, clause.Op)
                        {
                            case (CardStat.Att, '+'): target.AddAtt(clause.Value); break;
                            case (CardStat.Att, '-'): target.AddAtt(-clause.Value); break;
                            case (CardStat.Att, '*'): target.MultiplyAtt(clause.Value); break;
                            case (CardStat.Att, '/'): target.DivideAtt(clause.Value); break;
                            case (CardStat.Hp, '+'): target.AddHp(clause.Value); break;    // 성장 — MaxHp도 같이 오름
                            case (CardStat.Hp, '-'): target.AddHp(-clause.Value); break;   // 저하 — MaxHp도 같이 내려감(전투 피해와 다름, dmg 사용)
                            case (CardStat.Hp, '*'): target.MultiplyHp(clause.Value); break;
                            case (CardStat.Hp, '/'): target.DivideHp(clause.Value); break;
                        }
                        break;
                    case CardEffectClauseKind.Damage:
                        target.TakeDamage(clause.Value); // 방어막 소모 대상 — 전투와 같은 피해
                        break;
                    case CardEffectClauseKind.Heal:
                        target.Heal(clause.Value); // MaxHp까지만, MaxHp 자체는 불변
                        break;
                    case CardEffectClauseKind.HealToMax:
                        target.HealToMax();
                        break;
                    case CardEffectClauseKind.Keyword:
                        if (clause.Keyword == "Shield") target.AddShield();
                        // "MultiplierMerge"는 TriggerMergeAbility 진입 시점에 이미 걸러져 여기 도달하지 않음
                        break;
                    case CardEffectClauseKind.Spawn:
                        target.ApplySpawnMark(clause.SpawnKey, clause.SpawnAtt, clause.SpawnHp); // 포자감염류 — 이 대상이 나중에 죽을 때 스폰
                        break;
                }
            }

            if (target.IsDead)
            {
                AddToGraveyard(target.transform.parent.GetComponent<FieldSlot>().Index, target.Key);
                Destroy(target.gameObject);
            }
        }

        // BaseStone 대상 — 피해/회복만 의미가 있다(Att, 스폰 등은 본체를 대상으로 하는 카드가 없어 무시)
        private void ApplyClausesToBase(List<CardEffectClause> clauses, BaseStone target)
        {
            foreach (var clause in clauses)
            {
                switch (clause.Kind)
                {
                    case CardEffectClauseKind.Damage: target.TakeDamage(clause.Value); break;
                    case CardEffectClauseKind.Heal: target.Heal(clause.Value); break;
                }
            }
        }

        // 사망 확정된 Friend를 부활/포자감염 규칙에 따라 처리한다. true를 반환하면 부활 성공 — 파괴하지 않고 필드에 남긴다.
        private bool TryHandleDeath(Friend friend, Transform slotTransform)
        {
            var data = CardTable.Instance.Get(friend.Key);
            if (data.cond == CardCondition.Die)
            {
                foreach (var clause in data.EffectClauses)
                {
                    if (clause.Kind != CardEffectClauseKind.Spawn) continue;
                    if (!friend.TryRevive(clause.SpawnAtt, clause.SpawnHp)) break; // 이미 한 번 부활했으면 그대로 사망 처리로 진행

                    friend.PunchScale(_mergePunchScale, _mergePunchDuration); // 부활 연출은 별도로 만들지 않고 병합 펀치 재사용
                    return true;
                }
            }

            bool hasSpawnMark = friend.SpawnMark.HasMark;
            int spawnKey = friend.SpawnMark.Key, spawnAtt = friend.SpawnMark.Att, spawnHp = friend.SpawnMark.Hp;
            AddToGraveyard(slotTransform.GetComponent<FieldSlot>().Index, friend.Key);
            Destroy(friend.gameObject);
            if (hasSpawnMark) SpawnFriendDirectly(spawnKey, spawnAtt, spawnHp, slotTransform);
            return false;
        }

        private void SpawnFriendDirectly(int key, int att, int hp, Transform slotTransform)
        {
            var friend = Instantiate(_friendPrefab, slotTransform);
            friend.SetKey(key);
            friend.OverrideStats(att, hp);
        }

        protected override void OnDestroy()
        {
            _subs.Dispose();
            base.OnDestroy();
        }
    }
}
