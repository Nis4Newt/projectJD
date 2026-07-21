# SpriteManager 구현 계획

> 상위 문서: [공용 코어 시스템 설계 계획](../plan-core-systems.md) (시스템 목록 #11)
> 관련 문서: [테이블 리더 시스템 구현 계획](../../03-table/plan-table.md) (`Resources.Load` 기반 로드, static 진입점 패턴 재사용), [UserData 구현 계획](../../04-userdata/plan-userdata.md) (static 클래스로 전역 접근점을 두는 패턴 재사용)
> 범위: `Resources/Sprite/` 하위에 등록된 스프라이트를 이름만으로 조회하는 `SpriteManager` 설계. 실제 `.cs` 구현과 실제 스프라이트 카테고리(폴더) 확정은 이번 문서의 범위 밖(후속 구현 커밋에서 진행)

---

## 배경 / 문제 인식

인게임 곳곳에서 스프라이트를 참조해야 하는데, 지금까지는 Inspector에 개별 필드로 직접 드래그하거나 코드에서 각자 경로를 하드코딩하는 식으로 흩어질 여지가 있다. 스프라이트를 종류(카드, 아이콘 등)별로 폴더를 나눠 `Resources` 밑에 두고, 조회 로직을 한 곳에 모아 이름만 넘기면 꺼내 쓸 수 있게 한다.

프로젝트에는 이미 `Resources.Load` 기반 정적 접근점 패턴이 두 번 쓰였다 — `TableBase<TSelf,TData,TKey>.Instance`(`Resources.Load<TSelf>($"Tables/{typeof(TSelf).Name}")`)와 `UserManager`(static 클래스 + 전역 접근점). `SpriteManager`도 같은 사고방식을 재사용한다: 씬 전환에도 유지되어야 하는 상태가 아니고 Unity 콜백도 필요 없으므로 MonoBehaviour 싱글턴이 아니라 static 클래스로 둔다.

---

## 설계 목표

- 호출부는 스프라이트가 실제 어느 경로에 있는지 몰라도 되게 한다 — 이름만 넘기면 됨
- 종류(카드/아이콘 등)별로 `Resources/Sprite/` 하위 폴더를 나누고, 각 폴더는 `SpriteManager` 안의 `SpriteCategory` enum 값으로 고정해 임의의 문자열 경로 조합을 컴파일 타임에 원천 차단
- 없는 이름을 요청해도 예외로 죽지 않고 `null`을 반환 — 호출부가 방어적으로 처리(예: 기본 스프라이트로 대체)할 수 있게
- 카테고리가 늘어날 때 기존 카테고리 조회 코드에 영향을 주지 않고 enum 값 + 전용 메서드만 추가하면 되게 한다

---

## 핵심 설계 결정

### 네임스페이스: `Core/Sprites` (단수 `Sprite`가 아니라 복수)

`UnityEngine.Sprite` 타입이 이미 존재하므로, 네임스페이스를 `JungleDice.Core.Sprite`로 두면 이 네임스페이스 안에서 `using UnityEngine;`을 쓰고 반환 타입을 `Sprite`라고만 적었을 때 컴파일러가 이를 `UnityEngine.Sprite` 타입이 아니라 현재 네임스페이스 `JungleDice.Core.Sprite` 자신으로 해석해 `CS0118 'JungleDice.Core.Sprite'은(는) 네임스페이스이지만 형식으로 사용되었습니다` 오류가 난다. `Core/Table`, `Core/User`처럼 폴더/네임스페이스를 도메인명과 그대로 맞추고 싶은 관례를 따르되, 실제 UnityEngine 타입과 문자 그대로 겹치는 `Sprite` 한 단어만 예외적으로 복수형 `Sprites`로 바꿔 충돌을 피한다.

```
Assets/Scripts/Core/Sprites/SpriteManager.cs   ← namespace JungleDice.Core.Sprites
```

### static 클래스 — `TableLoader`/`UserManager`와 동일 패턴

**후보 검토:**

| 후보 | 기각/채택 사유 |
|------|----------------|
| MonoBehaviour 싱글턴 (`Singleton<T>`) | 기각 — `Update`/씬 유지 같은 Unity 생명주기가 전혀 필요 없음. GameObject 배치 비용만 늘어남 |
| **static 클래스** | **채택** — `TableBase<TSelf,TData,TKey>.Instance`가 이미 "요청 시점에 `Resources.Load`, 실패 시 `null` + 로그"라는 동일한 문제를 이 방식으로 풀었음. `SpriteManager`는 제네릭 베이스가 필요 없을 만큼 단순하므로(테이블처럼 타입별로 갈라질 필요가 없음) 별도 베이스 클래스 없이 이 하나의 static 클래스로 충분 |

### 카테고리는 `SpriteCategory` enum, 폴더 경로는 별도 매핑 없이 `enum.ToString()`으로 도출

처음에는 카테고리 폴더 경로를 `private const string`으로 하나씩 선언했다가, 그다음엔 `SpriteCategory` enum + `Dictionary<SpriteCategory, string>` 매핑으로 바꿨다. 하지만 이 방식은 카테고리를 추가할 때마다 "enum 값 이름"과 "매핑 문자열"을 항상 같은 값으로 나란히 유지해야 하는 중복이 생긴다(`SpriteCategory.Card`인데 매핑은 `"Sprite/Icon"`으로 잘못 적어도 컴파일러가 잡아주지 못함). 대신 enum 값 이름 자체를 폴더명으로 그대로 사용한다 — `category.ToString()`(문자열 보간 시 암묵적으로 호출됨)이 `"Card"`를 반환하므로, 별도 매핑 테이블 없이 `$"{RootPath}/{category}"`로 경로를 조립한다. 카테고리를 매개변수로 받는 범용 `Get(category, name)`을 공개 API로 노출하지는 않는다 — 그러면 호출부가 `SpriteCategory`를 알아야 해서 "이름만 넘기면 된다"는 목표와 어긋나기 때문에, 공개 메서드는 여전히 카테고리별 전용 메서드(`GetCard(name)` 등)로 감싸고 내부에서만 enum을 사용한다.

```csharp
public enum SpriteCategory
{
    // 카테고리 확정 시 여기에 값 추가 (예: Card) — 값 이름이 곧 Resources/Sprite/ 하위 폴더명이 된다
}

// 예: public static Sprite GetCard(string name) => Load(SpriteCategory.Card, name);
```

새 카테고리가 생기면 (1) `SpriteCategory`에 값 추가(값 이름을 실제 폴더명과 동일하게), (2) 전용 조회 메서드 한 줄 추가, 두 곳만 건드리면 되고 기존 카테고리는 손댈 필요가 없다. 대가로 폴더명과 enum 값 이름이 항상 문자 그대로 일치해야 한다는 제약이 생기는데(대소문자 포함), 이는 `TableBase.Instance`가 `typeof(TSelf).Name`으로 asset 경로를 짓는 것과 같은 종류의 관례이므로 프로젝트 전반의 패턴과 일관적이다. `Resources` 하위 루트 폴더명(`"Sprite"`)은 `Load` 안에서 한 번만 쓰이므로 별도 상수로 빼지 않고 그대로 문자열 보간에 인라인한다 — 재사용되지 않는 리터럴을 상수로 미리 빼두는 건 과잉 추상화(YAGNI).

### 조회 실패 시 `null` 반환 + `Debug.LogWarning`

`TableBase.Instance`는 테이블 asset 부재를 `Debug.LogError`로 다루지만(설정 누락은 기획 이슈), 스프라이트 조회 실패는 좀 더 흔하게 발생할 수 있는 상황(오타, 아직 리소스가 준비되지 않은 이름을 임시로 참조)이라 `LogWarning` 수준으로 낮춘다. 예외를 던지지 않고 `null`을 반환해 호출부가 기본 스프라이트로 대체하는 등 자체적으로 복구할 수 있게 한다. `SpriteCategory`는 별도 매핑 테이블이 없으므로 정의된 값이라면 항상 어떤 경로든 조립되고, 그 경로에 실제 폴더/파일이 없는 경우도 이 실패 경로로 자연스럽게 흡수된다.

```csharp
private static Sprite Load(SpriteCategory category, string name)
{
    var folder = $"Sprite/{category}";
    var sprite = Resources.Load<Sprite>($"{folder}/{name}");
    if (sprite == null)
        Debug.LogWarning($"[SpriteManager] Sprite not found: {folder}/{name}");
    return sprite;
}
```

### 캐싱은 추가하지 않음 (YAGNI)

Unity의 `Resources.Load`는 최초 로드 이후 동일 경로 재요청 시 이미 메모리에 올라온 오브젝트를 반환하므로(디스크 재접근이 아님), `SpriteManager` 레벨에서 별도 `Dictionary` 캐시를 얹는 것은 지금 시점에 실익이 없는 중복 최적화다. 요청 빈도가 실제 문제가 되면(예: 프레임마다 호출) 그때 캐시를 추가한다.

---

## 클래스 구조

```
SpriteCategory                                    (신규, Core/Sprites/, enum)
└── (카테고리 확정 시 값 추가 — 값 이름이 곧 Resources/Sprite/ 하위 폴더명, 예: Card)

SpriteManager                                     (신규, Core/Sprites/, static)
├── Load(SpriteCategory category, string name) : Sprite  ← private, "Sprite/{category}"로 폴더 경로 조립 + Resources.Load 공통 처리 + 실패 로그
└── (카테고리별 조회 메서드는 실제 카테고리 확정 시 추가 — 예: GetCard(string name))
```

---

## 파일 구성

```
Assets/
├── Resources/
│   └── Sprite/                          ← 신규 폴더, 종류별 하위 폴더는 추후 추가 (예: Card/, Icon/)
└── Scripts/
    └── Core/
        └── Sprites/                     ← 신규
            └── SpriteManager.cs
```

`Core/Sprites/`는 특정 하위 시스템에 속하지 않는 공용 유틸리티이므로 `Core/Table/`, `Core/User/`와 동일하게 `Core/` 바로 아래 배치한다.

---

## 상세 구현 명세

### SpriteManager.cs

```csharp
using UnityEngine;

namespace JungleDice.Core.Sprites
{
    public enum SpriteCategory
    {
        // 카테고리 확정 시 여기에 값 추가 (예: Card) — 값 이름이 곧 Resources/Sprite/ 하위 폴더명이 된다
    }

    public static class SpriteManager
    {
        // 카테고리 폴더가 확정되는 대로 여기에 전용 조회 메서드를 추가한다.
        // 예: public static Sprite GetCard(string name) => Load(SpriteCategory.Card, name);

        private static Sprite Load(SpriteCategory category, string name)
        {
            var folder = $"Sprite/{category}";
            var sprite = Resources.Load<Sprite>($"{folder}/{name}");
            if (sprite == null)
                Debug.LogWarning($"[SpriteManager] Sprite not found: {folder}/{name}");
            return sprite;
        }
    }
}
```

---

## 이번 범위에서 제외

- 실제 스프라이트 카테고리(폴더) 확정 — 카드/아이콘 등 구체적인 종류와 `Resources/Sprite/` 하위 폴더 구조는 아직 정해지지 않음. 확정되는 대로 `SpriteCategory` 값(= 실제 폴더명) + 전용 메서드를 추가
- 캐싱 — `Resources.Load` 자체가 이미 반복 호출에 대한 내부 캐시를 갖고 있어 지금 시점엔 불필요 (위 "캐싱은 추가하지 않음" 참고)
- 에디터 툴(예: 폴더/파일명 검증, 자동 목록화) — 카테고리 수가 늘어나 수작업이 부담되는 시점에 재검토
- 비동기 로드(`Resources.LoadAsync`) — 현재 요청사항은 동기 조회만 다룸. 로딩 스파이크가 실측으로 문제될 때 별도 API로 확장

---

## 엣지 케이스

| 상황 | 처리 방식 |
|------|-----------|
| 존재하지 않는 이름으로 조회 | `Resources.Load`가 `null` 반환 → `Debug.LogWarning` 출력 후 `null` 그대로 반환 (예외 없음) |
| 폴더 경로는 맞지만 파일이 아직 `Resources/Sprite/...`에 없음(리소스 준비 전) | 위와 동일하게 `null` + 경고 — 조회 실패와 오타를 구분하지 않음(둘 다 결과는 같으므로) |
| 같은 이름을 여러 번 조회 | `Resources.Load`의 내부 캐시로 두 번째 호출부터는 디스크 재접근 없이 반환됨(별도 처리 불필요) |
| 이름에 `null` 또는 빈 문자열 전달 | `Resources.Load<Sprite>("folder/")` 형태로 호출되어 매칭되는 asset이 없으므로 `null` 반환 + 경고 — 별도 가드 없이 동일한 실패 경로로 자연스럽게 처리됨 |
| `SpriteCategory` 값 이름과 실제 `Resources/Sprite/` 하위 폴더명이 어긋남(오타, 대소문자 등) | 컴파일 타임에 잡히지 않음 — `category.ToString()`으로 조립한 경로에 해당 폴더가 없으므로 매번 `null` + 경고로 귀결됨(스프라이트 자체가 없는 경우와 동일한 실패 경로) |

---

## 테스트 시나리오

카테고리가 하나 이상 추가된 이후에 실제로 검증 가능. 아래는 `GetCard(string name)`가 추가됐다고 가정한 예시:

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `Resources/Sprite/Card/Ace.png` 존재 상태에서 `SpriteManager.GetCard("Ace")` 호출 | 해당 `Sprite` 반환, 경고 없음 |
| 2 | 존재하지 않는 이름으로 `SpriteManager.GetCard("Missing")` 호출 | `null` 반환, `Debug.LogWarning` 1회 출력 |
| 3 | 같은 이름으로 `GetCard`를 반복 호출 | 매번 동일한 `Sprite` 참조 반환(예외/오류 없음) |

---

## 구현 시 주의사항

- **네임스페이스는 반드시 `JungleDice.Core.Sprites`(복수)로 둔다**: `Sprite`(단수)로 두면 `UnityEngine.Sprite`와 이름이 충돌해 컴파일 오류(`CS0118`)로 이어진다. 위 "네임스페이스" 설계 결정 참고.
- **`SpriteCategory` 값 이름은 반드시 실제 `Resources/Sprite/` 하위 폴더명과 문자 그대로(대소문자 포함) 일치시킨다**: 별도 매핑 테이블이 없으므로 `category.ToString()`이 곧 경로의 일부가 된다. 어긋나면 컴파일 타임에 잡히지 않고 항상 `null` + 경고만 반복된다(`TableBase.Instance`가 `typeof(TSelf).Name` 문자열에 의존해 로드 실패를 조용히 겪는 것과 같은 종류의 함정).
- **호출부는 `null` 반환을 항상 방어적으로 처리한다**: `SpriteManager`는 예외를 던지지 않고 `null`로 실패를 알리므로, `Image.sprite = SpriteManager.GetCard(name)`처럼 그대로 대입하면 조회 실패 시 조용히 빈 이미지가 될 수 있다 — 필요하면 기본 스프라이트로 대체하는 처리를 호출부에서 추가.
- **새 카테고리는 `Load` 헬퍼를 재사용**: 카테고리별 메서드가 각자 `Resources.Load`를 직접 호출하지 않고 공통 `Load(SpriteCategory category, string name)`을 거치게 해, 실패 로그 형식과 처리 방식이 카테고리마다 갈라지지 않게 한다.

---

## 구현 후 체크리스트

- [x] `Assets/Resources/Sprite/` 폴더 생성
- [x] `SpriteManager.cs` 작성 (`Assets/Scripts/Core/Sprites/`)
- [ ] 첫 스프라이트 카테고리 확정 후 `SpriteCategory` 값(= 실제 폴더명) + 전용 조회 메서드 추가
- [ ] 테스트 시나리오 3개 중 가능한 범위 수동 검증
- [x] `Assets/Plans/01-core-systems/spritemanager/plan-spritemanager.md`로 이 문서 저장 (프로젝트 관례)
