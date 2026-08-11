# InGame 친구카드 합체 계획

> 상위 문서: [InGame 로직 개요](plan-ingame.md) ([핸드/필드 배치 계획](plan-ingame-handfield.md)·[공격 판정 계획](plan-ingame-attack.md) 이후 — 두 문서 모두 "(추후) 합체 판정을 다루는 후속 계획 문서"로 남겨둔 지점)
> 관련 문서: [핸드/필드 배치 계획](plan-ingame-handfield.md) (`FriendCard`/`FieldSlot`/`InGameSceneManager.TryPlaceFriendCard`를 이번 문서가 그대로 확장), [공격 판정 계획](plan-ingame-attack.md) (`Friend.Att`/`CurrentHp`/`GetStatColor`를 이번 문서가 재사용)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`, `JungleDice.InGame.Friend`, `JungleDice.InGame.FriendCard`, `JungleDice.InGame.FieldSlot`, `JungleDice.Data.Table.CardTable`(`att`/`hp`)
> 범위: 유저 필드(4/5/6번)에서 핸드 카드를 이미 같은 종류(`Friend.Key` 일치)의 친구카드가 놓인 슬롯에 드롭했을 때, 새 카드는 사라지고 필드의 친구카드에 방금 낸 카드의 기본 공격력/체력이 더해지는 병합 판정만 다룬다. `CardTable`의 `cond`(Merge/Except)·`target`(Same/All) 필드를 이용한 합체 발동 효과·이종 합체는 범위 밖(아래 "이번 범위에서 제외" 참고).

---

## 배경

[핸드/필드 배치 계획](plan-ingame-handfield.md)의 `TryPlaceFriendCard`는 지금 "슬롯이 이미 점유돼 있으면 아무 것도 하지 않는다"(`if (slot.IsOccupied) return;`)로만 되어 있다 — 점유된 슬롯에 드롭하면 항상 실패로 취급되어 카드가 원래 자리로 돌아간다. [InGame 로직 개요](plan-ingame.md)와 [공격 판정 계획](plan-ingame-attack.md)는 이 지점을 "(추후) 합체 판정을 다루는 후속 계획 문서"로 명시적으로 남겨뒀다 — 이번 문서가 그 후속이다.

`CardTable`(`CardTable.cs`/`CardTable.csv`)에는 이미 `cond`(`None`/`Merge`/`Except`)와 `target`(`Same`/`All`) 컬럼이 정의돼 있고 실제 데이터도 채워져 있지만, 코드 어디에서도 아직 참조하지 않는다(`grep` 확인). `cond=Merge`인 카드들의 `explain`을 보면 "상대 무작위 유닛 능력치 절반", "상대 플레이어에게 2의 데미지", "내 모험가의 생명력 2 회복" 같은 발동 효과가 정의돼 있는데, 상대 필드에서 무작위 대상을 고르는 로직도 모험가 체력 시스템도 아직 코드베이스에 없다. `target=All`인 카드(1004, 파란 딸기)는 "모든 종류의 친구와 합칠 수 있다"는 예외 규칙이다.

요청자 확인 결과, 이번 문서는 **요청사항 그대로의 최소 병합 규칙**(같은 종류일 때만 병합, 공격력·체력 단순 합산)만 다루고, `cond`/`target` 필드를 이용한 확장(이종 합체, 발동 효과)은 각각 별도 후속 문서로 미룬다.

---

## 설계 목표

- 이미 같은 종류(`Friend.Key == FriendCard.Key`)의 친구카드가 놓인 필드 슬롯에 드롭하면 병합, 다르면 기존과 동일하게 배치 거부
- 병합 시 필드 친구카드의 `Att`/`CurrentHp`에 방금 낸 카드의 **기본**(`CardTable` 원본) 공격력/체력을 더한다 — 필드 친구카드가 전투로 깎이거나(`TakeDamage`) 자기 자신 병합으로 오른(`DoubleAtt`) 상태라도 그 "현재 값"에 더한다(고정 기준값이 아님)
- 새로 낸 카드(`FriendCard`)는 병합 성공 시 그대로 사라진다 — 필드에는 항상 기존 `Friend` 인스턴스 하나만 남는다(새 `Friend`를 생성하지 않음)
- 필드 슬롯 하나에는 언제나 자식이 최대 하나(`FieldSlot.IsOccupied == transform.childCount > 0`) — 병합도 이 불변식을 그대로 유지, 별도 상태 플래그 없음
- 병합 판정과 스탯 반영은 책임을 분리한다 — "병합해도 되는가"(키 비교)는 `InGameSceneManager.TryPlaceFriendCard`, "스탯을 어떻게 반영하는가"(값 갱신+색상)는 `Friend.MergeWith`가 담당한다. [공격 판정 계획](plan-ingame-attack.md)의 "하이라이트 on/off는 `Friend`, 지시는 매니저"와 같은 책임 분리 패턴
- 병합 시 시각 피드백은 기존 `Friend.PunchScale`을 재사용 — 새 트윈 로직을 만들지 않는다

---

## 핵심 설계 결정

### 1. 병합 판정: `TryPlaceFriendCard`에서 슬롯의 기존 `Friend.Key`와 비교

```csharp
[SerializeField] private float _mergePunchScale = 0.1f;
[SerializeField] private float _mergePunchDuration = 0.25f;

public void TryPlaceFriendCard(FieldSlot slot, FriendCard card)
{
    if (slot.IsOccupied)
    {
        var existing = slot.GetComponentInChildren<Friend>();
        if (existing.Key != card.Key) return; // 다른 종류 — 배치 거부, OnEndDrag가 원래 슬롯으로 복귀시킴

        var data = CardTable.Instance.Get(card.Key);
        existing.MergeWith(data.att, data.hp);
        existing.PunchScale(_mergePunchScale, _mergePunchDuration);

        card.NotifyPlaced();
        Destroy(card.gameObject);
        return;
    }

    var friend = Instantiate(_friendPrefab, slot.transform);
    friend.SetKey(card.Key);

    card.NotifyPlaced();
    Destroy(card.gameObject);
}
```

- 기존의 "점유돼 있으면 무조건 리턴" 분기를 "점유돼 있으면 키를 비교해 병합할지, 아예 거부할지 정하는" 분기로 확장한다 — 빈 슬롯 배치 경로(아래 절반)는 손대지 않는다.
- `existing.Key != card.Key`일 때는 `card.NotifyPlaced()`를 호출하지 않고 그대로 `return`한다 — [핸드/필드 배치 계획](plan-ingame-handfield.md)이 이미 정의한 "배치 실패 시 `_wasPlaced`가 `false`로 남아 `OnEndDrag`가 원래 슬롯으로 복귀시킨다"는 경로를 그대로 재사용, 병합 전용 되돌리기 로직을 새로 만들 필요가 없다.
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

---

## 클래스 구조

```
Friend (기존 파일 수정, InGame/)
└── MergeWith(int addAtt, int addHp)     ← 신규, Att/CurrentHp 누적 + 색상 갱신(GetStatColor 재사용)

InGameSceneManager (기존 파일 수정, InGame/)
├── _mergePunchScale : float = 0.1f [SerializeField]     ← 신규
├── _mergePunchDuration : float = 0.25f [SerializeField] ← 신규
└── TryPlaceFriendCard(FieldSlot, FriendCard)             ← 수정, 점유된 슬롯에서 키 비교 후 병합 분기 추가
```

---

## 파일 구성

```
Assets/Scripts/
└── InGame/
    ├── Friend.cs               ← 기존 파일 수정 (MergeWith 추가)
    └── InGameSceneManager.cs   ← 기존 파일 수정 (TryPlaceFriendCard 병합 분기, 신규 필드 2종)
```

씬/프리팹 변경 없음 — 기존 `Friend`/`FriendCard`/`FieldSlot`을 그대로 사용하고, `PunchScale`도 [공격 판정 계획](plan-ingame-attack.md)에서 이미 씬에 구성된 대로 동작한다.

---

## 이번 범위에서 제외

- `CardTarget`(`Same`/`All`) 반영 — `target=All`인 카드(예: 1004 파란 딸기, "모든 종류의 친구와 합칠 수 있습니다")가 다른 종류 카드와도 병합되는 규칙. 이번 문서는 `Friend.Key == FriendCard.Key`(완전히 같은 종류)일 때만 병합한다(요청자 확인).
- `CardCondition`(`Merge`/`Except`)에 정의된 합체 발동 효과 — 상대 무작위 유닛 능력치 절반, 상대 플레이어 피해, 내 무작위 친구 공격력 2배, 모험가 생명력 회복 등. 상대 필드에서 무작위 대상을 고르는 로직, 플레이어/모험가 체력 시스템이 아직 코드베이스에 없어 이번 범위에서 다룰 수 없다(요청자 확인).
- 컴퓨터 측 필드(1/2/3번) 병합 — 컴퓨터는 아직 실제로 핸드/필드에 카드를 놓지 않는다([핸드/필드 배치 계획](plan-ingame-handfield.md)부터 이어지는 기존 제외 범위)
- 병합 스택 횟수를 별도 카운터로 저장/표시(예: "x3" 배지) — `Att`/`CurrentHp` 누적값만으로 충분(YAGNI), 필요해지면 후속 문서에서 카운터 필드 추가
- 병합 가능 여부를 드래그 중 미리 알려주는 것(예: 같은 종류 슬롯 하이라이트) — 드롭해봐야 결과를 알 수 있는 지금 방식 그대로 유지

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

---

## 구현 시 주의사항

- 병합 판정은 오직 `Friend.Key == FriendCard.Key` 비교뿐이다 — `CardTable`의 `target` 필드는 이번 범위에서 참조하지 않는다(참조하면 파란 딸기 같은 `target=All` 카드가 의도와 다르게 거부/허용될 수 있음).
- `MergeWith`는 항상 "현재 값 + 기본값"이다 — 최초 스폰 시점의 값이나 카드 기본값 자체를 기준으로 삼지 않는다(전투로 깎인 채 병합해도 그 깎인 값에 더해짐).
- `card.NotifyPlaced()`는 병합 성공 시에도 배치 성공과 동일하게 반드시 `Destroy()` 이전에 호출한다 — 빠뜨리면 `OnEndDrag`가 이미 파괴 예정인 카드를 원래 슬롯으로 되돌리려다 어긋난다.
- 다른 종류라 거부하는 경로는 `NotifyPlaced()`를 호출하지 않고 그냥 `return`한다 — 여기서 실수로 호출하면 카드가 사라지지도, 복귀하지도 않는 상태가 된다.
- `CardTable.Instance.Get(card.Key)`는 호출 전에 결과를 따로 확인할 필요 없다 — `card.Key`는 애초에 `CardTable`에 있는 값으로 `SetKey`된 것이라 병합 시점에 다시 없을 수 없다(기존 `Get` 관례와 동일하게 방어 코드 없이 신뢰).

---

## 구현 후 체크리스트

- [ ] `Friend.cs`: `MergeWith(int addAtt, int addHp)` 추가
- [ ] `InGameSceneManager.cs`: `TryPlaceFriendCard`에 병합 분기 추가, `_mergePunchScale`/`_mergePunchDuration` 필드 추가
- [ ] 테스트 시나리오 6개 검증
- [ ] (추후) `CardTarget`(Same/All) 반영 — 이종 합체(`target=All`)를 다루는 별도 계획 문서
- [ ] (추후) `CardCondition`(Merge/Except) 발동 효과 — 상대 필드 무작위 대상 선택, 플레이어/모험가 체력 시스템과 함께 다루는 별도 계획 문서
- [ ] (추후) 컴퓨터 핸드/필드(1/2/3번) 배치가 구현되면 컴퓨터 측 병합도 자연히 동작하는지 확인
