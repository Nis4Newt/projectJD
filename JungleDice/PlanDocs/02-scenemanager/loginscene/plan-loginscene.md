# Login 씬(LoginSceneManager) 구현 계획 — 개요

> 상위 문서: [씬별 매니저(SceneManager) 구현 계획](../plan-scenemanager.md) (예시 스켈레톤으로 도입된 `LoginSceneManager`를 구체화), [Logo 씬 구현 계획](../logoscene/plan-logoscene.md) (이벤트 발행 후 GameManager가 전이를 수행하는 패턴을 그대로 재사용)   
> Phase 1 후속 — SceneSingleton/EventBus/GameManager/SceneLoader/LogoSceneManager 구현 이후 추가되는 인프라   
> 범위: Login 씬의 전체 흐름(task 진행률 → Google 로그인 → 탭 유도 → MainMenu 전이)을 4개의 작은 구현 문서로 나눠 순서대로 진행한다. 이 문서는 전체 그림과 순서만 안내하고, 실제 설계 결정·코드는 각 하위 문서에 있다.

---

## 배경

한 번에 전부 설계하기보다, 각 단계가 끝날 때마다 실제로 동작을 확인하고 커밋할 수 있도록 작은 단위로 나눈다. 각 하위 문서는 이전 문서가 끝난 상태를 전제로 하며, 그 문서만 읽고도 바로 구현할 수 있을 만큼 자기완결적으로 작성한다.

---

## 전체 흐름

```
Login 씬 진입
    │
    ├─ [1] task 순차 실행 (설정 로드 → 유저 데이터 로드 → 서버 시간 동기화)
    │       각 task 완료마다 진행률 이벤트 발행
    │
    ├─ [2] 진행률 UI가 이벤트를 구독해서 바/텍스트 갱신
    │
    ├─ [3] task 전부 끝나면 Google 로그인 자동 시도 (Mock)
    │
    └─ [4] 로그인 성공 → "탭하여 계속하기" 노출 → 탭 → MainMenu 전이 요청
```

---

## 하위 구현 문서

| # | 문서 | 다루는 것 | 의존 |
|---|------|-----------|------|
| 1 | [plan-loginscene-tasksequence.md](plan-loginscene-tasksequence.md) | `LoginTask` 목록 정의, 순차 실행, `LoginProgressChanged` 이벤트 발행. task 완료 후에는 로그만 남김 (Google 로그인 연결은 3번에서) | 없음 (Phase 1 인프라만) |
| 2 | [plan-loginscene-progressui.md](plan-loginscene-progressui.md) | `LoginProgressUI` — 진행률 이벤트를 Slider/TMP에 바인딩 | 1 |
| 3 | [plan-loginscene-googleauth.md](plan-loginscene-googleauth.md) | `IGoogleAuthProvider` / `MockGoogleAuthProvider`, task 완료 후 Google 로그인 자동 시도, 성공/실패 이벤트 | 1 |
| 4 | [plan-loginscene-taptocontinue.md](plan-loginscene-taptocontinue.md) | 로그인 성공 후 탭 유도 버튼, `LoginSceneReady` 발행, `GameManager`가 구독해 `MainMenu` 전이 | 3 |

`2`는 `1`이 끝난 뒤 아무 때나(순서 무관하게 `3`과 병행) 진행 가능 — UI는 이벤트만 구독하므로 로직 진행과 독립적이다. `4`는 `3`의 성공 이벤트가 있어야 하므로 반드시 마지막.

---

## 이번 범위에서 제외 (각 하위 문서에도 동일하게 명시)

- 실제 `SaveSystem` 연동 — task 내용은 계속 플레이스홀더(`WaitForSeconds`)로 남김
- 실제 Google Play Games/Firebase SDK 연동 — `MockGoogleAuthProvider`로 대체
- 로그인 실패 재시도/에러 팝업 — `UIManager` 도입 후 별도 문서
- 자동 로그인(캐시된 세션 재사용)
- `InputManager` 기반 탭 감지 — 지금은 UGUI `Button.OnClick`으로 처리

---

## 작업 순서 권장

```
1 → 3 → 4 순서로 구현 (핵심 흐름)
2는 1 이후 아무 때나 (UI는 이벤트 구독만 하므로 병행 가능)
각 문서 완료 시마다 테스트 시나리오 검증 후 커밋
```
