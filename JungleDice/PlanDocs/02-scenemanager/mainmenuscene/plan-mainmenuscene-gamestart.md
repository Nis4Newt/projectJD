# 메인메뉴 게임 시작(모드 선택) 버튼 연결 계획

> 상위 문서: [씬별 매니저 구현 계획](../plan-scenemanager.md) (`MainMenuSceneManager.OnAwake()`의 "씬 진입 시 초기화 로직" 스켈레톤 자리에서, 실제 버튼 연결 요구가 파생)
> 관련 문서: [GameManager 구현 계획](../../01-core-systems/gamemanager/plan-gamemanager.md) (`ChangeState`/`GameStateChanged`, `MainMenu → InGame` 허용 전이를 그대로 재사용), [SceneLoader 구현 계획](../../01-core-systems/sceneloader/plan-sceneloader.md) (`GameState.InGame → "InGame"` 매핑이 이미 있어 신규 매핑 불필요), [EventBus 구현 계획](../../01-core-systems/eventbus/plan-eventbus.md) (구독/해제 패턴 재사용), [메인메뉴 유저 정보 HUD 연결 계획](plan-mainmenuscene-userdata-hud.md) (`UserManager`처럼 도메인 데이터를 별도 정적 클래스에 두는 선례)
> 의존 관계: `JungleDice.Core.GameManager`(간접 — `MainMenuPlayRequested` 이벤트 구독을 통해서만), `JungleDice.Core.Scene.SceneLoader`(간접 — 기존 `MainMenu ↔ InGame` 매핑 재사용, 수정 없음), `JungleDice.Core.Event.EventBus`/`GameEvents`/`CompositeDisposable`
> 범위: MainMenu 씬의 "1인모드"(`Mode Solo`)/"정글탐험"(`Mode Battle`) 두 버튼을 `MainMenuSceneManager`가 받아, 클릭한 버튼에 따라 게임 타입(`Solo`/`Battle`)을 기록하고 InGame 씬 전환을 요청하는 배선까지 다룬다. InGame 씬에서 게임 타입을 실제로 소비하는 로직(매칭, Friend 구성 등)과 버튼 비주얼/사운드 연출은 범위 밖.

---

## 배경

`MainMenuSceneManager`(`plan-scenemanager.md`)는 아직 빈 스켈레톤이고, MainMenu 씬(`MainMenu.unity`)에는 `t (3)` 탭 페이지 하위에 `Mode Solo`/`Mode Battle` 두 GameObject가 이미 배치돼 있다(각각 카드 형태 `Image` + 안내 텍스트 `Text (TMP)` 자식 — `Mode Battle`은 "정글\n탐험", `Mode Solo`는 동일 형태의 라벨). 다만 현재 두 GameObject 모두 `Button` 컴포넌트는 붙어있지 않고 `Image`만 있는 상태라, 실제 클릭 가능한 버튼으로 쓰려면 `Button` 컴포넌트 추가가 함께 필요하다.

프로젝트에는 아직 "게임 타입(1인/대전)"이라는 개념 자체가 없다 — `GameState`(`plan-gamemanager.md`)는 화면 단위 상태(`MainMenu`, `InGame` 등)만 다루고, 같은 `InGame` 상태 안에서 "혼자 하는지, 다른 유저와 겨루는지"를 구분하는 데이터는 없다. 이번 문서가 그 최초 지점이다.

---

## 설계 목표

- "1인모드" 클릭 → 게임 타입 `Solo` 기록 후 InGame 씬 전환
- "정글탐험" 클릭 → 게임 타입 `Battle` 기록 후 InGame 씬 전환
- `GameManager.ChangeState` 호출 권한은 계속 `GameManager` 한 곳에만 둔다 — `LogoSceneManager`/`LoginTapToContinueUI`가 세운 "씬 매니저는 전이를 이벤트로만 요청한다" 원칙을 그대로 따름
- 게임 타입 데이터는 `GameManager`에 얹지 않는다 — `GameManager`의 책임은 상태 전이로 한정
- 씬 전환이 비동기(`SceneLoader`)로 진행되는 동안 두 버튼을 번갈아 오탭해도 게임 타입이 조용히 바뀌지 않도록 한다

---

## 핵심 설계 결정

### 1. 게임 타입은 `GameManager`가 아니라 별도 정적 클래스 `GameSession`에 둔다

`GameManager`는 `GameState` 전이만 책임진다(`plan-gamemanager.md`). "1인/대전"은 상태 전이와 무관한 세션 데이터이므로 `GameManager`에 필드를 추가하면 책임이 섞인다. 대신 `UserManager`(`plan-userdata.md`)와 동일한 패턴 — MonoBehaviour가 아닌 순수 정적 클래스 — 로 `GameSession`을 새로 둔다.

```csharp
namespace JungleDice.Core
{
    public static class GameSession
    {
        public static GameType CurrentGameType { get; private set; }

        public static void SetGameType(GameType type)
        {
            CurrentGameType = type;
        }
    }
}
```

`plan-scenemanager.md`의 원칙("씬 매니저는 `GameManager`/`SceneLoader`를 직접 참조하지 않는다")은 어디까지나 상태 전이를 담당하는 두 전역 시스템에 한정된다. `UserManager`를 `MainMenuHudView`가 직접 읽는 것처럼, `GameSession`도 향후 `InGameSceneManager`가 `GameManager`를 거치지 않고 직접 읽을 수 있다 — C# 정적 필드는 씬 전환과 무관하게 앱 생명주기 동안 값을 유지하므로 `DontDestroyOnLoad` 오브젝트 없이도 MainMenu → InGame 씬 경계를 넘어 값이 살아남는다.

### 2. 씬 전환 요청은 파라미터 없는 이벤트 하나(`MainMenuPlayRequested`)로 통일

`LoginTapToContinueUI`가 `LoginSceneReady`를 발행하고 `GameManager`가 그걸 구독해 `ChangeState`를 대신 호출하는 패턴을 그대로 재사용한다. 게임 타입은 이벤트를 발행하기 **전에** 이미 `GameSession`에 기록해두므로, 이벤트 자체에 페이로드를 실을 필요가 없다 — `GameManager`가 `GameType`이라는 도메인 개념을 알 필요도 없어진다.

```csharp
// GameEvents.cs
public record MainMenuPlayRequested();

// GameManager.OnAwake()
EventBus.Subscribe<MainMenuPlayRequested>(_ => ChangeState(GameState.InGame));
```

`SceneLoader`의 `_stateSceneMap`은 이미 `GameState.InGame → "InGame"`을 갖고 있으므로 수정이 필요 없다.

### 3. 중복 클릭 가드: `LoginTapToContinueUI`의 `_hasTapped` 패턴 재사용

`SceneLoader.LoadScene`은 비동기라 InGame 씬 로드가 끝날 때까지 MainMenu 씬과 두 버튼이 계속 활성 상태로 남는다. 이 틈에 "1인모드"를 누른 직후 "정글탐험"을 오탭하면:

```
1인모드 클릭 → GameSession.SetGameType(Solo) → MainMenuPlayRequested 발행
             → GameManager.ChangeState(InGame) 성공 (CurrentState: MainMenu → InGame)
             → SceneLoader가 "InGame" 로드 시작 (아직 완료 전)
정글탐험 클릭(가드 없다면) → GameSession.SetGameType(Battle)  ← 조용히 덮어써짐
             → MainMenuPlayRequested 발행 → GameManager.ChangeState(InGame)
               (CurrentState가 이미 InGame이라 조기 반환, 씬 재로드는 없음)
```

결과적으로 InGame 씬은 이미 로드가 확정된 상태에서 `GameSession.CurrentGameType`만 `Battle`로 바뀌어버리는 불일치가 생긴다. `LoginTapToContinueUI`가 `_hasTapped`로 두 번째 탭을 막듯, `MainMenuSceneManager`도 최초 1회만 처리하고 두 버튼 모두 비활성화한다.

```csharp
private bool _hasRequestedPlay;

private void OnPlayButtonClicked(GameType type)
{
    if (_hasRequestedPlay) return;
    _hasRequestedPlay = true;

    _soloButton.interactable = false;
    _battleButton.interactable = false;

    GameSession.SetGameType(type);
    EventBus.Publish(new MainMenuPlayRequested());
}
```

가드는 버튼별이 아니라 `MainMenuSceneManager` 전체에 하나만 둔다 — 한쪽을 눌렀으면 다른 쪽 클릭도 함께 막아야 하기 때문이다.

---

## 클래스 구조

```
GameType : enum                                    (신규, Core/GameType.cs)
├── Solo
└── Battle

GameSession : static class                         (신규, Core/GameSession.cs)
├── CurrentGameType : GameType   ← get; private set;
└── SetGameType(GameType type)   ← public static

GameEvents (기존 파일 수정, Core/Event/)
└── MainMenuPlayRequested() : record   ← 신규, 파라미터 없음

GameManager (기존 파일 수정, Core/)
└── OnAwake()
    └── EventBus.Subscribe<MainMenuPlayRequested>(_ => ChangeState(GameState.InGame))   ← 신규 구독 1줄 추가

MainMenuSceneManager (기존 파일 수정, MainMenu/)
├── _soloButton       : Button  [SerializeField]   ← 신규
├── _battleButton     : Button  [SerializeField]   ← 신규
├── _hasRequestedPlay : bool                        ← 신규, 중복 클릭 가드
├── OnAwake()               ← 두 버튼에 onClick 리스너 등록
├── OnPlayButtonClicked(GameType type)  ← private, 신규
└── OnDestroy()             ← 기존 그대로 (_subs.Dispose())
```

---

## 파일 구성

```
Assets/Scripts/
├── Core/
│   ├── GameType.cs           ← 신규
│   ├── GameSession.cs        ← 신규
│   ├── GameManager.cs        ← 기존 파일 수정 (MainMenuPlayRequested 구독 추가)
│   └── Event/
│       └── GameEvents.cs     ← 기존 파일 수정 (MainMenuPlayRequested 추가)
└── MainMenu/
    └── MainMenuSceneManager.cs   ← 기존 파일 수정 (버튼 2개 연결)
```

---

## 상세 구현 명세

### GameType.cs

```csharp
namespace JungleDice.Core
{
    public enum GameType
    {
        Solo,
        Battle,
    }
}
```

### GameSession.cs

```csharp
namespace JungleDice.Core
{
    public static class GameSession
    {
        public static GameType CurrentGameType { get; private set; }

        public static void SetGameType(GameType type)
        {
            CurrentGameType = type;
        }
    }
}
```

### MainMenuSceneManager.cs

```csharp
using JungleDice.Core;
using JungleDice.Core.Event;
using UnityEngine.UI;

namespace JungleDice.MainMenu
{
    public class MainMenuSceneManager : SceneSingleton<MainMenuSceneManager>
    {
        [SerializeField] private Button _soloButton;
        [SerializeField] private Button _battleButton;

        private readonly CompositeDisposable _subs = new();
        private bool _hasRequestedPlay;

        protected override void OnAwake()
        {
            _soloButton.onClick.AddListener(() => OnPlayButtonClicked(GameType.Solo));
            _battleButton.onClick.AddListener(() => OnPlayButtonClicked(GameType.Battle));
        }

        private void OnPlayButtonClicked(GameType type)
        {
            if (_hasRequestedPlay) return;
            _hasRequestedPlay = true;

            _soloButton.interactable = false;
            _battleButton.interactable = false;

            GameSession.SetGameType(type);
            EventBus.Publish(new MainMenuPlayRequested());
        }

        protected override void OnDestroy()
        {
            _subs.Dispose();
            base.OnDestroy();
        }
    }
}
```

### GameEvents.cs 추가

```csharp
// MainMenu 씬 — 게임 시작 요청 (게임 타입은 발행 전 GameSession에 이미 기록됨)
public record MainMenuPlayRequested();
```

### GameManager.cs 변경

```csharp
protected override void OnAwake()
{
    EventBus.Subscribe<LogoSceneReady>(_ => ChangeState(GameState.Login));
    EventBus.Subscribe<LoginSceneReady>(_ => ChangeState(GameState.MainMenu));
    EventBus.Subscribe<MainMenuPlayRequested>(_ => ChangeState(GameState.InGame));   // 신규
    StartCoroutine(LogoSequence());
}
```

---

## Unity 씬/오브젝트 구성

```
[Scene: MainMenu]
└── Canvas
    └── ... ScrollRect Content (MainMenuTabSlideController._pages)
        └── t (3)                  (기존 탭 페이지)
            ├── Mode Solo           (기존 GameObject, Image + Text(TMP) 자식만 존재 — Button 컴포넌트 없음)
            └── Mode Battle         (기존 GameObject, Image + Text(TMP) 자식만 존재 — Button 컴포넌트 없음)

└── MainMenuManagers (기존 GameObject, plan-scenemanager.md)
    └── MainMenuSceneManager.cs
        ├── _soloButton   ← Mode Solo의 Button 컴포넌트 참조
        └── _battleButton ← Mode Battle의 Button 컴포넌트 참조
```

`Mode Solo`/`Mode Battle`은 현재 `Image`만 갖고 있어 클릭을 받을 수 없다. 실제 구현 시 두 GameObject에 `Button` 컴포넌트를 추가한 뒤(인스펙터의 `onClick`은 비워둔다 — `MainMenuTabSlideController`와 동일하게 코드에서 `AddListener`로 연결), `MainMenuManagers`의 `MainMenuSceneManager`에 두 `Button` 참조를 드래그해 연결한다.

---

## 이번 범위에서 제외

- **InGame 씬에서 `GameSession.CurrentGameType`을 실제로 소비하는 로직** — 매칭 로직, Friend 구성 등은 아직 InGame 쪽 설계가 없어 미구현. `InGameSceneManager` 관련 후속 문서에서 다룬다.
- **버튼 비주얼/사운드 연출**(눌림 효과, 클릭 사운드 등) — 디자인 미확정
- **씬 전환 중 로딩 인디케이터** — `plan-sceneloader.md`가 이미 범위 밖으로 명시한 `LoadingScreen` 미도입 상태를 그대로 유지. 지금은 버튼 비활성화만으로 중복 요청을 막는다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| "1인모드" 클릭 | `GameSession.CurrentGameType = Solo`, `MainMenuPlayRequested` 발행 → `GameManager.ChangeState(InGame)` → `SceneLoader`가 `"InGame"` 로드 |
| "정글탐험" 클릭 | 위와 동일, `GameType = Battle` |
| 첫 클릭 처리 후(씬 전환 완료 전) 같은 버튼 또는 다른 버튼 재클릭 | `_hasRequestedPlay` 가드로 무시 — 버튼도 이미 `interactable = false`라 클릭 자체가 발생하지 않음(가드는 이중 안전장치) |
| 가드를 우회해 `MainMenuPlayRequested`가 중복 발행되는 경우(이론상) | `GameManager.ChangeState`의 `CurrentState == next` 조기 반환으로 두 번째 `GameStateChanged`/씬 재로드는 발생하지 않음 |
| `Mode Solo`/`Mode Battle`에 `Button` 컴포넌트가 없거나 인스펙터 연결 누락 | `NullReferenceException` — `Friend.cs` 등과 동일하게 방어 코드 없이 인스펙터 연결을 전제 |
| MainMenu 재진입(예: `Pause → MainMenu`) | 씬이 다시 로드되며 `MainMenuSceneManager`도 새로 생성 → `_hasRequestedPlay`가 `false`로 초기화되어 다시 버튼 클릭 가능 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | MainMenu 진입 후 "1인모드" 클릭 | `GameSession.CurrentGameType == GameType.Solo`, `GameManager.CurrentState == InGame`, `SceneLoader`가 `"InGame"` 씬 로드 |
| 2 | MainMenu 진입 후 "정글탐험" 클릭 | `GameSession.CurrentGameType == GameType.Battle`, 나머지 결과는 시나리오 1과 동일 |
| 3 | "1인모드" 클릭 직후(씬 전환 완료 전) "정글탐험" 재클릭 | 두 번째 클릭 무시, `GameSession.CurrentGameType`은 `Solo`로 유지, `MainMenuPlayRequested`는 1회만 발행 |
| 4 | 같은 버튼("1인모드")을 빠르게 연타 | 첫 클릭 이후 모두 무시, `MainMenuPlayRequested` 1회만 발행 |
| 5 | 두 `Button` 참조가 인스펙터에 정상 연결된 상태로 Play | `OnAwake()` 실행 중 예외 없이 onClick 리스너 2개 등록 |

---

## 구현 시 주의사항

- **`ChangeState` 호출 권한은 여전히 `GameManager` 하나뿐**: `MainMenuSceneManager`가 `GameManager.Instance`를 직접 참조하지 않도록 주의 — `LogoSceneManager`/`LoginTapToContinueUI`와 동일하게 이벤트 발행까지만 담당.
- **`GameSession.SetGameType`은 반드시 `MainMenuPlayRequested` 발행 전에 호출**: 이벤트가 파라미터 없는 신호이므로, 순서가 바뀌면 `GameManager`가 갱신 전 게임 타입으로 전이를 진행해버린다.
- **`GameManager`에 `GameType` 필드를 추가하지 않는다**: 상태 전이만 책임지는 `GameManager`의 단일 책임을 유지 — `UserManager`와 동일하게 도메인 데이터는 별도 정적 클래스(`GameSession`)에 둔다.
- **`_hasRequestedPlay` 가드는 두 버튼 공용**: 버튼별로 따로 두면 한쪽을 누른 뒤에도 다른 쪽이 여전히 클릭 가능해 레이스가 재발한다.
- **`Mode Solo`/`Mode Battle`에 `Button` 컴포넌트 추가 필요**: 현재는 `Image`만 있어 클릭을 받지 못한다. 인스펙터의 `onClick` 이벤트는 비워두고 코드(`AddListener`)로만 연결한다 — `MainMenuTabSlideController`의 기존 관례.

---

## 구현 후 체크리스트

- [ ] `GameType.cs` 작성 (`Core/GameType.cs`, `Solo`/`Battle`)
- [ ] `GameSession.cs` 작성 (`Core/GameSession.cs`)
- [ ] `GameEvents.cs`에 `MainMenuPlayRequested` 추가
- [ ] `GameManager.OnAwake()`에 `EventBus.Subscribe<MainMenuPlayRequested>(_ => ChangeState(GameState.InGame))` 추가
- [ ] `MainMenuSceneManager.cs`: `_soloButton`/`_battleButton`/`_hasRequestedPlay` 필드와 `OnPlayButtonClicked` 추가
- [ ] `Mode Solo`/`Mode Battle` GameObject에 `Button` 컴포넌트 추가 (Unity 에디터 작업 필요)
- [ ] `MainMenuManagers`의 `MainMenuSceneManager`에 두 `Button` 인스펙터 연결 (Unity 에디터 작업 필요)
- [ ] 테스트 시나리오 5개 검증
- [ ] (추후) InGame 씬에서 `GameSession.CurrentGameType`을 소비하는 로직은 `InGameSceneManager` 관련 별도 계획 문서에서 다룬다
