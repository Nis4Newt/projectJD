# Login 씬 — [1] task 순차 실행 구현 계획

> 상위 문서: [Login 씬 구현 계획 — 개요](plan-loginscene.md) (1번 하위 문서)
> 의존 관계: 없음 (Phase 1 인프라 — SceneSingleton/EventBus/GameManager/SceneLoader만 전제). [plan-loginscene-taptocontinue.md](plan-loginscene-taptocontinue.md)(4번, 이미 구현됨)의 `LoginSceneManager.OnAwake()` 임시 코드를 이 문서에서 교체함
> 범위: `LoginTask` 목록 정의, 순차 실행, task 완료마다 `LoginProgressChanged` 발행. 전체 완료 후에는 로그만 남기고 종료 (Google 로그인 자동 시도는 3번 문서 몫)

---

## 배경

4번 문서가 먼저 구현되면서 `LoginSceneManager.OnAwake()`는 씬 진입 즉시 `GoogleLoginSucceeded`를 발행하는 임시 코드를 갖고 있다. 이 문서는 그 자리에 실제 task 시퀀스를 채워 넣는다 — 진입 즉시 발행하던 것을, "3개 task를 순차 실행 → 완료" 이후 발행하는 것으로 대체한다. `GoogleLoginSucceeded` 발행 자체는 3번(Google 로그인) 완료 전까지 임시로 남긴다.

---

## 설계 목표

- `LoginTask`: 이름 + 실행 델리게이트만 갖는 상태 없는 값 타입
- `LoginSceneManager`가 `LoginTask[]`를 코루틴으로 순차 실행
- task 완료마다 `LoginProgressChanged(Completed, Total, TaskName)` 발행 — 구독자(2번 `LoginProgressUI`)는 아직 없어도 무방
- 각 task 본문은 `WaitForSeconds` placeholder (`SaveSystem` 등 실제 연동은 범위 밖)
- 전체 완료 후에는 로그만 남기고, 기존 임시 `GoogleLoginSucceeded` 발행을 이 시점으로 이동 (3번이 실제 로그인 시도로 교체할 지점)

---

## 핵심 설계 결정

### `LoginTask`: 이름 + `Func<IEnumerator>`만 갖는 readonly struct

```csharp
using System;
using System.Collections;

namespace JungleDice.Login
{
    public readonly struct LoginTask
    {
        public readonly string Name;
        public readonly Func<IEnumerator> Run;

        public LoginTask(string name, Func<IEnumerator> run)
        {
            Name = name;
            Run = run;
        }
    }
}
```

클래스가 아닌 값 타입 — `LoginSceneManager`가 배열로 들고 순서대로 실행하는 용도 외에 자체 상태나 생명주기를 갖지 않는다.

### `LoginSceneManager`: task 배열 순차 실행 + 진행률 이벤트

```csharp
private static readonly LoginTask[] _tasks =
{
    new("설정 로드", () => PlaceholderTask(0.3f)),
    new("유저 데이터 로드", () => PlaceholderTask(0.5f)),
    new("서버 시간 동기화", () => PlaceholderTask(0.3f)),
};

protected override void OnAwake()
{
    _subs.Add(EventBus.Subscribe<AppFocusChanged>(OnAppFocusChanged));
    StartCoroutine(TaskSequenceRoutine());
}

private IEnumerator TaskSequenceRoutine()
{
    for (int i = 0; i < _tasks.Length; i++)
    {
        yield return StartCoroutine(_tasks[i].Run());
        EventBus.Publish(new LoginProgressChanged(i + 1, _tasks.Length, _tasks[i].Name));
    }

    Debug.Log("[LoginSceneManager] task 시퀀스 완료");

    // TODO(plan-loginscene-googleauth.md): 실제 Google 로그인 자동 시도로 교체
    EventBus.Publish(new GoogleLoginSucceeded());
}

private static IEnumerator PlaceholderTask(float duration)
{
    yield return new WaitForSeconds(duration);
}
```

- 기존 `OnAwake()`가 즉시 발행하던 `GoogleLoginSucceeded`를 시퀀스 완료 시점으로 옮긴 것 — 4번의 탭 유도 흐름은 그대로 재사용되고, 노출 시점만 task 소요 시간만큼 늦춰진다
- `LoginProgressChanged`는 구독자 유무와 무관하게 매 task마다 발행 (2번 `LoginProgressUI`가 나중에 구독)

### `GameEvents.cs` 추가

```csharp
public record LoginProgressChanged(int Completed, int Total, string TaskName);
```

---

## 파일 구성

```
Assets/Scripts/
├── Core/Event/GameEvents.cs   ← LoginProgressChanged 추가
└── Login/
    ├── LoginTask.cs           ← 신규
    └── LoginSceneManager.cs   ← 변경, OnAwake()가 TaskSequenceRoutine 실행
```

---

## 엣지 케이스

| 상황 | 처리 방식 |
|------|-----------|
| `_tasks`가 빈 배열 | `for` 루프를 돌지 않고 즉시 "task 시퀀스 완료" 로그 + `GoogleLoginSucceeded` 발행 |
| task 실행 중 씬 전환/파괴 | `StartCoroutine`으로 실행된 코루틴이므로 `LoginSceneManager` 파괴 시 Unity가 자동 중단 |
| `LoginProgressChanged` 구독자 없음 (2번 미구현) | `EventBus.Publish`는 구독자가 없어도 안전 (no-op) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | Login 씬 진입 | "설정 로드" → "유저 데이터 로드" → "서버 시간 동기화" 순서로 각각 지정된 시간만큼 대기 후 실행 |
| 2 | 각 task 완료 시점 | `LoginProgressChanged(Completed, 3, TaskName)` 발행 (콘솔 로그 등으로 확인) |
| 3 | 3개 task 모두 완료 | "task 시퀀스 완료" 로그 + `GoogleLoginSucceeded` 발행 → 기존 4번 `TapPanel` 정상 노출 |
| 4 | 전체 소요 시간 | 약 1.1초(0.3+0.5+0.3) 후 `TapPanel` 노출 (기존 "즉시 노출" 대비 지연 확인) |

---

## 구현 시 주의사항

- **`GoogleLoginSucceeded`의 최종 소관은 3번**: 지금은 task 시퀀스 완료 직후 즉시 발행하는 임시 코드. 3번(`plan-loginscene-googleauth.md`) 구현 시 이 발행을 실제 로그인 시도/결과로 교체한다.
- **task 본문은 계속 placeholder**: `SaveSystem` 연동 등 실제 로직은 이 문서 범위 밖 (`plan-loginscene.md` 제외 범위와 동일).

---

## 구현 후 체크리스트

- [x] `GameEvents.cs`에 `LoginProgressChanged` 추가
- [x] `LoginTask.cs` 작성 (`Assets/Scripts/Login/LoginTask.cs`)
- [x] `LoginSceneManager.OnAwake()`를 `TaskSequenceRoutine` 코루틴 실행으로 교체 (기존 즉시 `GoogleLoginSucceeded` 발행을 시퀀스 완료 후로 이동)
- [ ] 테스트 시나리오 4개 검증 (에디터/플레이 모드 확인 필요)
- [ ] `plan-loginscene.md`의 하위 문서 표에서 1번 상태 갱신
