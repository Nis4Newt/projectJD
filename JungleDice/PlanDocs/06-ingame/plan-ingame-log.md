# InGame 로그 래핑(InGameLog) 구현 계획

> 상위 문서: [InGame 로직(Solo 모드) 구현 계획](plan-ingame.md) — 여러 하위 문서에서 `Debug.Log($"[InGame] ...")` 형태의 진행 로그가 반복 등장하며 파생된 공용 유틸리티 문서
> 의존 관계: `InGameSceneManager.cs`(호출부), `UnityEditor.EditorPrefs`(에디터 전용 토글 저장)
> 범위: `InGameSceneManager.cs` 내 `"[InGame]"` 태그가 붙은 턴/주사위/덱/승패 등 **게임 진행 로그**를 `InGameLog` 클래스로 래핑하고, 에디터에서 즉시 켜고 끌 수 있는 토글을 제공. `"[Cheat]"` 태그 로그(치트 에디터 피드백)와 `Table`/`EventBus`/`SettingsSystem` 등 시스템 레벨 "로직 로그"는 범위 밖.

---

## 배경 / 문제 인식

현재 `InGameSceneManager.cs`에는 턴 진행·주사위·덱 상태 같은 게임 진행 로그와, 시스템 예외 상황 로그가 모두 맨 `Debug.Log`/`Debug.LogWarning`으로 섞여 있다. 이 때문에:
- 콘솔에서 게임 진행 로그와 `[Table]`/`[EventBus]` 같은 시스템 로그가 뒤섞여 필터링이 번거롭다.
- 게임 진행 로그만 한 번에 끄고 싶어도(예: 다른 시스템 디버깅 중) 방법이 없다.
- 태그(`"[InGame]"`) 문자열이 13곳에 중복 하드코딩되어 있다.

## 설계 목표

- 게임 진행 로그 호출부를 `InGameLog.Log(...)` / `InGameLog.Warning(...)`로 통일하고 `"[InGame]"` 태그는 클래스가 자동으로 붙인다.
- 에디터에서 메뉴 클릭 한 번으로 즉시 on/off 가능(재컴파일 불필요), 상태는 에디터 재시작 후에도 유지.
- 플레이어 빌드(디바이스)에서는 호출 자체가 컴파일에서 제외되어 문자열 보간 비용조차 없다.
- 기존 `Debug.Log` 기반 시스템 로그(`[Table]`, `[EventBus]`, `[SettingsSystem]` 등)에는 영향을 주지 않는다.

---

## 핵심 설계 결정

### 1. 이중 게이트 — 컴파일 타임 스트립 + 런타임 토글

`[Conditional("UNITY_EDITOR")]`를 `Log`/`Warning` 메서드에 붙이면, `UNITY_EDITOR` 심볼이 없는 플레이어 빌드에서는 **호출부 자체가 컴파일에서 제거**된다(인자로 넘기는 문자열 보간까지 통째로 사라짐). 에디터 안에서는 별도의 `bool Enabled` 플래그로 즉시 on/off한다 — 두 게이트의 목적이 다르다: 하나는 "빌드에는 아예 안 들어가게", 하나는 "에디터에서 지금 당장 조용히 시키고 싶을 때".

```csharp
[System.Diagnostics.Conditional("UNITY_EDITOR")]
public static void Log(string message)
{
    if (!_enabled) return;
    Debug.Log($"[InGame] {message}");
}
```

### 2. 토글 상태는 `EditorPrefs`로 영속화

`SettingsSystem`처럼 게임 데이터를 저장하는 것이 아니라 "이 머신의 에디터에서 지금 로그를 보고 싶은가"를 저장하는 것이므로 `PlayerPrefs`/JSON이 아니라 `EditorPrefs`가 맞다. 키는 프로젝트 전역(머신 단위)에 저장되므로 다른 프로젝트와 충돌하지 않도록 `"JungleDice."` 접두사를 붙인다.

```csharp
private const string EditorPrefsKey = "JungleDice.InGameLog.Enabled";
```

### 3. 토글 UI는 체크마크가 있는 `MenuItem` 쌍

기존 `CheatEditorWindow`가 `"Tools/InGame/Cheat Editor"`로 노출되어 있는 것과 같은 위치(`Tools/InGame/...`)에 둔다. `MenuItem`을 두 개(실행용 + validate용)로 나누면 `Menu.SetChecked`로 현재 상태를 메뉴에 체크마크로 항상 동기화해서 보여줄 수 있다.

```csharp
[MenuItem("Tools/InGame/Toggle InGame Log")]
private static void Toggle() => InGameLog.Enabled = !InGameLog.Enabled;

[MenuItem("Tools/InGame/Toggle InGame Log", true)]
private static bool ToggleValidate()
{
    Menu.SetChecked("Tools/InGame/Toggle InGame Log", InGameLog.Enabled);
    return true;
}
```

### 4. 호출부 구분을 위해 `CallerMemberName`/`CallerLineNumber`로 출처를 자동 삽입

모든 로그가 `InGameLog.Log` 한 곳에서 `Debug.Log`를 호출하기 때문에, 콘솔에서 로그 항목을 더블클릭해도 항상 `InGameLog.cs`의 같은 줄로 이동해 "어느 호출부에서 찍힌 로그인지" 구분이 안 된다. 호출자의 메서드명·줄 번호를 컴파일 타임에 자동으로 채워주는 `[CallerMemberName]`/`[CallerLineNumber]`/`[CallerFilePath]`를 기본값 있는 선택적 매개변수로 추가해, 호출부 코드 변경 없이 로그 텍스트 자체에 출처를 남긴다.

```csharp
public static void Log(string message,
    [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
{
    if (!_enabled) return;
    Debug.Log($"[InGame] ({Path.GetFileNameWithoutExtension(file)}.{caller}:{line}) {message}");
}
```

`InGameLog.Log($"{_currentOwner} 턴 - 타겟 주사위: {targetRoll}")`처럼 호출부는 그대로 두면 되고, 실제 콘솔에는 `[InGame] (InGameSceneManager.EnterPhase:187) User 턴 - 타겟 주사위: 4`처럼 출력된다.

### 5. 메시지는 `Func<string>`로 지연 평가

`Log`/`Warning`이 `string`을 직접 받으면, 토글이 꺼져 있어도 호출부가 인자를 넘기기 위해 문자열 보간·조합을 항상 먼저 수행한다(예: 덱 전체를 나열하는 `string.Join`, 컴퓨터 턴마다 반복 호출되는 행동 로그). `Func<string> messageFactory`로 받아 `_enabled`가 `true`일 때만 호출하면, 토글 off 상태에서는 이 비용 자체가 발생하지 않는다.

```csharp
public static void Log(Func<string> messageFactory, ...)
{
    if (!_enabled) return;
    Debug.Log($"[InGame] (...) {messageFactory()}");
}
```

호출부는 `InGameLog.Log(() => $"{_currentOwner} 턴 - 타겟 주사위: {targetRoll}")`처럼 람다로 감싼다. `[Conditional("UNITY_EDITOR")]`가 이미 플레이어 빌드에서 호출 자체를 제거하므로, 이 지연 평가는 에디터 안에서 토글이 꺼져 있을 때의 비용만 줄이는 역할이다.

### 6. 런타임 클래스와 에디터 메뉴는 별도 파일/폴더로 분리

`InGameLog`는 `InGameSceneManager`(런타임 스크립트)가 직접 호출하므로 `Assets/Scripts/InGame/`에 있어야 하고, `UnityEditor` 네임스페이스 참조는 `#if UNITY_EDITOR`로 감싼 부분(정적 생성자, `Enabled` 프로퍼티)에만 국한한다. 메뉴(`MenuItem`)는 `UnityEditor.Editor` 어셈블리가 필요한 순수 에디터 코드이므로 `CheatEditorWindow.cs`와 동일하게 `Assets/Scripts/Editor/InGame/`에 별도 파일로 둔다.

---

## 클래스 구조

```
InGameLog (static class, JungleDice.InGame)
├── Enabled { get; set; }        ← #if UNITY_EDITOR 전용, EditorPrefs 연동
├── Log(Func<string> messageFactory)      ← [Conditional("UNITY_EDITOR")], _enabled일 때만 messageFactory() 호출(지연 평가) + "[InGame] " 접두 + 호출자 정보(파일.메서드:줄) 자동 삽입
└── Warning(Func<string> messageFactory)  ← 위와 동일

InGameLogMenu (static class, JungleDice.InGame.Editor)
├── Toggle()                     ← [MenuItem] 실행부
└── ToggleValidate()             ← [MenuItem(..., true)] 체크마크 동기화
```

---

## 파일 구성

```
Assets/
└── Scripts/
    ├── InGame/
    │   └── InGameLog.cs
    └── Editor/
        └── InGame/
            └── InGameLogMenu.cs
```

---

## 상세 구현 명세

### InGameLog.cs

```csharp
using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace JungleDice.InGame
{
    public static class InGameLog
    {
        private const string EditorPrefsKey = "JungleDice.InGameLog.Enabled";
        private static bool _enabled = true;

#if UNITY_EDITOR
        static InGameLog()
        {
            _enabled = UnityEditor.EditorPrefs.GetBool(EditorPrefsKey, true);
        }

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                UnityEditor.EditorPrefs.SetBool(EditorPrefsKey, value);
            }
        }
#endif

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Log(Func<string> messageFactory,
            [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            if (!_enabled) return;
            Debug.Log($"[InGame] ({Path.GetFileNameWithoutExtension(file)}.{caller}:{line}) {messageFactory()}");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Warning(Func<string> messageFactory,
            [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            if (!_enabled) return;
            Debug.LogWarning($"[InGame] ({Path.GetFileNameWithoutExtension(file)}.{caller}:{line}) {messageFactory()}");
        }
    }
}
```

### InGameSceneManager.cs 마이그레이션

`"[InGame]"` 태그가 붙은 13곳(턴/주사위/덱/승패/드로우 관련 `Debug.Log`·`Debug.LogWarning`)에서 태그 접두사를 제거하고, 메시지를 람다로 감싸 `InGameLog.Log`/`InGameLog.Warning` 호출로 교체한다.

```csharp
// Before
Debug.Log($"[InGame] {_currentOwner} 턴 - 타겟 주사위: {targetRoll}");

// After
InGameLog.Log(() => $"{_currentOwner} 턴 - 타겟 주사위: {targetRoll}");
```

`"[Cheat]"` 태그 3곳(614/621/634행)은 치트 에디터 조작 피드백으로 성격이 달라 이번 범위에서 제외한다(아래 "엣지 케이스" 및 "구현 후 체크리스트" 참고).

---

## Unity 씬/오브젝트 구성

해당 없음 — 씬에 배치되는 오브젝트가 없는 정적 유틸리티 클래스 + 에디터 메뉴.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `EditorPrefs`에 키가 아예 없음(최초 실행) | `GetBool(key, true)` 기본값 `true` → 기존과 동일하게 로그 출력 |
| 에디터에서 토글을 꺼둔 채로 에디터 재시작 | `EditorPrefs`가 값을 유지하므로 다음 실행에도 꺼진 상태 유지 |
| 플레이어(디바이스) 빌드 실행 | `UNITY_EDITOR` 심볼이 없어 `Log`/`Warning` 호출 자체가 컴파일에서 제거됨 — 콘솔에 `[InGame]` 로그가 원천적으로 없음 |
| `"[Cheat]"` 태그 로그(614/621/634행) | 이번 범위에서 제외, 기존 `Debug.LogWarning` 그대로 유지 |
| `Table`/`EventBus`/`SettingsSystem` 등 시스템 로그 | `InGameLog`와 무관, 기존 `Debug.Log*` 그대로 유지 — 토글 영향 없음 |
| 메뉴 체크마크와 실제 상태 불일치 | validate 함수가 메뉴가 열릴 때마다 `Menu.SetChecked`로 갱신하므로 항상 동기화됨 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `EditorPrefs` 키 없는 상태에서 Play 후 InGame 씬 진입 | 턴/주사위/덱 로그가 `[InGame]` 태그로 Console에 정상 출력 |
| 2 | `Tools/InGame/Toggle InGame Log` 클릭(끄기) 후 Play | InGame 진행 로그가 Console에 출력되지 않음, 메뉴 체크마크 해제 |
| 3 | 다시 클릭(켜기) 후 Play | 로그 재개, 메뉴 체크마크 표시 |
| 4 | 끈 상태에서 에디터 재시작 후 Play | 재시작 후에도 로그가 계속 출력되지 않음(`EditorPrefs` 영속) |
| 5 | 끈 상태에서도 `[Table]`/`[EventBus]` 등 다른 시스템 로그는 정상 출력되는지 확인 | InGame 외 로그는 토글과 무관하게 그대로 출력 |

---

## 구현 시 주의사항

- **`"[InGame] "` 접두사 중복 금지**: 마이그레이션 시 호출부 메시지 문자열에 이미 들어있던 `"[InGame] "`을 반드시 제거한다 — 안 지우면 `[InGame] [InGame] ...`로 중복 출력된다.
- **`[Conditional]`은 `void` 반환 메서드에만 적용 가능**: 값을 반환해야 하는 용도로는 쓸 수 없으므로 순수 로깅 전용으로만 사용한다.
- **`EditorPrefs` 키에 프로젝트 접두사 필수**: `EditorPrefs`는 프로젝트가 아니라 머신 단위로 저장되므로 `"JungleDice."` 접두사 없이 짧은 키를 쓰면 다른 프로젝트와 충돌할 수 있다.
- **`UnityEditor` 참조는 `#if UNITY_EDITOR`로만 감싼다**: `InGameLog.cs`는 런타임 스크립트가 호출하는 파일이므로, `EditorPrefs` 접근 코드가 가드 밖으로 나가면 플레이어 빌드가 컴파일 실패한다.
- **`Conditional`은 완전 한정 이름으로 쓴다**: `using System.Diagnostics;`와 `using UnityEngine;`을 한 파일에 같이 두면 `Debug`가 어느 네임스페이스인지 모호(ambiguous)해져 컴파일 에러가 난다. `using System.Diagnostics;`를 넣지 말고 `[System.Diagnostics.Conditional("UNITY_EDITOR")]`처럼 특성만 완전 한정해서, 나머지 코드베이스와 동일하게 `Debug.Log`를 맨 이름으로 쓴다.
- **호출부는 항상 람다로 감싼다**: `Log`/`Warning`이 `string`이 아니라 `Func<string>`을 받으므로 `InGameLog.Log("메시지")`처럼 문자열을 바로 넘기면 컴파일 에러다. `InGameLog.Log(() => $"메시지")` 형태를 지켜야 토글 off일 때 문자열 보간 비용을 실제로 건너뛴다.

---

## 구현 후 체크리스트

- [x] `InGameLog.cs` 작성 (`Assets/Scripts/InGame/`)
- [x] `InGameLogMenu.cs` 작성 (`Assets/Scripts/Editor/InGame/`)
- [x] `InGameSceneManager.cs`의 `"[InGame]"` 태그 `Debug.Log`/`Debug.LogWarning` 13곳 → `InGameLog.Log`/`InGameLog.Warning` 교체
- [ ] 테스트 시나리오 5개 검증
- [ ] (추후) `"[Cheat]"` 태그 로그도 포함할지 재검토 — 필요해지면 `InGameLog`에 태그 파라미터 추가 방향으로 확장
- [ ] (추후) Development Build에서도 로그를 남기고 싶어지면 `[Conditional("UNITY_EDITOR")]`를 별도 심볼(`DEVELOPMENT_BUILD` 등)로 확장 검토
