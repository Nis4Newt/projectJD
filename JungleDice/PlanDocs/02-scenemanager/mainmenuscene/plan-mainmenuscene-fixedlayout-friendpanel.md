# 친구탭 → FriendPanel 전환 구현 계획

> 상위 문서: [메인메뉴 고정형 레이아웃 전환 개요](./plan-mainmenuscene-fixedlayout.md) (하위 문서 #1)
> 관련 문서: [UIManager 팝업 스택 구현 계획](../../01-core-systems/uimanager/plan-uimanager-popupstack.md) (`UIPanel`/`UIManager.Show<T>`/`HideTop`/`HideAll`/`Register<T>`를 이 문서가 처음으로 실사용 — 씬에 이미 배치된 패널은 자기 `Awake()`에서 `Register(this)`로 캐시에 스스로 등록, 결정 11), [친구 카드 분리/선택 계획](./plan-mainmenuscene-friendselect.md), [친구탭 목록 숨김/슬롯 팝업 계획](./plan-mainmenuscene-friendlist-equipped.md) (전환 대상인 `FriendTabController`의 기존 동작 정의), [탭슬라이드 제거 및 모험 화면 고정화 계획](./plan-mainmenuscene-fixedlayout-slideremoval.md) (`MainMenuSceneManager`가 공용 닫기 버튼을 두는 문서 — 이 문서와 달리 `FriendPanel`을 인스펙터로 알 필요가 없음)
> 의존 관계: `JungleDice.Core.UI.UIPanel`/`UIManager`(`Show<T>`/`Register<T>`), 기존 `FriendTabController`의 모든 의존성(`UserManager`, `EventBus`, `FriendPickPopup`, `FriendSlot` 등) 그대로 유지
> 범위: `FriendTabController`를 `UIPanel`을 상속하는 `FriendPanel`로 이름을 바꾼다. **씬 밖으로 옮기지 않는다** — MainMenu 씬에 그대로 두고 `Awake()`에서 스스로 `UIManager.Register(this)`를 호출해 캐시에 등록한 뒤 `UIManager.Show<FriendPanel>(parent)`로 열리게 한다. 재오픈 시 상태 리셋까지 다룬다. 내부 로직(목록/슬롯/교체 흐름) 자체는 변경하지 않는다. **패널 자신의 닫기 버튼은 두지 않는다** — 닫기는 [slideremoval 문서](./plan-mainmenuscene-fixedlayout-slideremoval.md)가 `MainMenuSceneManager`에 두는 공용 닫기 버튼(`UIManager.HideAll()`)과 백버튼/ESC(`HandleBackButton()`→`HideTop()`)만으로 처리한다.

---

## 배경 / 문제 인식

`FriendTabController`(`FriendTabController.cs`)는 지금까지 `ScrollRect` 4페이지 중 하나로 존재해 "스크롤해서 보이는" 방식이었다 — `Awake()`가 씬 로드 시 1회만 실행되고 그 뒤로는 계속 활성 상태였다. 고정형 전환 후에는 "친구" 버튼을 눌러야 뜨고, 닫으면 사라지는 진짜 팝업이 돼야 한다. `UIPanel`(`Open`/`Close`)과 `UIManager.Show<T>`/`HideTop`(팝업 스택, 백버튼 자동 처리)이 이미 이 용도로 만들어져 있으므로 그대로 얹는다.

---

## 설계 목표

- "친구" 버튼 클릭 → `FriendPanel`이 열리고, `MainMenuSceneManager`의 공용 닫기 버튼 또는 백버튼/ESC로 닫힌다
- 내부 목록/슬롯/교체 로직은 한 줄도 바꾸지 않는다 — 진입 방식만 바뀐다
- 이전에 "교체 모드"(Replace) 상태로 닫혔더라도 다음에 다시 열면 항상 목록(List) 상태로 시작한다 — 상시 표시 페이지였을 때는 없던 문제로, 패널이 되면서 새로 생기는 엣지 케이스
- `UserDataChanged` 구독은 패널이 닫혀있는 동안에도 계속 유지한다(재구독 비용 없음) — `Load<T>` 캐시로 인스턴스가 최초 1회만 생성되므로 `Awake()`의 구독도 1회뿐

---

## 핵심 설계 결정

### 1. `FriendTabController` → `FriendPanel : UIPanel`, 파일/클래스명 변경

```csharp
public class FriendPanel : UIPanel
{
    // 기존 FriendTabController의 필드/Awake/PopulateList/RefreshSlots/RefreshListVisibility/
    // OnListItemClicked/EnterReplaceMode/ExitReplaceMode/RequestReplace/OnSlotClicked/
    // OnSlotDropped/OnPanelBackgroundClicked/OnDestroy 전부 그대로 이관
}
```

`FriendSlot._controller` 필드 타입도 `FriendTabController` → `FriendPanel`로 함께 바뀐다(같은 프리팹 안의 형제 컴포넌트 참조).

### 2. `Open()` 오버라이드 — 재오픈 시 항상 List 상태로 리셋

기존 `ExitReplaceMode()`가 이미 "List 상태로 되돌리기"를 정확히 수행하므로 새 메서드를 만들지 않고 그대로 재사용한다 — 닫힐 때 List 상태였어도 `ExitReplaceMode()`를 다시 호출하는 건 멱등이라 안전하다.

```csharp
public override void Open()
{
    base.Open(); // gameObject.SetActive(true)
    ExitReplaceMode(); // Replace 상태로 닫혔던 경우를 포함해 항상 List 상태로 시작
}
```

### 3. 패널 자신은 닫기 버튼을 갖지 않는다 — 닫기는 호출부(`MainMenuSceneManager`)와 백버튼/ESC의 책임

친구/상품 패널마다 같은 위치에 같은 모양의 닫기 버튼을 반복해서 두는 대신, `MainMenuSceneManager`(모험 화면, [slideremoval 문서](./plan-mainmenuscene-fixedlayout-slideremoval.md))에 공용 닫기 버튼 하나를 두고 `UIManager.HideAll()`을 호출한다 — 열려있는 패널이 몇 개든 한 번에 전부 닫는다. `FriendPanel`은 `Close()`/`HideTop()`/`HideAll()` 중 어느 것도 스스로 호출하지 않는다 — 닫힘은 항상 외부(공용 버튼 또는 `HandleBackButton()`)에서 트리거된다.

백버튼/ESC는 `UIManagerDriver.Update()`의 `UIManager.HandleBackButton()`→`HideTop()`이 이미 스택 기반으로 처리하므로 이 문서에서 추가로 배선할 것은 없다 — 패널에 닫기 버튼이 없어져도 백버튼 동작은 영향받지 않는다.

### 4. `Awake()` 맨 앞에 `gameObject.SetActive(false)` 추가 — `OptionPanel` 관례 재사용

씬에 활성 상태로 배치돼 있어도, "친구" 버튼 클릭 시점에야 `Show<T>`→`Open()`을 호출하므로 그 사이(씬 로드 직후)에 잠깐이라도 보이면 안 된다. `OptionPanel.Awake()`와 동일하게 방어적으로 넣어 "씬 로드 직후 기본 상태 = 닫힘"을 코드로 보장한다.

### 5. 씬에 그대로 둔다 — `FriendPanel`은 MainMenu 씬 전용이라 프리팹화할 이유가 없다

`FriendPanel`은 MainMenu 씬에서만 쓰이므로 `Resources/UI/`로 옮겨 여러 씬에서 재사용 가능하게 만들 이유가 없다. 씬에 둔 채로 [popupstack 문서 결정 11](../../01-core-systems/uimanager/plan-uimanager-popupstack.md)의 자가 `Register` 패턴을 그대로 따른다 — `Awake()`에서 스스로 `UIManager.Register(this)`를 호출해 등록하므로, `MainMenuSceneManager`는 `FriendPanel`의 존재 자체를 몰라도 된다(타입만 알면 `Show<FriendPanel>(parent)` 호출 가능).

```csharp
private void Awake()
{
    gameObject.SetActive(false);
    UIManager.Register(this);
    // ... 기존 초기화 ...
}
```

`Register`는 `SetParent`를 하지 않으므로 씬에 원래 있던 위치(이미 MainMenu 씬의 Canvas 밑)에 그대로 남는다 — 자기 `Canvas`를 따로 붙일 필요가 없다(popupstack 결정 6 "단," 참고). `Show<FriendPanel>(_canvasTransform)`의 `parent` 인자는 캐시 히트 경로에서는 사실상 쓰이지 않지만, `Show<T>` 시그니처를 다른 소비자와 통일하기 위해 그대로 넘긴다.

---

## 클래스 구조

```
FriendPanel : UIPanel                                  (FriendTabController.cs → 이름 변경, MainMenu/)
├── (기존 FriendTabController 필드/메서드 전부 유지)
├── Awake()                                              ← 맨 앞에 gameObject.SetActive(false) + UIManager.Register(this) 추가
└── Open()                                               ← override 신규, base.Open() + ExitReplaceMode()

FriendSlot (기존 파일 수정)
└── _controller : FriendPanel                            ← 타입만 FriendTabController → FriendPanel
```

---

## 파일 구성

```
Assets/
└── Scripts/
    └── MainMenu/
        ├── FriendTabController.cs → FriendPanel.cs   ← 파일명/클래스명 변경
        └── FriendSlot.cs                              ← 기존 파일 수정(_controller 타입만)
```

`Resources/UI/`에는 아무것도 추가하지 않는다 — `FriendPanel`은 MainMenu 씬에 그대로 남는다(핵심 설계 결정 5).

---

## Unity 씬/오브젝트 구성

```
MainMenu.unity
└── (구 t (2) 자리) FriendPanel (FriendPanel.cs 부착, 기존 t (2) 하위 구조 그대로 — 목록/그리드, 슬롯 3개, FriendPickPopup, ReplaceUI 등)
```

`FriendTabController`가 붙어있던 씬 GameObject는 그대로 두고 컴포넌트만 `FriendPanel`로 바뀐다(스크립트 재바인딩은 Unity가 같은 GUID로 자동 처리) — 별도 프리팹화나 인스펙터 연결 작업이 없다. `FriendPanel.Awake()`가 스스로 등록하므로 `MainMenuSceneManager` 쪽엔 아무것도 추가할 게 없다. 패널 자체 닫기 버튼은 두지 않으므로 그 UI도 추가하지 않는다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| Replace 상태(카드 교체 중)에서 공용 닫기 버튼으로 패널을 닫음 | 다음에 "친구" 버튼으로 다시 열 때 `Open()`이 `ExitReplaceMode()`를 호출해 항상 List 상태로 시작 — 이전 Replace 잔여 상태 없음 |
| 패널이 닫혀있는 동안 다른 경로(예: 서버 동기화)로 `UserDataChanged` 발행 | `Awake()`의 구독은 인스턴스 생존 기간(앱 종료까지) 유지되므로 닫혀있어도 `RefreshSlots`/`RefreshListVisibility`가 계속 반영됨 — 다음에 열었을 때 최신 상태 |
| "친구" 버튼 연타로 `UIManager.Show<FriendPanel>(parent)`이 중복 호출됨 | `Load<T>` 캐시로 같은 인스턴스가 재사용되지만 `_popupStack`에는 중복으로 쌓인다 — [popupstack 문서](../../01-core-systems/uimanager/plan-uimanager-popupstack.md)가 이미 알려진 제약으로 남겨둔 것과 동일, 이 문서에서 별도 가드 추가하지 않음 |
| `FriendPanel`이 붙은 GameObject가 씬에서 비활성 계층 밑에 있어 `Awake()`가 아예 실행되지 않음 | `Register(this)` 자체가 호출되지 않아 캐시가 비어있는 채로 남음 → "친구" 버튼 클릭 시 `Show<T>`가 캐시 미스로 `Resources.Load`를 시도하다 실패(프리팹이 없으므로) — Unity는 비활성 부모 밑의 오브젝트에 대해 `Awake()`를 호출하지 않으므로, `FriendPanel`을 비활성 컨테이너 밑에 두지 않도록 씬 배치 시 주의 |
| 백버튼과 공용 닫기 버튼을 거의 동시에 입력 | 백버튼은 `HandleBackButton()`→`HideTop()`(한 단계), 공용 버튼은 `HideAll()`(전부) — 두 경로 모두 스택이 이미 비어있으면 조기 반환하므로 예외 없음 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 모험 화면에서 "친구" 버튼 클릭 | `FriendPanel`이 열리고 List 상태(카드 목록 표시), 슬롯 3개가 `UserData.Friends` 기준으로 채워짐 |
| 2 | 목록에서 카드 클릭 → 픽업 팝업에서 선택 → 슬롯에 배치(Replace 상태 진입) 후 공용 닫기 버튼으로 패널 닫기 | 패널이 닫힘, 스택 0개 |
| 3 | 시나리오 2 직후 다시 "친구" 버튼 클릭 | List 상태로 열림(Replace UI 아님), 이전에 교체한 슬롯 값이 반영돼 있음 |
| 4 | `FriendPanel`이 열린 상태에서 백버튼(Escape) 입력 | 패널이 닫히고 모험 화면으로 복귀, 스택 0개 |
| 5 | `FriendPanel`이 닫힌 상태에서 다른 코드가 `UserManager.Current.SetFriends(...)` 호출 | 이후 패널을 열면 최신 덱으로 슬롯이 표시됨(닫혀있는 동안에도 구독이 갱신을 반영) |

---

## 구현 시 주의사항

- **`Open()`은 반드시 `base.Open()`을 먼저 호출한다** — `ExitReplaceMode()`가 `_friendCardList.SetActive(true)` 등 자식 GameObject를 건드리므로, 패널 자신이 아직 비활성 상태(`SetActive(false)`)인 동안 호출해도 동작은 하지만 순서를 `UIPanel.Open()`의 기본 계약(먼저 자신을 활성화)과 일치시켜 혼동을 없앤다.
- **패널 자신은 닫기를 트리거하지 않는다** — `gameObject.SetActive(false)`/`Close()`/`HideTop()`/`HideAll()`을 패널 내부에서 호출하는 코드를 추가하지 않는다. 닫기는 오직 `MainMenuSceneManager`의 공용 버튼(`HideAll()`)과 백버튼/ESC(`HandleBackButton()`)에서만 일어난다.
- **`FriendSlot._controller` 타입 변경을 빠뜨리지 않는다** — 컴파일 에러로 바로 드러나므로 놓치기는 어렵지만, 인스펙터 연결(씬 오브젝트 참조)은 컴포넌트 타입이 바뀌어도 유지되는지 확인 필요.
- **`FriendPanel` GameObject를 비활성 부모 밑에 두지 않는다** — `Awake()`가 안 불리면 `Register(this)`도 안 불려서 캐시가 비게 된다(위 엣지 케이스 참고). 자기 자신은 `Awake()` 안에서 `SetActive(false)`로 숨기는 것과, 부모가 처음부터 비활성인 것은 다르다.
- **`Resources/UI/`에 `FriendPanel.prefab`을 만들지 않는다** — 씬에 그대로 두고 스스로 `Register`하는 게 최종 설계다(핵심 설계 결정 5).

---

## 구현 후 체크리스트

- [x] `FriendTabController.cs` → `FriendPanel.cs` 이름 변경, `UIPanel` 상속으로 변경
- [x] `Awake()`에 `SetActive(false)` 추가
- [x] `Open()` 오버라이드 추가
- [x] `FriendSlot._controller` 타입을 `FriendPanel`로 변경
- [x] 패널 자체 닫기 버튼 제거 (공용 닫기 버튼으로 대체, [slideremoval 문서](./plan-mainmenuscene-fixedlayout-slideremoval.md) 참고)
- [x] `Awake()`에 `UIManager.Register(this)` 추가(자가 등록)
- [ ] 씬에서 `FriendPanel` GameObject가 비활성 부모 밑에 있지 않은지 확인 (Unity 에디터 작업)
- [ ] 테스트 시나리오 5개 검증 — Unity 에디터 Play 모드 필요
