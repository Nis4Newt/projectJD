# Login 씬 — [4] 탭하여 계속하기 → MainMenu 전이 구현 계획

> 상위 문서: [Login 씬 구현 계획 — 개요](plan-loginscene.md) (4번 하위 문서), [씬별 매니저(SceneManager) 구현 계획](../plan-scenemanager.md) (`LogoSceneReady` 패턴을 그대로 재사용)   
> 의존 관계: EventBus, GameEvents, GameManager(기존 `LogoSceneReady` 구독 확장), [plan-loginscene-googleauth.md](plan-loginscene-googleauth.md)(3번 문서 — 아직 미작성, 로그인 성공 이벤트 계약만 이 문서에서 선고정)   
> 범위: Google 로그인 성공 후 "탭하여 계속하기" UI 노출 → 탭 → `LoginSceneReady` 발행 → `GameManager`가 구독해 `MainMenu`로 전이. task 진행률 UI(2번), Google 로그인 로직 자체(3번)는 제외   

---

## 배경 / 문제 인식

[개요 문서](plan-loginscene.md)의 작업 순서는 `1 → 3 → 4`이지만, 이 문서는 그보다 먼저 작성한다 — 4번이 다루는 "탭 → 전이" 흐름은 `LogoSceneManager`가 이미 검증한 `{Scene}SceneReady` 패턴을 그대로 재사용하는 것이라 3번(Google 로그인 자동 시도)의 세부 구현과 독립적으로 설계를 고정할 수 있기 때문이다.

다만 4번은 3번이 발행할 로그인 성공 이벤트를 구독해야 시작된다. 3번 문서가 아직 없으므로, 이 문서에서 **최소한의 이벤트 계약**만 먼저 못 박는다:

> 로그인 성공 시 `GoogleLoginSucceeded` 이벤트가 정확히 1회 발행된다 (필드 없는 마커 레코드, `LogoSceneReady`와 동일 형태).

3번 문서 작성 시 이름이나 필드가 달라지면 이 문서와 `GameEvents.cs`를 함께 수정한다.

**3번 완료 전 임시 처리**: 실제 Google 로그인 없이도 4번 흐름을 바로 테스트/사용할 수 있도록, `LoginSceneManager.OnAwake()`가 **Login 씬 진입 시 로그인에 이미 성공했다고 가정하고 `GoogleLoginSucceeded`를 즉시 발행**한다. 3번 문서가 실제 Google 로그인 자동 시도 로직을 구현하면 이 임시 발행을 걷어내고 대체한다.

---

## 설계 목표

- 로그인 성공 시 "탭하여 계속하기" UI를 노출하고, 주목을 끌기 위해 깜빡임 처리
- 탭하면 `LoginSceneReady` 이벤트를 발행 (`LogoSceneReady`와 동일 패턴 — 완료 신호는 이벤트 하나뿐)
- `GameManager`가 `LoginSceneReady`를 구독해 `ChangeState(GameState.MainMenu)` 수행 — `ChangeState` 호출 권한은 계속 `GameManager` 한 곳
- 탭 유도 UI는 `LoginSceneManager`에 로직을 몰아넣지 않고 별도의 얇은 컴포넌트로 분리 (2번 문서의 `LoginProgressUI`와 동일하게 "이벤트 구독 → UI 갱신"만 담당)
- 중복 탭으로 `LoginSceneReady`가 두 번 발행되지 않도록 방지
- `InputManager` 없이 UGUI `Button.OnClick`으로 처리 (개요 문서의 제외 범위와 동일)

---

## 핵심 설계 결정

### `LoginTapToContinueUI`: 로그인 성공 이벤트 구독 → 버튼 노출(깜빡임) → 탭 시 `LoginSceneReady` 발행만

`LoginSceneManager`(`SceneSingleton`)에 직접 UI 참조를 넣지 않는다. `LoginProgressUI`와 대칭되는 평범한 `MonoBehaviour`로 분리해 씬 오브젝트에 부착한다. 깜빡임은 `CanvasGroup.alpha`를 코루틴으로 토글하는 방식으로 처리한다 — `Update()`/트윈 라이브러리 없이 기존 `WaitForSeconds` 코루틴 관례를 그대로 재사용.

`CanvasGroup` 하나로 "표시/숨김"과 "깜빡임"을 모두 처리한다 — 별도 `GameObject`(`_tapPanel`) 참조와 `SetActive` 토글은 두지 않는다. `alpha`(밝기) + `interactable`/`blocksRaycasts`(클릭 가능 여부) 세 값으로 숨김 상태(`alpha=0`, 비활성)까지 표현할 수 있어, 같은 오브젝트를 가리키는 참조 두 개를 따로 관리할 이유가 없다.

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using JungleDice.Core.Event;

namespace JungleDice.Login
{
    public class LoginTapToContinueUI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _tapCanvasGroup; // 표시/숨김 + 깜빡임 겸용
        [SerializeField] private Button _tapButton;
        [SerializeField] private float _blinkInterval = 0.5f;
        [SerializeField] private float _dimAlpha = 0.3f;

        private readonly CompositeDisposable _subs = new();
        private bool _hasTapped;
        private Coroutine _blinkRoutine;

        private void Awake()
        {
            SetVisible(false);
            _tapButton.onClick.AddListener(OnTapButtonClicked);
            _subs.Add(EventBus.Subscribe<GoogleLoginSucceeded>(OnGoogleLoginSucceeded));
        }

        private void OnGoogleLoginSucceeded(GoogleLoginSucceeded e)
        {
            SetVisible(true);
            _blinkRoutine = StartCoroutine(BlinkRoutine());
        }

        private void SetVisible(bool visible)
        {
            _tapCanvasGroup.alpha = visible ? 1f : 0f;
            _tapCanvasGroup.interactable = visible;
            _tapCanvasGroup.blocksRaycasts = visible;
        }

        private IEnumerator BlinkRoutine()
        {
            while (true)
            {
                _tapCanvasGroup.alpha = _dimAlpha;
                yield return new WaitForSeconds(_blinkInterval);
                _tapCanvasGroup.alpha = 1f;
                yield return new WaitForSeconds(_blinkInterval);
            }
        }

        private void OnTapButtonClicked()
        {
            if (_hasTapped) return; // 중복 탭 방지
            _hasTapped = true;

            if (_blinkRoutine != null)
            {
                StopCoroutine(_blinkRoutine);
                _blinkRoutine = null;
            }
            _tapCanvasGroup.alpha = 1f;

            _tapButton.interactable = false;
            EventBus.Publish(new LoginSceneReady());
        }

        private void OnDestroy()
        {
            _subs.Dispose();
        }
    }
}
```

- `Awake`/`OnDestroy`를 그대로 사용(다른 `MonoBehaviour`와 동일한 생명주기 관례) — `SceneSingleton`이 아니므로 `OnAwake` 오버라이드 대상이 아님
- `_hasTapped` 가드로 빠른 연속 클릭에도 `LoginSceneReady`는 정확히 1회만 발행
- 숨김 상태에서는 `blocksRaycasts = false`로 둬서 `TapButton`이 화면 뒤에서 다른 UI의 클릭을 가로채지 않게 함; 표시 상태에서는 `blocksRaycasts = true`이지만 `alpha`만 깜빡이므로 어두워진 프레임에도 `TapButton`은 항상 클릭 가능
- 탭 시 `StopCoroutine`으로 깜빡임을 즉시 멈추고 알파를 1로 고정 — 버튼이 비활성화된 뒤에도 계속 깜빡이는 것을 방지
- `GameManager.Instance`를 참조하지 않음 — `plan-scenemanager.md`의 "씬 매니저/UI는 전역 시스템을 직접 참조하지 않는다" 원칙을 그대로 따름

### `GameManager` 확장: `LoginSceneReady` 구독 → `ChangeState(MainMenu)`

`LogoSceneReady` 처리와 완전히 동일한 형태로 한 줄만 추가한다.

```csharp
protected override void OnAwake()
{
    EventBus.Subscribe<GameStateChanged>(gsc => Debug.LogError($"GameStateChanged: {gsc.Previous} → {gsc.Next}"));
    EventBus.Subscribe<LogoSceneReady>(_ => ChangeState(GameState.Login));
    EventBus.Subscribe<LoginSceneReady>(_ => ChangeState(GameState.MainMenu));   // 신규
    StartCoroutine(LogoSequence());
}
```

- `GameState.Login → MainMenu`는 이미 `_validTransitions`에 등록돼 있고, `SceneLoader._stateSceneMap`에도 `MainMenu`가 이미 매핑돼 있음 — `GameManager.cs`/`SceneLoader.cs` 쪽은 구독 한 줄 추가 외에 변경 불필요
- `LoginTapToContinueUI`는 `EventBus.Publish(new LoginSceneReady())`까지만 담당하고, 실제 `ChangeState` 호출은 `GameManager`가 수행 — `LogoSceneManager`/`GameManager` 관계와 동일한 경계

### `GameEvents.cs` 추가

```csharp
// 씬 매니저
public record LogoSceneReady();
public record LoginSceneReady();   // 신규

// Login 씬 — Google 로그인 (plan-loginscene-googleauth.md에서 확정 예정, 시그니처만 선반영)
public record GoogleLoginSucceeded();
```

`GoogleLoginSucceeded`의 최종 소관은 3번 문서(`plan-loginscene-googleauth.md`)이지만, 지금은 4번을 독립적으로 구현/테스트하기 위해 시그니처만 먼저 반영한다.

### `LoginSceneManager` 임시 처리: 씬 진입 시 로그인 성공을 가정하고 즉시 발행

3번(Google 로그인 자동 시도)이 아직 없으므로, `LoginSceneManager.OnAwake()`에서 `GoogleLoginSucceeded`를 바로 발행해 4번 흐름을 막힘 없이 검증한다.

```csharp
protected override void OnAwake()
{
    _subs.Add(EventBus.Subscribe<AppFocusChanged>(OnAppFocusChanged));

    // TODO(plan-loginscene-googleauth.md): 실제 Google 로그인 자동 시도로 교체
    // 지금은 Login 씬 진입 시 로그인에 성공했다고 가정하고 즉시 발행
    EventBus.Publish(new GoogleLoginSucceeded());
}
```

- 3번 문서 구현 시 이 두 줄(TODO 주석 + `Publish`)을 실제 `IGoogleAuthProvider` 호출 결과로 교체한다 — task 진행률(1번) 완료 후 로그인 시도, 실패 처리 등은 3번의 책임
- 그 전까지는 `LoginTapToContinueUI`가 씬 진입 직후(사실상 한 프레임 뒤) 바로 탭 패널을 노출하게 되어, 3번 없이도 탭 → `LoginSceneReady` → `MainMenu` 전이 전체 경로를 즉시 확인 가능

---

## 클래스 구조

```
LoginTapToContinueUI : MonoBehaviour        ← 신규, SceneSingleton 아님 (LoginProgressUI와 동일한 지위)
├── Awake()                 ← SetVisible(false), 버튼 리스너 등록, GoogleLoginSucceeded 구독
├── OnGoogleLoginSucceeded() → SetVisible(true) + BlinkRoutine 시작
├── SetVisible(bool)        → CanvasGroup의 alpha/interactable/blocksRaycasts 일괄 설정 (표시/숨김 겸용)
├── BlinkRoutine()          → CanvasGroup.alpha를 dimAlpha ↔ 1 토글 (코루틴)
├── OnTapButtonClicked()    → 깜빡임 정지, LoginSceneReady 발행 (1회 한정)
└── OnDestroy()             → 구독 해제

GameManager : Singleton<GameManager>         ← 기존, LoginSceneReady 구독 한 줄만 추가
```

새 씬 매니저나 싱글턴을 추가하지 않는다. `LoginSceneManager` 자체는 변경 없음.

---

## 파일 구성

```
Assets/
└── Scripts/
    ├── Core/
    │   ├── Event/
    │   │   └── GameEvents.cs              ← LoginSceneReady 추가 (+ GoogleLoginSucceeded 임시 반영 여부는 3번과 협의)
    │   └── GameManager.cs                 ← LoginSceneReady 구독 한 줄 추가
    └── Login/
        ├── LoginSceneManager.cs           ← 변경, OnAwake()에서 GoogleLoginSucceeded 임시 발행 (3번 대체 전까지)
        └── LoginTapToContinueUI.cs        ← 신규
```

---

## Unity 씬/오브젝트 구성

```
[Scene: Login]
├── GameManagers (기존, 영속, 변경 없음)
├── LoginManagers (GameObject, 씬 로컬 — 기존)
│   └── LoginSceneManager.cs
├── LoginCanvas (GameObject, 씬 로컬 — 2번 문서에서 이미 만들어졌다면 재사용, 없으면 이 문서에서 신규 생성)
│   ├── Canvas (Render Mode: Screen Space - Overlay)
│   ├── CanvasScaler / GraphicRaycaster
│   ├── ProgressGroup (2번 문서 담당 — 이 문서에서 다루지 않음)
│   └── TapToContinueGroup (신규)
│       ├── TapPanel (CanvasGroup 부착 — 표시/숨김 + 깜빡임을 이 컴포넌트 하나로 처리)
│       │   └── TapButton (Button + TMP_Text "탭하여 계속하기")
│       └── LoginTapToContinueUI.cs (TapToContinueGroup 또는 LoginCanvas에 부착, TapPanel의 CanvasGroup/TapButton 참조 연결)
├── EventSystem (Canvas 표준 동반 오브젝트 — 이미 있으면 재사용)
├── Main Camera
└── Global Light 2D
```

`LoginCanvas`가 2번 문서보다 먼저 만들어지는 경우, `ProgressGroup`은 빈 자리로 남겨두고 2번 작업 시 채운다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `GoogleLoginSucceeded`가 여러 번 발행됨 (3번의 재시도 로직 등) | `_tapPanel.SetActive(true)`는 멱등이지만, `BlinkRoutine`은 매번 새 코루틴이 시작됨 — 기존 `_blinkRoutine` 참조를 덮어써 중복 실행 가능. 발행이 1회임을 보장하는 건 3번의 책임 (`LoginSceneManager`의 임시 발행은 씬당 1회이므로 지금은 문제 없음) |
| 탭 버튼을 빠르게 두 번 이상 클릭 | `_hasTapped` 가드로 두 번째 클릭부터 무시, `LoginSceneReady`는 정확히 1회만 발행 |
| 깜빡이는 도중(알파가 `_dimAlpha`인 프레임) 탭 | `CanvasGroup.alpha`는 `blocksRaycasts`와 무관 — 어두운 프레임에도 정상적으로 클릭 인식 |
| `LoginSceneReady`가 중복 발행됨 | `GameManager.ChangeState`의 `CurrentState == next` 가드로 안전 (`plan-scenemanager.md`와 동일 근거) |
| 3번(Google 로그인) 미구현 상태에서 4번을 먼저 검증해야 함 | `LoginSceneManager.OnAwake()`가 씬 진입 시 로그인 성공을 가정하고 `GoogleLoginSucceeded`를 즉시 발행 — 3번 구현 시 이 임시 발행을 실제 로그인 결과로 교체 |
| `MainMenu.unity`가 Build Settings에 미등록 상태에서 전이 시도 | `SceneManager.LoadSceneAsync` 에러 로그, `SceneLoader.IsLoading`은 `finally`로 정상 복구 (`plan-sceneloader.md`/`plan-logoscene.md`와 동일 근거) |
| `LoginTapToContinueUI`가 씬 전환 도중 파괴됨 | `OnDestroy`에서 `_subs.Dispose()` 호출 → 구독 정리, 이후 콜백 호출 없음 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | Login 씬 진입 직후 (한 프레임 이전) | `TapPanel` 비활성 상태 (화면에 보이지 않음) |
| 2 | `LoginSceneManager.OnAwake()`가 `GoogleLoginSucceeded` 즉시 발행 (3번 대체 전 임시 동작) | `TapPanel` 활성화, `_blinkInterval` 주기로 알파가 `_dimAlpha` ↔ 1 사이를 반복 (깜빡임), `TapButton` 항상 클릭 가능 |
| 3 | `TapButton` 클릭 | 깜빡임 즉시 정지(알파 1 고정), `LoginSceneReady` 발행 → `GameManager.ChangeState(MainMenu)` 자동 호출 → `SceneLoader`가 `MainMenu` 씬 로드 |
| 4 | `TapButton`을 연속으로 빠르게 두 번 클릭 | `LoginSceneReady`는 1회만 발행됨 (`_hasTapped` 가드 확인) |
| 5 | 알파가 `_dimAlpha`로 어두워진 프레임에 `TapButton` 클릭 | 정상적으로 클릭 인식 (`blocksRaycasts` 영향 없음) |
| 6 | `MainMenu.unity`가 Build Settings에 미등록 상태에서 실행 | 씬 전환 실패 로그, `SceneLoader.IsLoading`이 `false`로 정상 복구 |
| 7 | `MainMenu.unity` 등록 후 재실행, 탭까지 완료 | `Login → MainMenu` 전이 성공, `LoginSceneManager`/`LoginTapToContinueUI`는 자동 파괴(코루틴도 함께 정리됨), `MainMenuSceneManager.Instance` non-null |

---

## 구현 시 주의사항

- **`GoogleLoginSucceeded`의 최종 소관은 3번 문서**: 지금은 `LoginSceneManager.OnAwake()`가 즉시 발행하는 임시 구현이다. 3번(`plan-loginscene-googleauth.md`) 작성 시 이름/필드가 바뀌거나 실제 로그인 결과로 교체되면 이 문서와 `LoginSceneManager`/`LoginTapToContinueUI`를 함께 갱신해야 한다.
- **탭 로직은 `LoginSceneManager`에 넣지 않는다**: 씬 진입 로직(자동 로그인 시도 트리거 등)과 UI 반응(탭 유도)을 분리해, 2번 문서의 `LoginProgressUI`와 동일하게 "이벤트만 구독하는 얇은 UI 컴포넌트" 원칙을 유지한다.
- **`ChangeState` 호출 권한은 `GameManager`만 보유**: `LoginTapToContinueUI`가 `GameManager.Instance.ChangeState(...)`를 직접 호출하지 않도록 주의 — `EventBus.Publish(new LoginSceneReady())`까지만.
- **`MainMenu.unity` Build Settings 등록 여부 재확인**: `plan-logoscene.md` 체크리스트에서 이미 처리했을 가능성이 높지만, 실제 전이 테스트 전에 반드시 확인한다.

---

## 구현 후 체크리스트

- [x] `GameEvents.cs`에 `LoginSceneReady`, `GoogleLoginSucceeded`(임시 시그니처) 추가
- [x] `LoginTapToContinueUI.cs` 작성 (`Assets/Scripts/Login/LoginTapToContinueUI.cs`)
- [x] `GameManager.OnAwake()`에 `EventBus.Subscribe<LoginSceneReady>(_ => ChangeState(GameState.MainMenu))` 추가
- [x] `LoginSceneManager.OnAwake()`에서 `GoogleLoginSucceeded` 임시 즉시 발행 (3번 대체 전까지)
- [ ] Login 씬에 `TapToContinueGroup`(패널/버튼) 배치, `TapPanel`에 `CanvasGroup` 추가, `LoginTapToContinueUI`에 `CanvasGroup`/`Button` 참조 연결 (에디터 작업)
- [ ] `MainMenu.unity` Build Settings 등록 여부 확인
- [ ] 테스트 시나리오 7개 검증
- [ ] 3번 문서(`plan-loginscene-googleauth.md`) 작성 후 `LoginSceneManager`의 임시 발행을 실제 로그인 결과로 교체
- [ ] `plan-loginscene.md`의 하위 문서 표에서 4번 상태 갱신
