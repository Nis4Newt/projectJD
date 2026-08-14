# InGame 친구카드 합체 계획

> 상위 문서: [InGame 로직 개요](plan-ingame.md) ([핸드/필드 배치 계획](plan-ingame-handfield.md)·[공격 판정 계획](plan-ingame-attack.md) 이후 — 두 문서 모두 "(추후) 합체 판정을 다루는 후속 계획 문서"로 남겨둔 지점)
> 관련 문서: [핸드/필드 배치 계획](plan-ingame-handfield.md) (`FriendCard`/`FieldSlot`/`InGameSceneManager.TryPlaceFriendCard`를 이번 문서가 그대로 확장), [공격 판정 계획](plan-ingame-attack.md) (`Friend.Att`/`CurrentHp`/`GetStatColor`를 이번 문서가 재사용), [친구카드 능력 계획](plan-ingame-ability.md) (이 문서가 정의하는 `TryPlaceFriendCard`의 병합 지점에 그 문서가 `cond` 발동 효과 훅을 추가로 연결)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`, `JungleDice.InGame.Friend`, `JungleDice.InGame.FriendCard`, `JungleDice.InGame.FieldSlot`, `JungleDice.Data.Table.CardTable`(`att`/`hp`/`target`)
> 범위: 유저 필드(4/5/6번)에서 핸드 카드를 필드 슬롯에 드롭했을 때의 병합 판정 — 같은 종류(`Friend.Key` 일치)이거나, 필드 카드가 `target=Any`(무엇이든 받아주는 베이스, 예: 1019 하이에나)이거나, 낸 카드가 `target=All`(무엇에든 합쳐지는, 예: 1004 블루베리)이면 병합되어 필드의 친구카드에 방금 낸 카드의 기본 공격력/체력이 더해지고, 그 외엔 배치를 거부한다. `All`/`Any`는 역할이 정해진 별개 규칙이다(요청자 확인 — 아래 "배경" 참고). 드래그 중 병합 가능한 필드 슬롯을 초록색으로 미리 보여주는 것도 이번 문서가 다룬다. `CardTable`의 `cond`(Merge/Except/Die)를 이용한 실제 발동 효과는 [친구카드 능력 계획](plan-ingame-ability.md)에서 다룬다(범위 밖).

---

## 배경

[핸드/필드 배치 계획](plan-ingame-handfield.md)의 `TryPlaceFriendCard`는 지금 "슬롯이 이미 점유돼 있으면 아무 것도 하지 않는다"(`if (slot.IsOccupied) return;`)로만 되어 있다 — 점유된 슬롯에 드롭하면 항상 실패로 취급되어 카드가 원래 자리로 돌아간다. [InGame 로직 개요](plan-ingame.md)와 [공격 판정 계획](plan-ingame-attack.md)는 이 지점을 "(추후) 합체 판정을 다루는 후속 계획 문서"로 명시적으로 남겨뒀다 — 이번 문서가 그 후속이다.

`CardTable`(`CardTable.cs`/`CardTable.csv`)에는 이미 `cond`(`None`/`Merge`/`Except`)와 `target`(`Same`/`All`) 컬럼이 정의돼 있고 실제 데이터도 채워져 있지만, 코드 어디에서도 아직 참조하지 않는다(`grep` 확인). `cond=Merge`인 카드들의 `explain`을 보면 "상대 무작위 유닛 능력치 절반", "상대 플레이어에게 2의 데미지", "내 모험가의 생명력 2 회복" 같은 발동 효과가 정의돼 있는데, 상대 필드에서 무작위 대상을 고르는 로직도 모험가 체력 시스템도 아직 코드베이스에 없다. `target=All`인 카드(1004, 파란 딸기)는 "모든 종류의 친구와 합칠 수 있다"는 예외 규칙이다.

최초 요청자 확인 결과 이번 문서는 요청사항 그대로의 최소 병합 규칙(같은 종류일 때만 병합, 공격력·체력 단순 합산)만 다루고 `cond`/`target` 필드를 이용한 확장은 후속 문서로 미뤘다. 이후 요청자 확인으로 범위를 다시 조정했다 — `target`(이종 합체 판정)과 드래그 중 병합 가능 슬롯 미리보기는 이 문서 범위에 포함하고, `cond`(발동 효과)만 [친구카드 능력 계획](plan-ingame-ability.md)으로 분리한다.

**`CardTarget`에 `Any` 추가 필요.** `CardTable.csv`를 다시 확인하니 1019(하이에나)의 `target` 값이 `any`인데, 코드의 `CardTarget` enum(`CardTable.cs`)에는 `Same`/`All`만 있다 — `any`는 지금 상태로 로드하면 파싱 실패 후 기본값 `Same`으로 조용히 떨어져 하이에나의 "모든 종류의 친구를 잡아먹을 수 있음" 규칙이 동작하지 않는다. 요청자 확인 결과 `any`는 오타가 아니라 `all`과 의도적으로 다른 값이다.

설계는 세 단계를 거쳐 확정됐다:

1. **최초**: "`All`은 받는 쪽(필드 카드)일 때만, `Any`는 내는 쪽(낸 카드)일 때만 적용된다"는 방향이 있는 조건으로 설계했다(1004 "합쳐질 수 있습니다" = 수동태, 1019 "잡아먹을 수 있음" = 능동태라는 표현 차이에 근거).
2. **1차 수정**: 실제 동작 확인 결과 1004/1019 모두 이종 합체가 동작하지 않는 문제가 발견돼, "슬롯의 카드·낸 카드 양쪽의 `target`을 모두 조사"하는 대칭 판정으로 바꿨다.
3. **최종 확정**: 이후 요청자가 정확한 규칙을 다시 확인해줬다 — **`All`(1004 블루베리)은 "이 카드를 낼 때 무엇에든 합쳐질 수 있다"(능동, 낸 카드 역할일 때만), `Any`(1019 하이에나)는 "이 카드가 필드에 있을 때 무엇이든 받아주는 베이스가 된다"(수동, 필드 카드 역할일 때만)** — 방향이 있는 조건 자체는 맞았지만, 1단계에서 `All`/`Any`를 서로 반대 역할에 배정한 것이 문제였다. 예시로 확정됨:
   - 슬롯에 1000(코끼리)이 있을 때 블루베리를 드래그 → 병합됨(블루베리가 능동으로 합쳐짐). 하이에나를 드래그 → 병합 안 됨(하이에나는 능동 역할이 없음).
   - 슬롯에 블루베리가 있을 때 코끼리를 드래그 → 병합 안 됨(블루베리는 받아주는 역할이 없음). 슬롯에 하이에나가 있을 때 코끼리를 드래그 → 병합됨(하이에나가 베이스로 받아줌).

최종 조건: `existingData.target == CardTarget.Any`(필드 카드가 베이스) **또는** `data.target == CardTarget.All`(낸 카드가 무엇에든 합쳐짐).

---

## 설계 목표

- 같은 종류(`Friend.Key == FriendCard.Key`)이거나, 필드 카드가 `target=Any`(베이스 — 무엇이든 받아줌)이거나, 낸 카드가 `target=All`(무엇에든 합쳐짐)이면 병합, 그 외엔 배치 거부. `All`/`Any`는 역할이 고정돼 있다 — 서로 바꿔 쓰거나 양쪽 다 검사하면 안 된다.
- 병합 시 필드 친구카드의 `Att`/`CurrentHp`에 방금 낸 카드의 **기본**(`CardTable` 원본) 공격력/체력을 더한다 — 필드 친구카드가 전투로 깎이거나(`TakeDamage`) 자기 자신 병합으로 오른(`DoubleAtt`) 상태라도 그 "현재 값"에 더한다(고정 기준값이 아님)
- 새로 낸 카드(`FriendCard`)는 병합 성공 시 그대로 사라진다 — 필드에는 항상 기존 `Friend` 인스턴스 하나만 남는다(새 `Friend`를 생성하지 않음)
- 필드 슬롯 하나에는 언제나 자식이 최대 하나(`FieldSlot.IsOccupied == transform.childCount > 0`) — 병합도 이 불변식을 그대로 유지, 별도 상태 플래그 없음
- 병합 판정과 스탯 반영은 책임을 분리한다 — "병합해도 되는가"(키/`target` 비교)는 `InGameSceneManager.TryPlaceFriendCard`, "스탯을 어떻게 반영하는가"(값 갱신+색상)는 `Friend.MergeWith`가 담당한다. [공격 판정 계획](plan-ingame-attack.md)의 "하이라이트 on/off는 `Friend`, 지시는 매니저"와 같은 책임 분리 패턴
- 병합 시 시각 피드백은 기존 `Friend.PunchScale`을 재사용 — 새 트윈 로직을 만들지 않는다

---

## 핵심 설계 결정

### 1. 병합 판정: `CanMerge`에서 슬롯의 기존 `Friend.Key`와 비교(+ 역할이 있는 `target` 예외)

`CardTarget`에 `Any`를 추가한다(`CardTable.cs`) — 기존 값(`Same=0`/`All=1`)의 정수값이 바뀌면 안 되므로 반드시 끝에 붙인다.

```csharp
public enum CardTarget
{
    Same,
    All,
    Any, // 신규
}
```

```csharp
[SerializeField] private float _mergePunchScale = 0.1f;
[SerializeField] private float _mergePunchDuration = 0.25f;

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

// 같은 종류, 또는 슬롯의 카드가 target=Any(베이스), 또는 낸/합칠 카드가 target=All(무엇에든 합쳐짐)일 때 합체 가능
private bool CanMerge(Friend existing, int mergeKey)
{
    var existingData = CardTable.Instance.Get(existing.Key);
    var data = CardTable.Instance.Get(mergeKey);

    bool sameKind = existing.Key == mergeKey;
    bool existingAcceptsAnything = existingData.target == CardTarget.Any; // 필드 카드가 베이스 역할(하이에나류) — 어떤 카드가 와도 받아줌
    bool mergeJoinsAnything = data.target == CardTarget.All; // 낸/합칠 카드가 무엇에든 합쳐지는 역할(블루베리류)
    return sameKind || existingAcceptsAnything || mergeJoinsAnything;
}
```

- 기존의 "점유돼 있으면 무조건 리턴" 분기를 "점유돼 있으면 병합 가능 여부를 정하는" 분기로 확장한다 — 빈 슬롯 배치 경로(아래 절반)는 손대지 않는다.
- 병합 판정(`CanMerge`)과 실행(`MergeCardIntoSlot`, 2번 결정)은 별도 메서드로 분리돼 있다 — [InGame 필드 슬롯 치트 에디터 계획](plan-ingame-cheat.md)이 드래그 없이 key 입력만으로 정상 합체를 재현하기 위해 이렇게 추출했다. 판정/실행이 분리돼 있어도 정상 드래그(`TryPlaceFriendCard`)와 치트(`CheatMergeIntoSlot`)는 항상 같은 규칙을 공유한다.
- 병합 가능 조건은 "키가 같다" **또는** "필드 카드가 `Any`다" **또는** "낸 카드가 `All`이다" — 역할이 고정돼 있다. `existingData.target == CardTarget.All`(필드 카드가 우연히 `All`)이나 `data.target == CardTarget.Any`(낸 카드가 우연히 `Any`)는 병합을 허용하지 않는다. **요청자 확인 경위**: 최초 방향이 있는 설계에서 `All`/`Any`를 반대로 배정했다가(1번 실패), 한 차례 대칭 판정으로 수정했지만(2번, 이것도 틀림), 최종적으로 `All`=낸 카드 전용/`Any`=필드 카드 전용으로 다시 확정했다(위 "배경" 참고).
- 예: 1004(블루베리)를 손에서 내면 다른 종류 필드 카드에도 합쳐지지만, 블루베리가 필드에 있을 때는 다른 종류를 받아주지 않는다. 1019(하이에나)는 반대로 필드에 있을 때만 무엇이든 받아주고, 손에서 내면 다른 종류에 합쳐지지 않는다.
- 병합 불가일 때는 `card.NotifyPlaced()`를 호출하지 않고 그대로 `return`한다 — [핸드/필드 배치 계획](plan-ingame-handfield.md)이 이미 정의한 "배치 실패 시 `_wasPlaced`가 `false`로 남아 `OnEndDrag`가 원래 슬롯으로 복귀시킨다"는 경로를 그대로 재사용, 병합 전용 되돌리기 로직을 새로 만들 필요가 없다.
- 이종 합체가 성공해도 필드에 남는 카드의 정체성은 항상 `existing`이다 — 카드 교체가 아니라 "다른 종류인데도 스탯만 흡수시킨다"는 의미. [친구카드 능력 계획](plan-ingame-ability.md)의 발동 효과도 이 `existing.Key` 기준을 그대로 따른다.
- `MergeCardIntoSlot`의 세 번째 인자(`slot.Index`)는 `existing`이 실제로 놓인 필드 절대 번호다 — [친구카드 능력 계획](plan-ingame-ability.md)의 발동 효과가 "내 필드"/"상대 필드"를 유저 고정이 아니라 이 위치 기준으로 판정하는 데 쓰인다(치트로 컴퓨터 필드에서 병합해도 정확히 동작해야 하므로).
- `data.att`/`data.hp`는 **카드의 기본값**이다 — `FriendCard`는 애초에 런타임 상태(피해/버프)를 갖지 않는 컴포넌트([핸드/필드 배치 계획](plan-ingame-handfield.md) 참고, `SetKey`가 매번 `CardTable`에서 값을 그대로 표시)이므로, `card.Key`로 다시 조회한 `CardTableData`가 곧 "방금 낸 카드의 기본 공격력/체력"이다.
- `PunchScale` 강도/시간은 [공격 판정 계획](plan-ingame-attack.md)의 `_selectPunchScale`/`_attackerPunchDuration`과 같은 방식으로 `InGameSceneManager`가 값을 들고 `Friend`의 범용 메서드에 넘긴다 — `Friend`는 "얼마나/몇 초"를 스스로 정하지 않는다.

### 2. `Friend.MergeWith` — 현재 값 기준으로 누적, 색상은 기존 헬퍼 재사용

```csharp
public void MergeWith(int addAtt, int addHp)
{
    int previousAtt = Att;
    int previousHp = CurrentHp;

    Att += addAtt;
    CurrentHp += addHp;

    _attText.text = Att.ToString();
    _attText.color = GetStatColor(Att, previousAtt);
    _hpText.text = CurrentHp.ToString();
    _hpText.color = GetStatColor(CurrentHp, previousHp);
}
```

- `Att`/`CurrentHp`에 그대로 더한다 — "3번 겹쳐 12/6인 카드에 하나 더 겹치면 기본 4/2가 더해져 16/8"이라는 요청사항 예시가 정확히 이 누적 방식이다(매번 "현재 값 + 새로 낸 카드의 기본값").
- `CurrentHp`가 전투로 깎인 상태였다면 병합이 곧 회복을 겸한다 — 요청사항에 회복과 강화를 구분하는 언급이 없으므로 그대로 합산한다.
- `GetStatColor`는 [공격 판정 계획](plan-ingame-attack.md)에서 `TakeDamage`/`DoubleAtt`가 이미 쓰던 `private static` 헬퍼를 그대로 재사용한다 — 병합으로 두 값 다 오르므로 실질적으로 항상 초록으로 표시된다.
- 새 `Friend` 인스턴스를 만들지 않고 기존 인스턴스의 필드만 바꾼다 — 필드 슬롯의 자식 개수가 늘지 않으므로 `FieldSlot.IsOccupied` 계약이 그대로 유지된다.

### 3. 드래그 중 병합 가능 슬롯을 초록색으로 미리 보여주기

```csharp
// FriendCard.cs
public void OnBeginDrag(PointerEventData eventData)
{
    if (!InGameSceneManager.Instance.CanPlayFriend) { eventData.pointerDrag = null; return; }

    _canvasGroup.blocksRaycasts = false;
    transform.SetParent(_dragLayer, worldPositionStays: true);
    transform.SetAsLastSibling();

    InGameSceneManager.Instance.ShowMergePreview(Key); // 신규
}

public void OnEndDrag(PointerEventData eventData)
{
    _canvasGroup.blocksRaycasts = true;
    InGameSceneManager.Instance.HideMergePreview(); // 신규 — 드롭 성공/실패와 무관하게 항상 호출

    if (_wasPlaced) return;
    AttachToSlot(_homeSlot);
}
```

```csharp
// InGameSceneManager.cs
public void ShowMergePreview(int draggedKey)
{
    foreach (var slot in _fieldSlots)
    {
        if (slot.Index < 4 || !slot.IsOccupied) continue; // 유저 필드(4~6)만 — 유저가 드래그하는 카드는 유저 필드에만 놓임
        var friend = slot.GetComponentInChildren<Friend>();
        if (CanMerge(friend, draggedKey)) friend.SetHighlight(true, Color.green);
    }
}

public void HideMergePreview()
{
    foreach (var slot in _fieldSlots)
    {
        if (slot.Index < 4 || !slot.IsOccupied) continue;
        slot.GetComponentInChildren<Friend>().SetHighlight(false, Color.clear);
    }
}
```

- 병합 가능 여부 판정은 1번 결정의 `CanMerge`를 그대로 호출한다 — 판정식을 중복 작성하지 않으므로 "미리보기는 초록인데 드롭하면 거부되는" 불일치가 애초에 구조적으로 생길 수 없다.
- [공격 판정 계획](plan-ingame-attack.md)에서 이미 도입한 `Friend.SetHighlight` 오버레이를 그대로 재사용한다 — 새 시각 요소를 만들지 않고 색만 초록으로 켠다.
- 컴퓨터 필드(1~3)는 미리보기 대상이 아니다 — 지금은 유저만 드래그로 카드를 낸다.
- `HideMergePreview`는 `OnEndDrag` 맨 위에서 성공/실패 여부와 무관하게 호출한다 — 병합에 성공해 `existing`이 갱신된 뒤에도 초록 하이라이트가 그대로 남지 않도록.

---

## 클래스 구조

```
Friend (기존 파일 수정, InGame/)
└── MergeWith(int addAtt, int addHp)     ← 신규, Att/CurrentHp 누적 + 색상 갱신(GetStatColor 재사용)
    (SetHighlight는 공격 판정 계획에서 이미 추가됨 — 3번 결정이 그대로 재사용)

FriendCard (기존 파일 수정, InGame/)
├── OnBeginDrag(PointerEventData)   ← 수정, ShowMergePreview 호출 추가
└── OnEndDrag(PointerEventData)     ← 수정, HideMergePreview 호출 추가

InGameSceneManager (기존 파일 수정, InGame/)
├── _mergePunchScale : float = 0.1f [SerializeField]     ← 신규
├── _mergePunchDuration : float = 0.25f [SerializeField] ← 신규
├── TryPlaceFriendCard(FieldSlot, FriendCard)             ← 수정, 점유된 슬롯에서 `CanMerge` 판정 후 병합 분기 추가
├── CanMerge(Friend, int) : bool                          ← 신규(역할이 있는 target 판정, [치트 에디터 계획](plan-ingame-cheat.md)과 공유)
├── ShowMergePreview(int draggedKey)                      ← 신규, `CanMerge` 재사용
└── HideMergePreview()                                    ← 신규

CardTable.cs (기존 파일 수정, Data/Table/)
└── CardTarget.Any                    ← 신규 enum 멤버(1019 하이에나 등 — 필드 카드 역할일 때만 적용, `All`은 낸 카드 역할일 때만 적용)
```

---

## 파일 구성

```
Assets/Scripts/
├── Data/Table/
│   └── CardTable.cs         ← 기존 파일 수정 (CardTarget.Any 추가)
└── InGame/
    ├── Friend.cs               ← 기존 파일 수정 (MergeWith 추가)
    ├── FriendCard.cs           ← 기존 파일 수정 (드래그 시작/종료에 미리보기 호출 추가)
    └── InGameSceneManager.cs   ← 기존 파일 수정 (TryPlaceFriendCard 병합 분기, 미리보기 메서드 2종, 신규 필드 2종)
```

씬/프리팹 변경 없음 — 기존 `Friend`/`FriendCard`/`FieldSlot`을 그대로 사용하고, `PunchScale`도 [공격 판정 계획](plan-ingame-attack.md)에서 이미 씬에 구성된 대로 동작한다.

---

## 이번 범위에서 제외

- `CardCondition`(`Merge`/`Except`/`Die`)에 정의된 합체 발동 효과 — 상대 무작위 유닛 능력치 절반, 상대 플레이어 피해, 내 무작위 친구 공격력 2배, 모험가 생명력 회복, 사망 시 부활 등. [친구카드 능력 계획](plan-ingame-ability.md)에서 다룬다.
- 컴퓨터 측 필드(1/2/3번) 병합 — 컴퓨터는 아직 실제로 핸드/필드에 카드를 놓지 않는다([핸드/필드 배치 계획](plan-ingame-handfield.md)부터 이어지는 기존 제외 범위)
- 병합 스택 횟수를 별도 카운터로 저장/표시(예: "x3" 배지) — `Att`/`CurrentHp` 누적값만으로 충분(YAGNI), 필요해지면 후속 문서에서 카운터 필드 추가

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 빈 필드 슬롯에 드롭 | 기존 로직 그대로 — 새 `Friend` 생성 |
| 같은 종류(`Key` 일치) 친구카드가 있는 슬롯에 드롭 | `MergeWith`로 스탯 누적 + `PunchScale` 연출, 새로 낸 카드는 파괴 |
| 다른 종류(`Key` 불일치) 친구카드가 있는 슬롯에 드롭 | 기존과 동일하게 배치 거부 — `OnEndDrag`가 원래 핸드 슬롯으로 복귀 |
| 전투로 `CurrentHp`가 깎인 필드 친구카드에 병합 | 깎인 현재 값에 새 카드의 기본 `hp`를 더함(회복 겸 강화) |
| `DoubleAtt`(자기 자신 대상 공격)로 `Att`가 오른 필드 친구카드에 병합 | 오른 현재 값에 새 카드의 기본 `att`를 더함 |
| 필드가 아닌 곳(빈 화면 등)에 드롭 | 기존과 동일 — 원래 핸드 슬롯으로 복귀(이번 문서로 인한 변경 없음) |
| `CardTable.Instance.Get(card.Key)`가 `null`을 반환(테이블에 없는 key) | `CardTable.Get`이 이미 `LogError` 남김 — 기존 관례와 동일하게 방어 코드 없이 `NullReferenceException`으로 즉시 드러남 |
| 필드에 이미 `target=Any` 카드(1019 하이에나)가 있는 슬롯에 다른 종류 카드를 드롭 | 병합 허용 — `existingAcceptsAnything`로 통과, `existing`(하이에나)의 스탯에 낸 카드의 기본값이 더해짐 |
| 필드에 이미 `target=All` 카드(1004 블루베리)가 있는 슬롯에 다른 종류 카드를 드롭 | 병합 **불가** — `All`은 낸 카드 역할일 때만 적용되고 필드 카드 쪽에는 적용되지 않는다(`existingAcceptsAnything`가 `false`). 원래 핸드 슬롯으로 복귀 |
| `target=All` 카드(1004 블루베리)를 다른 종류가 있는 필드 슬롯(`target=Same`)에 드롭 | 병합 허용 — `mergeJoinsAnything`로 통과, 필드 카드의 스탯에 블루베리 기본값이 더해짐(필드 카드 정체성 유지) |
| `target=Any` 카드(1019 하이에나)를 다른 종류가 있는 필드 슬롯(`target=Same`)에 드롭 | 병합 **불가** — `Any`는 필드 카드 역할일 때만 적용되고 낸 카드 쪽에는 적용되지 않는다(`mergeJoinsAnything`가 `false`). 원래 핸드 슬롯으로 복귀 |
| 필드에 `target=All` 카드(블루베리)가 있는 상태에서 `target=Any` 카드(하이에나)를 드롭(또는 그 반대) | 병합 불가 — 블루베리는 받아주는 역할이 없고(`existingAcceptsAnything`=false), 하이에나는 합쳐지는 역할이 없다(`mergeJoinsAnything`=false). 어느 쪽도 서로의 역할과 맞지 않으면 거부된다 |
| 서로 다른 종류이고 둘 다 `target=Same` | 병합 불가 — 배치 거부, 원래 핸드 슬롯으로 복귀(기존과 동일) |
| 핸드 카드를 드래그하는 동안 유저 필드에 병합 가능한 슬롯이 하나도 없음 | `ShowMergePreview`가 아무 슬롯도 초록으로 켜지 않음 — 드롭하면 항상 거부(빈 슬롯 제외) |
| 드래그 중 초록으로 표시된 슬롯이 아닌 곳에 드롭 | 미리보기는 시각적 안내일 뿐 별도 강제력이 없다 — 실제 배치 가능 여부는 여전히 `TryPlaceFriendCard`가 판정 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 필드 4번에 att4/hp2 친구카드가 있는 상태에서 같은 key 카드를 4번에 드롭 | `Att` 4→8, `CurrentHp` 2→4, 두 텍스트 모두 초록, 드롭한 카드는 파괴, 원래 핸드 슬롯은 빈 자리 |
| 2 | 시나리오 1을 3회 반복해 12/6이 된 4번 필드에 같은 key 카드 1장 추가 드롭 | `Att` 12→16, `CurrentHp` 6→10 |
| 3 | 필드 4번(key A)에 다른 key B 카드를 드롭 | 배치 거부 — 카드가 원래 핸드 슬롯으로 복귀, 필드 4번의 `Att`/`CurrentHp` 불변 |
| 4 | 전투로 `CurrentHp`가 2(기본 4에서 감소)가 된 필드 친구카드에 같은 key(기본 hp 4) 카드 병합 | `CurrentHp` 2→6(초록 표시) |
| 5 | `DoubleAtt`로 `Att`가 8(기본 4에서 상승)이 된 필드 친구카드에 같은 key(기본 att 4) 카드 병합 | `Att` 8→12(초록 표시) |
| 6 | 빈 필드 슬롯에 카드를 드롭(회귀 확인) | 기존과 동일하게 새 `Friend` 생성, 병합 분기를 타지 않음 |
| 7 | 필드 4번에 1004(블루베리, `target=All`)가 있는 상태에서 다른 key 카드를 4번에 드롭 | 병합 **거부** — 블루베리는 필드 카드 역할일 때 받아주는 힘이 없음(`existingAcceptsAnything`가 `false`). 원래 핸드 슬롯으로 복귀, 필드 4번 불변 |
| 8 | 핸드에서 1004(블루베리, `target=All`)를 다른 key(`target=Same`) 카드가 있는 필드 슬롯으로 드래그 | 병합 허용 — `mergeJoinsAnything`로 통과, 필드 카드의 스탯에 블루베리 기본값이 더해짐, 필드 카드 정체성 유지 |
| 9 | 핸드에서 1019(하이에나, `target=Any`)를 다른 key(`target=Same`) 카드가 있는 필드 슬롯으로 드래그 | 병합 **거부** — 하이에나는 낸 카드 역할일 때 합쳐지는 힘이 없음(`mergeJoinsAnything`가 `false`). 원래 핸드 슬롯으로 복귀, 필드 카드 불변 |
| 10 | 필드 4번에 1019(하이에나, `target=Any`)가 있는 상태에서 다른 key(`target=Same`) 카드를 4번에 드롭 | 병합 허용 — `existingAcceptsAnything`로 통과, 하이에나(`existing`)의 스탯에 낸 카드 기본값이 더해짐, 필드에 남는 카드는 여전히 하이에나 |
| 11 | 서로 다른 key이고 둘 다 `target=Same`인 카드를 드래그해 필드에 드롭 | 배치 거부 — 원래 핸드 슬롯으로 복귀 |
| 12 | 필드에 1004(블루베리, `target=All`)가 있는 상태에서 1019(하이에나, `target=Any`)를 드래그해 드롭(또는 그 반대) | 병합 거부 — 블루베리는 받아주는 역할이 없고, 하이에나는 합쳐지는 역할이 없어 어느 쪽 조건도 만족하지 않음 |
| 13 | 유저 필드 4/5/6에 각각 key A/B/A 카드가 있는 상태에서 key A 카드를 드래그 시작 | 4번·6번 슬롯의 카드가 초록으로 하이라이트, 5번은 그대로 |
| 14 | 시나리오 13에서 드래그를 필드 밖에 드롭(취소) | `OnEndDrag`에서 초록 하이라이트 전부 해제, 카드는 원래 핸드 슬롯으로 복귀 |

---

## 구현 시 주의사항

- 병합 가능 여부는 "키가 같다 **또는** 필드 카드가 `Any` **또는** 낸 카드가 `All`"이다 — `All`/`Any`를 서로 바꿔 쓰거나 양쪽 다 검사하면 안 된다. `All`은 낸 카드 역할에서만, `Any`는 필드 카드 역할에서만 의미가 있다(요청자 확인으로 두 차례 수정된 끝에 확정된 최종 규칙, 위 "배경" 참고).
- `MergeWith`는 항상 "현재 값 + 기본값"이다 — 최초 스폰 시점의 값이나 카드 기본값 자체를 기준으로 삼지 않는다(전투로 깎인 채 병합해도 그 깎인 값에 더해짐).
- `card.NotifyPlaced()`는 병합 성공 시에도 배치 성공과 동일하게 반드시 `Destroy()` 이전에 호출한다 — 빠뜨리면 `OnEndDrag`가 이미 파괴 예정인 카드를 원래 슬롯으로 되돌리려다 어긋난다.
- 병합 불가로 거부하는 경로는 `NotifyPlaced()`를 호출하지 않고 그냥 `return`한다 — 여기서 실수로 호출하면 카드가 사라지지도, 복귀하지도 않는 상태가 된다.
- `CardTable.Instance.Get(card.Key)`는 호출 전에 결과를 따로 확인할 필요 없다 — `card.Key`는 애초에 `CardTable`에 있는 값으로 `SetKey`된 것이라 병합 시점에 다시 없을 수 없다(기존 `Get` 관례와 동일하게 방어 코드 없이 신뢰).
- `ShowMergePreview`/`TryPlaceFriendCard`는 둘 다 `CanMerge`를 호출한다 — 판정식을 각자 다시 작성하지 않는다. 과거에는 두 곳에 판정식을 따로 적어 유지보수 부담이 있었다.
- [친구카드 능력 계획](plan-ingame-ability.md)이 `MergeCardIntoSlot`의 `existing.MergeWith(...)` 직후에 `TriggerMergeAbility(existing, slotIndex)` 호출을 추가로 끼워 넣는다 — 이 문서의 코드를 그 문서 작업 시점에 다시 열어 수정하게 된다는 점을 미리 인지해 둔다.
- [InGame 필드 슬롯 치트 에디터 계획](plan-ingame-cheat.md)이 `CanMerge`/`MergeCardIntoSlot`을 판정/실행으로 분리해 `CheatMergeIntoSlot`과 공유하도록 리팩터링한다 — 정상 드래그 경로의 동작은 그대로 유지돼야 한다.

---

## 구현 후 체크리스트

- [x] `CardTable.cs`: `CardTarget.Any` 추가
- [x] `Friend.cs`: `MergeWith(int addAtt, int addHp)` 추가
- [x] `InGameSceneManager.cs`: `CanMerge`(역할이 있는 `target` 판정) 추출, `TryPlaceFriendCard`가 이를 호출하도록 수정, `_mergePunchScale`/`_mergePunchDuration` 필드 추가, `ShowMergePreview`/`HideMergePreview` 추가(`CanMerge` 재사용)
- [x] `FriendCard.cs`: `OnBeginDrag`/`OnEndDrag`에 미리보기 호출 연결
- [x] `CardTable.csv` → `CardTable.asset` 재생성 — 실제 Play 모드 테스트로 확인됨
- [x] 1004/1019 이종 합체 규칙 확정 — 최초 방향이 있는 판정(`All`=필드 카드 전용/`Any`=낸 카드 전용)이 실제로는 동작하지 않아 대칭 판정으로 1차 수정했으나, 요청자가 정확한 규칙(`All`=낸 카드가 무엇에든 합쳐짐/`Any`=필드 카드가 베이스로 받아줌 — 처음과 반대 역할 배정)을 다시 확인해줘 최종 반영(위 "배경"/1번 결정 참고)
- [ ] 테스트 시나리오 14개 재검증(Unity Play 모드에서 확인 필요 — 최종 역할 배정 반영 후)
- [ ] [plan-ingame-ability.md](plan-ingame-ability.md) — `CardCondition`(Merge/Except/Die) 발동 효과를 다루는 후속 계획 문서
- [ ] (추후) 컴퓨터 핸드/필드(1/2/3번) 배치가 구현되면 컴퓨터 측 병합도 자연히 동작하는지 확인
