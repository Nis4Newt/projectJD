# 기본 옵션 구현 개요

> 상위 문서: [SettingsSystem 구현 계획](../01-core-systems/settingssystem/plan-settingssystem.md) (`SettingsSystem`이 "이번 범위에서 제외"에 남겨둔 "실제 설정 UI 구현" 항목, 그리고 "옵션 창 슬라이더의 드래그 중 저장 흐름" 규칙에서 파생)
> 관련 문서: [UIManager 구현 계획](../01-core-systems/uimanager/plan-uimanager.md)(`OptionPanel`을 씬에 미리 배치하지 않고 `UIManager.Load<T>`로 런타임에 생성·캐시하는 데 사용)
> 범위: 볼륨 슬라이더 + 진동 토글을 `SettingsSystem`/`AudioSystem`에 연결하는 공용 로직(`OptionManager`) + 메인메뉴·인게임이 공유하는 단일 옵션 패널(`OptionPanel`, `Assets/Resources/UI/OptionPanel.prefab`) + 두 씬 매니저 연결. 인게임(Solo 모드 한정)은 옵션 패널 진입 시 `Time.timeScale = 0f`로 실제 진행을 멈춘다. 언어 설정 UI, `GameType.Battle`의 일시정지는 범위 밖.

---

## 배경

`plan-settingssystem.md`가 이미 확정해둔 규칙: 옵션 창 슬라이더를 드래그하는 동안은 `AudioSystem.Instance.SetVolume`으로 믹서 값만 즉시 바꾸고(저장 없음), 닫는 시점에 `SettingsSystem.Instance.SetVolume`을 호출해야 `settings.json`에 저장된다. 이번 문서 세트는 이 규칙을 실제 UI로 구현한다.

메인메뉴·인게임 두 곳에 필요한 옵션 패널은 디자이너가 미리 만들어둔 프리팹(버튼 3개 "포기"/"종료"/"나가기" + 딤 배경 클릭 닫기, 슬라이더 2개, 진동 토글 1개) 하나를 그대로 공유한다 — 씬마다 다른 것(부가 버튼 활성화, 일시정지 여부)은 `OptionPanel.Configure(OptionPanelMode)` 한 번으로 코드에서 결정하고, 씬에는 인스턴스를 미리 배치하지 않는다(각 씬 매니저가 [UIManager](../01-core-systems/uimanager/plan-uimanager.md)로 런타임 생성).

---

## 흐름도

```
Assets/Resources/UI/OptionPanel.prefab — 메인메뉴/인게임 공유, 씬에는 배치하지 않음
├── 항상 활성: BGM/SFX 슬라이더, 진동 토글, "나가기" 버튼, 딤 배경 클릭 시 닫기
├── Configure(OptionPanelMode.MainMenu): "종료" 버튼 활성, "포기" 비활성, 일시정지 없음
└── Configure(OptionPanelMode.InGame):   "포기" 버튼 활성, "종료" 비활성, Time.timeScale 0f/1f

MainMenuSceneManager                                    InGameSceneManager
  OnAwake: UIManager.Load<OptionPanel>(canvas,            OnAwake(Solo): UIManager.Load<OptionPanel>(canvas,
    p => p.Configure(MainMenu))                             p => p.Configure(InGame))
  옵션 버튼 클릭 → panel.Show()                              설정 버튼 클릭 → panel.Show()
  패널 안 "종료" → Application.Quit()                        패널 안 "포기" → GameManager.ChangeState(MainMenu)
```

---

## 하위 문서

| # | 문서 | 내용 |
|---|------|------|
| 1 | [공용 옵션 패널 구현 계획](plan-option-panel.md) | `OptionManager`(볼륨/진동 바인딩) + `OptionPanel`(프리팹에 부착된 패널 컴포넌트) |
| 2 | [옵션 패널 씬 통합 계획](plan-option-scenes.md) | `MainMenuSceneManager`/`InGameSceneManager` 연결, `GameManager` 상태 전이표에 `InGame → MainMenu` 추가 |

---

## 이번 범위에서 제외

- **언어 설정 UI** — `LocalizationSystem` 미구현으로 지원 언어 목록이 없다(`plan-settingssystem.md`가 이미 범위 밖으로 명시).
- **`GameState.Pause`를 경유하는 일시정지** — 일시정지는 `Time.timeScale`로만 구현한다. `InGameSceneManager.OnGameStateChanged`의 `Pause` 관련 주석 자리는 채우지 않는다.
- **`GameType.Battle`의 일시정지** — 설정 버튼이 `InGameSceneManager.OnAwake`의 Solo 전용 가드 안에서만 연결되므로 Battle 모드에서는 패널이 생성되지 않는다.
- **`UIManager`의 팝업 스택/백버튼/레이어 정렬 활용** — `OptionPanel`은 `UIPanel`을 상속하지 않고 `UIManager.Load<T>`(단순 로드/캐시)만 사용한다. 여러 팝업이 겹쳐 쌓이는 상황이 아니라 스택 관리가 필요 없다.
