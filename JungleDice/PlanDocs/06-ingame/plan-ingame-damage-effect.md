# 데미지 이펙트 구현 계획

> 상위 문서: [공격 판정 계획](plan-ingame-attack.md) — `ResolveAttackRoutine`에 "타격음, 타격 이펙트 재생 지점"으로 예견해 두고 "타격음/타격 이펙트의 실제 재생", "카드 사망 이펙트/사운드"를 범위 제외했던 후속 작업
> 관련 문서: [WorldFriend 월드스페이스 전환](worldspace/plan-ingame-worldspace-worldfriend.md), [BaseStone 월드스페이스 전환](worldspace/plan-ingame-worldspace-basestone.md) — `TakeDamage`가 지금 이 두 문서가 만든 world space 컴포넌트에 있고, 이번 이펙트도 그 자식으로 붙는다. [친구카드 상태 머신 계획](plan-ingame-friendstate.md) — 사망 확정 시 진동 후 파괴는 이미 그 문서의 `WorldFriend.Die()`가 구현했다. 이 문서는 그 파괴 시점에 데미지 팝업을 안전하게 이전시키는 것만 다룬다.
> 의존 관계: `DG.Tweening`(`Ease.OutBack`), `TMPro.TextMeshPro`, `JungleDice.InGame.WorldFriend`(`Die()`), `JungleDice.InGame.BaseStone`, `InGameSceneManager.TryHandleDeath`/`ApplyClausesToFriend`(친구카드가 실제로 파괴되는 두 지점)
> 범위: 친구-친구/친구-베이스 전투를 포함해 `WorldFriend.TakeDamage`/`BaseStone.TakeDamage`가 호출되는 모든 경로(능력 효과 피해, 치트, 덱 소진 페널티 포함)에서 피격 대상이 자기 위치에 데미지 숫자 팝업(배경 스프라이트 + TextMeshPro, `Ease.OutBack` 팝인 후 파괴)을 띄우는 것. 그 팝업이 카드의 사망/파괴 타이밍과 겹쳐도 끊기지 않게 이전시키는 것까지 포함. 사망 확정 시 진동·실제 파괴 자체(`WorldFriend.Die()`)는 [친구카드 상태 머신 계획](plan-ingame-friendstate.md)에서 이미 구현했으므로 범위 밖. 타격음, 그 외의 사망 파괴 연출(파티클 등), 방어막으로 무효화된 피해의 별도 표시("BLOCK" 등)도 범위 밖.

---

## 배경

`ResolveAttackRoutine`(`InGameSceneManager.cs:318`)에는 이미 `// 타격음, 타격 이펙트 재생 지점` 주석이 있고, [공격 판정 계획](plan-ingame-attack.md)이 "타격음/타격 이펙트의 실제 재생"과 "카드 사망 이펙트/사운드"를 명시적으로 범위 제외하며 후속 작업으로 남겨뒀다. 사망 직전 진동은 [친구카드 상태 머신 계획](plan-ingame-friendstate.md)이 이미 채웠고, 이번 문서는 그중 데미지 숫자 이펙트만 채운다(타격음·사망 파티클은 여전히 범위 밖).

준비물도 이미 있다: `Assets/Prefabs/attack_0bj.prefab`(배경 `SpriteRenderer` + 자식 `value`의 world space `TextMeshPro`, 텍스트 `"-10"`)와 `Assets/Sprites/damageEff.png`. 아직 어떤 스크립트도 참조하지 않는 미완성 프리팹이라, 이번 문서가 컴포넌트를 붙이고 코드에서 스폰하도록 완성한다.

---

## 설계 목표

- 새 이벤트를 EventBus에 추가하지 않는다 — 데미지 관련 이벤트가 현재 없고, `ResolveAttackRoutine`도 `Friend`/`BaseStone`을 직접 호출하는 관례이므로, 피격 대상 스스로(`TakeDamage` 내부)가 이펙트를 생성해 결합을 늘리지 않는다.
- 이펙트는 항상 피격 대상 자신의 자식으로 스폰한다 — 부모가 카드냐 슬롯이냐에 따라 다른 위치 상수를 계산하지 않기 위해서다. 안전한 부모(슬롯)로의 승격은 이미 슬롯에 있으면 즉시, 공격 중이면 슬롯 복귀 시점이나 파괴 직전에 `ReleaseDamageEffects`로 처리한다.
- 카드가 파괴되는 시점(전투 사망, 능력 효과 사망 모두)에는 예외 없이 이펙트가 먼저 슬롯으로 옮겨진 뒤여야 한다 — "죽으면 이펙트도 같이 사라진다"는 문제가 생기지 않게 한다.
- 오브젝트 풀링은 도입하지 않는다 — 프로젝트 전역에 카드/이펙트용 풀링 인프라가 없고(유일한 풀은 오디오 SFX 8개), 한 판에서 이펙트 스폰 빈도가 낮아 `Instantiate`/`Destroy`로 충분하다(YAGNI).

---

## 핵심 설계 결정

### 1. 스폰은 항상 피격 대상 자신의 자식으로

`ResolveAttackRoutine`의 세 `TakeDamage` 호출(공격자, 타겟, 베이스)과 능력 효과·치트·덱 소진 페널티가 호출하는 `TakeDamage`까지, 한 곳(`TakeDamage` 내부)에서 스폰해야 호출 경로와 무관하게 "데미지를 받는 순간"이 보장된다. `Instantiate`의 parent는 항상 피격 대상 자신(`transform`)이다 — 최종적으로 슬롯의 자식이 될 경우에도 슬롯을 직접 parent로 넘기지 않는다. `_localDepth`(결정 4)가 "부모가 카드 자신"이라는 전제로 계산된 값이라, 부모가 슬롯이냐 카드냐에 따라 다른 상수를 써야 하는 상황을 피하기 위해서다. 슬롯으로의 승격은 전부 `ReleaseDamageEffects`(결정 2)를 통한 재부모 지정으로만 이뤄진다.

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
    if (State != FriendState.Attack) ReleaseDamageEffects(transform.parent); // 공격 중이 아니면 이미 슬롯에 있는 것

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

`BaseStone`은 슬롯 개념이 없고 전투 중 파괴되지도 않으므로 재부모 지정 없이 항상 자기 자신의 자식으로 남아도 안전하다.

### 2. `ReleaseDamageEffects` — `worldPositionStays`로 위치 재계산 없이 재부모 지정

```csharp
// WorldFriend.cs
// 아직 재생 중인 데미지 이펙트를 새 부모로 옮긴다 — 이 오브젝트가 사라져도 이펙트는 끝까지 재생된다
public void ReleaseDamageEffects(Transform newParent)
{
    foreach (var effect in GetComponentsInChildren<DamageEffect>(includeInactive: true))
        effect.transform.SetParent(newParent, worldPositionStays: true);
}
```

`GetComponentsInChildren`로 먼저 배열을 만든 뒤 반복하며 재부모 지정한다 — `foreach (Transform child in transform)`처럼 순회 중에 자식 목록을 바꾸면 인덱스가 밀려 일부를 건너뛸 수 있어 피한다. `worldPositionStays: true`이므로 스폰 시점에 이미 정해진 월드 z(결정 4)를 그대로 유지한 채 부모만 바뀐다 — 슬롯으로 옮길 때 위치를 다시 계산할 필요가 없다.

### 3. `ReleaseDamageEffects`를 호출하는 세 지점

| 시점 | 위치 | 이유 |
|---|---|---|
| 스폰 직후, 공격 중이 아닐 때(타겟/능력 효과/치트/덱 소진) | `WorldFriend.TakeDamage`(결정 1) | 이미 자기 슬롯에 앉아있는 카드라 곧바로 슬롯 자식으로 승격 |
| 공격자가 살아서 슬롯으로 복귀할 때 | `ResolveAttackRoutine`의 `attackerAlive` 분기 | 공격 중엔 이펙트가 카드를 따라 이동하다가, 슬롯 복귀가 확정되는 시점에 슬롯 자식으로 |
| 친구카드가 파괴되기 직전(`Die()` 호출 전) | `TryHandleDeath`/`ApplyClausesToFriend`가 공통 헬퍼 `DestroyFriend`를 거쳐 | 파괴돼도 이펙트가 끊기지 않도록 안전한 부모로 미리 이전 |

```csharp
// ResolveAttackRoutine, attackerAlive 분기
attacker.SetParent(_attackerSlot.transform); // 공격 레이어에서 원래 슬롯으로 복귀
attacker.SetHighlight(false, Color.clear);
attacker.EnterIdle();
attacker.ReleaseDamageEffects(_attackerSlot.transform); // 슬롯으로 돌아왔으니 데미지 이펙트도 슬롯 자식으로
```

죽는 두 경로(`TryHandleDeath`/`ApplyClausesToFriend`)는 "그레이브야드 등록 → 슬롯 비우기 → `ReleaseDamageEffects` → `Die()`" 네 줄이 그대로 겹쳐, 공통 private 헬퍼 `DestroyFriend`로 뽑는다 — 두 곳 중 한쪽만 고치고 다른 쪽을 빠뜨리는 실수를 막는다.

```csharp
// InGameSceneManager.cs
private void DestroyFriend(WorldFriend friend, FieldSlot slot)
{
    AddToGraveyard(slot.Index, friend.Key);
    slot.RemoveFriend();
    friend.ReleaseDamageEffects(slot.transform);
    friend.Die();
}
```

```csharp
// TryHandleDeath 꼬리(부활 실패 이후)
DestroyFriend(friend, slotTransform.GetComponent<FieldSlot>());
```

```csharp
// ApplyClausesToFriend 사망 분기
var slot = target.transform.parent.GetComponent<FieldSlot>();
DestroyFriend(target, slot);
```

### 4. `Ease.OutBack` 팝인, 로컬 z·스케일은 프리팹 값 대신 코드가 보정

```csharp
[SerializeField] private SpriteRenderer _backgroundRenderer;
[SerializeField] private TextMeshPro _valueText;
[SerializeField] private float _popDuration = 0.25f;
[SerializeField] private float _holdDuration = 0.5f;
[SerializeField] private float _localDepth = -1.5f;

public static void Spawn(DamageEffect prefab, Transform parent, int amount)
{
    Instantiate(prefab, parent).Show(amount);
}

private void Show(int amount)
{
    var localPosition = transform.localPosition;
    localPosition.z = _localDepth;
    transform.localPosition = localPosition;

    _valueText.text = $"-{amount}";
    var targetScale = transform.localScale; // 프리팹에 세팅된 원래 크기 — Vector3.one으로 고정하지 않는다
    transform.localScale = Vector3.zero;
    transform.DOScale(targetScale, _popDuration).SetEase(Ease.OutBack).OnComplete(FadeOutAndDestroy);
}
```

- `Ease.OutBack`은 이 프로젝트에서 처음 쓰는 이즈다(기존엔 `OutQuint`/`InQuad`/`Linear`만 사용).
- `_localDepth`(-1.5)는 부모가 `WorldFriend` 루트인 것을 전제로 잡은 값이다. `WorldFriend`의 실제 카드 그림(`Image` 자식)은 루트보다 z -1 더 앞에 있어(`WorldFriend.prefab` 구조), 이펙트가 카드보다 확실히 앞에 그려지려면 로컬 z가 -1보다 더 작아야 한다. `BaseStone`은 스프라이트가 같은 오브젝트에 있어 이 문제가 없지만, 두 경우 모두 안전하도록 같은 값(-1.5)으로 통일한다.
- `targetScale`은 `Instantiate` 직후의 `localScale`(프리팹에 세팅된 크기)을 캡처해 애니메이션 목표로 쓴다 — `Vector3.one`으로 고정하면 프리팹 스케일이 1이 아닐 때 의도보다 작거나 크게 재생된다.

### 5. 팝인이 끝나면 `_holdDuration` 동안 페이드아웃 후 파괴

```csharp
// 팝인이 끝난 뒤 holdDuration 동안 배경/텍스트를 서서히 투명하게 만들고, 그 시간이 끝나면 파괴한다
private void FadeOutAndDestroy()
{
    _backgroundRenderer.DOFade(0f, _holdDuration);
    DOTween.To(() => _valueText.alpha, a => _valueText.alpha = a, 0f, _holdDuration); // TextMeshPro용 DOTween 모듈이 없어 DOTween.To로 직접 트윈
    Destroy(gameObject, _holdDuration);
}
```

- `_holdDuration`은 "고정 표시 시간"에서 "페이드아웃 지속 시간"으로 의미가 바뀐다 — 팝인 직후 곧바로 투명해지기 시작해, 그 끝에서 완전히 사라지는 동시에 파괴된다.
- `SpriteRenderer`는 이 프로젝트에 이미 DOTween Sprite 모듈이 있어 `DOFade`를 그대로 쓴다. `TextMeshPro`(`TMP_Text`)용 DOTween 모듈은 이 프로젝트에 설치돼 있지 않아 `DOFade` 확장 메서드가 없으므로, `TMP_Text.alpha` 프로퍼티를 `DOTween.To`로 직접 트윈한다.
- 코루틴 없이 `Destroy(gameObject, delay)`로 지연 파괴한다 — 인스턴스가 1회성이라 `DOKill()`로 트윈 겹침을 방지할 필요도 없다.

---

## 클래스 구조

```
DamageEffect : MonoBehaviour                              (신규, InGame/)
├── _backgroundRenderer : SpriteRenderer [SerializeField]
├── _valueText : TextMeshPro [SerializeField]
├── _popDuration : float = 0.25f [SerializeField]
├── _holdDuration : float = 0.5f [SerializeField]           ← 페이드아웃 지속 시간으로 사용
├── _localDepth : float = -1.5f [SerializeField]            ← WorldFriend의 Image 자식(z -1)보다 앞에 그려지도록 고정
├── Spawn(DamageEffect prefab, Transform parent, int amount) : static void  ← parent 자식으로 생성
├── Show(int amount)                                        ← private, 로컬 z 고정 + 텍스트 설정 + OutBack 팝인
└── FadeOutAndDestroy()                                     ← private, 팝인 완료 콜백. 배경/텍스트 페이드아웃 + 지연 파괴

WorldFriend (기존 파일 수정, InGame/ — Die()/State는 [친구카드 상태 머신 계획]에서 이미 구현됨)
├── _damageEffectPrefab : DamageEffect [SerializeField]     ← 신규
├── TakeDamage(int amount)                                   ← 수정, DamageEffect.Spawn 호출 + State != Attack이면 즉시 ReleaseDamageEffects
└── ReleaseDamageEffects(Transform newParent)                ← 신규, 자식 DamageEffect들을 재부모 지정

BaseStone (기존 파일 수정, InGame/)
├── _damageEffectPrefab : DamageEffect [SerializeField]     ← 신규
└── TakeDamage(int amount)                                   ← 수정, DamageEffect.Spawn 호출 추가

InGameSceneManager (기존 파일 수정, InGame/)
├── ResolveAttackRoutine(...)     ← 수정, attackerAlive 분기에서 attacker.ReleaseDamageEffects(_attackerSlot.transform) 추가
├── DestroyFriend(WorldFriend friend, FieldSlot slot)  ← 신규 private, 그레이브야드 등록/슬롯 비우기/ReleaseDamageEffects/Die() 공통 처리
├── TryHandleDeath(...)           ← 수정, 사망 꼬리 로직을 DestroyFriend 호출로 교체
└── ApplyClausesToFriend(...)     ← 수정, 사망 분기를 DestroyFriend 호출로 교체
```

---

## 파일 구성

```
Assets/Scripts/InGame/
├── DamageEffect.cs             ← 신규
├── WorldFriend.cs              ← 기존 파일 수정
├── BaseStone.cs                ← 기존 파일 수정
└── InGameSceneManager.cs       ← 기존 파일 수정(ResolveAttackRoutine/TryHandleDeath/ApplyClausesToFriend에 ReleaseDamageEffects 호출 추가)

Assets/Prefabs/
└── attack_0bj.prefab           ← 기존 파일(이미 존재) 수정 — 루트에 DamageEffect 컴포넌트 부착
```

---

## Unity 씬/오브젝트 구성

```
[Assets/Prefabs/attack_0bj.prefab] (기존 — 배경 SpriteRenderer + value 자식(TextMeshPro) 보유)
└── attack_0bj(루트, SpriteRenderer: damageEff.png)
    ├── DamageEffect.cs 부착(신규) — _backgroundRenderer에 루트 자신의 SpriteRenderer, _valueText에 아래 value 연결
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
| 타겟이 피격됨(공격 상태가 아닌 카드) | `TakeDamage` 안에서 `State != Attack`이 바로 참이라, 스폰 직후 곧바로 슬롯 자식으로 승격됨(카드가 그 뒤 죽든 살든 이미 슬롯에 있어 안전) |
| 공격자가 타격 후 제자리로 복귀하며 살아남는 경우 | 이동하는 동안은 계속 공격자의 자식으로 따라다니다가, `ResolveAttackRoutine`이 슬롯으로 복귀시키는 시점에 `ReleaseDamageEffects`로 슬롯 자식으로 넘어감 |
| 포자감염(`SpawnMark`)으로 같은 슬롯에 새 카드가 즉시 스폰됨 | 죽은 카드가 `Die()`의 진동(기본 0.2초, [친구카드 상태 머신 계획] 참고)이 끝날 때까지 잠깐 같은 슬롯 아래 남아 새 카드와 짧게 겹쳐 보일 수 있음 — 이 문서와 무관한 기존 트레이드오프 |
| 능력 효과(`CardEffectClauseKind.Damage`)로 죽는 경우 | 복귀 연출이 없을 뿐, 전투 사망과 동일하게 `ReleaseDamageEffects` 후 `Die()`를 거침 |
| 치트(`CheatDamageSlot`)로 죽는 경우 | `TryHandleDeath`를 그대로 재사용하므로 동일하게 이펙트 이전 후 파괴됨 |
| 덱 소진 페널티(`BaseStone.TakeDamage(1)`) | `BaseStone`은 파괴되지 않는 오브젝트라 이전 로직과 무관 — 이펙트만 정상 재생 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|---|---|
| 1 | 친구-친구 전투, 둘 다 생존 | 타겟 쪽 이펙트는 스폰 즉시 슬롯 자식으로, 공격자 쪽 이펙트는 복귀 이동을 따라간 뒤 슬롯 복귀 시점에 슬롯 자식으로 넘어감 — 둘 다 `Ease.OutBack` 팝인 후 배경/텍스트가 `_holdDuration` 동안 서서히 투명해지며 사라지고 카드는 진동 없이 필드에 남음 |
| 2 | 친구-친구 전투, 타겟이 즉사 | 타겟 이펙트는 스폰 즉시 이미 슬롯 자식이라, 타겟 카드가 `Die()`로 진동 후 사라져도 영향 없이 원래 타이머대로 끝까지 재생됨 |
| 3 | 친구-친구 전투, 공격자가 반격으로 즉사 | 공격자가 제자리로 복귀하는 동안 이펙트가 함께 따라 움직이다가, 복귀 직후 `Die()`로 진동→파괴되며(슬롯 복귀 자체가 무산되므로 `ReleaseDamageEffects`는 `TryHandleDeath` 쪽에서 실행) 이펙트는 슬롯에 남아 계속 재생됨 |
| 4 | 친구-베이스 전투(타겟 슬롯이 비어 본체 피격) | 해당 진영 `BaseStone`에 `-N` 이펙트가 재생됨(베이스는 파괴되지 않으므로 이전 로직 자체가 관여하지 않음) |
| 5 | 방어막을 보유한 `WorldFriend`가 피격됨 | 이펙트가 생성되지 않음(체력 텍스트도 변화 없음) |
| 6 | 능력 효과 피해로 카드가 사망 | `ApplyClausesToFriend`의 사망 분기에서도 동일하게 이펙트가 먼저 이전된 뒤 `Die()`로 파괴, 이펙트는 슬롯에서 끝까지 재생 |
| 7 | 포자감염으로 사망 직후 같은 슬롯에 새 카드가 스폰됨 | 죽은 카드가 진동하는 짧은 시간(0.2초) 동안 새 카드와 같은 슬롯에서 잠깐 겹쳐 보일 수 있음(허용된 동작, `Die()` 쪽 트레이드오프) |

---

## 구현 시 주의사항

- `attack_0bj.prefab` 루트의 로컬 z는 `Show()`가 `_localDepth`(-1.5)로 항상 덮어쓴다 — 프리팹 자체에 저장된 z 값은 의미가 없다. 스케일도 마찬가지로 `Show()`가 `Instantiate` 시점의 값을 캡처해 그대로 애니메이션 목표로 쓰므로, 루트 스케일은 프리팹에서 원하는 크기로 자유롭게 바꿔도 된다. `value` 자식의 회전(z 20도)·폰트 크기(4.4)는 그대로 쓰되, 실제 필드 스케일과 맞는지는 에디터에서 확인해야 한다.
- `ReleaseDamageEffects`는 `GetComponentsInChildren<DamageEffect>()`로 먼저 배열을 뽑은 뒤 반복해야 한다 — `foreach (Transform child in transform)`처럼 순회 중 자식 목록 자체를 바꾸면 인덱스가 밀려 일부 이펙트를 놓칠 수 있다.
- `DestroyFriend` 내부에서 `ReleaseDamageEffects`는 반드시 `Die()` 호출보다 먼저 실행돼야 한다 — `Die()`가 곧바로 진동을 시작하므로, 그 전에 이펙트가 이미 안전한 부모 아래 있어야 한다.
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

- [x] `DamageEffect.cs` 작성(팝인 + 페이드아웃 + 지연 파괴), `attack_0bj.prefab` 루트에 부착 및 `_backgroundRenderer`/`_valueText` 연결
- [x] `WorldFriend.cs`에 `_damageEffectPrefab` 필드 추가, `TakeDamage`에 스폰 호출 + `State != Attack`이면 즉시 `ReleaseDamageEffects` 호출 추가, `ReleaseDamageEffects` 구현, `WorldFriend.prefab`에 프리팹 연결
- [x] `BaseStone.cs`에 `_damageEffectPrefab` 필드 추가 + `TakeDamage`에 스폰 호출 추가, `mybase`/`oppobase` 두 인스턴스에 각각 프리팹 연결
- [x] `InGameSceneManager.cs`의 `ResolveAttackRoutine`(attackerAlive 분기)에 `ReleaseDamageEffects` 호출 추가, `TryHandleDeath`/`ApplyClausesToFriend`의 사망 처리를 공통 헬퍼 `DestroyFriend`로 통합
- [ ] 에디터에서 친구-친구(생존/타겟 사망/공격자 사망), 친구-베이스, 방어막 보유, 능력 효과 사망, 포자감염 케이스 실제 재생 확인(z-깊이/크기/이펙트 이전 타이밍 포함) — 프리팹/씬 필드는 YAML을 직접 편집해 연결했으므로, Unity 에디터에서 한 번 열어 `attack_0bj`/`WorldFriend`/`mybase`·`oppobase`의 `_damageEffectPrefab` 슬롯이 실제로 채워져 보이는지도 함께 확인 필요
- [x] [InGame 로직 개요](plan-ingame.md) 체크리스트에 이 문서 링크 추가
