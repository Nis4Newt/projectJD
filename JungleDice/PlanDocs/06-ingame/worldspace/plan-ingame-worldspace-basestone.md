# BaseStone World Space 전환 계획

> 상위 문서: [InGame 필드 World Space 전환 개요](plan-ingame-worldspace.md) (4단계, 다른 단계와 독립적으로 진행 가능)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`(`_userBase`/`_computerBase` 참조)
> 범위: 본체 체력 오브젝트 `BaseStone`을 uGUI(`RectTransform`/`Image`/`TextMeshProUGUI`)에서 world space(`Transform`/`SpriteRenderer`/`TextMeshPro`)로 전환한다. `Friend`와 달리 `BaseStone`은 필드 밖 다른 사용처가 없어([WorldFriend 신설 계획](plan-ingame-worldspace-worldfriend.md)과 달리) 별도 클래스를 만들지 않고 `BaseStone.cs`를 직접 수정한다.

---

## 배경

`BaseStone`은 `InGameSceneManager.cs`(필드 로직)와 `ComputerAI.cs`(주석 언급뿐, 실제 참조 없음) 외에는 어디서도 쓰이지 않는다 — `Friend`가 `MainMenuSceneManager`의 덱 미리보기에서 별도로 쓰여 `WorldFriend`를 새로 만들어야 했던 것과 다른 상황이다. 그래서 `BaseStone`은 기존 컴포넌트를 그대로 world space로 바꾸면 된다.

씬 구조(`InGame.unity`, `mybase`/`oppobase` 하위):
- `my_bastone`/`oppo_basestone` GameObject — `RectTransform` + `Image`(본체 스프라이트) + `CanvasRenderer` + `BaseStone.cs`
- 자식으로 `Text (TMP)`(`TextMeshProUGUI`, 체력 숫자) 등 3개 자식 오브젝트 포함

`FieldSlot`을 world space로 바꿨을 때와 완전히 같은 패턴(`RectTransform`/`Image` → `Transform`/`SpriteRenderer`)이라 반복 설명하지 않는다 — [FieldSlot 전환 계획](plan-ingame-worldspace-fieldslot.md) 참고.

---

## 설계 목표

- `BaseStone.cs`의 퍼블릭 인터페이스(`CurrentHp`/`MaxHp`/`TakeDamage`/`Heal`)는 그대로 유지 — `InGameSceneManager.cs`는 코드 변경 불필요
- 별도 클래스(`WorldBaseStone` 같은)를 만들지 않는다(YAGNI) — 다른 사용처가 없어 `Friend`/`WorldFriend`처럼 나눌 이유가 없다
- 씬 오브젝트 전환은 `FieldSlot`과 동일한 패턴 재사용 — 새로운 설계 결정 없음

---

## 핵심 설계 결정

### 1. `BaseStone.cs` — 컴포넌트 타입만 교체

```csharp
public class BaseStone : MonoBehaviour
{
    [SerializeField] private TextMeshPro _hpText; // 기존 TextMeshProUGUI에서 교체
    [SerializeField] private int _maxHp = 30;

    // CurrentHp/MaxHp/Awake/TakeDamage/Heal — 변경 없음 (TextMeshPro와 TextMeshProUGUI 모두 TMP_Text 상속, .text API 동일)
}
```

### 2. 씬 오브젝트 전환 — FieldSlot과 동일한 패턴

`my_bastone`/`oppo_basestone` GameObject: `RectTransform`+`Image`+`CanvasRenderer` 제거 → `Transform`+`SpriteRenderer`(기존 본체 스프라이트 재사용) 추가. 자식 `Text (TMP)`도 `TextMeshProUGUI` → `TextMeshPro`(3D)로 교체.

`BaseStone`은 `FieldSlot`과 달리 드롭 대상이 아니므로 `Collider2D`/`Physics2DRaycaster`는 필요 없다 — 순수 표시 전용 오브젝트다.

---

## 클래스 구조

변경 없음 — `BaseStone`은 필드 타입 하나(`TextMeshProUGUI`→`TextMeshPro`)만 바뀌고 나머지 구조 동일.

---

## 파일 구성

```
Assets/Scripts/InGame/
└── BaseStone.cs   ← 기존 파일 수정 (필드 타입만 교체)
```

---

## Unity 씬/오브젝트 구성

```
[Scene: InGame.unity]
├── my_bastone (유저 본체)
│   ├── RectTransform/Image/CanvasRenderer 제거 → Transform + SpriteRenderer 추가(본체 스프라이트 재사용)
│   └── 자식 Text (TMP) — TextMeshProUGUI → TextMeshPro(3D)로 교체 → BaseStone._hpText 재연결
└── oppo_basestone (컴퓨터 본체) — 위와 동일하게 전환
```

`InGameSceneManager`의 `_userBase`/`_computerBase` 필드는 타입(`BaseStone`)이 안 바뀌므로 재연결 불필요 — 씬에서 오브젝트 자체를 교체하지 않는 한(같은 GameObject에 컴포넌트만 갈아 끼우면) 기존 참조가 그대로 유지된다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `Text (TMP)` 전환 시 `_hpText` 필드 연결이 끊어짐 | `TextMeshProUGUI` 컴포넌트를 제거하고 `TextMeshPro`를 새로 추가하면 기존 Inspector 연결이 끊어진다 — 재연결 필요(인스펙터 작업) |
| 본체 스프라이트가 `Image`의 `Sprite`와 다른 import 설정 필요 | `Image`용 스프라이트는 이미 Sprite(2D and UI) 타입이라 `SpriteRenderer`에서도 그대로 재사용 가능 — 추가 import 설정 불필요 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `BaseStone.TakeDamage(5)` 호출 | world space `_hpText.text`가 감소된 값으로 갱신 |
| 2 | `BaseStone.Heal(3)` 호출 | `_hpText.text`가 `MaxHp`를 넘지 않고 증가 |
| 3 | 본체 체력 0 도달(`GameState.GameOver` 전이) | 기존 `TryEndGameIfBaseDestroyed` 로직 그대로 동작(코드 변경 없음이므로 회귀 없어야 함) |

---

## 구현 시 주의사항

- `BaseStone.cs`는 필드 타입 교체 외에 로직을 건드리지 않는다 — 공격/게임오버 판정이 이 클래스에 의존한다.
- `Collider2D`/`Physics2DRaycaster`는 이 오브젝트에 필요 없다 — 드롭 대상이 아니다(혼동해서 추가하지 않도록 주의).
- 씬 전환 시 GameObject 자체를 새로 만들지 말고 기존 `my_bastone`/`oppo_basestone`의 컴포넌트만 교체한다 — 그래야 `InGameSceneManager._userBase`/`_computerBase` Inspector 연결이 끊어지지 않는다.

---

## 구현 후 체크리스트

- [x] `BaseStone.cs`의 `_hpText` 타입을 `TextMeshPro`로 교체
- [x] `my_bastone`/`oppo_basestone`을 `Transform`+`SpriteRenderer`로 전환
- [x] 자식 `Text (TMP)`를 world space `TextMeshPro`로 교체 + `_hpText` 재연결
- [x] 테스트 시나리오 검증
- [x] [InGame 필드 World Space 전환 개요](plan-ingame-worldspace.md) 체크리스트 갱신
