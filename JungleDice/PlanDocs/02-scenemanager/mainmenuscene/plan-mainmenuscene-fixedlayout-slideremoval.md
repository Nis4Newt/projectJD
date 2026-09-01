# 탭슬라이드 제거 및 모험 화면 고정화 구현 계획

> 상위 문서: [메인메뉴 고정형 레이아웃 전환 개요](./plan-mainmenuscene-fixedlayout.md) (하위 문서 #3, 마지막)
> 의존 관계: [친구탭 → FriendPanel 전환 계획](./plan-mainmenuscene-fixedlayout-friendpanel.md)(`FriendPanel` 타입), [StorePanel 신규 구현 계획](./plan-mainmenuscene-fixedlayout-storepanel.md)(`StorePanel` 타입) — **이 두 문서가 먼저 구현되어 있어야** 이 문서의 버튼 연결 코드가 컴파일된다
> 범위: `MainMenuTabSlideController`와 4탭 `ScrollRect`/`Content` 구조, "미정" 탭을 완전히 제거하고, 기존 "모험" 탭 콘텐츠(1인모드/정글탐험 버튼, 덱 미리보기)를 Canvas에 상시 고정 배치한다. `MainMenuSceneManager`에 "친구"/"상품" 오픈 버튼 2개와, 열려있는 패널을 전부 닫는 공용 닫기 버튼 1개를 연결한다. 옵션 버튼과 공용 닫기 버튼은 같은 자리에 겹쳐 배치돼 패널이 열려있는 동안만 서로 자리를 바꾼다.

---

## 배경 / 문제 인식

[friendpanel](./plan-mainmenuscene-fixedlayout-friendpanel.md)/[storepanel](./plan-mainmenuscene-fixedlayout-storepanel.md) 문서로 두 패널이 `UIPanel`로 전환됐으므로(둘 다 MainMenu 씬에 배치된 채, 자기 `Awake()`에서 `UIManager.Register(this)`로 캐시에 스스로 등록 — [popupstack 결정 11](../../01-core-systems/uimanager/plan-uimanager-popupstack.md)), 이제 씬에 남은 `TabSlide`(탭 4개 + `ScrollRect` 스냅 슬라이드)는 더 이상 쓸모가 없다 — 페이지가 하나(모험)만 남고 나머지는 팝업이 됐기 때문이다. 이 문서가 마지막으로 옛 구조를 걷어내고 새 진입점을 연결한다.

---

## 설계 목표

- 모험 탭 콘텐츠(Solo/Battle 버튼, 덱 미리보기 3장)가 씬 진입과 동시에 항상 보인다 — 탭 전환이나 스크롤 없이
- "친구"/"상품" 버튼이 각각 `FriendPanel`/`StorePanel`을 연다
- 공용 닫기 버튼 하나로 열려있는 패널을 전부 닫는다 — 패널마다 자기 닫기 버튼을 따로 두지 않는다([friendpanel](./plan-mainmenuscene-fixedlayout-friendpanel.md)/[storepanel](./plan-mainmenuscene-fixedlayout-storepanel.md) 문서가 이미 전제)
- 옵션 버튼과 공용 닫기 버튼은 화면상 같은 자리에 겹쳐있다 — 평소(패널이 하나도 안 열린 상태)엔 옵션 버튼만 보이고, 친구/상품 패널이 하나라도 열리면 옵션 버튼 대신 닫기 버튼이 보인다. 백버튼/ESC로 패널이 닫혀 스택이 비어도 자동으로 옵션 버튼으로 되돌아간다
- 백버튼(Android)/ESC(PC)는 공용 닫기 버튼과 무관하게 항상 최상단 패널 하나만 닫는다 — 기존 `UIManager.HandleBackButton()` 그대로 유지
- `MainMenuTabSlideController.cs`와 관련 씬 구조(Tabs, ScrollRect, Content, 미정 페이지)를 완전히 제거한다 — 죽은 코드로 남기지 않는다
- `MainMenuHudView`(상단 유저 정보)와 `OptionPanel` 연결(`MainMenuSceneManager`가 이미 하던 것)은 변경하지 않는다

---

## 핵심 설계 결정

### 1. `MainMenuTabSlideController.cs` 삭제, 씬에서 `TabSlide` 하위 구조 제거

탭 인덱스 동기화, 스냅 애니메이션, 플릭 판정 등 이 컴포넌트의 모든 책임이 더 이상 필요 없다 — 페이지가 하나뿐이면 "전환"이라는 개념 자체가 사라진다. 사용처가 없는 상태로 남기지 않고 파일째 삭제한다.

### 2. 기존 모험 탭 콘텐츠를 `Content`의 자식에서 Canvas 직속 고정 컨테이너로 이동

`Content`(구 `t (3)`)에 있던 Solo/Battle 버튼과 [모험탭 덱 미리보기](./plan-mainmenuscene-adventuredeck.md)가 정의한 `_deckCards` 참조 대상들을, `TabSlide`를 걷어낸 자리에 새 고정 컨테이너(`AdventureView`)로 옮긴다. `MainMenuSceneManager`가 참조하던 `_soloButton`/`_battleButton`/`_deckCards` 인스펙터 연결은 오브젝트 자체가 유지된 채 부모만 바뀌므로(같은 GameObject를 재부모화) 참조가 끊기지 않는다 — Unity에서 오브젝트 참조는 계층 이동과 무관하게 유지된다.

### 3. `MainMenuSceneManager`에 버튼 2개만 추가 — 새 컴포넌트를 만들지 않는다

[모험탭 덱 미리보기 문서의 결정 3](./plan-mainmenuscene-adventuredeck.md)과 같은 이유 — 버튼 클릭 → `UIManager.Show<T>(parent)` 한 줄짜리 배선을 위해 별도 클래스를 만들 이유가 없다. 이미 이 씬의 진입점 역할을 하는 `MainMenuSceneManager`에 합친다.

`FriendPanel`/`StorePanel`은 `Resources/UI/` 프리팹이 아니라 MainMenu 씬에 직접 배치돼있지만, [popupstack 결정 11](../../01-core-systems/uimanager/plan-uimanager-popupstack.md)에 따라 각자 자기 `Awake()`에서 `UIManager.Register(this)`로 스스로 캐시에 등록한다([friendpanel 결정 5](./plan-mainmenuscene-fixedlayout-friendpanel.md)/[storepanel 결정 1](./plan-mainmenuscene-fixedlayout-storepanel.md)) — 그래서 `MainMenuSceneManager`는 이 두 패널의 존재 자체를 몰라도 되고, 버튼 클릭 시 타입만으로 `Show<T>`를 호출하면 된다. `Show<T>`의 `parent` 인자는 `OptionPanel`이 쓰던 `_canvasTransform`을 그대로 넘기지만, 캐시 히트 경로에서는 실질적으로 무시된다(시그니처 통일 목적).

```csharp
[SerializeField] private Button _friendButton;
[SerializeField] private Button _storeButton;

// OnAwake() 안, 기존 버튼 연결 옆에 추가
_friendButton.onClick.AddListener(() => UIManager.Show<FriendPanel>(_canvasTransform));
_storeButton.onClick.AddListener(() => UIManager.Show<StorePanel>(_canvasTransform));
```

`_soloButton`/`_battleButton`처럼 중복 클릭 가드(`_hasRequestedPlay`)가 필요 없다 — 패널을 여러 번 열어도 [friendpanel 엣지 케이스](./plan-mainmenuscene-fixedlayout-friendpanel.md)에서 이미 다룬, 팝업 스택에 중복으로 쌓이는 정도이며 씬 전환처럼 되돌릴 수 없는 부작용이 없다.

### 4. 패널 공용 닫기 버튼 — `UIManager.HideAll()`로 스택 전체를 비운다

`FriendPanel`/`StorePanel` 둘 다 자기 닫기 버튼을 갖지 않기로 했으므로([friendpanel 결정 3](./plan-mainmenuscene-fixedlayout-friendpanel.md)), 모험 화면에 버튼 하나를 두고 [`UIManager.HideAll()`](../../01-core-systems/uimanager/plan-uimanager-popupstack.md)을 호출한다 — `HideTop()`(한 단계씩)이 아니라 스택에 뭐가 쌓여있든 전부 닫는 쪽을 쓴다. "닫기"는 사용자 입장에서 "모험 화면으로 돌아가기" 하나의 의미이지, 팝업을 몇 겹 열었는지 신경 쓸 이유가 없기 때문이다.

```csharp
[SerializeField] private Button _panelCloseButton;

// OnAwake() 안에 추가
_panelCloseButton.onClick.AddListener(UIManager.HideAll);
```

백버튼(Android)/ESC(PC)는 이 버튼과 별개로 기존 `UIManagerDriver.Update()`→`UIManager.HandleBackButton()`→`HideTop()`이 계속 처리한다 — 패널에서 닫기 버튼을 걷어내도 이 경로는 스택 기반이라 영향받지 않으므로 이 문서에서 추가로 손댈 코드가 없다.

### 5. 옵션 버튼 ↔ 공용 닫기 버튼 전환은 `UIManager`의 `PopupStackChanged` 이벤트로 처리

옵션 버튼과 공용 닫기 버튼이 같은 자리에 겹쳐 배치되므로, "패널이 열려있는가"에 따라 둘 중 하나만 `SetActive(true)`로 보여야 한다. 문제는 백버튼/ESC로 패널이 닫히는 경로(`UIManager.HandleBackButton()`→`HideTop()`)는 `MainMenuSceneManager`가 직접 호출하는 게 아니라서, 그 순간을 알 방법이 없다는 것이다 — 그래서 [popupstack 문서 결정 10](../../01-core-systems/uimanager/plan-uimanager-popupstack.md)이 추가한 `PopupStackChanged` 이벤트(`EventBus`)를 구독해 스택 상태가 바뀔 때마다 버튼을 갱신한다. 기존 `UserDataChanged` 구독과 동일한 `_subs`(`CompositeDisposable`)를 그대로 재사용한다.

```csharp
UpdatePanelToggleButtons(UIManager.HasOpenPanel); // 초기 상태 반영 — 이벤트는 "변경"에만 발행되므로 최초 1회는 직접 조회
_subs.Add(EventBus.Subscribe<PopupStackChanged>(e => UpdatePanelToggleButtons(e.HasOpenPanel)));

private void UpdatePanelToggleButtons(bool hasOpenPanel)
{
    _optionButton.gameObject.SetActive(!hasOpenPanel);
    _panelCloseButton.gameObject.SetActive(hasOpenPanel);
}
```

`_friendButton`/`_storeButton`을 눌러 `Show<T>()`가 스택에 패널을 쌓을 때도, `_panelCloseButton`을 눌러 `HideAll()`이 스택을 비울 때도 모두 같은 이벤트를 거치므로 버튼 전환 코드가 호출 경로별로 흩어지지 않는다.

### 6. "미정" 탭은 GameObject/에셋 전부 삭제

콘텐츠가 없던 자리표시자이므로 변환 대상이 아니라 삭제 대상이다 — 탭 아이콘 스프라이트까지 포함해 참조가 끊어진 에셋이 남지 않게 정리한다.

---

## 클래스 구조

```
MainMenuTabSlideController.cs                          ← 파일 삭제

MainMenuSceneManager (기존 파일 수정)
├── _friendButton : Button                              ← [SerializeField] 신규
├── _storeButton : Button                                ← [SerializeField] 신규
├── _panelCloseButton : Button                           ← [SerializeField] 신규, FriendPanel/StorePanel 공용, _optionButton과 같은 자리에 겹침
├── OnAwake()                                            ← 기존 버튼 연결 옆에 세 줄 추가 + PopupStackChanged 구독 + 초기 상태 반영
└── UpdatePanelToggleButtons(bool hasOpenPanel)          ← private 신규, _optionButton/_panelCloseButton SetActive 토글
```

`FriendPanel`/`StorePanel` 자체는 `MainMenuSceneManager`가 참조하지 않는다 — 각자 자기 `Awake()`에서 `UIManager.Register(this)`로 등록한다([friendpanel](./plan-mainmenuscene-fixedlayout-friendpanel.md)/[storepanel](./plan-mainmenuscene-fixedlayout-storepanel.md) 문서).

---

## 파일 구성

```
Assets/
└── Scripts/
    └── MainMenu/
        ├── MainMenuTabSlideController.cs   ← 삭제
        └── MainMenuSceneManager.cs         ← 기존 파일 수정
```

---

## 상세 구현 명세

### MainMenuSceneManager.cs (변경분만)

```csharp
[SerializeField] private Button _friendButton;
[SerializeField] private Button _storeButton;
[SerializeField] private Button _panelCloseButton;

protected override void OnAwake()
{
    _optionPanel = UIManager.Load<OptionPanel>(_canvasTransform, p => p.Configure(OptionPanelMode.MainMenu));

    _soloButton.onClick.AddListener(() => OnPlayButtonClicked(GameType.Solo));
    _battleButton.onClick.AddListener(() => OnPlayButtonClicked(GameType.Battle));
    _optionButton.onClick.AddListener(_optionPanel.Show);
    _friendButton.onClick.AddListener(() => UIManager.Show<FriendPanel>(_canvasTransform));
    _storeButton.onClick.AddListener(() => UIManager.Show<StorePanel>(_canvasTransform));
    _panelCloseButton.onClick.AddListener(UIManager.HideAll);

    UpdatePanelToggleButtons(UIManager.HasOpenPanel);
    _subs.Add(EventBus.Subscribe<PopupStackChanged>(e => UpdatePanelToggleButtons(e.HasOpenPanel)));

    RefreshDeckCards();
    _subs.Add(EventBus.Subscribe<UserDataChanged>(_ => RefreshDeckCards()));
}

// _optionButton과 _panelCloseButton은 같은 자리에 겹쳐있다 — 패널이 열려있는 동안만 옵션 대신 닫기 버튼을 보여준다.
private void UpdatePanelToggleButtons(bool hasOpenPanel)
{
    _optionButton.gameObject.SetActive(!hasOpenPanel);
    _panelCloseButton.gameObject.SetActive(hasOpenPanel);
}
```

---

## Unity 씬/오브젝트 구성

```
[작업 전]
Canvas
└── TabSlide
    ├── Tabs (탭 버튼 4 + 아이콘)
    └── ScrollView (MainMenuTabSlideController)
        └── Viewport/Content
            ├── t (?) 상품 (빈 자리표시자)              ← 삭제
            ├── t (2) 친구 (FriendTabController → FriendPanel로 컴포넌트만 교체, 씬 위치는 유지)
            ├── t (3) 모험 (Solo/Battle, DeckPreview)    ← AdventureView로 재부모화
            └── t (?) 미정 (빈 자리표시자)                ← 삭제

[작업 후]
Canvas
├── AdventureView (신규 컨테이너, 구 t (3) 내용물)
│   ├── Mode Solo / Mode Battle
│   ├── DeckPreview (Card_0~2)
│   ├── FriendButton   ← MainMenuSceneManager._friendButton
│   ├── StoreButton    ← MainMenuSceneManager._storeButton
│   └── OptionCloseSlot (같은 자리, 두 버튼 겹침)
│       ├── OptionButton      ← MainMenuSceneManager._optionButton (기본 활성)
│       └── PanelCloseButton  ← MainMenuSceneManager._panelCloseButton (패널이 열려있을 때만 활성)
├── FriendPanel (구 t (2) 자리, FriendPanel.cs — 자가 Register)
├── StorePanel (신규 배치, StorePanel.cs — 자가 Register)
└── MainMenuHudView (변경 없음)
```

`TabSlide` 오브젝트(및 그 하위 `Tabs`, `ScrollView`)는 전부 삭제한다. `MainMenuManagers`의 `MainMenuSceneManager`에는 `_friendButton`/`_storeButton`/`_panelCloseButton` 인스펙터 연결만 추가한다 — `FriendPanel`/`StorePanel`은 각자 알아서 등록하므로 연결 대상이 아니다. `OptionButton`/`PanelCloseButton`은 같은 위치에 겹쳐 배치하고, 인스펙터의 초기 활성 상태는 `OptionButton`만 켜둔다(코드가 `OnAwake()`에서 다시 한번 정확히 세팅하므로 씬 저장 시 실수로 둘 다 켜져 있어도 실행 중엔 바로잡힌다). `FriendPanel`/`StorePanel`은 `TabSlide` 밖(Canvas 직속 등)으로 옮겨도 무방하다 — 더 이상 탭 페이지가 아니므로.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `_deckCards`/`_soloButton`/`_battleButton`이 참조하던 GameObject를 재부모화 | Unity 오브젝트 참조는 계층 경로가 아니라 인스턴스 자체를 가리키므로, `Content`에서 `AdventureView`로 부모만 바꿔도 인스펙터 연결은 끊기지 않는다 — 단, 실수로 오브젝트를 삭제 후 재생성하면 새 인스턴스라 연결이 끊기므로 반드시 "이동"으로 처리 |
| `FriendPanel`/`StorePanel`이 아직 씬에 배치되지 않은 상태에서 이 문서를 먼저 진행 | 컴파일은 되지만(`Show<T>` 호출 자체는 타입만 있으면 됨) 버튼 클릭 시 캐시 미스로 `Resources.Load`가 실패해 런타임 예외 — [의존 관계]에 명시한 대로 반드시 앞의 두 문서(클래스 작성 + 씬 배치)를 먼저 끝낼 것 |
| `Resources.Load`가 삭제된 구 `FriendTabController` 타입 이름을 참조하는 곳이 남아있음 | 컴파일 에러로 즉시 드러남 — `FriendSlot` 외 다른 참조가 있는지 전역 검색으로 확인 필요 |
| 어떤 패널도 열려있지 않은 상태에서 `_panelCloseButton` 클릭 | `UIManager.HideAll()`이 빈 스택이면 루프를 돌지 않고 즉시 반환 — `PopupStackChanged`도 발행하지 않으므로 버튼 상태 변화 없음. 다만 이 상태에선 `_panelCloseButton` 자체가 비활성이라 클릭 이벤트가 애초에 발생하지 않음 |
| `FriendPanel`과 `StorePanel`이 동시에 열린 상태에서 `_panelCloseButton` 클릭 | `HideAll()`이 스택에 쌓인 두 패널을 모두 `Pop`+`Close()` 후 `PopupStackChanged(false)` 1회 발행 — 옵션 버튼으로 전환, 모험 화면으로 복귀 |
| 백버튼(Escape)으로 마지막 남은 패널을 닫음(스택이 1→0) | `HideTop()`이 `PopupStackChanged(false)` 발행 → `UpdatePanelToggleButtons`가 자동으로 옵션 버튼으로 되돌림 — `MainMenuSceneManager`가 백버튼 입력을 직접 알 필요 없음 |
| 백버튼(Escape)으로 두 패널 중 하나만 닫음(스택이 2→1) | `HideTop()`이 `PopupStackChanged(true)` 발행 → 여전히 닫기 버튼 상태 유지(옵션 버튼으로 바뀌지 않음) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | MainMenu 씬 진입 | 탭/스크롤 없이 모험 화면(Solo/Battle 버튼, 덱 미리보기 3장)이 즉시 보임, 옵션 버튼만 활성(닫기 버튼 비활성) |
| 2 | "친구" 버튼 클릭 | `FriendPanel`이 열림(모험 화면 위에 표시), 옵션 버튼이 비활성으로 바뀌고 닫기 버튼이 활성으로 바뀜 |
| 3 | "상품" 버튼 클릭 | `StorePanel`이 열림, 닫기 버튼 활성 상태 유지 |
| 4 | 씬 전체에서 `MainMenuTabSlideController`/`TabSlide`/"미정" 관련 GameObject 검색 | 아무 것도 남아있지 않음 |
| 5 | 친구탭에서 덱 교체 후 공용 닫기 버튼으로 `FriendPanel` 닫기 | 모험 화면의 덱 미리보기 3장이 즉시 갱신([adventuredeck 문서](./plan-mainmenuscene-adventuredeck.md) 동작 그대로 유지 확인), 옵션 버튼으로 복귀 |
| 6 | "친구" → "상품" 순으로 연달아 클릭(두 패널 동시에 열림) 후 공용 닫기 버튼 클릭 | 두 패널이 한 번에 모두 닫히고 모험 화면으로 복귀, 옵션 버튼으로 복귀 |
| 7 | `FriendPanel`이 열린 상태에서 백버튼(Escape) 입력 | 최상단 패널(`FriendPanel`)만 닫힘 — 스택이 비었으므로 옵션 버튼으로 자동 전환 |
| 8 | "친구" → "상품" 순으로 열어 스택 2개인 상태에서 백버튼(Escape) 1회 입력 | `StorePanel`만 닫히고 `FriendPanel`은 유지 — 스택이 아직 1개라 닫기 버튼 상태 유지(옵션 버튼으로 안 바뀜) |

---

## 구현 시 주의사항

- **`FriendPanel`/`StorePanel` 클래스와 씬 배치가 먼저 끝나 있어야 한다** — 이 문서는 반드시 [friendpanel](./plan-mainmenuscene-fixedlayout-friendpanel.md)/[storepanel](./plan-mainmenuscene-fixedlayout-storepanel.md) 완료 후 진행한다. 두 패널은 스스로 등록하므로 `MainMenuSceneManager`에 별도 필드를 추가하지 않는다.
- **재부모화는 "이동"으로, 삭제 후 재생성이 아니다** — `_deckCards`/`_soloButton`/`_battleButton` 인스펙터 연결이 깨지지 않도록 Hierarchy에서 드래그로 부모만 바꾼다.
- **`MainMenuTabSlideController.cs`를 삭제하기 전에 다른 스크립트가 참조하고 있지 않은지 확인** — 현재는 자기 자신 안에서만 닫힌 컴포넌트라 없을 것으로 예상되지만, 삭제 전 전역 검색으로 확인.
- **`CanvasScaler`/`Player Settings` 방향 고정 설정은 [tabslide 문서](./plan-mainmenuscene-tabslide.md)에서 이미 적용 완료 상태** — 이번 작업으로 되돌리지 않는다(고정형이라도 해상도 대응 자체는 계속 필요).
- **옵션 버튼 ↔ 닫기 버튼 전환은 반드시 `PopupStackChanged` 구독을 통해서만 한다** — `_friendButton`/`_storeButton`/`_panelCloseButton`의 `onClick` 안에서 직접 `SetActive`를 호출하지 않는다. 백버튼/ESC 경로까지 한 곳(`UpdatePanelToggleButtons`)에서 일관되게 처리하기 위함(위 "핵심 설계 결정 5" 참고).

---

## 구현 후 체크리스트

- [x] `MainMenuTabSlideController.cs` 삭제
- [x] `MainMenuSceneManager.cs`: `_friendButton`/`_storeButton`/`_panelCloseButton` 필드 + `OnAwake()` 버튼 연결 추가
- [x] `UIManager.cs`: `HasOpenPanel` 프로퍼티 + `Show`/`HideTop`/`HideAll`에서 `PopupStackChanged` 발행 ([popupstack 문서](../../01-core-systems/uimanager/plan-uimanager-popupstack.md) 결정 10)
- [x] `UIManager.cs`: `Register<T>` 추가 ([popupstack 문서](../../01-core-systems/uimanager/plan-uimanager-popupstack.md) 결정 11) — 호출은 이 문서가 아니라 `FriendPanel`/`StorePanel` 각자의 `Awake()`에서
- [x] `MainMenuSceneManager.cs`: `PopupStackChanged` 구독 + `UpdatePanelToggleButtons` 추가
- [ ] 씬: `TabSlide`(Tabs + ScrollView) 삭제, "미정" 탭 GameObject/에셋 삭제 (Unity 에디터 작업)
- [ ] 씬: 구 `t (3)`(모험) 콘텐츠를 `AdventureView`로 재부모화, `_friendButton`/`_storeButton`/`_panelCloseButton` 인스펙터 연결, 옵션/닫기 버튼을 같은 자리에 겹쳐 배치 (Unity 에디터 작업)
- [ ] 테스트 시나리오 8개 검증 — Unity 에디터 Play 모드 필요
- [ ] (추후) `plan-mainmenuscene-tabslide*.md` 3개 문서를 `deprecated/`로 이동
