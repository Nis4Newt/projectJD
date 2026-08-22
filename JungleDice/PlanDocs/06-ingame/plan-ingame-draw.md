# InGame 드로우 예외 처리 계획

> 상위 문서: [InGame 핸드/필드 배치 계획](plan-ingame-handfield.md) ("이번 범위에서 제외" 절의 "핸드 4장 꽉 찬 상태에서 필드에 안 놓고 턴 넘어갈 때의 카드 버리기"와 "덱 소진 시 처리"를 이번 문서가 이어받아 구현)
> 관련 문서: [InGame 덱 구성 계획](plan-ingame-decksetup.md) (`_userDeck`/`_computerDeck` 필드 출처), [InGame 턴 진행 계획](plan-ingame-turnsystem.md) (`EnterPhase(TurnPhase.PlayFriend)`가 이번 문서의 진입점), [InGame 공격 판정 계획](plan-ingame-attack.md) (`TryEndGameIfBaseDestroyed`/`GetBase`를 그대로 재사용)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`, `JungleDice.InGame.FriendCard`, `JungleDice.InGame.BaseStone`, `DG.Tweening`
> 범위: 유저 `DrawHandCards()`/컴퓨터 `RefillComputerHand()`에 "풀 핸드 드로우"(핸드가 꽉 찬 상태에서 드로우된 카드는 파괴)와 "덱 소진 후 드로우"(덱에 카드가 없으면 드로우 대신 본체 피해 1) 두 예외 상태를 추가. 새 연출 이펙트 시스템, 컴퓨터 AI가 이 상태를 인지해 전략을 바꾸는 것, 데미지 팝업 UI 등은 범위 밖.

---

## 배경

[핸드/필드 배치 계획](plan-ingame-handfield.md)은 두 가지를 명시적으로 범위 밖으로 미뤄뒀다.

- "핸드 4장 꽉 찬 상태에서 필드에 안 놓고 턴 넘어갈 때의 카드 버리기 페널티 — 그냥 드로우가 0장이 될 뿐"
- "덱 소진 시 처리(패배 조건 등) — `needed`가 0이 되어 드로우가 멈출 뿐"

현재 `DrawHandCards()`는 `needed = Mathf.Min(emptySlots.Count, _userDeck.Count)`가 0이면 그대로 `return`한다 — 핸드가 꽉 찼든 덱이 비었든 똑같이 "아무 일도 안 일어남"으로 뭉뚱그려져 있다. 이번 문서는 이 두 상태를 구분해서 각각 다른 결과를 내도록 만든다.

- **풀 핸드 드로우**: 핸드에 빈 슬롯이 없는데 드로우가 일어나면, 그 카드는 덱에서 실제로 소비되지만 핸드에 들어가지 못하고 파괴된다. 카드를 계속 필드에 내지 않고 방치하면 덱만 축나게 해 핸드 관리에 실질적인 유인을 준다.
- **덱 소진 후 드로우**: 덱에 더 이상 카드가 없는 상태에서 드로우가 일어나면, 아예 드로우가 불가능하므로 대신 본인 진영의 `BaseStone`(모험가 본체)이 1의 피해를 입는다. 본체 피해는 [공격 판정 계획](plan-ingame-attack.md)의 `TryEndGameIfBaseDestroyed`를 그대로 재사용하므로, 이 피해로 체력이 0이 되면 다른 피해 원인과 동일하게 `GameState.GameOver`로 이어진다.

두 상태는 상호 배타적으로 처리한다 — 덱이 비어 있으면 핸드가 꽉 찼든 아니든 무조건 피해로만 처리한다(애초에 파괴할 카드 자체가 존재하지 않음). 유저(화면에 보이는 `FriendCard`)와 컴퓨터(화면에 없는 `_computerHand`) 양쪽에 동일한 규칙을 적용하되, 표현 방식만 다르다.

---

## 설계 목표

- 덱 소진 상태의 드로우는 조용히 무시되지 않고 본체 피해로 이어진다 — 기존 `TakeDamage`/`TryEndGameIfBaseDestroyed` 경로를 그대로 재사용해 다른 피해 원인과 동일하게 GameOver 판정을 받는다.
- 핸드가 가득 찬 상태에서도 덱은 계속 소비된다 — 뽑힌 카드는 핸드에 들어가지 못하고 파괴된다.
- 유저와 컴퓨터에 동일한 규칙을 적용하되, 유저는 카드가 잠깐 나타났다 사라지는 연출을 보여주고 컴퓨터는 연출 없이 덱 카운트만 조용히 줄어든다(기존 `RefillComputerHand`의 "화면에 그리지 않으므로 연출 없이 즉시 채움" 원칙과 동일).
- 덱 소진 분기가 풀 핸드 분기보다 우선한다 — 두 상태가 동시에 발생해도(핸드 꽉 참 + 덱도 빔) 분기가 겹치지 않는다.
- 이 드로우로 인해 게임이 끝나면(`TryEndGameIfBaseDestroyed`가 `true` 반환) `EnterPhase(PlayFriend)`는 버튼 텍스트 갱신, `PlayMyTurnAlert`, 컴퓨터 턴 시작(`RunComputerTurnRoutine`)을 실행하지 않고 즉시 반환한다 — 게임오버 이후에도 턴이 계속 진행되는 것처럼 보이는 상태 불일치를 막는다.
- 기존 `DrawHandCardsRoutine`/`SpawnFriendCard`/`RefillComputerHand`의 정상 드로우 경로(빈 슬롯이 있고 덱도 있는 경우)는 건드리지 않는다 — 두 예외 상태를 새 조기 분기로만 추가한다.

---

## 핵심 설계 결정

### 1. `DrawHandCards()`/`RefillComputerHand()`를 `bool` 반환으로 변경 — "이번 호출로 게임이 끝났는가"

`RollAttacker`에서 공격자가 없을 때 이미 쓰고 있는 "조기 `return`으로 이후 턴 진행을 건너뛰는" 패턴을 그대로 재사용한다. 반환값은 게임 종료 여부만 의미하고, 실제 종료 처리(`_userWon` 세팅, `GameState.GameOver` 전이)는 여전히 `TryEndGameIfBaseDestroyed` 하나가 전담한다.

```csharp
// 반환값 true = 이번 드로우(덱 소진 피해)로 본체가 파괴되어 GameOver로 전이함 — 호출부가 이후 턴 진행을 중단해야 함
private bool DrawHandCards()
{
    if (_userDeck.Count == 0)
    {
        Debug.LogWarning("[InGame] User 덱 소진 — 드로우 대신 본체 피해 1");
        _userBase.TakeDamage(1);
        return TryEndGameIfBaseDestroyed(_userBase);
    }

    var emptySlots = new List<HandSlot>();
    foreach (var slot in _handSlots)
        if (!slot.IsOccupied) emptySlots.Add(slot);

    if (emptySlots.Count == 0)
    {
        DrawAndDiscardOne();
        return false;
    }

    int needed = Mathf.Min(emptySlots.Count, _userDeck.Count);
    StartCoroutine(DrawHandCardsRoutine(emptySlots, needed));
    return false;
}
```

덱 소진 체크가 가장 먼저다 — 덱이 비어 있으면 핸드 상태를 계산할 필요도 없이 곧장 피해로 처리한다. 기존의 `if (needed <= 0) return;` 가드는 이 시점에서 이미 `emptySlots.Count >= 1`이고 `_userDeck.Count >= 1`임이 보장되므로(둘 다 위에서 걸러짐) `needed`가 0이 될 수 없어 제거한다.

### 2. `SpawnCardAtDeck(int key)` — 덱 위치에서 카드를 생성하는 공용 전처리, `DrawAndDiscardOne`/`SpawnFriendCard`가 공유

카드를 "덱 오브젝트 위치에서 생성 + key 세팅"하는 전처리는 정상 드로우(`SpawnFriendCard`)와 풀 핸드 드로우(`DrawAndDiscardOne`)가 동일하다 — 둘 다 이후 처리(슬롯으로 이동 vs 페이드 아웃 파괴)만 다르므로, 공용 헬퍼로 뽑아 중복을 없앤다.

```csharp
// 덱 오브젝트의 위치에서 FriendCard를 생성하고 key를 세팅한다 — 정상 드로우(SpawnFriendCard)와
// 풀 핸드 드로우(DrawAndDiscardOne)가 공유하는 전처리, 이후 처리(슬롯 이동/파괴)만 호출부마다 다르다.
private FriendCard SpawnCardAtDeck(int key)
{
    var card = Instantiate(_friendCardPrefab, _dragLayer);
    card.transform.position = _deckOrigin.position; // 덱 오브젝트의 위치에서 생성
    card.SetKey(key);
    return card;
}

// 풀 핸드 상태에서도 덱은 그대로 소비된다 — 뽑은 카드는 핸드에 들어가지 못하고 파괴된다
private void DrawAndDiscardOne()
{
    int key = _userDeck[0];
    _userDeck.RemoveAt(0);

    Debug.LogWarning($"[InGame] User 풀 핸드 드로우 — key={key} 카드 파괴됨");

    SpawnCardAtDeck(key).Discard(_drawDuration);
}
```

`_drawDuration`(기존 필드, 슬롯까지 이동하는 데 걸리던 시간)을 그대로 파괴 연출 시간으로 재사용한다 — 새 `[SerializeField]`를 추가하지 않는다. `SpawnFriendCard`도 이 헬퍼를 쓰도록 함께 바뀐다("클래스 구조" 절 참고).

### 3. `FriendCard.Discard` — 기존 `CanvasGroup`으로 페이드 아웃 후 파괴

`FriendCard`는 이미 드래그 중 레이캐스트 토글용으로 `CanvasGroup`을 갖고 있다(`[RequireComponent(typeof(CanvasGroup))]`). 같은 컴포넌트를 페이드에 재사용한다.

```csharp
// 뽑았지만 핸드에 들어가지 못한 카드를 페이드 아웃 후 파괴한다(풀 핸드 드로우 전용)
public void Discard(float duration)
{
    _canvasGroup.blocksRaycasts = false; // HomeSlot이 없어 드래그를 시작하면 되돌아갈 곳이 없으므로 애초에 입력을 막는다
    _canvasGroup.DOFade(0f, duration).OnComplete(() => Destroy(gameObject));
}
```

`blocksRaycasts = false`가 없으면, 파괴 연출이 끝나기 전 짧은 시간 안에 유저가 이 카드를 드래그할 경우 `_homeSlot`이 `null`인 채로 `OnBeginDrag`가 실행되고, 드롭 실패 시 `AttachToSlot(null)`에서 `NullReferenceException`이 난다 — `DrawAndDiscardOne`에서 `Initialize`를 호출하지 않기 때문에 더더욱 이 카드는 애초에 드래그 가능한 상태로 두면 안 된다.

### 4. `RefillComputerHand()` — 동일한 두 분기, 연출 없이

컴퓨터 손패는 화면에 그려지지 않으므로(`RefillComputerHand` 기존 주석 "화면에 그리지 않으므로 연출 없이 즉시 채움" 원칙) 풀 핸드 드로우도 `_computerDeck.RemoveAt(0)` 한 줄로 끝난다.

```csharp
// 반환값 true = 이번 드로우(덱 소진 피해)로 본체가 파괴되어 GameOver로 전이함 — 호출부가 RunComputerTurnRoutine 시작을 건너뛰어야 함
private bool RefillComputerHand()
{
    if (_computerDeck.Count == 0)
    {
        Debug.LogWarning("[InGame] Computer 덱 소진 — 드로우 대신 본체 피해 1");
        _computerBase.TakeDamage(1);
        return TryEndGameIfBaseDestroyed(_computerBase);
    }

    if (_computerHand.Count == ComputerHandSize)
    {
        Debug.LogWarning($"[InGame] Computer 풀 핸드 드로우 — key={_computerDeck[0]} 카드 파괴됨");
        _computerDeck.RemoveAt(0); // 풀 핸드 드로우 — 화면에 없는 손패라 파괴 연출 없이 그대로 버려짐
        return false;
    }

    while (_computerHand.Count < ComputerHandSize && _computerDeck.Count > 0)
    {
        _computerHand.Add(_computerDeck[0]);
        _computerDeck.RemoveAt(0);
    }
    return false;
}
```

### 5. 호출부: `EnterPhase(PlayFriend)`에서 반환값 확인 후 조기 반환

```csharp
case TurnPhase.PlayFriend:
    Debug.Log($"[InGame] {_currentOwner} 턴 - 친구카드 플레이");
    if (_currentOwner == TurnOwner.User)
    {
        if (DrawHandCards()) return; // 덱 소진 피해로 게임오버 — 턴 진행 중단
        _resultPanel.PlayMyTurnAlert();
    }
    else
    {
        if (RefillComputerHand()) return; // 덱 소진 피해로 게임오버 — 턴 진행 중단
        StartCoroutine(RunComputerTurnRoutine());
    }
    _actionButtonText.text = "roll attacker";
    _actionButton.interactable = _currentOwner == TurnOwner.User;
    break;
```

`RollAttacker`에서 공격자가 없을 때 이미 쓰고 있는 조기 `return` 패턴과 동일한 자리(같은 `case` 블록 안)에서 같은 방식으로 처리한다.

---

## 클래스 구조

```
FriendCard (기존 파일 수정, InGame/)
└── Discard(float duration)                 ← 신규, public — 페이드 아웃 후 파괴(풀 핸드 드로우 전용)

InGameSceneManager (기존 파일 수정, InGame/)
├── DrawHandCards() : bool                  ← 반환 타입 변경(void → bool), 덱 소진/풀 핸드 분기 추가
├── SpawnCardAtDeck(int key) : FriendCard   ← 신규, private — SpawnFriendCard/DrawAndDiscardOne 공용 전처리
├── DrawAndDiscardOne()                     ← 신규, private, SpawnCardAtDeck 재사용
├── SpawnFriendCard(int, HandSlot)          ← 기존 파일 수정 — 전처리를 SpawnCardAtDeck 재사용으로 교체(동작 변화 없음)
├── RefillComputerHand() : bool             ← 반환 타입 변경(void → bool), 덱 소진/풀 핸드 분기 추가
└── EnterPhase(TurnPhase.PlayFriend) 분기    ← 두 메서드의 반환값 확인 후 조기 return 추가
```

---

## 파일 구성

```
Assets/Scripts/InGame/
├── FriendCard.cs           ← 기존 파일 수정 (Discard 추가)
└── InGameSceneManager.cs   ← 기존 파일 수정 (DrawHandCards/RefillComputerHand 예외 분기 + EnterPhase 반환 처리)
```

---

## 이번 범위에서 제외

- 덱 소진 피해에 대한 별도 이펙트(파티클, 사운드 등) — 기존 `BaseStone.TakeDamage`가 이미 하는 HP 텍스트 갱신 이상은 다루지 않음
- 컴퓨터 AI(`ComputerAI`)가 자신의 덱 소진/풀 핸드 상태를 인지해 카드 소비 전략을 바꾸는 것 — `ComputerObservation`은 이미 `aiDeckRemainingCount`를 담고 있지만, 이번 문서는 그 값을 판단 로직에 새로 반영하지 않는다(기존 히스토그램 가중치 계산 용도 그대로)
- 파괴되는 카드 수 표시, 피해량 팝업 등 데미지 UI 폴리시
- 유저/컴퓨터가 아닌 제3의 진영(향후 확장) — 이 문서는 현재 존재하는 두 진영만 다룸

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 핸드에 빈 슬롯이 있고 덱도 있음(정상 케이스) | 기존 `DrawHandCardsRoutine` 그대로 — 이번 문서로 인한 동작 변화 없음 |
| 핸드 풀 + 덱도 소진(동시 발생) | 덱 소진 분기가 우선 — 카드 파괴 없이 본체 피해 1만 적용(뽑을 카드 자체가 없음) |
| 컴퓨터 손패가 4장 미만인 채로 덱이 도중에 바닥남(예: `while` 루프 중 3장째에 덱이 0이 됨) | 기존 `while` 루프가 자연 종료 — 신규 `_computerDeck.Count == 0` 체크는 `RefillComputerHand` **진입 시점**에 이미 0장인 경우만 잡으므로, 루프 도중 소진되는 경우는 기존처럼 그냥 적게 채워지고 페널티 없음(요청사항이 "모든 카드를 드로우한 후"이므로 이미 소진된 상태만 다룸) |
| 드로우로 인한 본체 피해가 체력을 0으로 만듦 | `TryEndGameIfBaseDestroyed`가 `_userWon` 세팅 + `GameManager.Instance.ChangeState(GameState.GameOver)` 호출 → `OnGameStateChanged`가 `_resultPanel.ShowResult` 표시. `DrawHandCards`/`RefillComputerHand`가 `true`를 반환하므로 `EnterPhase`가 즉시 반환해 버튼 텍스트 갱신·`PlayMyTurnAlert`·컴퓨터 턴 시작이 실행되지 않음 |
| 드로우로 인한 본체 피해인데 체력이 남음(0이 안 됨) | `TryEndGameIfBaseDestroyed`가 `false` 반환 → `EnterPhase`가 평소처럼 계속 진행(버튼 텍스트 갱신 등) |
| `DrawAndDiscardOne`의 파괴 연출(`Discard`) 도중 씬 전환 | `FriendCard`가 `MonoBehaviour`이므로 씬 전환 시 자동 파괴 — DOTween도 함께 정리됨(프로젝트 전역 안전 모드, 기존 관례와 동일) |
| 파괴 연출이 끝나기 전(`_drawDuration` 이내) 유저가 그 카드를 드래그 시도 | `Discard`가 `_canvasGroup.blocksRaycasts = false`를 즉시 설정하므로 `EventSystem`이 이 카드를 드래그 대상으로 인식하지 않음 — `OnBeginDrag`가 아예 호출되지 않음 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 유저 핸드 4장 모두 점유, `_userDeck.Count > 0` 상태에서 `PlayFriend` 진입 | `_userDeck.Count` 1 감소, `FriendCard`가 덱 위치에 생성됐다가 페이드 아웃 후 파괴, `_handSlots` 점유 상태 불변 |
| 2 | 유저 `_userDeck.Count == 0`, `_userBase.CurrentHp > 1` 상태에서 `PlayFriend` 진입 | 카드 생성 없음, `_userBase.CurrentHp` 1 감소, 액션 버튼 텍스트 `"roll attacker"`로 정상 갱신(턴 계속 진행) |
| 3 | 유저 `_userBase.CurrentHp == 1`, `_userDeck.Count == 0` 상태에서 `PlayFriend` 진입 | `_userBase.CurrentHp == 0`, `GameState.GameOver` 전이, `_userWon == false`, `_resultPanel.PlayMyTurnAlert()` 미호출, 액션 버튼 텍스트/활성화 변경 없음 |
| 4 | 컴퓨터 손패 4장 모두 채워진 상태, `_computerDeck.Count > 0`에서 컴퓨터 `PlayFriend` 진입 | `_computerDeck.Count` 1 감소, `_computerHand.Count`는 4로 불변, `RunComputerTurnRoutine`은 정상 실행 |
| 5 | 컴퓨터 `_computerDeck.Count == 0`, `_computerBase.CurrentHp > 1` 상태에서 `PlayFriend` 진입 | `_computerBase.CurrentHp` 1 감소, `RunComputerTurnRoutine` 미실행(컴퓨터 행동 로그 없음), 2초 뒤 자동 진행 없이 다음 유저 턴으로 넘어가지 않고 게임이 그 상태로 멈추지 않는지 확인(= `EnterPhase`가 조기 반환했더라도 다음 트리거는 없으므로, 이 시나리오는 반드시 `_computerBase.CurrentHp > 1`이어야 무한 대기 없이 관찰 가능) |
| 6 | 컴퓨터 `_computerBase.CurrentHp == 1`, `_computerDeck.Count == 0`에서 `PlayFriend` 진입 | GameOver 전이, `_userWon == true` |
| 7 | (회귀) 유저 핸드에 빈 슬롯 1개 이상, `_userDeck.Count > 0` | 기존 `DrawHandCardsRoutine` 그대로 동작 — 빈 슬롯 개수만큼만 드로우, 이번 문서로 인한 변화 없음 |

시나리오 5는 컴퓨터 턴이 `RunComputerTurnRoutine` 없이 멈추는 상태를 만들므로, 실제 검증 시에는 별도로 `ComputerAdvanceAfterDelay`가 걸리지 않는다는 것까지 로그로 확인해야 한다(의도된 동작 — 게임이 끝나지 않았다면 다음 턴 트리거가 없는 것이 버그처럼 보일 수 있으니 테스트 시 주의).

---

## 구현 시 주의사항

- **덱 소진 체크는 반드시 핸드 풀 체크보다 먼저 온다**: 덱이 비어 있으면 애초에 뽑을 카드가 없으므로 파괴할 대상도 없다 — 순서를 바꾸면 존재하지 않는 카드를 파괴하려는 로직이 된다.
- **`DrawHandCards`/`RefillComputerHand`의 반환값은 반드시 호출부에서 확인한다**: `EnterPhase(PlayFriend)`가 반환값을 무시하면 게임오버 이후에도 버튼 텍스트 갱신·`PlayMyTurnAlert`·컴퓨터 턴 시작이 실행되는 상태 불일치가 생긴다 — `RollAttacker`의 "공격자 없음" 조기 반환과 동일한 이유.
- **`TryEndGameIfBaseDestroyed`는 그대로 재사용하고 새로 구현하지 않는다**: `_userWon` 세팅과 `GameState.GameOver` 전이를 이미 전담하고 있으므로 중복 로직을 만들면 두 경로가 어긋날 위험이 있다.
- **컴퓨터 손패 파괴는 `FriendCard`/연출을 쓰지 않는다**: 화면에 그려지지 않는 손패이므로 `_computerDeck.RemoveAt(0)` 한 줄로 충분 — 유저 쪽과 다르게 처리한다고 당황하지 않는다.
- **`FriendCard.Discard`는 새 컴포넌트를 요구하지 않는다**: `[RequireComponent(typeof(CanvasGroup))]`가 이미 있으므로 프리팹 수정 없이 스크립트만 고치면 된다.

---

## 구현 후 체크리스트

- [ ] `FriendCard.cs`: `Discard(float duration)` 추가
- [ ] `InGameSceneManager.cs`: `DrawHandCards()`를 `bool` 반환으로 변경 + 덱 소진/풀 핸드 분기 추가, `SpawnCardAtDeck()`/`DrawAndDiscardOne()` 신규(`SpawnFriendCard()`도 `SpawnCardAtDeck()` 재사용으로 정리)
- [ ] `InGameSceneManager.cs`: `RefillComputerHand()`를 `bool` 반환으로 변경 + 덱 소진/풀 핸드 분기 추가
- [ ] `InGameSceneManager.cs`: `EnterPhase(PlayFriend)`에서 두 메서드의 반환값 확인 후 조기 `return` 추가
- [ ] 테스트 시나리오 7개 검증(특히 #3, #6: 드로우로 인한 GameOver 전이, #5: 컴퓨터 덱 소진 시 턴 진행 중단)
