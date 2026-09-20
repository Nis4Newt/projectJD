# 데미지 이펙트 구현 계획

> 상위 문서: [공격 판정 계획](plan-ingame-attack.md) — `ResolveAttackRoutine`에 "타격음, 타격 이펙트 재생 지점"으로 예견해 두고 "타격음/타격 이펙트의 실제 재생", "카드 사망 이펙트/사운드"를 범위 제외했던 후속 작업
> 관련 문서: [WorldFriend 월드스페이스 전환](worldspace/plan-ingame-worldspace-worldfriend.md), [BaseStone 월드스페이스 전환](worldspace/plan-ingame-worldspace-basestone.md) — `TakeDamage`가 지금 이 두 문서가 만든 world space 컴포넌트에 있고, 이번 이펙트도 그 자식으로 붙는다. [친구카드 상태 머신 계획](plan-ingame-friendstate.md) — 사망 확정 시 진동 후 파괴는 이미 그 문서의 `WorldFriend.Die()`가 구현했다. 이 문서는 그 파괴 시점에 데미지 팝업을 안전하게 이전시키는 것만 다룬다.
> 의존 관계: `DG.Tweening`(`Ease.OutBack`), `TMPro.TextMeshPro`, `JungleDice.InGame.WorldFriend`(`Die()`), `JungleDice.InGame.BaseStone`, `InGameSceneManager.TryHandleDeath`/`ApplyClausesToFriend`(친구카드가 실제로 파괴되는 두 지점)
> 범위: 친구-친구/친구-베이스 전투를 포함해 `WorldFriend.TakeDamage`/`BaseStone.TakeDamage`가 호출되는 모든 경로(능력 효과 피해, 치트, 덱 소진 페널티 포함)에서 피격 대상이 자기 위치에 데미지 숫자 팝업(배경 스프라이트 + TextMeshPro, `Ease.OutBack` 팝인 후 파괴)을 띄우는 것. 그 팝업이 카드의 사망/파괴 타이밍과 겹쳐도 끊기지 않게 이전시키는 것까지 포함. 사망 확정 시 진동·실제 파괴 자체(`WorldFriend.Die()`)는 [친구카드 상태 머신 계획](plan-ingame-friendstate.md)에서 이미 구현했으므로 범위 밖. 타격음, 그 외의 사망 파괴 연출(파티클 등), 방어막으로 무효화된 피해의 별도 표시("BLOCK" 등)도 범위 밖.

---

## 배경

`ResolveAttackRoutine`(`InGameSceneManager.cs:318`)에는 이미 `// 타격음, 타격 이펙트 재생 지점` 주석이 있고, [공격 판정 계획](plan-ingame-attack.md)이 "타격음/타격 이펙트의 실제 재생"과 "카드 사망 이펙트/사운드"를 명시적으로 범위 제외하며 후속 작업으로 남겨뒀다. 사망 직전 진동은 [친구카드 상태 머신 계획](plan-ingame-friendstate.md)이 이미 채웠고, 이번 문서는 그중 데미지 숫자 이펙트만 채운다(타격음·사망 파티클은 여전히 범위 밖).

준비물도 이미 있다: `Assets/Prefabs/attack_0bj.prefab`(배경 `SpriteRenderer` + 자식 `value`의 world space `TextMeshPro`, 텍스트 `"-10"`)와 `Assets/Sprites/damageEff.png`. 아직 어떤 스크립트도 참조하지 않는 미완성 프리팹이라, 이번 문서가 컴포넌트를 붙이고 코드에서 스폰하도록 완성한다.

**설계 검토 과정에서 나온 문제**: 이펙트를 피격 대상의 자식으로 계속 두면, 그 대상이 전투/능력 효과로 파괴될 때 이펙트도 즉시 같이 사라져 데미지 숫자가 끝까지 보이지 않는다(특히 즉사·킬링 블로우에서 가장 보고 싶은 순간에 잘림). 아래 핵심 설계 결정 2번이 이 문제를 다룬다.

---

## 설계 목표

- 새 이벤트를 EventBus에 추가하지 않는다 — 데미지 관련 이벤트가 현재 없고, `ResolveAttackRoutine`도 `Friend`/`BaseStone`을 직접 호출하는 관례이므로, 피격 대상 스스로(`TakeDamage` 내부)가 이펙트를 생성해 결합을 늘리지 않는다.
- 이펙트는 항상 "데미지를 받은 오브젝트 자신"의 자식으로 생성한다 — 공격자가 제자리로 복귀하는 동안 이펙트가 자연히 함께 움직이길 원하고, 별도의 "이동 중엔 이렇게, 복귀 후엔 저렇게" 분기 코드를 만들지 않기 위해서다.
- 그 오브젝트가 실제로 파괴되는 시점(전투 사망, 능력 효과 사망 모두)에는 예외 없이 이펙트를 안전한 위치(자신의 `FieldSlot`)로 옮긴 뒤 파괴한다 — "죽으면 이펙트도 같이 사라진다"는 문제를 스폰 시점이 아니라 파괴 시점에서 한 곳으로 막는다.
- 오브젝트 풀링은 도입하지 않는다 — 프로젝트 전역에 카드/이펙트용 풀링 인프라가 없고(유일한 풀은 오디오 SFX 8개), 한 판에서 이펙트 스폰 빈도가 낮아 `Instantiate`/`Destroy`로 충분하다(YAGNI).

---

## 핵심 설계 결정

### 1. `TakeDamage` 내부에서 자기 자신의 자식으로 스폰

`ResolveAttackRoutine`의 세 `TakeDamage` 호출(공격자, 타겟, 베이스)과 능력 효과·치트·덱 소진 페널티가 호출하는 `TakeDamage`까지 한 곳(`TakeDamage` 내부)에서 스폰해야 호출 경로와 무관하게 "데미지를 받는 순간"이 보장된다. 부모는 항상 자기 자신(`transform`)이다 — 공격자가 타격 직후 `MoveTo(originalPosition, ...)`로 복귀하는 동안 이펙트가 자식으로 붙어 있으면 자연히 함께 이동하고, 타겟처럼 움직이지 않는 경우도 위치가 그대로 유지된다.

```csharp
// WorldFriend.cs
public void TakeDamage(int amount)
{
    if (HasShield)
    {
        HasShield = false;
        return; // 이번 피해 전부 무효 — 이펙트도 띄우지 않음
    }

    DamageEffect.Spawn(_damageEffectPrefab, transform, amount);

    int previousHp = CurrentHp;
    CurrentHp = Mathf.Max(0, CurrentHp - amount);
    _hpText.text = CurrentHp.ToString();
    _hpText.color = GetStatColor(CurrentHp, previousHp);
}
```

```csharp
// BaseStone.cs
public void TakeDamage(int amount)
{
    DamageEffect.Spawn(_damageEffectPrefab, transform, amount);
    CurrentHp = Mathf.Max(0, CurrentHp - amount);
    _hpText.text = CurrentHp.ToString();
}
```

`BaseStone`은 전투 중 파괴되지 않는 오브젝트라 이 이후의 "파괴 시점 안전 이전" 로직이 필요 없다 — 항상 자기 자신 자식으로 남아도 안전하다.

### 2. 파괴 직전 `ReleaseDamageEffects`로 슬롯으로 이전 — 스폰 시점이 아니라 파괴 시점에서 막는다

친구카드가 실제로 `Destroy`되는 지점은 코드에 두 곳뿐이다: 전투/치트 사망 처리(`TryHandleDeath`)와 능력 효과 사망 처리(`ApplyClausesToFriend`). 둘 다 파괴 직전에 이펙트를 자신의 `FieldSlot`으로 옮긴다 — `worldPositionStays: true`로 옮기므로 월드 좌표(따라서 z-depth)가 그대로 보존된다.

```csharp
// WorldFriend.cs
// 파괴되기 전 아직 재생 중인 데미지 이펙트를 안전한 부모(보통 자신의 FieldSlot)로 옮긴다 — 이 오브젝트가 사라져도 이펙트는 끝까지 재생된다
public void ReleaseDamageEffects(Transform newParent)
{
    foreach (var effect in GetComponentsInChildren<DamageEffect>(includeInactive: true))
        effect.transform.SetParent(newParent, worldPositionStays: true);
}
```

`GetComponentsInChildren`로 먼저 배열을 만든 뒤 반복하며 재부모 지정한다 — `foreach (Transform child in transform)`처럼 순회 중에 자식 목록을 바꾸면 인덱스가 밀려 일부를 건너뛸 수 있어 피한다.

이 메서드는 `friend.Die()` 호출 직전에 `InGameSceneManager`가 직접 호출한다(아래 결정 4).

**왜 스폰 시점에 슬롯에 바로 붙이지 않는가**: `attack_0bj.prefab`의 로컬 z(-0.6)는 "`WorldFriend`의 자식"이 되는 것을 전제로 잡힌 값이다. `WorldFriend` 자신도 `SnapDepthToParent`로 슬롯보다 z -1만큼 앞에 그려지므로, 이펙트를 곧바로 슬롯의 자식으로 만들면 그 -1 단계를 건너뛰어 카드 스프라이트보다 뒤에 그려질 수 있다(로컬 z 보정을 다시 계산해야 함). `worldPositionStays: true`로 파괴 시점에만 옮기면 이미 올바르게 상속된 월드 z를 그대로 유지하므로 이 문제가 생기지 않는다.

### 3. `Ease.OutBack` 팝인 + `Destroy(gameObject, delay)`로 자동 파괴

```csharp
[SerializeField] private TextMeshPro _valueText;
[SerializeField] private float _popDuration = 0.25f;
[SerializeField] private float _holdDuration = 0.5f;

public static void Spawn(DamageEffect prefab, Transform parent, int amount)
{
    Instantiate(prefab, parent).Show(amount);
}

private void Show(int amount)
{
    _valueText.text = $"-{amount}";
    transform.localScale = Vector3.zero;
    transform.DOScale(Vector3.one, _popDuration).SetEase(Ease.OutBack);
    Destroy(gameObject, _popDuration + _holdDuration);
}
```

- `Ease.OutBack`은 현재 코드베이스에 없는 이즈라 이번이 첫 도입이다(기존엔 `OutQuint`/`InQuad`/`Linear`만 사용).
- 코루틴 없이 `Destroy(gameObject, delay)`로 지연 파괴한다 — 인스턴스가 1회성이라 `DOKill()`로 트윈 겹침을 방지할 필요도 없다.
- `_popDuration`/`_holdDuration`은 인스펙터에서 조정 가능한 기본값 제안일 뿐 — 실제 느낌은 에디터에서 확인 후 튜닝한다.

### 4. `ReleaseDamageEffects`를 `Die()` 호출 직전에 끼워 넣는다

친구카드가 실제로 파괴되는 두 지점(`TryHandleDeath`/`ApplyClausesToFriend`)은 이미 [친구카드 상태 머신 계획](plan-ingame-friendstate.md)이 "그레이브야드 등록 → 슬롯 비우기 → `friend.Die()`" 순서로 정리해뒀다. 이 문서가 할 일은 그 흐름에 새 코드를 추가하는 게 아니라, `Die()` 호출 바로 앞에 `ReleaseDamageEffects` 한 줄을 끼워 넣는 것뿐이다 — 진동(`Die()` 내부)이 시작되기 전에 이펙트가 이미 안전한 부모 아래 있어야 하기 때문이다.

```csharp
// TryHandleDeath 꼬리(부활 실패 이후)
AddToGraveyard(deadSlot.Index, friend.Key);
deadSlot.RemoveFriend();
friend.ReleaseDamageEffects(deadSlot.transform); // Die() 진동보다 먼저 이전
friend.Die();
```

```csharp
// ApplyClausesToFriend 사망 분기
AddToGraveyard(slot.Index, target.Key);
slot.RemoveFriend();
target.ReleaseDamageEffects(slot.transform);
target.Die();
```

---

## 클래스 구조

```
DamageEffect : MonoBehaviour                              (신규, InGame/)
├── _valueText : TextMeshPro [SerializeField]
├── _popDuration : float = 0.25f [SerializeField]
├── _holdDuration : float = 0.5f [SerializeField]
├── Spawn(DamageEffect prefab, Transform parent, int amount) : static void  ← parent 자식으로 생성
└── Show(int amount)                                        ← private, 텍스트 설정 + OutBack 팝인 + 지연 파괴

WorldFriend (기존 파일 수정, InGame/ — Die()/State는 [친구카드 상태 머신 계획]에서 이미 구현됨)
├── _damageEffectPrefab : DamageEffect [SerializeField]     ← 신규
├── TakeDamage(int amount)                                   ← 수정, shield 조기 반환 이후 DamageEffect.Spawn 호출 추가
└── ReleaseDamageEffects(Transform newParent)                ← 신규, 자식 DamageEffect들을 재부모 지정

BaseStone (기존 파일 수정, InGame/)
├── _damageEffectPrefab : DamageEffect [SerializeField]     ← 신규
└── TakeDamage(int amount)                                   ← 수정, DamageEffect.Spawn 호출 추가

InGameSceneManager (기존 파일 수정, InGame/)
├── TryHandleDeath(...)           ← 수정, friend.Die() 호출 직전에 friend.ReleaseDamageEffects(deadSlot.transform) 추가
└── ApplyClausesToFriend(...)     ← 수정, target.Die() 호출 직전에 target.ReleaseDamageEffects(slot.transform) 추가
```

---

## 파일 구성

```
Assets/Scripts/InGame/
├── DamageEffect.cs             ← 신규
├── WorldFriend.cs              ← 기존 파일 수정
├── BaseStone.cs                ← 기존 파일 수정
└── InGameSceneManager.cs       ← 기존 파일 수정(TryHandleDeath/ApplyClausesToFriend에 ReleaseDamageEffects 호출 추가)

Assets/Prefabs/
└── attack_0bj.prefab           ← 기존 파일(이미 존재) 수정 — 루트에 DamageEffect 컴포넌트 부착
```

---

## Unity 씬/오브젝트 구성

```
[Assets/Prefabs/attack_0bj.prefab] (기존 — 배경 SpriteRenderer + value 자식(TextMeshPro) 보유)
└── attack_0bj(루트, SpriteRenderer: damageEff.png)
    ├── DamageEffect.cs 부착(신규) — _valueText에 아래 value 연결
    └── value(자식, TextMeshPro, 기존 그대로 유지)

[Assets/Prefabs/WorldFriend.prefab]
└── WorldFriend.cs
    └── _damageEffectPrefab ← attack_0bj.prefab 연결(신규)

[Scene: InGame.unity]
├── mybase > bastone → BaseStone.cs
│     └── _damageEffectPrefab ← attack_0bj.prefab 연결(신규)
└── oppobase > bastone (1) → BaseStone.cs
      └── _damageEffectPrefab ← 동일 프리팹 연결(신규)
```

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|---|---|
| `WorldFriend`가 방어막(`HasShield`)으로 피해를 완전히 막음 | 이펙트 생성 안 함 — `TakeDamage`의 shield 조기 반환 이전에는 스폰 호출에 도달하지 않음 |
| 데미지 이펙트가 재생되는 도중 피격 대상이 사망 확정됨(공격자·타겟·능력 효과 대상 모두) | `TryHandleDeath`/`ApplyClausesToFriend`가 `Die()` 호출 직전 `ReleaseDamageEffects(slot.transform)`로 이펙트를 슬롯으로 옮김 — 이펙트는 끊기지 않고 끝까지 재생 |
| 공격자가 타격 후 제자리로 복귀하며 살아남는 경우 | 이펙트는 계속 공격자의 자식으로 남아 함께 이동한 뒤 자기 타이머로 자연 파괴 — 죽지 않는 한 위험하지 않으므로 굳이 옮기지 않음 |
| 포자감염(`SpawnMark`)으로 같은 슬롯에 새 카드가 즉시 스폰됨 | 죽은 카드가 `Die()`의 진동(기본 0.2초, [친구카드 상태 머신 계획] 참고)이 끝날 때까지 잠깐 같은 슬롯 아래 남아 새 카드와 짧게 겹쳐 보일 수 있음 — 이 문서와 무관한 기존 트레이드오프 |
| 능력 효과(`CardEffectClauseKind.Damage`)로 죽는 경우 | 복귀 연출이 없을 뿐, 전투 사망과 동일하게 `ReleaseDamageEffects` 후 `Die()`를 거침 |
| 치트(`CheatDamageSlot`)로 죽는 경우 | `TryHandleDeath`를 그대로 재사용하므로 동일하게 이펙트 이전 후 파괴됨 |
| 덱 소진 페널티(`BaseStone.TakeDamage(1)`) | `BaseStone`은 파괴되지 않는 오브젝트라 이전 로직과 무관 — 이펙트만 정상 재생 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|---|---|
| 1 | 친구-친구 전투, 둘 다 생존 | 각자 위치에서 `-N` 텍스트가 `Ease.OutBack`으로 팝인 후 자기 타이머로 사라짐, 카드는 진동 없이 그대로 필드에 남음 |
| 2 | 친구-친구 전투, 타겟이 즉사 | 타겟 카드가 `Die()`로 짧게 진동한 뒤 사라지고, 데미지 이펙트는 미리 슬롯으로 옮겨져 원래 타이머대로 끝까지 재생됨 |
| 3 | 친구-친구 전투, 공격자가 반격으로 즉사 | 공격자가 제자리로 복귀하는 동안 이펙트가 함께 따라 움직이다가, 복귀 직후 `Die()`로 진동→파괴되며 이펙트는 슬롯에 남아 계속 재생됨 |
| 4 | 친구-베이스 전투(타겟 슬롯이 비어 본체 피격) | 해당 진영 `BaseStone`에 `-N` 이펙트가 재생됨(베이스는 파괴되지 않으므로 이전 로직 자체가 관여하지 않음) |
| 5 | 방어막을 보유한 `WorldFriend`가 피격됨 | 이펙트가 생성되지 않음(체력 텍스트도 변화 없음) |
| 6 | 능력 효과 피해로 카드가 사망 | `ApplyClausesToFriend`의 사망 분기에서도 동일하게 이펙트가 먼저 이전된 뒤 `Die()`로 파괴, 이펙트는 슬롯에서 끝까지 재생 |
| 7 | 포자감염으로 사망 직후 같은 슬롯에 새 카드가 스폰됨 | 죽은 카드가 진동하는 짧은 시간(0.2초) 동안 새 카드와 같은 슬롯에서 잠깐 겹쳐 보일 수 있음(허용된 동작, `Die()` 쪽 트레이드오프) |

---

## 구현 시 주의사항

- `attack_0bj.prefab`의 루트 로컬 z(-0.6)/`value` 자식의 회전(z 20도)·폰트 크기(4.4)는 이미 세팅돼 있어 그대로 쓰되, `WorldFriend`/`BaseStone` 자식으로 붙었을 때 카드/베이스 스프라이트보다 앞에 그려지는지, 크기(5.12×5.12)가 필드 스케일과 맞는지는 에디터에서 실제로 확인해야 한다.
- `ReleaseDamageEffects`는 `GetComponentsInChildren<DamageEffect>()`로 먼저 배열을 뽑은 뒤 반복해야 한다 — `foreach (Transform child in transform)`처럼 순회 중 자식 목록 자체를 바꾸면 인덱스가 밀려 일부 이펙트를 놓칠 수 있다.
- `TryHandleDeath`/`ApplyClausesToFriend`에서 `ReleaseDamageEffects`는 반드시 `Die()` 호출보다 먼저 실행돼야 한다 — `Die()`가 곧바로 진동을 시작하므로, 그 전에 이펙트가 이미 안전한 부모 아래 있어야 한다.
- `WorldFriend.prefab`과 씬의 두 `BaseStone` 인스턴스(`mybase`/`oppobase`) 모두에 `_damageEffectPrefab`을 각각 연결해야 한다 — 공용 리소스 로더(`Resources.Load` 등)를 새로 만들지 않고 기존 `SerializeField` 참조 관례를 그대로 따른다.
- 텍스트는 `$"-{amount}"` 형식으로 고정 — `TakeDamage`는 항상 양수 피해량만 받으므로 부호 반전이나 색상 분기는 만들지 않는다.

---

## 이번 범위에서 제외

- 타격음 재생 — `ResolveAttackRoutine`의 같은 주석이 가리키는 또 다른 항목이지만, 오디오 리소스/클립 선정이 필요해 별도 후속 작업
- 방어막으로 무효화된 피해의 별도 표시(예: `"BLOCK"` 텍스트) — 요청 범위 밖(YAGNI), 필요해지면 `TakeDamage`의 shield 분기에 추가
- 데미지 숫자가 위로 떠오르는 이동 연출 — 요청은 팝인 후 파괴까지만, 이동은 범위 밖
- 사망 확정 시 진동·실제 파괴(`Die()`/`FriendState`) 및 그 외 사망 연출(파티클, 페이드아웃, 사운드) — [친구카드 상태 머신 계획](plan-ingame-friendstate.md)이 이미 구현했거나 그 문서의 후속 범위
- 이펙트 오브젝트 풀링 — 프로젝트에 카드/이펙트용 풀링 인프라가 없고 스폰 빈도가 낮아 도입하지 않음(추후 이펙트가 잦아지면 재검토)
- 포자감염 시 죽은 카드와 새 카드가 짧게 겹쳐 보이는 문제의 해결 — [친구카드 상태 머신 계획]의 기존 트레이드오프를 그대로 수용, 이 문서에서 추가로 다루지 않음

---

## 구현 후 체크리스트

- [ ] `DamageEffect.cs` 작성, `attack_0bj.prefab` 루트에 부착 및 `_valueText` 연결
- [ ] `WorldFriend.cs`에 `_damageEffectPrefab` 필드 추가, `TakeDamage`에 스폰 호출 추가, `ReleaseDamageEffects` 구현, `WorldFriend.prefab`에 프리팹 연결
- [ ] `BaseStone.cs`에 `_damageEffectPrefab` 필드 추가 + `TakeDamage`에 스폰 호출 추가, `mybase`/`oppobase` 두 인스턴스에 각각 프리팹 연결
- [ ] `InGameSceneManager.cs`의 `TryHandleDeath`/`ApplyClausesToFriend`에서 `friend.Die()`/`target.Die()` 호출 직전에 `ReleaseDamageEffects` 호출 추가
- [ ] 에디터에서 친구-친구(생존/타겟 사망/공격자 사망), 친구-베이스, 방어막 보유, 능력 효과 사망, 포자감염 케이스 실제 재생 확인(z-깊이/크기/이펙트 이전 타이밍 포함)
- [ ] [InGame 로직 개요](plan-ingame.md) 체크리스트에 이 문서 링크 추가
