# 옵션 패널 씬 통합 계획

> 상위 문서: [기본 옵션 구현 개요](plan-option.md) (2단계, [공용 옵션 패널 구현 계획](plan-option-panel.md) 이후)
> 의존 관계: `JungleDice.Core.Settings.OptionPanel`/`OptionPanelMode`, [UIManager.Load<T>](../01-core-systems/uimanager/plan-uimanager.md), `JungleDice.MainMenu.MainMenuSceneManager`, `JungleDice.InGame.InGameSceneManager`, `JungleDice.Core.GameManager`(포기 시 `MainMenu` 전이 — 상태 전이표 수정 필요)
> 범위: 두 씬 매니저가 `OnAwake()`에서 `UIManager.Load<OptionPanel>(_canvasTransform, p => p.Configure(mode))`로 패널을 가져와 여는 버튼에 연결하는 것, 인게임의 `GameOver` 시 설정 버튼 비활성화, `GameManager` 상태 전이표에 `InGame → MainMenu` 추가.

---

## 배경

포기는 InGame 씬을 벗어나 MainMenu 씬으로 돌아가야 하는데, 씬 전환은 `SceneLoader`가 `GameStateChanged`를 구독해 처리하므로 반드시 `GameManager.ChangeState(GameState.MainMenu)`를 거쳐야 한다. 그런데 기존 상태 전이표는 `InGame → MainMenu` 직접 경로가 없었다(`InGame → GameOver → MainMenu`만 있었음). `GameState.Pause`가 이미 `MainMenu`로의 전이를 허용하고 있었다는 점에 착안해 `InGame`도 동일하게 허용 목록에 더한다 — `Pause`를 실제로 거치지는 않지만(옵션 패널은 `GameState`와 무관하게 직접 여닫음), 허용해야 할 최종 목적지는 같다.

`InGameSceneManager`는 설정 버튼(옵션 패널을 여는 버튼)을 `_actionButton`과 달리 턴 상태에 따라 `interactable`을 바꾸지 않는다 — 상대 턴이든 내 턴이든 언제나 열 수 있어야 `Time.timeScale` 일시정지가 쓸모 있다. 다만 `GameOver` 이후에는 다시 열 이유가 없고, `ResultPanel`은 화면 전체를 덮는 위젯이 아니라 설정 버튼을 시각적으로 가리지 못하므로 `GameOver` 전이 시점에 `_settingsButton.interactable = false`로 명시적으로 막는다.

---

## 상세 구현 명세

### GameManager.cs

```csharp
{ GameState.InGame, new() { GameState.Pause, GameState.GameOver, GameState.MainMenu } }, // MainMenu 추가 — 포기
```

### MainMenuSceneManager.cs / InGameSceneManager.cs

```csharp
// 필드
[SerializeField] private Button _optionButton;    // InGame은 _settingsButton
[SerializeField] private Transform _canvasTransform;
private OptionPanel _optionPanel;

// OnAwake (InGame은 Solo 전용 가드 이후)
_optionPanel = UIManager.Load<OptionPanel>(_canvasTransform, p => p.Configure(OptionPanelMode.MainMenu)); // InGame은 .InGame
_optionButton.onClick.AddListener(_optionPanel.Show);
```

`InGameSceneManager.OnGameStateChanged`의 `GameOver` 분기:

```csharp
else if (e.Next == GameState.GameOver)
{
    _settingsButton.interactable = false;
    _resultPanel.ShowResult(_userWon);
}
```

`_canvasTransform`이 필요한 이유: `OptionPanel.prefab`이 자체 `Canvas`가 없어(루트에 `RectTransform`+`Image`만 있음) 기존 UI 계층 밑에서만 올바르게 렌더링된다 — `InGameSceneManager`가 `FriendCard`/`Friend`를 `_dragLayer`/`_attackLayer` 밑에 `Instantiate`하는 것과 같은 이유.

---

## Unity 씬/오브젝트 구성

```
[Scene: MainMenu]                              [Scene: InGame]
└── Canvas ← _canvasTransform                  └── Canvas ← _canvasTransform
    └── OptionButton (신규)                        └── SettingsButton (신규)
```

`optionPanel.prefab`은 씬에 배치하지 않는다 — 두 씬 매니저 모두 프리팹 에셋 자체를 몰라도 되고(`UIManager.Load<T>`가 `Resources` 경로로 찾음), 인스펙터에는 `_canvasTransform`과 버튼만 연결하면 된다.

---

## 이번 범위에서 제외

- **게임종료/포기 확인 팝업** — `UIManager`의 `ShowConfirm`([팝업 스택 문서](../01-core-systems/uimanager/plan-uimanager-popupstack.md))이 아직 구현되지 않아, 두 버튼 모두 확인 절차 없이 즉시 처리한다.
- **`GameType.Battle` 모드의 일시정지** — 설정 버튼이 `InGameSceneManager.OnAwake`의 Solo 전용 가드 안에서만 연결되므로 Battle 모드에서는 패널이 생성되지 않는다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `GameManager._validTransitions`에 `InGame → MainMenu`를 추가하지 않은 상태에서 포기 버튼 클릭 | `ChangeState`가 경고 로그만 남기고 무시 |
| 컴퓨터 턴 진행 중 설정 버튼 클릭 | `Time.timeScale = 0f`로 코루틴이 멈춤 — 패널을 닫을 때까지 진행 안 됨(요구사항대로) |
| `GameOver` 상태에서 설정 버튼 `interactable` 비활성화가 누락된 경우 | 패널이 열려 `Time.timeScale = 0f`가 되고, `ResultPanel.AutoExitAfterDelay`의 자동 복귀 타이머까지 함께 멈춰버리는 버그가 됨 |
| `_canvasTransform`을 연결하지 않음 | 패널이 `Canvas` 밖에서 생성돼 화면에 안 보임 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | MainMenu 진입 직후 | 옵션 패널 비활성 상태 |
| 2 | 옵션 버튼 클릭 | "닫기"/"종료"만 보임, "포기"는 안 보임 |
| 3 | InGame 진입 직후 | `Time.timeScale == 1f` |
| 4 | 설정 버튼 클릭 | "닫기"/"포기"만 보임, `Time.timeScale == 0f` |
| 5 | 포기 버튼 클릭 | `MainMenu`로 전이, InGame 씬 언로드, `Time.timeScale == 1f`로 복구 |
| 6 | `GameOver` 전이 후 설정 버튼 클릭 | 반응 없음(`interactable == false`) |

---

## 구현 후 체크리스트

- [x] `GameManager.cs`의 `_validTransitions[GameState.InGame]`에 `GameState.MainMenu` 추가
- [x] `MainMenuSceneManager.cs`/`InGameSceneManager.cs`에 `_canvasTransform` 필드 추가, `OnAwake()`에서 `UIManager.Load<OptionPanel>` + 리스너 연결
- [x] `InGameSceneManager.OnGameStateChanged`의 `GameOver` 분기에 `_settingsButton.interactable = false` 추가
- [ ] **(에디터 작업)** 두 씬에 `OptionButton`/`SettingsButton` 배치, `_canvasTransform`을 각 씬 `Canvas`로 연결
- [ ] 테스트 시나리오 6개 검증
- [ ] (추후) `UIManager.ShowConfirm` 구현 시 포기/게임종료 확인 팝업 추가 검토
