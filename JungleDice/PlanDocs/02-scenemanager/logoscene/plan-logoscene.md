# Logo 씬(LogoSceneManager) 구현 계획

> 상위 문서: [씬별 매니저(SceneManager) 구현 계획](../plan-scenemanager.md) (예시 스켈레톤으로 도입된 `LogoSceneManager`를 구체화)
> Phase 1 후속 — GameManager/SceneLoader/EventBus/SceneSingleton 구현 이후 추가되는 인프라
> 의존 관계: EventBus, GameEvents(`LogoSceneReady`), SceneSingleton
> 범위: Logo 씬은 스플래시 이미지 노출 + 일정 시간 후 Login 씬 전환 기능만 가진다. 진행률 표시, 데이터 로드(SaveSystem 등) 작업 구조는 Login 씬에서 담당할 예정 — 이 문서 범위에서 제외

---

## 배경 / 문제 인식

`LogoSceneManager`는 `plan-scenemanager.md`에서 "예시 스켈레톤"으로 도입된 이후 `WaitForSeconds(_minSplashDuration)`만 대기하다 `LogoSceneReady`를 발행하는 상태로 남아 있었다. 실제 스플래시 이미지가 없어 빈 화면만 노출된다는 점만 비어 있다.

Logo 씬의 역할은 **스플래시 이미지(로고)를 화면에 띄우고, 정해진 최소 노출 시간이 지나면 Login 씬으로 전환하는 것**으로 한정한다. 진행률 표시(바/텍스트)와 데이터 로드(SaveSystem 등) 작업 구조는 Logo가 아니라 **Login 씬에서 담당한다** — 로그인 과정에서 계정/설정 데이터를 불러오는 것이 자연스러운 위치이기도 하고, 지금 Logo 단계에는 실제로 기다려야 할 로드 작업이 없기 때문이다. 진행률 UI와 데이터 로드 구조는 Login 씬 설계 문서에서 별도로 다룬다.

---

## 설계 목표

- 스플래시 이미지(로고)를 Logo 씬에 노출
- 최소 노출 시간(`_minSplashDuration`)이 지나면 `LogoSceneReady` 이벤트를 발행해 Login 전환을 트리거
- `GameManager`/`SceneLoader`를 직접 참조하지 않는 기존 원칙 유지 — 완료 신호는 `EventBus.Publish(new LogoSceneReady())` 하나뿐
- 코루틴 기반 유지 (UniTask 미도입, async/await 미사용)
- 이미지 에셋 없이도 동작하는 플레이스홀더 로고 (Sprite 미지정 흰색 사각형)
- 진행률 표시, 데이터 로드 작업 구조는 Logo에 만들지 않음 — Login 씬의 역할로 넘김

---

## 핵심 설계 결정

### `LogoSceneManager`는 시간 대기 후 이벤트 발행만 담당 — 기존 구조 그대로 유지

로직 자체는 기존 스켈레톤과 동일하다. 새로 필요한 것은 코드가 아니라 **씬에 로고 이미지를 배치하는 것**뿐이다.

```csharp
private IEnumerator LogoRoutine()
{
    // 스플래시/로고 연출, 버전 표시 등 Logo 씬 전용 연출
    yield return new WaitForSeconds(_minSplashDuration);

    // 준비 완료를 이벤트로만 알림 — GameManager를 직접 참조하지 않음
    EventBus.Publish(new LogoSceneReady());
}
```

- 진행률 이벤트, 진행률 UI 바인딩 스크립트는 Logo에 만들지 않는다 — 진행률/데이터 로드는 Login 씬 몫이고, Logo에는 표시할 진행 상태 자체가 없기 때문
- 로고 이미지는 `Image` 컴포넌트에 Source Image를 비워두면 흰색 사각형으로 렌더링됨 — 실제 아트 에셋 없이도 자리 확보 가능, 별도 스크립트 참조 없이 정적으로 배치

---

## 클래스 구조

```
LogoSceneManager : SceneSingleton<LogoSceneManager>   ← 변경 없음
└── LogoRoutine() : IEnumerator            ← 최소 노출 시간 대기 → LogoSceneReady 발행
```

새 클래스나 이벤트를 추가하지 않는다. 이번 작업은 씬 구성(로고 배치)만 다룬다.

---

## 파일 구성

```
Assets/
└── Scripts/
    └── Logo/
        └── LogoSceneManager.cs     ← 변경 없음 (기존 스켈레톤 그대로)
```

코드 변경 없음. `Assets/Scenes/Logo.unity`에 로고 표시용 오브젝트만 추가한다.

---

## Unity 씬/오브젝트 구성

```
[Scene: Logo]
├── GameManagers (GameObject, DontDestroyOnLoad — 기존, 변경 없음)
│   ├── GameManager.cs
│   └── SceneLoader.cs
├── LogoSceneManager (GameObject, 씬 로컬 — 기존, 변경 없음)
├── LogoCanvas (GameObject, 씬 로컬 — 신규)
│   ├── Canvas (Render Mode: Screen Space - Overlay)
│   ├── CanvasScaler (UI Scale Mode: Scale With Screen Size)
│   ├── GraphicRaycaster
│   └── Logo (Image, Source Image 없음 → 흰색, 화면 중앙)
├── EventSystem (GameObject, 신규 — Canvas 표준 동반 오브젝트)
├── Main Camera
└── Global Light 2D
```

`LogoCanvas`는 스크립트 부착 없이 순수 표시용 오브젝트다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `_minSplashDuration`이 0 이하로 설정됨 | `WaitForSeconds(0)`은 한 프레임만 대기 후 즉시 통과 — 별도 보정 불필요 |
| 코루틴 도중 씬 파괴 (Editor Stop 등) | Unity가 소유 MonoBehaviour의 모든 코루틴을 자동 정지 |
| `LogoSceneReady` 중복 발행 | 기존 `GameManager.ChangeState`의 `CurrentState == next` 가드로 안전 (`plan-scenemanager.md`와 동일 근거) |
| Login 씬이 Build Settings에 미등록 상태에서 전환 시도 | `SceneManager.LoadSceneAsync` 에러 로그, `SceneLoader.IsLoading`은 `finally`로 정상 복구 (`plan-sceneloader.md` 엣지 케이스와 동일) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | Logo 씬 실행 | 로고 이미지 즉시 표시 |
| 2 | `_minSplashDuration` 경과 | `LogoSceneReady` 발행 → `GameStateChanged(Logo, Login)` → `SceneLoadRequested("Login")` 순서로 발행 |
| 3 | Build Settings에 `Login.unity` 미등록 상태에서 실행 | 씬 전환 실패 로그, `SceneLoader.IsLoading`이 `false`로 정상 복구됨 (Logo 쪽 변경과 무관하게 기존 갭이 그대로 드러남을 확인) |
| 4 | `Login.unity` 등록 후 재실행 | `Logo → Login` 전환 성공, `LoginSceneManager.Instance` non-null, `LogoSceneManager`/`LogoCanvas`는 Single 모드 로드로 자동 파괴 |

---

## 구현 시 주의사항

- **Build Settings 선행 조건**: `ProjectSettings/EditorBuildSettings.asset`에 `Login.unity`가 아직 미등록 상태다(`plan-sceneloader.md`에 이미 남아있던 체크리스트 항목). 이 작업과 무관하게 반드시 먼저 등록해야 Logo→Login 전환이 실제로 동작한다. 가능하면 `MainMenu.unity`/`InGame.unity`도 함께 등록해 같은 문제를 나중에 재발견하지 않도록 한다.
- **진행률/데이터 로드는 Login 씬의 책임**: Logo 씬에는 관련 UI나 이벤트를 만들지 않는다. Login 씬 설계 시 별도 계획 문서로 다룬다.
- **`LogoSceneManager` 코드는 건드릴 필요 없음**: 이번 작업은 씬에 로고 오브젝트를 배치하는 것만으로 완결된다.

---

## 구현 후 체크리스트

- [ ] Logo 씬에 `LogoCanvas`(로고) + `EventSystem` 배치
- [ ] `Login.unity`(가능하면 `MainMenu`/`InGame`도) Build Settings 등록 — `plan-sceneloader.md`에서 이미 열려있던 항목 마감
- [ ] 테스트 시나리오 4개 검증
- [ ] (추후) Login 씬 설계 시 진행률 표시/데이터 로드 작업 구조를 별도 계획 문서로 작성
