# WorldFriend 신설 계획

> 상위 문서: [InGame 필드 World Space 전환 개요](plan-ingame-worldspace.md) (2단계, [FieldSlot 전환 계획](plan-ingame-worldspace-fieldslot.md) 이후)
> 의존 관계: `JungleDice.Core.Sprites.SpriteManager`, `JungleDice.Data.Table.CardTable`, `DG.Tweening`, `TMPro.TextMeshPro`(world space 3D 텍스트)
> 범위: 필드에 배치되는 친구카드의 world space 전용 컴포넌트 `WorldFriend`를 신설하고, `InGameSceneManager`가 필드 인스턴스 타입으로 `Friend` 대신 `WorldFriend`를 쓰도록 바꾼다. 기존 `Friend.cs`는 수정하지 않는다.

---

## 배경

`InGameSceneManager.cs`는 필드에 놓인 카드를 `Friend` 타입으로 40여 곳에서 참조한다(공격 판정, 병합, 치트, 카드 능력 발동 등). 그런데 `Friend`는 `MainMenuSceneManager._deckCards`(모험탭 덱 미리보기, [FriendDeckDisplay](../../../Assets/Scripts/MainMenu/FriendDeckDisplay.cs) 경유)에서도 실사용 중이라 그대로 둬야 한다 — 이것이 [개요 문서](plan-ingame-worldspace.md)가 "`Friend`(UI)는 유지, `WorldFriend`(world space)는 신설"로 범위를 정한 이유다.

이번 문서는 `WorldFriend.cs`를 `Friend.cs`와 동일한 로직으로 작성하되 렌더링 컴포넌트만 `SpriteRenderer`/`TextMeshPro`(3D)로 바꾸고, `InGameSceneManager.cs`의 필드 관련 타입 참조를 `WorldFriend`로 교체한다. 여기에 더해 world space에서는 여러 스프라이트가 같은 화면 위치에서 겹칠 수 있어, `WorldFriend`가 어디에 부모로 붙든(필드 슬롯/공격 연출 레이어) 일관된 렌더링 순서를 갖도록 깊이(z) 규칙을 정한다.

---

## 설계 목표

- `WorldFriend`는 `Friend`와 퍼블릭 멤버(이름/시그니처)를 동일하게 유지 — `InGameSceneManager.cs` 호출부는 타입 이름만 바꾸면 되도록
- 공유 베이스 클래스/인터페이스는 만들지 않는다(YAGNI) — `Friend`는 "건드리지 않는다"는 제약이 있고, 각 메서드가 4~6줄 수준으로 짧아 상속 계층을 새로 만드는 비용이 코드 중복 비용보다 크다. 두 클래스 모두 `CardTable` 기반 규칙이라 로직이 따로 바뀔 일도 적다
- `SpriteRenderer.sprite`/`TextMeshPro.text`/`.color`는 각각 `Image.sprite`/`TextMeshProUGUI.text`/`.color`와 프로퍼티명이 동일해 로직(수치 계산, `GetStatColor`)은 한 글자도 바꾸지 않고 포팅 가능
- 텍스트는 world space `TextMeshPro`(3D) — `TMP_Text` 상속이라 UGUI와 API가 같다
- 부모(필드 슬롯 또는 공격 연출 레이어)가 무엇이든 항상 부모보다 z -1만큼 카메라 쪽에 그려지도록 깊이를 고정 — uGUI의 그리기 순서(sibling index)를 대신할 world space 규칙

---

## 핵심 설계 결정

### 1. `WorldFriend` — `Friend.cs`를 컴포넌트 타입만 바꿔 포팅

```csharp
public class WorldFriend : MonoBehaviour
{
    [SerializeField] private SpriteRenderer _cardRenderer;      // Friend의 Image _cardImage
    [SerializeField] private TextMeshPro _attText;               // Friend의 TextMeshProUGUI _attText
    [SerializeField] private TextMeshPro _hpText;                // Friend의 TextMeshProUGUI _hpText
    [SerializeField] private SpriteRenderer _highlightRenderer;  // Friend의 Image _highlightImage

    // Key/Att/CurrentHp/MaxHp/HasShield/HasRevived/IsDead/SpawnMark 및
    // SetKey/TakeDamage/MergeWith/DoubleAtt/AddAtt/MultiplyAtt/DivideAtt/AddHp/Heal/
    // MultiplyHp/DivideHp/HealToMax/AddShield/ApplySpawnMark/TryRevive/OverrideStats/
    // GetStatColor/SetHighlight/PunchScale/MoveTo/SetParent — Friend.cs와 완전히 동일한 구현
    // (컴포넌트 타입만 바뀌었을 뿐 계산/색상 로직은 한 줄도 다르지 않다)
}
```

전체 구현은 [WorldFriend.cs](../../../Assets/Scripts/InGame/WorldFriend.cs) 참고.

### 2. `InGameSceneManager.cs` — 필드 인스턴스 타입을 `Friend`→`WorldFriend`로 교체

- `_friendPrefab`(`Friend`) → `_worldFriendPrefab`(`WorldFriend`)로 필드명까지 함께 변경(타입이 바뀌는 시점이라 어차피 Inspector 재연결이 필요해, 이름도 명확하게 맞췄다)
- `List<Friend>`, 메서드 파라미터 `Friend existing/target/friend` 등 필드 로직에 쓰인 타입은 전부 `WorldFriend`로 치환. 필드에 놓인 인스턴스를 얻는 방식 자체는 [FieldSlot 전환 계획](plan-ingame-worldspace-fieldslot.md)의 `FieldSlot.PlacedFriend` 참조를 쓴다(트랜스폼 탐색 아님)
- `MainMenuSceneManager.cs`/`FriendEditor.cs`의 `Friend` 참조는 대상이 아니다 — 전역 치환이 아니라 `InGameSceneManager.cs` 안의 필드 인스턴스 참조만 바꿨다

### 3. 깊이(z) 규칙 — 부모보다 항상 -1

world space에서는 카드가 슬롯 배경 위에, 공격 연출 중에는 다른 카드 위에 그려져야 한다. uGUI였다면 sibling index(그리기 순서)로 해결했을 문제를, world space에서는 z 좌표로 해결한다.

```csharp
public void SetParent(Transform parent)
{
    transform.SetParent(parent, worldPositionStays: true);
    SnapDepthToParent();
}

// 슬롯/공격 레이어 등 어디에 붙든 parent보다 z -1만큼 앞(카메라 쪽)에 그려지도록 강제
public void SnapDepthToParent()
{
    var localPosition = transform.localPosition;
    localPosition.z = -1f;
    transform.localPosition = localPosition;
}

public void MoveTo(Vector3 worldPosition, float duration, Ease ease)
{
    transform.DOKill();
    worldPosition.z = transform.position.z; // 깊이는 SnapDepthToParent가 정한 값을 그대로 유지 — 이동은 x/y만
    transform.DOMove(worldPosition, duration).SetEase(ease);
}
```

- `SnapDepthToParent`는 `SetParent`(슬롯 ↔ 공격 레이어 이동)뿐 아니라 최초 `Instantiate` 직후에도 호출해야 한다 — `Instantiate(prefab, parent)`는 프리팹의 로컬 z(0)를 그대로 쓰므로 별도 보정이 필요하다. `InGameSceneManager`는 `Instantiate` + `SnapDepthToParent` + `FieldSlot.PlaceFriend`를 묶은 `SpawnWorldFriend(FieldSlot slot)` 헬퍼를 통해서만 `WorldFriend`를 생성한다 — 생성과 배치를 한 곳에서 묶어, 호출부마다 `PlaceFriend` 호출을 반복하거나 빠뜨릴 여지를 없앤다.
- `MoveTo`는 목표 좌표의 z를 무시하고 자기 현재 z로 덮어쓴다 — 공격 연출 중 타겟 카드/본체의 z를 그대로 따라가면 `SnapDepthToParent`가 정한 깊이가 이동 도중 흐트러지기 때문이다. 이동은 항상 x/y만 일어난다.

---

## 클래스 구조

```
WorldFriend : MonoBehaviour                     (신규, InGame/)
├── (퍼블릭 멤버는 Friend와 동일 — 위 코드 스니펫의 주석 목록 참고)
├── SetParent(Transform)          ← SnapDepthToParent 포함
├── SnapDepthToParent()           ← 신규, parent보다 z -1
├── MoveTo(Vector3, float, Ease)  ← 목표 z 무시, x/y만 이동
├── _cardRenderer : SpriteRenderer [SerializeField]
├── _attText/_hpText : TextMeshPro [SerializeField]
└── _highlightRenderer : SpriteRenderer [SerializeField]

InGameSceneManager (기존 파일 수정, InGame/)
├── _worldFriendPrefab : WorldFriend [SerializeField]   ← 기존 _friendPrefab(Friend)에서 이름+타입 변경
├── SpawnWorldFriend(FieldSlot slot) : WorldFriend       ← 신규, Instantiate + SnapDepthToParent + slot.PlaceFriend를 묶은 헬퍼
└── 필드 인스턴스를 다루는 모든 메서드의 Friend → WorldFriend 치환
```

---

## 파일 구성

```
Assets/Scripts/InGame/
├── WorldFriend.cs           ← 신규 (Friend.cs 포팅)
└── InGameSceneManager.cs    ← 기존 파일 수정 (Friend → WorldFriend 타입/필드명 치환)
```

---

## Unity 씬/오브젝트 구성 (`WorldFriend.prefab`)

```
[Assets/Prefabs/WorldFriend.prefab]
└── WorldFriend (루트) — Transform + WorldFriend.cs
    ├── Image   — SpriteRenderer(카드 스프라이트) → _cardRenderer
    ├── Att     — SpriteRenderer(공격력 아이콘)
    │   └── txt — TextMeshPro(3D) → _attText
    ├── Hp      — SpriteRenderer(체력 아이콘)
    │   └── txt — TextMeshPro(3D) → _hpText
    └── Square  — SpriteRenderer(하이라이트 오버레이) → _highlightRenderer
```

`InGameSceneManager` GameObject의 `_worldFriendPrefab` 필드에 이 프리팹이 연결돼 있다(타입이 `Friend`→`WorldFriend`로 바뀌어 기존 연결이 끊어졌던 것을 재연결).

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `SpriteRenderer` 정렬 순서가 배경/슬롯과 뒤섞임 | Sorting Layer 분리 또는 Order in Layer로 카드가 항상 슬롯 배경 위에 오도록 |
| `TextMeshPro`(3D)가 카메라 각도에 따라 안 보임 | 카드 오브젝트는 z축 회전만 허용, x/y 회전은 0으로 고정(카메라를 향하도록) |
| `Friend.cs`가 나중에 수정돼도 `WorldFriend.cs`에 자동 반영 안 됨 | 의도된 트레이드오프(설계 목표 2번) — 두 클래스가 크게 벌어지면 그때 공통화 재검토 |
| `Instantiate(_worldFriendPrefab, ...)`를 `SpawnWorldFriend` 헬퍼 없이 직접 호출 | `SnapDepthToParent`가 호출되지 않아 parent와 같은 z에 생성됨 — `WorldFriend`는 항상 `SpawnWorldFriend`로만 생성한다 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `WorldFriend.SetKey(key)` 호출 | `_cardRenderer.sprite`/`_attText.text`/`_hpText.text`가 `CardTable` 값과 일치 |
| 2 | `TakeDamage`/`MergeWith`/`DoubleAtt` 등 각 메서드 호출 | `Friend.cs`와 동일한 수치 변화 및 색상(`GetStatColor`) 반영 |
| 3 | `SetHighlight(true, Color.red)` → `SetHighlight(false, Color.clear)` | `_highlightRenderer` 활성화+색상 반영 → 비활성화 |
| 4 | 공격 연출 중 `SetParent(_attackLayer)`로 옮겨갔다가 `MoveTo`로 타겟까지 이동 | 이동 중에도 z가 `_attackLayer` 기준 -1로 고정 유지, x/y만 변함 |

---

## 구현 시 주의사항

- `Friend.cs`는 절대 수정하지 않는다 — `MainMenuSceneManager`의 덱 미리보기가 실사용 중이다.
- `WorldFriend.cs`를 다시 손댈 때도 `Friend.cs`를 참고해 로직(수치 계산, `GetStatColor`)을 동일하게 유지한다 — 공격/합체/능력 판정이 이 로직에 의존한다.
- 필드에 `WorldFriend`를 새로 생성하는 코드를 추가할 때는 `Instantiate`를 직접 쓰지 말고 `SpawnWorldFriend`를 거친다 — 깊이 규칙이 빠지면 렌더링 순서가 깨진다.

---

## 구현 후 체크리스트

- [x] `WorldFriend.cs` 작성(`Assets/Scripts/InGame/`)
- [x] `InGameSceneManager.cs`의 `Friend` → `WorldFriend` 치환(필드명 `_friendPrefab` → `_worldFriendPrefab` 포함)
- [x] `SetParent`/`SnapDepthToParent`/`MoveTo` 깊이 규칙 구현, `SpawnWorldFriend` 헬퍼로 생성 지점 통일
- [x] `WorldFriend.prefab` 생성 + 컴포넌트 연결
- [x] `InGameSceneManager`의 `_worldFriendPrefab` 필드 재연결
- [x] 테스트 시나리오 검증
- [x] 다음 문서로 이동: [plan-ingame-worldspace-carddrop.md](plan-ingame-worldspace-carddrop.md)
