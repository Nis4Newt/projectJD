# 친구카드 상태 머신 구현 계획

> 상위 문서: [데미지 이펙트 구현 계획](plan-ingame-damage-effect.md) — "사망 확정 시 진동 후 지연 파괴" 연출을 이미 설계했으나 미구현 상태였다. 이번 문서가 그 사망 처리를 명시적 상태(enum)로 구현한다. 공격 상태는 [공격 판정 계획](plan-ingame-attack.md)이 이미 구현한 `ResolveAttackRoutine`의 연출 흐름에 상태 전이만 얹는다.
> 의존 관계: `JungleDice.InGame.WorldFriend`, `JungleDice.InGame.InGameSceneManager`(`ResolveAttackRoutine`/`TryHandleDeath`/`ApplyClausesToFriend`), `DG.Tweening`
> 범위: 필드에 배치되는 `WorldFriend`의 생명주기를 `Spawn → Idle ⇄ Attack`, `(Idle/Attack) → Dead → Destroying → 실제 파괴`로 명시적 상태(enum)로 관리한다. 사망 확정 시 짧은 진동(Dead)만 연출하고, 진동이 끝나면 곧바로 `Destroying`으로 전이해 `Destroy`를 실행한다(Destroying 자체는 별도 연출 없음). 덱 미리보기용 `Friend`(UI)는 전투에 참여하지 않아 대상이 아니다([WorldFriend 신설 계획](worldspace/plan-ingame-worldspace-worldfriend.md)이 이미 "`Friend.cs`는 수정하지 않는다"로 확정한 정책을 그대로 따름). 데미지 숫자 팝업(`DamageEffect`)·타격음·부활/포자감염 판정 로직 자체는 범위 밖(기존 로직 그대로, 상태 전이 지점만 이 문서가 제공).

---

## 배경

지금 `WorldFriend`에는 명시적 상태가 없다 — "지금 공격 중인지", "죽었는지"는 `InGameSceneManager`가 그때그때 `IsDead`를 읽거나 코루틴 진행 위치로만 판단한다. 사망 시에는 `TryHandleDeath`/`ApplyClausesToFriend`가 그 자리에서 곧바로 `Destroy(friend.gameObject)`를 호출해 아무 연출 없이 즉시 사라진다. 요청사항은 카드 생명주기를 `spawn → idle → attack → idle`(반복), `idle → dead → destroy`의 명시적 상태로 정리하고, 사망 시 진동 후 파괴되는 연출을 추가하는 것이다.

---

## 설계 목표

- 상태는 열거형(`FriendState`) + 가드된 전이 메서드로 표현한다 — 별도 FSM 프레임워크는 도입하지 않는다(YAGNI, 프로젝트에 상태 전이 라이브러리가 없고 상태 종류도 5개로 적다).
- 상태 전이는 `WorldFriend` 스스로 책임진다 — `InGameSceneManager`는 기존과 동일하게 "언제 공격시키고 언제 데미지를 줄지"만 지시하고, `Attack` 진입/해제 호출과 `Die()` 호출만 추가한다.
- 사망 처리(`Dead → Destroying → 파괴`)는 `WorldFriend` 내부에서 자기 완결적으로 진행한다 — 매니저는 게임 로직 상태(그레이브야드 등록, 슬롯 비우기)만 즉시 처리하고, 시각적 파괴 타이밍은 `WorldFriend`가 스스로 예약한다([데미지 이펙트 계획]이 세운 "로직은 즉시, 연출만 지연" 원칙을 그대로 계승).
- 이미 `Dead`/`Destroying` 상태인 카드에 대한 중복 사망 처리를 막는다(가드).
- EventBus에 새 이벤트를 추가하지 않는다 — 상태 변화를 구독해야 할 제3의 시스템이 현재 없고, `WorldFriend`↔`InGameSceneManager` 사이의 직접 호출 관례를 그대로 따른다.

---

## 핵심 설계 결정

### 1. `FriendState` 열거형 + `State` 프로퍼티

```csharp
public enum FriendState { Spawn, Idle, Attack, Dead, Destroying }

public FriendState State { get; private set; } = FriendState.Spawn;
```

### 2. `SetKey` 종료 시 `Spawn → Idle` 자동 전이

스폰 연출이 아직 없으므로, `SpawnWorldFriend` 직후 항상 호출되는 `SetKey`의 마지막 줄에서 그 프레임에 바로 `Idle`로 전이한다. `SpawnWorldFriend`를 호출하는 4개 지점(`TryPlaceFriendCard`/`ExecuteComputerAction`/`CheatSetSlot`/`SpawnFriendDirectly`) 모두 직후 `SetKey`를 호출하므로, 호출부를 하나도 건드리지 않고 `WorldFriend` 자신만 수정하면 된다.

```csharp
public void SetKey(int key)
{
    Key = key;

    var data = CardTable.Instance?.Get(key);
    if (data == null)
    {
        State = FriendState.Idle; // CardTable.Get이 이미 LogError를 남김 — 상태 전이는 그대로 진행
        return;
    }

    // ... 기존 로직(Att/CurrentHp/스프라이트/텍스트 세팅) ...

    State = FriendState.Idle; // 스폰 연출 없음 — 즉시 전이. 연출이 생기면 이 한 줄만 지연시키면 됨
}
```

`CardTable` 조회가 실패하는 경로(잘못된 key)에서도 `State`는 반드시 `Idle`로 전이시킨다 — 조기 반환 전에 전이를 빼먹으면 이 카드가 영원히 `Spawn`에 머물러, 이후 "Spawn 상태 카드는 아직 상호작용 대상이 아니다" 류의 코드가 추가됐을 때 이 카드만 예외적으로 걸러지지 않는 문제가 생긴다.

### 3. `Attack` 상태 — attacker에만 적용, `ResolveAttackRoutine`을 감싼다

공격을 받는 쪽(target)은 공격 주체가 아니므로 `Attack` 상태를 거치지 않는다 — 사용자 요청의 "idle → 공격할 때 attack"은 attacker 관점이다.

```csharp
public void EnterAttack() => State = FriendState.Attack;
public void EnterIdle() => State = FriendState.Idle;
```

`ResolveAttackRoutine`(`InGameSceneManager.cs`) 수정:

```csharp
private IEnumerator ResolveAttackRoutine(FieldSlot targetSlot)
{
    var attacker = _attackerSlot.PlacedFriend;
    attacker.EnterAttack(); // 코루틴 시작 시점에 Attack 진입

    // ... 하이라이트/펀치/이동/피해 판정은 기존 그대로 ...

    // attackerDied가 false면 TryHandleDeath를 호출하지 않고(단락 평가) 곧바로 true — 생존 또는 부활 성공
    bool attackerAlive = !attackerDied || TryHandleDeath(attacker, _attackerSlot.transform);
    if (attackerAlive)
    {
        attacker.SetParent(_attackerSlot.transform);
        attacker.SetHighlight(false, Color.clear);
        attacker.EnterIdle(); // Attack 종료, Idle로 복귀
    }

    // ... target 처리는 기존 그대로(EnterAttack 호출 없음) ...
}
```

부활 실패(진짜 사망)나 target 사망은 `TryHandleDeath`/`ApplyClausesToFriend` 내부에서 `Die()`로 이어지므로(아래 결정 4) 여기서 별도 처리하지 않는다.

### 4. `Dead → Destroying → 파괴` 자기 완결 시퀀스

```csharp
[SerializeField] private float _deathShakeDuration = 0.2f;
[SerializeField] private float _deathShakeStrength = 0.15f;

// 사망 확정된 카드를 파괴한다 — 이미 Dead/Destroying이면 무시(같은 프레임에 중복 호출되는 것을 방어)
public void Die()
{
    if (State == FriendState.Dead || State == FriendState.Destroying) return;

    State = FriendState.Dead;
    transform.DOKill();
    transform.DOShakePosition(_deathShakeDuration, _deathShakeStrength)
        .OnComplete(EnterDestroying);
}

// Destroying은 별도 연출 없이 곧바로 파괴 — Dead(진동)만 연출을 가지고, Destroying은 상태 전이 자체가 "파괴 실행"이다
private void EnterDestroying()
{
    State = FriendState.Destroying;
    Destroy(gameObject);
}
```

`InGameSceneManager`의 두 사망 처리 지점을 "즉시 `Destroy`"에서 "그레이브야드/슬롯 정리 후 `Die()` 호출"로 교체한다. 두 지점의 정리 로직이 동일해 공통 private 헬퍼 `DestroyFriend(WorldFriend, FieldSlot)`로 묶여 있다([데미지 이펙트 계획](plan-ingame-damage-effect.md) 결정 3 — 그 문서가 추가한 `ReleaseDamageEffects` 호출도 이 헬퍼 안에 있다).

```csharp
// TryHandleDeath 꼬리(부활 실패 이후)
bool hasSpawnMark = friend.SpawnMark.HasMark;
int spawnKey = friend.SpawnMark.Key, spawnAtt = friend.SpawnMark.Att, spawnHp = friend.SpawnMark.Hp;
DestroyFriend(friend, slotTransform.GetComponent<FieldSlot>());
if (hasSpawnMark) SpawnFriendDirectly(spawnKey, spawnAtt, spawnHp, slotTransform);
return false;
```

```csharp
// ApplyClausesToFriend 사망 분기
if (target.IsDead)
{
    var slot = target.transform.parent.GetComponent<FieldSlot>();
    DestroyFriend(target, slot);
}
```

`RemoveFriend()`가 슬롯 점유 여부(`PlacedFriend`)를 즉시 `null`로 만들기 때문에, `Die()`가 진행 중이어도(오브젝트가 아직 파괴 전이어도) 그 슬롯은 곧바로 "비어있음"으로 판정된다 — 다음 턴 판정이 연출을 기다리지 않는다.

### 5. `CheatClearSlot`은 그대로 둔다 — 상태 머신 대상이 아님

`CheatClearSlot`은 기존 주석대로 "`TryHandleDeath`를 거치지 않아 부활/포자감염을 트리거하지 않는" 강제 제거이지 사망이 아니다. 이번 문서로 도입하는 `Die()`를 여기 끼워 넣지 않고 기존 즉시 `Destroy`를 그대로 유지한다.

---

## 클래스 구조

```
WorldFriend (기존 파일 수정, InGame/)
├── FriendState 열거형(Spawn/Idle/Attack/Dead/Destroying)   ← 신규
├── State : FriendState { get; private set; } = Spawn       ← 신규
├── SetKey(...)                                              ← 수정, 마지막에 State = Idle
├── EnterAttack() / EnterIdle()                              ← 신규
├── Die()                                                     ← 신규, Dead 진입 + 진동 트윈, 중복 호출 가드
├── EnterDestroying()                                         ← 신규 private, Destroying 진입 + 즉시 Destroy(연출 없음)
└── _deathShakeDuration/_deathShakeStrength : float [SerializeField]  ← 신규, 0.2f/0.15f

InGameSceneManager (기존 파일 수정, InGame/)
├── ResolveAttackRoutine(...)     ← 수정, attacker.EnterAttack()/EnterIdle() 호출 추가(생존/부활 복귀 분기 모두)
├── TryHandleDeath(...)           ← 수정, 즉시 Destroy 대신 friend.Die() 호출(그레이브야드/슬롯 정리는 그대로 즉시)
└── ApplyClausesToFriend(...)     ← 수정, 사망 분기에서 즉시 Destroy 대신 target.Die() 호출
```

---

## 파일 구성

```
Assets/Scripts/InGame/
├── WorldFriend.cs           ← 기존 파일 수정
└── InGameSceneManager.cs    ← 기존 파일 수정
```

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|---|---|
| `Die()` 중복 호출(같은 프레임에 전투 사망과 능력 사망이 겹치는 등) | `State`가 이미 `Dead`/`Destroying`이면 조기 반환 — 진동/파괴 트윈이 중복으로 걸리지 않음 |
| `TryHandleDeath`에서 부활(`TryRevive`) 성공 | `Die()`를 호출하지 않고 `attacker.EnterIdle()`로 되돌림 — `Dead` 상태를 거치지 않는다("죽었다가 되살아남"이 아니라 "죽음이 확정되지 않음"으로 취급) |
| `CheatClearSlot`(치트, 강제 슬롯 비우기) | 상태 전이 없이 기존 즉시 `Destroy` 유지 — 애초에 사망이 아닌 강제 제거로 설계됨(결정 5) |
| 포자감염(`SpawnMark`)으로 같은 슬롯에 새 카드가 즉시 스폰 | 죽은 카드가 `Dead`(진동, 기본 0.2초) 동안 새 카드와 잠깐 같은 슬롯에서 겹쳐 보일 수 있음 — 코스메틱 오버랩으로 수용(슬롯 점유 판정은 `RemoveFriend()`가 즉시 반영해 로직엔 영향 없음) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|---|---|
| 1 | `SpawnWorldFriend` 후 `SetKey` 호출 | `State`가 `Spawn`에서 `Idle`로 전이 |
| 2 | 유저 공격자가 `RollTarget`으로 공격 연출 시작 | `attacker.State`가 `Attack`으로 전이, 공격 연출(펀치/이동/타격/복귀) 종료 후 `Idle`로 복귀 |
| 3 | 전투로 타겟이 즉사 | 타겟이 `Dead`로 전이해 짧게 진동한 뒤 `Destroying`으로 전이해 곧바로 실제 `Destroy`됨(별도 파괴 연출 없음). `AddToGraveyard`/슬롯 비우기는 진동 시작 전에 이미 반영됨 |
| 4 | 공격자가 반격으로 즉사했지만 부활 조건(`CardCondition.Die`) 만족 | `Die()`를 거치지 않고 `State`가 `Attack → Idle`로 전이(부활 연출은 기존 `PunchScale` 재사용 그대로) |
| 5 | 능력 효과 피해(`ApplyClausesToFriend`)로 카드 사망 | `Idle` 상태였던 카드가 곧바로 `Dead → Destroying → 파괴`로 전이 |
| 6 | `CheatDamageSlot`으로 사망 | `TryHandleDeath` 경유이므로 시나리오 3과 동일하게 진동 후 파괴 |
| 7 | `CheatClearSlot` 호출 | 상태 전이 없이 즉시 파괴(기존 동작 유지) |

---

## 구현 시 주의사항

- `Die()`는 항상 그레이브야드 등록/슬롯 비우기(`RemoveFriend`) 이후에 호출한다 — 게임 로직 상태(다음 턴 판정)는 진동/파괴 연출을 기다리면 안 된다.
- `EnterAttack`/`EnterIdle`은 attacker에만 호출한다 — target은 공격 주체가 아니므로 `Attack` 상태를 거치지 않는다.
- `TryRevive` 성공 경로는 `Die()`를 호출하지 않는다.
- `CheatClearSlot`은 의도적으로 상태 머신을 우회한다 — 건드리지 않는다.
- `DOShakePosition` 호출 전 `DOKill()`을 먼저 호출해 직전 트윈(공격 복귀 이동 등)과 겹치지 않게 한다.

---

## 이번 범위에서 제외

- 데미지 숫자 팝업(`DamageEffect`)·타격음 — [데미지 이펙트 계획](plan-ingame-damage-effect.md)의 남은 몫(팝업 프리팹/사운드)은 이 문서와 무관하게 여전히 유효하다. 스폰(`TakeDamage` 내부)은 상태 머신과 완전히 독립적이지만, `Die()` 호출 직전에 `ReleaseDamageEffects` 한 줄을 끼워 넣어야 하는 지점 하나는 이 문서가 만든 `Die()`에 의존한다(그 문서의 결정 3 참고).
- 스폰 연출(파티클 등) — `Spawn` 상태는 구조적으로만 존재하고 지금은 `SetKey` 즉시 `Idle`로 전이한다. 실제 스폰 연출이 필요해지면(추후) 이 전이 시점만 지연시키면 된다.
- `Friend`(UI, 덱 미리보기) — 전투에 참여하지 않아 상태 머신 대상이 아님, 기존 "수정 금지" 정책 유지.

---

## 구현 후 체크리스트

- [x] `WorldFriend.cs`에 `FriendState` 열거형/`State` 프로퍼티/`EnterAttack`/`EnterIdle`/`Die`/`EnterDestroying` 추가, `SetKey` 끝에 `Idle` 전이 추가
- [x] `InGameSceneManager.ResolveAttackRoutine`에 `attacker.EnterAttack()`/`EnterIdle()` 호출 추가(생존/부활 복귀 분기 모두)
- [x] `TryHandleDeath`/`ApplyClausesToFriend`를 즉시 `Destroy`에서 `friend.Die()`/`target.Die()` 호출로 교체(그레이브야드/슬롯 정리는 그대로 즉시)
- [ ] 테스트 시나리오 검증(에디터에서 전투 사망/부활/능력 사망/치트 각각 확인)
- [x] [InGame 로직 개요](plan-ingame.md) 체크리스트에 이 문서 링크 추가
- [x] [데미지 이펙트 구현 계획](plan-ingame-damage-effect.md)에 이 문서로 사망 처리 설계가 대체됐다는 참조 추가
