# PlayFromFirstScene 구현 계획

> 상위 문서: 없음 (독립 에디터 편의 기능 — 특정 상위 로드맵에서 파생되지 않음)
> 의존 관계: 없음 (런타임 시스템을 참조하지 않는 순수 Editor 전용 스크립트)
> 범위: "어느 씬에서 작업 중이든 Build Settings의 첫 번째 씬부터 Play를 시작"하는 에디터 확장(메뉴/단축키/메인 툴바 버튼)만 다룸. 런타임 코드나 씬 자체 변경은 포함하지 않음.

---

## 배경 / 문제 인식

Login, MainMenu, InGame 등 다른 씬에서 작업하다가 Play를 누르면 그 씬부터 바로 시작되어, 게임 흐름 전체를 확인하려면 매번 첫 씬(Logo)으로 직접 전환한 뒤 Play를 눌러야 하는 번거로움이 있다. [SceneLoader](../01-core-systems/sceneloader/plan-sceneloader.md)는 런타임 상태-씬 매핑만 다루고 "에디터에서 임의 씬 작업 중 첫 씬부터 재생"은 다루지 않으므로, 에디터 레벨에서 별도로 해결한다.

---

## 설계 목표

- 현재 에디터에 어떤 씬이 열려 있든 관계없이 Build Settings의 첫 번째 씬으로 Play 시작
- 작업 중인 씬(현재 열려 있는 씬)은 그대로 유지 — 실제로 씬을 로드/전환하지 않음
- 메인 상단 툴바(Play 버튼 옆)에 클릭 가능한 버튼 제공
- 에디터 모드에서 단축키(Ctrl+L)로 즉시 실행
- 기본 Play 버튼/단축키(Ctrl+P) 동작에는 영향을 주지 않는 일회성 오버라이드로 동작

---

## 핵심 설계 결정

### 1. "첫 번째 씬"은 실제 로드 없이 `playModeStartScene`으로 지정

`EditorSceneManager.OpenScene`으로 씬을 직접 로드하면 작업 중인 씬이 바뀌어 버린다. 대신 `EditorSceneManager.playModeStartScene`에 SceneAsset을 지정하면, Play 진입 시 Unity가 내부적으로만 해당 씬으로 시작하고 에디터 창에는 원래 열려 있던 씬이 그대로 남는다.

```csharp
var entry = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);
var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.path);
EditorSceneManager.playModeStartScene = sceneAsset;
EditorApplication.EnterPlaymode();
```

씬 이름을 `"Logo"`처럼 하드코딩하지 않고 `EditorBuildSettings.scenes`의 0번째 **활성(enabled)** 항목을 사용한다 — Build Settings 순서만 맞으면 항상 올바르게 동작하며, [SceneLoader](../01-core-systems/sceneloader/plan-sceneloader.md)의 `_stateSceneMap`과는 독립적으로 유지된다.

### 2. `playModeStartScene`은 이번 실행에만 적용되는 일회성 오버라이드

값을 계속 남겨두면 이후 기본 Play 버튼(Ctrl+P)까지 항상 첫 씬으로 강제되어 버린다. Play 종료(EnteredEditMode) 시점에 반드시 `null`로 되돌려 기본 Play 동작을 원상 복구한다.

```csharp
EditorApplication.playModeStateChanged += state =>
{
    if (state == PlayModeStateChange.EnteredEditMode)
        EditorSceneManager.playModeStartScene = null;
};
```

### 3. 이미 Play 중이면 무시

[SceneLoader의 `IsLoading` 중복 방지 패턴](../01-core-systems/sceneloader/plan-sceneloader.md)과 동일하게, 이미 실행 중인 상태에서 재실행하면 경고 로그만 남기고 무시한다.

```csharp
if (EditorApplication.isPlaying)
{
    Debug.LogWarning("[PlayFromFirstScene] 이미 Play 중입니다.");
    return;
}
```

### 4. 메뉴 항목 + 단축키를 `MenuItem` 하나로 통합 제공

```csharp
[MenuItem("Tools/Play From First Scene %l")]
public static void Execute() { ... }
```

`%l` = Ctrl+L(Windows) / Cmd+L(Mac) — 기본 Play 단축키(Ctrl+P)와 충돌하지 않는 조합으로 확정. `Tools` 메뉴에 노출됨과 동시에 에디터 전역 단축키로 등록되어, 어떤 씬/창에 포커스가 있어도 Ctrl+L로 즉시 실행된다.

### 5. 메인 툴바 버튼: 비공식 UIElements 리플렉션 삽입

Unity는 메인 상단 툴바(Play/Pause/Step이 있는 그 줄)를 확장하는 공식 API를 제공하지 않는다 (공식 확장 지점은 Scene 뷰 Overlay뿐). 커뮤니티에서 널리 쓰이는 방식(`marijnz/unity-toolbar-extender` 계열)을 따라, `UnityEditor.Toolbar`의 private `m_Root`(VisualElement)를 리플렉션으로 얻어 `"ToolbarZoneRightAlign"` 존에 버튼을 삽입한다.

```csharp
[InitializeOnLoad]
static class PlayFromFirstSceneToolbar
{
    static PlayFromFirstSceneToolbar() => EditorApplication.delayCall += TryInject;

    static void TryInject()
    {
        var toolbarType = typeof(Editor).Assembly.GetType("UnityEditor.Toolbar");
        var currentToolbar = Resources.FindObjectsOfTypeAll(toolbarType).FirstOrDefault();
        if (currentToolbar == null)
        {
            EditorApplication.delayCall += TryInject; // 툴바 초기화 전 - 다음 프레임에 재시도
            return;
        }

        var root = toolbarType.GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance)
                              ?.GetValue(currentToolbar) as VisualElement;
        var zone = root?.Q("ToolbarZoneRightAlign");
        if (zone == null)
        {
            Debug.LogWarning("[PlayFromFirstScene] 메인 툴바 삽입 실패 - Unity 버전 호환성 확인 필요. 메뉴/단축키는 정상 동작합니다.");
            return;
        }

        zone.Add(new EditorToolbarButton("▶1", PlayFromFirstScene.Execute) { tooltip = "첫 씬부터 재생 (Ctrl+L)" });
    }
}
```

- `m_Root`, `"ToolbarZoneRightAlign"`은 Unity 버전 업그레이드 시 이름/구조가 바뀔 수 있는 비공식 내부 구현이다. 삽입 실패 시 예외를 던지지 않고 경고 로그만 남기고 조용히 스킵해, 메뉴/단축키 경로는 항상 정상 동작하도록 보장한다.
- 정확한 필드명/존 이름은 프로젝트의 실제 Unity 버전(Unity 6 LTS)에서 구현 시점에 직접 확인이 필요하다 — 위 코드는 알려진 패턴을 기술한 것이며 그대로 동작하지 않을 수 있다.

---

## 클래스 구조

```
PlayFromFirstScene (static class)
├── Execute()                    ← [MenuItem] 진입점, 툴바 버튼도 동일 메서드 호출
├── GetFirstEnabledSceneAsset()  ← Build Settings 0번째 활성 씬 조회 (private)
└── (static ctor)                ← playModeStateChanged 구독, EnteredEditMode 시 리셋

PlayFromFirstSceneToolbar (static class, [InitializeOnLoad])
└── TryInject()                  ← 메인 툴바에 버튼 리플렉션 삽입 (실패 시 재시도/조용히 스킵)
```

---

## 파일 구성

```
Assets/
└── Scripts/
    └── Editor/
        └── Tools/
            ├── PlayFromFirstScene.cs
            └── PlayFromFirstSceneToolbar.cs
```

---

## 상세 구현 명세

### PlayFromFirstScene.cs

```csharp
public static class PlayFromFirstScene
{
    static PlayFromFirstScene()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Play %l")]
    public static void Execute()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[PlayFromFirstScene] 이미 Play 중입니다.");
            return;
        }

        var sceneAsset = GetFirstEnabledSceneAsset();
        if (sceneAsset == null)
        {
            Debug.LogWarning("[PlayFromFirstScene] Build Settings에 활성화된 씬이 없습니다.");
            return;
        }

        EditorSceneManager.playModeStartScene = sceneAsset;
        EditorApplication.EnterPlaymode();
    }

    private static SceneAsset GetFirstEnabledSceneAsset()
    {
        var entry = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);
        return entry != null ? AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.path) : null;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorSceneManager.playModeStartScene = null;
    }
}
```

---

## Unity 씬/오브젝트 구성

해당 없음 — 씬에 배치되는 오브젝트가 없는 순수 에디터 스크립트.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 이미 Play 모드인 상태에서 실행 | 경고 로그, 무시 |
| Build Settings에 활성 씬이 하나도 없음 | 경고 로그, Play 진입 안 함 |
| Build Settings 0번 씬의 에셋 파일이 삭제/이동됨 | `AssetDatabase.LoadAssetAtPath`가 `null` 반환 → 위와 동일하게 경고 후 무시 |
| 저장되지 않은 씬 변경사항이 있는 상태에서 실행 | Unity 기본 `EnterPlaymode` 동작 그대로 저장 확인 다이얼로그 표시 (별도 처리 불필요) |
| 메인 툴바 리플렉션 삽입 실패 (Unity 버전 불일치) | 경고 로그만 남기고 스킵 — 메뉴/단축키 경로는 영향 없음 |
| Play 도중 SceneLoader 등으로 다른 씬으로 전환 | `playModeStartScene`은 진입 시점 1회만 사용되므로 영향 없음 |
| Play 종료 후 기본 Play 버튼(Ctrl+P) 재사용 | `playModeStartScene`이 `null`로 복원되어 현재 열린 씬에서 정상 시작 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | InGame 씬을 열어놓은 상태에서 Ctrl+L 실행 | Build Settings 0번 씬(Logo)부터 Play 시작, 에디터에는 InGame 씬이 그대로 열려 있음 |
| 2 | 메인 툴바 버튼 클릭 | 시나리오 1과 동일 동작 |
| 3 | `Tools > Play From First Scene` 메뉴 클릭 | 시나리오 1과 동일 동작 |
| 4 | Play 중 상태에서 Ctrl+L 재실행 | 경고 로그, 아무 동작 없음 (기존 Play 유지) |
| 5 | Play 종료 후 기본 Play 버튼(Ctrl+P) 실행 | 첫 씬으로 강제되지 않고 현재 열린 씬에서 정상 시작 |
| 6 | Build Settings의 모든 씬이 비활성화된 상태에서 실행 | 경고 로그, Play 진입 안 함 |
| 7 | 씬에 저장되지 않은 변경사항이 있는 상태에서 실행 | Unity 기본 저장 확인 다이얼로그 표시 |

---

## 구현 시 주의사항

- **툴바 리플렉션은 비공식 API**: Unity 에디터 버전이 바뀌면 `m_Root` 필드명, `"ToolbarZoneRightAlign"` 존 이름이 달라질 수 있다. 구현 시 현재 프로젝트의 Unity 버전에서 실제 필드/존 이름을 직접 확인하고, 실패해도 예외 없이 조용히 스킵되도록 방어적으로 작성한다.
- **`playModeStartScene` 복원 누락 주의**: `EnteredEditMode`에서 반드시 `null`로 리셋하지 않으면 이후 기본 Play 버튼까지 항상 첫 씬으로 강제되어 버린다 — 가장 놓치기 쉬운 버그 지점.
- **씬을 실제로 로드하지 않는다**: `EditorSceneManager.OpenScene`을 호출하지 않는다. 작업 중인 씬을 건드리지 않는 것이 이 기능의 핵심 요구사항이다.
- **"첫 번째 씬"은 Build Settings 기준**: 씬 이름을 하드코딩(`"Logo"`)하지 않고 `EditorBuildSettings.scenes`의 활성 항목 0번을 사용한다.

---

## 구현 후 체크리스트

- [ ] `PlayFromFirstScene.cs` 작성 (MenuItem/단축키, `playModeStartScene` 설정/복원)
- [ ] `PlayFromFirstSceneToolbar.cs` 작성 (메인 툴바 버튼 리플렉션 삽입)
- [ ] Build Settings에 Logo가 0번 씬으로 등록되어 있는지 확인
- [ ] 테스트 시나리오 7개 검증
- [ ] (추후) 툴바 리플렉션이 깨지는 Unity 버전 업그레이드 시 필드/존 이름 재확인
