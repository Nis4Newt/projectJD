# Friend 컴포넌트 구현 계획

> 상위 문서: 없음 (신규 최상위 시스템, `03-table`/`04-userdata`와 동일한 위계)
> 관련 문서: [테이블 리더 시스템 구현 계획](../03-table/plan-table.md) (`CardTable.Instance.Get(key)`로 att/hp/cardname 조회, "없는 key → LogError + default" 컨벤션 재사용), [SpriteManager 구현 계획](../01-core-systems/spritemanager/plan-spritemanager.md) (`SpriteCategory.Card` 카테고리를 이번 문서에서 확정하고 `GetCard(string)` 전용 메서드를 추가 — 스프라이트 폴더 확정이 이번 범위에 포함됨), [UserData 구현 계획](../04-userdata/plan-userdata.md) (`UserData.Friends : IReadOnlyList<int>`가 이 `Friend` 오브젝트들이 표시할 key 목록의 출처가 될 예정이나, 실제 스폰/배치 로직은 이번 범위 밖)
> 범위: 에디터에서 이미 만들어 둔 "친구" GameObject(UI, `Canvas` 하위)에 붙일 `Friend` 컴포넌트 설계. `key`를 받아 카드 이미지·공격력·생명력을 표시하는 것까지만 다룬다. 실제 `.cs`/에셋 이동 구현은 이번 문서의 범위 밖(후속 구현 커밋에서 진행)

---

## 배경 / 문제 인식

"친구"는 `CardTable`의 한 행(`CardTableData`)을 화면에 표시하는 개체다 — 카드 이미지, 공격력, 생명력을 key 하나로 결정한다. 이미 두 가지 조회 인프라가 준비돼 있다: `CardTable.Instance.Get(key)`(`GetAtt`/`GetHp` 포함)와 `SpriteManager`(이름만 넘기면 `Resources/Sprite/{카테고리}/`에서 스프라이트를 찾아주는 static 클래스). 다만 `SpriteManager`는 아직 카테고리가 하나도 확정되지 않은 상태([spritemanager 계획](../01-core-systems/spritemanager/plan-spritemanager.md)의 "이번 범위에서 제외" 항목)라, 카드 스프라이트 조회 메서드가 없다.

카드 원본 이미지는 현재 `Assets/Sprites/Cards/{key}.png`(예: `1000.png`~`1009.png`) 형태로 존재하는데, 두 가지 이유로 지금 위치·설정 그대로는 `SpriteManager`가 찾을 수 없다:

1. `SpriteManager.Load`는 `Resources.Load<Sprite>($"Sprite/{category}/{name}")`를 쓰므로, 대상 파일이 `Assets/Resources/` 하위에 있어야 한다. 지금 위치(`Assets/Sprites/Cards/`)는 `Resources` 밖이라 애초에 조회 대상이 아니다.
2. 카드 png들의 Texture Import 설정이 `Sprite Mode: Multiple`이고 내부 서브 스프라이트 이름이 `1000_0`처럼 `{key}_0` 형태로 슬라이스돼 있다(`1000.png.meta` 확인). `Sprite Mode: Multiple`인 텍스처는 해당 경로의 "메인 오브젝트"가 여전히 `Texture2D`이고 실제 `Sprite`는 이름이 붙은 서브 에셋이므로, `Resources.Load<Sprite>("Sprite/Card/1000")`처럼 경로만으로 조회하면 `null`이 반환된다. `Sprite Mode: Single`로 바꿔야 그 경로의 메인 오브젝트 자체가 `Sprite`가 되어 정상 조회된다.

따라서 이번 문서는 컴포넌트 설계와 함께, `SpriteManager`의 `Card` 카테고리를 확정하는 데 필요한 사전 작업(에셋 이동 + Import 설정 수정)도 범위에 포함한다.

---

## 설계 목표

- `Friend` 컴포넌트는 `key`(`int`) 하나만 받으면 이미지·공격력·생명력을 전부 채운다 — 호출부가 `CardTableData`나 스프라이트 경로를 직접 다루지 않는다
- 이미지 조회는 새 `Resources.Load` 호출을 추가하지 않고 기존 `SpriteManager`를 재사용한다 (프로젝트 관례: "새 카테고리는 `Load` 헬퍼를 재사용" — spritemanager 계획 참고)
- 능력치 조회는 `CardTable`의 기존 공개 API(`GetAtt`/`GetHp`)를 그대로 쓴다 — `Friend`가 `TryGet`/인덱서 등 protected 내부를 직접 건드리지 않는다
- 존재하지 않는 key, 조회 실패한 스프라이트 모두 예외 없이 처리한다 (프로젝트 전역 컨벤션: 데이터/리소스 부재는 기획 이슈지 크래시 사유가 아님)
- 지금 요청 범위(이미지/공격력/생명력 표시)를 넘어서는 상태(피격에 따른 생명력 변화, 합체 조건 판정 등 실제 게임플레이 로직)는 이번 컴포넌트에 넣지 않는다

---

## 핵심 설계 결정

### `Friend` : `MonoBehaviour`, UI 프리팹(Canvas 하위) 대상

**후보 검토:**

| 후보 | 기각/채택 사유 |
|------|----------------|
| `SpriteRenderer` + 월드 스페이스 `TextMesh` | 기각 — 에디터에서 이미 만든 친구 오브젝트가 `Canvas` 하위 UI(요청자 확인)이므로 대상이 아님 |
| **`Image` + `TextMeshProUGUI` (UI)** | **채택** — 프로젝트에 `TextMesh Pro` 패키지가 이미 임포트돼 있고(`Assets/TextMesh Pro/`), UI 표시가 확정된 방식 |

```csharp
[SerializeField] private Image _cardImage;
[SerializeField] private TextMeshProUGUI _attText;
[SerializeField] private TextMeshProUGUI _hpText;
```

### 조회 진입점: `SetKey(int key)` 하나로 통일

"이미지 설정 / 공격력 설정 / 생명력 설정"을 세 개의 공개 메서드로 나누지 않는다 — 셋 다 항상 같은 `key`에서 파생되는 값이라 따로 호출할 이유가 없고, 호출부가 순서를 신경 쓰거나 일부만 호출해 상태가 어긋나는 상황(예: 이미지는 카드 A인데 능력치는 카드 B)을 원천 차단한다.

```csharp
public void SetKey(int key)
{
    Key = key;

    var data = CardTable.Instance?.Get(key);
    if (data == null) return; // CardTable.Get이 이미 LogError를 남김 — 중복 로그 없음

    _cardImage.sprite = SpriteManager.GetCard(key.ToString());
    _attText.text = data.att.ToString();
    _hpText.text = data.hp.ToString();
}
```

- `Key`는 `{ get; private set; }` 공개 프로퍼티로 노출한다 — "친구는 `CardTable` 내용으로 하는 개체"라는 정의상, 이 오브젝트가 지금 어떤 카드를 표시 중인지는 이 컴포넌트가 들고 있어야 하는 최소한의 상태다(추후 합체 판정 등에서 필요해질 조회 지점이지만, 지금은 읽기 전용 프로퍼티 하나만 추가 — 그 이상의 게임플레이 로직은 범위 밖)
- `CardTable.Instance`가 `null`인 경우(asset 자체가 없는 설정 누락 상황)까지 방어한다 — `TableBase<TSelf,TData,TKey>.Instance`가 이미 이 경우 `null` + `LogError`로 처리하므로, `Friend`는 널 체크만 하고 추가 로그를 남기지 않는다

### 이미지 조회: `SpriteManager.GetCard(string)` 신규 추가, `Card` 카테고리 확정

[spritemanager 계획](../01-core-systems/spritemanager/plan-spritemanager.md)이 미리 설계해 둔 확장 지점을 그대로 채운다 — `SpriteCategory`에 `Card` 값 추가, 전용 조회 메서드 추가:

```csharp
public enum SpriteCategory
{
    Card,
}

public static class SpriteManager
{
    public static Sprite GetCard(string name) => Load(SpriteCategory.Card, name);

    private static Sprite Load(SpriteCategory category, string name) { /* 변경 없음 */ }
}
```

- `GetCard`는 `int`가 아니라 `string`을 받는다 — `SpriteManager`의 기존 계약(이름 기반 조회)을 그대로 따르고, `Friend` 쪽에서 `key.ToString()`으로 변환해 호출한다. `SpriteManager`에 `int` 오버로드를 추가하는 것도 고려했으나, 카드 외 다른 카테고리(향후 아이콘 등)는 애초에 이름이 문자열이라 `int` 전용 오버로드는 `Card` 카테고리에만 쓰이는 특수 케이스가 되어 기각(YAGNI)
- 조회 실패(스프라이트 없음) 시 `SpriteManager`가 이미 `Debug.LogWarning` + `null` 반환을 처리하므로 `Friend`는 별도 처리를 추가하지 않는다 — `_cardImage.sprite`가 `null`이 되어 빈 이미지로 보이는 것은 spritemanager 계획이 이미 알고 있는 동작(호출부가 필요하면 기본 스프라이트로 대체 가능하다고 명시했지만 지금은 요구되지 않음)

### 에디터 미리보기: `[CustomEditor(typeof(Friend))]` + key 입력 필드 + 버튼

`Friend`는 인스펙터에서 `_cardImage`/`_attText`/`_hpText` 필드를 연결한 뒤 실제로 카드가 잘 뜨는지 확인하려면 지금까지는 플레이 모드로 진입해 코드로 `SetKey`를 호출해야 했다. 이는 `TableAssetEditor`(`Assets/Scripts/Editor/Table/TableAssetEditor.cs`)가 이미 쓰는 패턴 — 커스텀 에디터에 버튼 하나를 얹어 즉시 실행 — 을 그대로 재사용해, 에디터에서(플레이 모드 아님) key를 입력하고 버튼을 누르면 바로 반영되게 한다.

```csharp
[CustomEditor(typeof(Friend))]
public class FriendEditor : UnityEditor.Editor
{
    private int _previewKey;

    private void OnEnable() => _previewKey = ((Friend)target).Key;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        _previewKey = EditorGUILayout.IntField("Card Key", _previewKey);
        if (GUILayout.Button("Apply Key"))
        {
            var friend = (Friend)target;
            friend.SetKey(_previewKey);
            EditorUtility.SetDirty(friend);
        }
    }
}
```

- `Friend.SetKey`는 순수하게 `CardTable.Instance`/`SpriteManager`(둘 다 `Resources.Load` 기반)만 호출하므로 플레이 모드가 아니어도 에디터에서 그대로 동작한다 — 별도 에디터 전용 로직을 분기하지 않고 런타임 메서드를 그대로 재사용
- 입력값은 `Friend`가 아니라 `FriendEditor` 쪽 필드(`_previewKey`)에 둔다 — `Friend` 자체에 "에디터에서 마지막으로 입력한 값" 같은 상태를 추가하면 런타임 컴포넌트에 에디터 전용 필드가 섞이게 되므로, 그 상태는 에디터 클래스 쪽에 격리
- `OnEnable`에서 `target.Key`로 초기화해 이미 `SetKey`가 적용된 오브젝트를 다시 선택했을 때 입력 필드가 0으로 리셋되지 않게 한다
- 버튼을 누른 뒤 `EditorUtility.SetDirty(friend)`를 호출한다 — 씬에 배치된 컴포넌트의 값을 코드로 바꾼 뒤 저장(Ctrl+S)해도 반영되게 하려면 필요(`TableAssetEditor`가 asset에 대해 `SetDirty`를 쓰는 것과 같은 이유, 대상만 씬 오브젝트로 다를 뿐)
- `Friend.cs`(런타임)와 별개로 `Assets/Scripts/Editor/InGame/FriendEditor.cs`(에디터 전용 어셈블리 폴더)에 둔다 — `TableAssetEditor`/`TableGenerator`가 `Editor/Table/`에 있는 것과 동일한 배치 원칙

### 사전 작업: 카드 스프라이트를 `Resources/Sprite/Card/`로 이동 + Import 설정 수정

`SpriteCategory.Card` 값 이름과 실제 폴더명이 문자 그대로 일치해야 하는 기존 컨벤션에 따라(spritemanager 계획의 "구현 시 주의사항" 참고), 카드 이미지 10장(`1000.png`~`1009.png`)을 다음과 같이 옮긴다:

```
Assets/Resources/Cards/1000.png ~ 1009.png   →   Assets/Resources/Sprite/Card/1000.png ~ 1009.png
```

이동과 함께 각 파일의 Texture Import 설정에서 `Sprite Mode`를 `Multiple` → `Single`로 바꾼다(위 "배경" 절 참고). `att.png`/`def.png`/`M_bg.png`(카드 능력치 아이콘·배경 마스크로 추정, key 기반 조회 대상이 아님)는 `SpriteManager.GetCard`가 절대 요청하지 않는 이름이라 애초 계획은 별도 폴더에 남겨두는 것이었으나, 실제 작업 시점에는 이미 카드 10장과 한 폴더(`Assets/Resources/Cards/`)에 함께 있었으므로 굳이 다시 분리하지 않고 같은 폴더째로 `Sprite/Card/`에 이동했다 — `SpriteManager.GetCard`가 이 이름들을 조회하는 코드가 없는 한 그냥 같이 있어도 동작에 영향은 없다.

---

## 클래스 구조

```
SpriteManager                                     (Core/Sprites/, 기존 static 클래스에 추가)
├── SpriteCategory.Card                            ← 신규 enum 값 (= Resources/Sprite/Card 폴더명)
└── GetCard(string name) : Sprite                  ← 신규, Load(SpriteCategory.Card, name) 위임

Friend : MonoBehaviour                            (신규, InGame/)
├── Key : int { get; private set; }                ← 현재 표시 중인 카드 key
├── SetKey(int key)                                 ← 공개 진입점 유일. 이미지+att+hp 동시 갱신
├── _cardImage : Image                             ← [SerializeField]
├── _attText : TextMeshProUGUI                     ← [SerializeField]
└── _hpText : TextMeshProUGUI                      ← [SerializeField]

FriendEditor : UnityEditor.Editor                 (신규, Editor/InGame/, 에디터 전용)
├── [CustomEditor(typeof(Friend))]
├── _previewKey : int                              ← 인스펙터 입력용, Friend가 아니라 이 클래스에 보관
└── OnInspectorGUI() — key 입력 필드 + "Apply Key" 버튼 → target.SetKey(...) 호출
```

---

## 파일 구성

```
Assets/
├── Resources/
│   └── Sprite/
│       └── Card/                        ← 신규 폴더, Cards/에서 카드 10장 + att/def/M_bg 이동, 카드 10장은 Sprite Mode: Single로 재설정
└── Scripts/
    ├── Core/
    │   └── Sprites/
    │       └── SpriteManager.cs         ← 기존 파일 수정 (Card 카테고리 + GetCard 추가)
    ├── InGame/
    │   └── Friend.cs                    ← 신규, 이번 문서의 핵심 컴포넌트
    └── Editor/
        └── InGame/
            └── FriendEditor.cs          ← 신규, 인스펙터 key 입력 + 적용 버튼
```

`Friend`를 `Core/`가 아니라 `InGame/`에 두는 이유: `CardTable`/`SpriteManager`처럼 여러 씬·시스템이 공유하는 범용 프레임워크가 아니라, 인게임 화면에만 등장하는 구체적인 게임 오브젝트이기 때문이다 (`Login/`, `MainMenu/`가 각 씬 전용 스크립트를 그대로 그 씬 폴더에 두는 것과 동일한 원칙).

---

## 이번 범위에서 제외

- 생명력 변화(피격), 합체 조건(`CardCondition`/`CardTarget`) 판정 등 실제 게임플레이 로직 — 이번 컴포넌트는 표시(view)만 담당
- `UserData.Friends`(보유 카드 key 목록)를 기반으로 여러 `Friend` 오브젝트를 실제로 스폰/배치하는 로직 — 이 문서는 컴포넌트 하나의 설계만 다룸
- 오브젝트 풀링 — 지금은 몇 개가 동시에 필요한지, 얼마나 자주 생성/파괴되는지 알 수 없음(YAGNI)
- `att.png`/`def.png`/`M_bg.png` 등 고정 UI 장식 스프라이트의 `SpriteManager` 편입 — key로 조회할 대상이 아니라 프리팹에 고정 배치되는 값이므로 기존 방식(Inspector 직접 참조) 유지
- `Sprite` 조회 실패 시 기본(placeholder) 스프라이트로 대체하는 기능 — spritemanager 계획에서 이미 "필요해지면 호출부에서 추가"로 미룬 항목, 지금 요구사항에 없음

---

## 엣지 케이스

| 상황 | 처리 방식 |
|------|-----------|
| `CardTable.Instance`가 `null`(테이블 asset 부재) | `CardTable.Instance?.Get(key)`가 `null`이 되어 `SetKey`가 조기 반환 — `Friend`가 추가로 로그를 남기지 않음(`Instance` getter가 이미 `LogError` 남김), 크래시 없음 |
| 존재하지 않는 `key`로 `SetKey` 호출 | `CardTable.Get`이 `Debug.LogError` 후 `null` 반환 → `SetKey`가 조기 반환, 이미지/텍스트는 이전 상태 그대로 유지(변경되지 않음), 예외 없음 |
| `Resources/Sprite/Card/{key}.png`가 없음(오타·미준비) | `SpriteManager.GetCard`가 `Debug.LogWarning` 후 `null` 반환 → `_cardImage.sprite = null`, 이미지가 빈 상태로 보임(크래시 없음) |
| 같은 `Friend` 오브젝트에 `SetKey`를 여러 번 호출(재사용) | 상태 없이 매번 전체 값을 덮어쓰므로 몇 번을 호출해도 마지막 호출 값으로 일관되게 반영됨 |
| 카드 png를 옮겼는데 `Sprite Mode`를 `Single`로 바꾸지 않음 | `Resources.Load<Sprite>`의 메인 오브젝트가 여전히 `Texture2D`라 `null` 반환 — 파일은 있지만 항상 조회 실패로 귀결(스프라이트가 아예 없는 경우와 구분되지 않음, spritemanager 계획의 기존 컨벤션과 동일) |
| 인스펙터에서 "Apply Key" 버튼을 존재하지 않는 key로 클릭 | `SetKey` 내부의 기존 처리(`CardTable.Get` → `LogError` + 조기 반환)가 그대로 적용됨 — 에디터 전용 별도 예외 처리 없음 |
| "Apply Key"를 플레이 모드가 아닌 씬 편집 상태에서 클릭 | `Resources.Load` 기반 API만 쓰므로 정상 동작. `EditorUtility.SetDirty`로 씬을 저장(Ctrl+S)해야 변경이 유지됨 — 저장하지 않고 씬을 닫으면 되돌아감(일반 인스펙터 값 수정과 동일한 동작) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 이동 완료 후 `Friend.SetKey(1000)` 호출 | `_cardImage.sprite`에 `Resources/Sprite/Card/1000.png` 반영, `_attText.text == "2"`, `_hpText.text == "2"` |
| 2 | `Friend.SetKey(9999)`(존재하지 않는 key) 호출 | `Debug.LogError` 1회(`CardTable` 쪽), 이미지/텍스트 변경 없음, 예외 없음 |
| 3 | 같은 오브젝트에 `SetKey(1000)` 후 `SetKey(1001)` 연속 호출 | 최종적으로 1001의 이미지/att/hp만 반영(1000 값이 남지 않음) |
| 4 | `Resources/Sprite/Card/1005.png`를 임시로 이름 변경해 없앤 뒤 `SetKey(1005)` 호출 | `Debug.LogWarning` 1회(`SpriteManager` 쪽), `_attText`/`_hpText`는 정상 반영되고 이미지만 빈 상태 |

---

## 구현 시 주의사항

- **`SpriteManager.SpriteCategory.Card` 값 이름은 `Resources/Sprite/Card/` 폴더명과 대소문자까지 정확히 일치해야 한다** — 어긋나면 컴파일 에러 없이 항상 `null` + 경고만 반복된다(spritemanager 계획의 기존 함정과 동일).
- **카드 png 10장은 반드시 `Sprite Mode: Single`로 바꾼 뒤 이동한다** — `Multiple` 그대로면 `SpriteManager.GetCard`가 절대 값을 찾지 못한다. Unity 에디터에서 Import 설정을 바꾼 뒤 재임포트가 필요하므로, 이 작업은 에디터가 열려 있는 상태에서 진행한다.
- **`Friend`는 `CardTable`/`SpriteManager`의 `public` API만 호출한다** — `TryGet`, 인덱서 등 protected 내부를 우회하지 않는다.
- **`SetKey` 하나만 공개 진입점으로 유지** — 이미지/att/hp를 각각 별도 public 메서드로 쪼개지 않는다(위 "핵심 설계 결정" 참고). 향후 부분 갱신이 실제로 필요해지면 그때 확장한다.
- **`att.png`/`def.png`/`M_bg.png`는 이동하지 않는다** — `SpriteManager.GetCard`가 조회할 이름(카드 key)에 속하지 않으므로 기존 위치·참조 방식을 그대로 유지한다.
- **`FriendEditor`의 미리보기 입력값(`_previewKey`)은 `Friend`가 아니라 에디터 클래스에 둔다** — 런타임 컴포넌트에 "에디터 전용 상태"가 섞이지 않도록 분리 유지.

---

## 구현 후 체크리스트

- [x] `Assets/Resources/Sprite/Card/` 폴더 생성
- [x] 카드 png 10장(`1000.png`~`1009.png`) + `att.png`/`def.png`/`M_bg.png` `Assets/Resources/Cards/` → `Assets/Resources/Sprite/Card/` 이동
- [x] 이동한 카드 10장 전부 Texture Import 설정 `Sprite Mode: Multiple` → `Single`로 변경 (메타 파일 직접 수정 — 에디터가 다음 포커스/도메인 리로드 시 재임포트하는지 확인 필요)
- [x] `SpriteManager.cs`: `SpriteCategory.Card` 값 추가, `GetCard(string name)` 메서드 추가
- [x] `Friend.cs` 작성 (`Assets/Scripts/InGame/`): `Key` 프로퍼티, `SetKey(int key)`, `_cardImage`/`_attText`/`_hpText` 필드
- [x] `FriendEditor.cs` 작성 (`Assets/Scripts/Editor/InGame/`): key 입력 필드 + "Apply Key" 버튼으로 `SetKey` 즉시 호출
- [ ] 에디터에서 만들어 둔 친구 UI 오브젝트에 `Friend` 컴포넌트 부착, `_cardImage`/`_attText`/`_hpText` 인스펙터 연결
- [ ] 인스펙터 "Apply Key" 버튼으로 실제 카드 key 입력 → 미리보기 반영 확인
- [ ] 테스트 시나리오 4개 수동 검증 (특히 #1, #4: 실제 스프라이트 반영 여부 육안 확인)
- [ ] `Assets/Plans/05-prefab/plan-prefab.md`로 이 문서 저장 (프로젝트 관례)
