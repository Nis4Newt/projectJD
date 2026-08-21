# InGame 친구카드 무덤(파괴 기록) 구현 계획

> 상위 문서: 없음 (독립 추가 기능 — 특정 상위 로드맵에서 파생되지 않음)
> 관련 문서: [공격 판정 계획](plan-ingame-attack.md) (`ResolveAttackRoutine`이 전투 사망을 `TryHandleDeath`로 넘기는 지점), [친구카드 능력 계획](plan-ingame-ability.md) (`TryHandleDeath`/`ApplyClausesToFriend`가 정의된 문서 — 이번 문서는 이 두 제거 지점에 무덤 기록만 끼워 넣는다), [InGame 필드 슬롯 치트 에디터 계획](plan-ingame-cheat.md) (`CheatDamageSlot`은 `TryHandleDeath`를 그대로 타므로 무덤 기록도 자동 적용, `CheatClearSlot`은 그대로 제외)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`, `JungleDice.InGame.Friend`, `JungleDice.InGame.FieldSlot`
> 범위: 필드의 친구카드가 실제로 파괴(`Destroy`)될 때 그 카드의 `Key`를 소유 진영(유저/컴퓨터)별 무덤 리스트에 저장한다. 무덤을 화면에 표시하는 UI, 무덤을 소비하는 카드 효과(예: "무덤에 있는 카드 수만큼"), 씬 전환 이후로도 무덤 데이터를 유지하는 것은 범위 밖.

---

## 배경

친구카드가 파괴되는 지점은 코드베이스에 이미 두 곳이 있다 — 전투/치트 데미지로 죽어 부활·포자감염 판정까지 거치는 `TryHandleDeath`([친구카드 능력 계획](plan-ingame-ability.md) 8번 결정, `InGameSceneManager.cs`), 그리고 병합 발동 효과로 죽어 즉시 제거되는 `ApplyClausesToFriend`(같은 문서 7번 결정). 두 경로 모두 지금은 `Destroy(friend.gameObject)`만 호출하고 끝난다 — 어떤 카드가 어느 진영에서 죽었는지는 즉시 사라지고 아무 데도 남지 않는다.

각 모험가(유저/컴퓨터)가 "지금까지 자신의 필드에서 파괴된 친구카드의 key"를 각자의 리스트로 갖고 있어야, 이후 무덤을 참조하는 카드 효과나 UI를 붙일 때 그 시점부터 새로 데이터를 쌓지 않고 바로 사용할 수 있다. 이번 문서는 그 리스트를 만들고, 정상적으로 파괴가 확정되는 두 지점에 key만 기록하는 최소 범위를 다룬다.

---

## 설계 목표

- 유저/컴퓨터 각각 자신만의 무덤 리스트를 갖는다 — 한 리스트에 소유자 태그를 붙이는 대신 `TurnOwner`별로 완전히 분리한다.
- 저장하는 값은 `Friend.Key`(`int`)뿐이다 — 죽은 시점의 att/hp, 부활 여부 같은 부가 정보는 저장하지 않는다(요청사항 그대로 "key만").
- "파괴"는 실제로 `Destroy`가 호출되는 순간만 해당한다 — `cond=Die` 카드가 `TryRevive`로 부활에 성공해 필드에 남는 경우는 죽지 않았으므로 기록하지 않는다.
- 소유 진영 판정은 새 규칙을 만들지 않고 기존 필드 절대 번호 판정(`slotIndex <= 3`이면 컴퓨터, 그 외엔 유저 — `GetBase`가 이미 쓰는 기준과 동일)을 그대로 재사용한다.
- `CheatClearSlot`(치트 에디터의 강제 슬롯 비우기)은 기존에도 `TryHandleDeath`를 거치지 않아 부활/포자감염을 트리거하지 않는다 — 같은 이유로 무덤에도 기록하지 않는다. 실제 게임 규칙상의 "죽음"이 아니라 에디터가 상태를 강제로 지우는 것이기 때문이다.

---

## 핵심 설계 결정

### 1. 진영별 무덤 리스트 2개 + 슬롯 번호 기반 저장 헬퍼

```csharp
private readonly List<int> _userGraveyard = new();
private readonly List<int> _computerGraveyard = new();

// slotIndex(필드 절대 번호 1~6)로 소유 진영을 판정해 그 진영의 무덤에 key를 저장한다 — GetBase와 동일한 기준(1~3 컴퓨터, 4~6 유저)
private void AddToGraveyard(int slotIndex, int key)
{
    var graveyard = slotIndex <= 3 ? _computerGraveyard : _userGraveyard;
    graveyard.Add(key);
}

public IReadOnlyList<int> GetGraveyard(TurnOwner owner) => owner == TurnOwner.User ? _userGraveyard : _computerGraveyard;
```

- `slotIndex <= 3 ? 컴퓨터 : 유저` 판정은 `GetBase(int slotIndex)`(`InGameSceneManager.cs`)가 이미 쓰는 것과 완전히 같은 기준이다 — 새 상수/판정식을 추가하지 않는다.
- `GetGraveyard`는 지금 당장 호출하는 곳이 없지만, 리스트를 `private`으로만 두면 "만들어 놓고 아무도 못 읽는" 상태가 된다 — 이후 무덤을 참조하는 카드 효과나 디버그 확인이 곧바로 이 메서드 하나로 가능하도록 최소한의 읽기 전용 공개 API만 남긴다(쓰기는 `AddToGraveyard`로만, 외부에서 직접 리스트를 변경할 수 없도록 `IReadOnlyList<int>`로 반환).

### 2. `TryHandleDeath` — 부활 실패로 실제 `Destroy`가 확정되는 지점에 기록

```csharp
private bool TryHandleDeath(Friend friend, Transform slotTransform)
{
    var data = CardTable.Instance.Get(friend.Key);
    if (data.cond == CardCondition.Die)
    {
        foreach (var clause in data.EffectClauses)
        {
            if (clause.Kind != CardEffectClauseKind.Spawn) continue;
            if (!friend.TryRevive(clause.SpawnAtt, clause.SpawnHp)) break; // 이미 한 번 부활했으면 그대로 사망 처리로 진행

            friend.PunchScale(_mergePunchScale, _mergePunchDuration);
            return true; // 부활 성공 — 죽지 않았으므로 무덤에 기록하지 않음
        }
    }

    bool hasSpawnMark = friend.SpawnMark.HasMark;
    int spawnKey = friend.SpawnMark.Key, spawnAtt = friend.SpawnMark.Att, spawnHp = friend.SpawnMark.Hp;
    AddToGraveyard(slotTransform.GetComponent<FieldSlot>().Index, friend.Key); // 신규
    Destroy(friend.gameObject);
    if (hasSpawnMark) SpawnFriendDirectly(spawnKey, spawnAtt, spawnHp, slotTransform);
    return false;
}
```

- `slotTransform`은 `ResolveAttackRoutine`(`_attackerSlot.transform`/`targetSlot.transform`)과 `CheatDamageSlot`(`slot.transform`) 양쪽 모두에서 항상 `FieldSlot` 자신의 트랜스폼이다 — 공격 연출 중 `attacker.SetParent(_attackLayer)`로 카드 오브젝트 자체는 잠시 다른 부모로 옮겨가지만, `TryHandleDeath`에 넘기는 `slotTransform` 인자는 그것과 무관하게 항상 원래 필드 슬롯을 가리키므로 `GetComponent<FieldSlot>()`이 어긋날 일이 없다.
- 부활(`return true`) 분기는 기존 코드 흐름을 그대로 두고 손대지 않는다 — 무덤 기록은 오직 실제 `Destroy` 직전 한 줄로만 추가된다.
- 포자감염으로 새 카드가 같은 슬롯에 태어나도(`hasSpawnMark`) 무덤에는 원래 죽은 카드의 `friend.Key`만 남는다 — 새로 스폰된 카드는 아직 죽지 않았으므로 별개다.

### 3. `ApplyClausesToFriend` — 능력 피해로 즉시 제거되는 지점에 기록

```csharp
private void ApplyClausesToFriend(List<CardEffectClause> clauses, Friend target)
{
    if (target == null) return;

    foreach (var clause in clauses)
    {
        // ... 기존 스탯/데미지/회복/방어막/스폰 처리 그대로 ...
    }

    if (target.IsDead)
    {
        AddToGraveyard(target.transform.parent.GetComponent<FieldSlot>().Index, target.Key); // 신규
        Destroy(target.gameObject);
    }
}
```

- `target`은 `Instantiate(_friendPrefab, slot.transform)`으로 생성돼 필드 슬롯의 바로 아래 자식으로 있는 인스턴스다(신규 배치/컴퓨터 배치/치트 배치 전부 동일한 패턴) — 능력 적용 과정에는 `ResolveAttackRoutine`과 달리 부모를 임시로 바꾸는 연출이 없으므로 `target.transform.parent`가 항상 그 카드가 실제로 놓인 `FieldSlot`이다.
- 능력으로 죽는 경우는 `TryHandleDeath`를 거치지 않는다(부활/포자감염과 무관하게 즉시 제거 — [친구카드 능력 계획](plan-ingame-ability.md) 7번 결정) — 무덤 기록도 그 지점에 독립적으로 추가한다.

---

## 클래스 구조

```
InGameSceneManager (기존 파일 수정, InGame/)
├── _userGraveyard : List<int>                    ← 신규
├── _computerGraveyard : List<int>                ← 신규
├── AddToGraveyard(int slotIndex, int key)        ← 신규, 슬롯 번호로 소유 진영 판정 후 저장
├── GetGraveyard(TurnOwner owner) : IReadOnlyList<int> ← 신규, 읽기 전용 공개 API
├── TryHandleDeath(Friend, Transform)              ← 수정, 실제 Destroy 직전 AddToGraveyard 호출 추가
└── ApplyClausesToFriend(List<CardEffectClause>, Friend) ← 수정, 사망 확정 시 AddToGraveyard 호출 추가
```

---

## 파일 구성

```
Assets/Scripts/InGame/
└── InGameSceneManager.cs   ← 기존 파일 수정(무덤 리스트 2종, AddToGraveyard/GetGraveyard 추가, TryHandleDeath/ApplyClausesToFriend에 기록 호출 추가)
```

씬/프리팹 변경 없음.

---

## 이번 범위에서 제외

- 무덤 내용을 화면에 표시하는 UI(아이콘 나열, 개수 텍스트 등) — 지금은 내부 데이터만 쌓는다. 필요해지면 후속 문서에서 다룬다.
- 무덤을 참조하는 카드 효과(예: "무덤에 있는 카드 수만큼 효과 증폭") — `CardEffectClauseKind`/`CardAbilityScope` 확장이 필요한 별도 기능이라 범위 밖.
- 죽은 카드의 att/hp, 죽은 시점, 몇 번째로 죽었는지 등 key 이외의 부가 정보 저장 — 요청사항이 "key만"으로 명시했다.
- `CheatClearSlot`(치트 강제 슬롯 비우기)에서의 기록 — 기존에도 부활/포자감염을 트리거하지 않는 비-사망 경로이므로 무덤 기록도 동일하게 제외한다.
- 씬 재진입/게임 재시작 시 무덤 데이터 유지(영속화) — `InGameSceneManager`가 씬 전환으로 파괴되면 무덤도 함께 사라지는 기존 씬 로컬 싱글턴 생명주기를 그대로 따른다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 유저 필드(4~6)의 카드가 전투로 사망 | `AddToGraveyard(slotIndex, key)`가 `slotIndex > 3`이므로 `_userGraveyard`에 저장 |
| 컴퓨터 필드(1~3)의 카드가 전투로 사망 | `_computerGraveyard`에 저장 |
| `cond=Die` 카드가 `TryRevive`로 부활 성공 | `TryHandleDeath`가 `return true`로 조기 종료 — `Destroy`/`AddToGraveyard` 모두 호출되지 않음 |
| `cond=Die` 카드가 이미 한 번 부활한 뒤 다시 사망 | `HasRevived == true`라 `TryRevive`가 `false` → 일반 사망 처리로 진행, 이번엔 정상적으로 무덤에 기록 |
| 포자감염 마킹된 카드가 사망해 같은 슬롯에 새 카드가 태어남 | 무덤에는 원래 죽은 카드의 key만 기록 — 새로 태어난 카드는 아직 죽지 않았으므로 무덤과 무관 |
| 능력 발동(`ApplyClausesToFriend`)으로 대상이 사망 | `TryHandleDeath`를 거치지 않지만 동일하게 즉시 무덤 기록 후 `Destroy` |
| `CheatDamageSlot`으로 데미지를 줘서 사망 | `TryHandleDeath`를 그대로 호출하는 경로라 무덤 기록도 정상 사망과 동일하게 자동 적용 |
| `CheatClearSlot`으로 슬롯을 강제로 비움 | 무덤에 기록되지 않음(설계 목표) |
| 같은 카드가 부활 후 다시 죽는 등 같은 key가 여러 번 파괴됨 | `List<int>.Add`는 중복을 그대로 허용 — 무덤에 같은 key가 여러 번 쌓일 수 있다(의도된 동작, 파괴 "횟수" 자체가 정보) |
| `GetGraveyard`를 아직 아무도 호출하지 않는 상태에서 게임 진행 | 리스트는 계속 쌓이기만 하고 소비되지 않음 — 이후 이 데이터를 쓰는 기능이 추가될 때 그 시점부터 바로 사용 가능(설계 목표) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 유저 필드 카드가 전투에서 상대 공격으로 사망(부활 카드 아님) | `GetGraveyard(TurnOwner.User)`에 그 카드의 key가 1개 추가, `GetGraveyard(TurnOwner.Computer)`는 불변 |
| 2 | 컴퓨터 필드 카드가 전투에서 사망 | `GetGraveyard(TurnOwner.Computer)`에 key 추가 |
| 3 | `cond=Die`(1018) 카드가 처음 사망 | 부활 성공 — 무덤에 기록되지 않음, 필드에 그대로 남음 |
| 4 | 시나리오 3의 카드가 다시 사망 | `HasRevived == true`라 이번엔 정상 사망 — 무덤에 key 추가 |
| 5 | 포자감염(1010) 마킹된 카드가 사망 | 무덤에 원래 카드의 key가 추가, 같은 슬롯에 새로 태어난 1010은 무덤과 무관 |
| 6 | 병합 발동 효과(`dmg`/`Att÷2,Hp÷2` 등)로 대상이 즉시 제거 | `ApplyClausesToFriend` 경로로 사망한 대상 진영의 무덤에 key 추가 |
| 7 | [치트 에디터](plan-ingame-cheat.md)로 `CheatDamageSlot`을 써서 카드를 죽임 | `TryHandleDeath` 경유라 정상 사망과 동일하게 무덤에 기록됨 |
| 8 | 치트 에디터로 `CheatClearSlot`을 써서 슬롯을 강제로 비움 | 무덤에 아무 것도 추가되지 않음 |
| 9 | 같은 key의 카드가 서로 다른 시점에 2번 죽음(부활 후 재사망 등) | 무덤 리스트에 같은 key가 2개 쌓임(중복 허용) |

---

## 구현 시 주의사항

- `AddToGraveyard`는 반드시 `Destroy` 호출 **전에**(또는 같은 프레임 내 `Destroy` 이전 어디서든) `friend.Key`/`target.Key`를 읽어 호출한다 — `Destroy`는 프레임 끝까지 실제 파괴를 지연시키므로 순서 자체가 크래시로 이어지지는 않지만, 두 지점의 기존 코드 흐름과 자연스럽게 맞추기 위해 `Destroy` 직전에 둔다.
- `TryHandleDeath`에서 부활(`return true`) 분기와 무덤 기록 분기를 혼동하지 않는다 — 부활 성공 시점에는 아직 `Destroy`가 호출되지 않으므로 무덤에 기록하면 안 된다(설계 목표 위반).
- `ApplyClausesToFriend`의 `target.transform.parent`는 능력 적용 시점에 항상 필드 슬롯이라는 전제가 깨지지 않도록 주의 — 만약 이후 다른 문서에서 이 메서드 호출 전에 대상을 임시로 다른 부모로 옮기는 연출을 추가한다면(현재는 없음), 이 지점의 슬롯 판정도 함께 재검토해야 한다.
- 새 카드 key나 진영을 하드코딩하지 않는다 — `AddToGraveyard`는 `slotIndex <= 3`이라는 기존 판정 기준 하나만 재사용한다(`GetBase`와 동일 기준 유지, 별도 상수 중복 정의 금지).

---

## 구현 후 체크리스트

- [x] `InGameSceneManager.cs`: `_userGraveyard`/`_computerGraveyard` 필드, `AddToGraveyard(int, int)`, `GetGraveyard(TurnOwner)` 추가
- [x] `InGameSceneManager.cs`: `TryHandleDeath`의 실제 `Destroy` 직전에 `AddToGraveyard` 호출 추가
- [x] `InGameSceneManager.cs`: `ApplyClausesToFriend`의 사망 확정 `Destroy` 직전에 `AddToGraveyard` 호출 추가
- [ ] 테스트 시나리오 9개 재검증(Unity Play 모드에서 확인 필요)
- [ ] (추후) 무덤 표시 UI, 무덤을 참조하는 카드 효과
