# 공용 옵션 패널 구현 계획

> 상위 문서: [기본 옵션 구현 개요](plan-option.md) (1단계)
> 의존 관계: `JungleDice.Core.Settings.SettingsSystem`(볼륨/진동 조회·저장), `JungleDice.Core.Audio.AudioSystem`(볼륨 실시간 프리뷰), `JungleDice.Core.GameManager`(포기 시 `MainMenu` 전이), [UIManager](../01-core-systems/uimanager/plan-uimanager.md)(`Load<T>`로 생성·캐시 — 이 문서는 `OptionPanel` 쪽만 다루고 `UIManager` 자체는 별도 문서)
> 범위: bgm/sfx 슬라이더·진동 토글을 `SettingsSystem`/`AudioSystem`에 연결하는 정적 유틸리티(`OptionManager`) + 메인메뉴·인게임이 공유하는 단일 패널 컴포넌트(`OptionPanel`, `Assets/Resources/UI/OptionPanel.prefab`에 부착)까지. 씬 매니저가 `UIManager.Load<OptionPanel>(...)`을 호출하는 지점은 [씬 통합 계획](plan-option-scenes.md)에서 다룬다.

---

## 배경

`Assets/Prefabs/panel/optionPanel.prefab`(스크립트 연결 전 디자이너 작업물)의 실제 계층을 확인한 결과, 슬라이더 2개(BGM/SFX)·진동 토글 1개·버튼 3개("포기"/"종료"/"나가기")가 이미 다 준비돼 있었다 — 정확히 메인메뉴("닫기"+"게임종료")와 인게임("닫기"+"포기")이 필요로 하는 버튼의 합집합이다. 그래서 **프리팹 하나 + 컴포넌트 하나(`OptionPanel`)를 두 씬이 공유**하고, `Configure(OptionPanelMode)`로 씬별 부가 버튼·일시정지 여부만 코드에서 결정한다. 프리팹은 `Resources/UI/OptionPanel.prefab`으로 옮겨 이름도 타입명과 맞췄다(`UIManager.Load<T>`의 경로 컨벤션 때문 — [plan-uimanager.md](../01-core-systems/uimanager/plan-uimanager.md) 참고).

두 패널이 필요로 하는 슬라이더 초기화·실시간 프리뷰·닫힘 시 확정 저장 로직은 완전히 동일하므로 `OptionManager`(정적 유틸리티)로 뽑았다. 진동은 볼륨과 달리 `AudioSystem` 같은 forwarding 대상이 없어(`SettingsSystem`이 유일한 소유자) 토글이 바뀌는 즉시 저장하면 되고 "드래그 중 프리뷰 / 닫힘 시 확정"을 나눌 이유가 없다.

프리팹의 GameObject 이름과 실제 라벨 텍스트가 직관적으로 안 맞는다 — 인스펙터에서 자식 텍스트를 직접 확인하고 연결해야 헷갈리지 않는다:

```
OptionPanel (Image, 검정 40% 알파 딤 배경 — Button 컴포넌트도 있어 클릭 시 닫힘)
└── Image (다이얼로그 박스)
    ├── Text (TMP)         "옵션"   ← 제목
    ├── Text (TMP) (1)     "BGM"    ← 라벨, 자식 Slider
    ├── Text (TMP) (2)     "SFX"    ← 라벨, 자식 Slider
    ├── Text (TMP) (3)     "진동"   ← 라벨, 자식 Toggle
    ├── Button             "종료"   ← 메인메뉴 전용 → _quitButton
    ├── Button (2)         "포기"   ← 인게임 전용 → _surrenderButton
    └── Button (1)         "나가기" ← 공용 → _closeButton
```

---

## 핵심 설계 결정

### 1. `OptionManager`: 파라미터로 UI 요소를 받는 정적 클래스 (`SpriteManager`와 동일한 이유)

인스턴스 상태가 없고 호출부(`OptionPanel`)가 참조만 넘기면 되므로, `MonoBehaviour` 베이스 클래스 대신 `SpriteManager`처럼 정적 클래스로 만든다. 볼륨은 Bind/Sync/Commit 3분리, 진동은 Bind/Sync 2개뿐(Commit 없음 — 이유는 위 "배경" 참고):

```csharp
public static class OptionManager
{
    public static void BindVolumeSliders(Slider bgmSlider, Slider sfxSlider)
    {
        bgmSlider.onValueChanged.AddListener(v => AudioSystem.Instance.SetVolume(AudioChannel.BGM, v));
        sfxSlider.onValueChanged.AddListener(v => AudioSystem.Instance.SetVolume(AudioChannel.SFX, v));
    }

    public static void SyncVolumeSliders(Slider bgmSlider, Slider sfxSlider)
    {
        bgmSlider.SetValueWithoutNotify(SettingsSystem.Instance.GetVolume(AudioChannel.BGM));
        sfxSlider.SetValueWithoutNotify(SettingsSystem.Instance.GetVolume(AudioChannel.SFX));
    }

    public static void CommitVolumeSliders(Slider bgmSlider, Slider sfxSlider)
    {
        SettingsSystem.Instance.SetVolume(AudioChannel.BGM, bgmSlider.value);
        SettingsSystem.Instance.SetVolume(AudioChannel.SFX, sfxSlider.value);
    }

    public static void BindVibrationToggle(Toggle vibrationToggle) =>
        vibrationToggle.onValueChanged.AddListener(v => SettingsSystem.Instance.SetVibration(v));

    public static void SyncVibrationToggle(Toggle vibrationToggle) =>
        vibrationToggle.SetIsOnWithoutNotify(SettingsSystem.Instance.Vibration);
}
```

- `Bind*`는 **패널 생애 중 단 1회**(`Awake`) — 중복 등록 시 조작 1회에 여러 번 호출됨.
- `Sync*`는 **패널을 열 때마다**(`Show`) — `SetValueWithoutNotify` 필수(리스너 재발동 방지).
- `CommitVolumeSliders`는 **패널을 닫을 때**(나가기/종료/포기 공통) — 볼륨만 `AudioSystem` forwarding 때문에 확정 저장 단계가 필요.

### 2. `OptionPanel`: 씬 차이는 `Configure(OptionPanelMode)` 한 번으로 결정

```csharp
public enum OptionPanelMode { MainMenu, InGame }

public class OptionPanel : MonoBehaviour
{
    [SerializeField] private Slider _bgmSlider;
    [SerializeField] private Slider _sfxSlider;
    [SerializeField] private Toggle _vibrationToggle;
    [SerializeField] private Button _closeButton;   // "나가기" — 공용
    [SerializeField] private Button _dimButton;      // 딤 배경 — 공용, 닫기와 동일 동작
    [SerializeField] private Button _quitButton;     // "종료" — MainMenu에서만 활성
    [SerializeField] private Button _surrenderButton; // "포기" — InGame에서만 활성

    private bool _pausesTimeScale;

    public void Configure(OptionPanelMode mode)
    {
        _pausesTimeScale = mode == OptionPanelMode.InGame;
        _quitButton.gameObject.SetActive(mode == OptionPanelMode.MainMenu);
        _surrenderButton.gameObject.SetActive(mode == OptionPanelMode.InGame);
    }
}
```

`_quitButton`/`_surrenderButton`은 두 씬 모두 항상 채워져 있다(같은 프리팹) — 다른 건 어느 쪽이 활성인지뿐이고, 비활성 버튼은 클릭될 수 없으므로 `Awake`에서 둘 다 리스너를 걸어도 안전하다. `_pausesTimeScale`은 인스펙터 필드가 아니라 `Configure`가 세팅하는 순수 코드 값이다 — 호출부(각 씬 매니저)가 이미 자기 모드를 알고 있으므로 그대로 전달하면 된다.

`_dimButton`은 닫기와 완전히 같은 동작(`OnCloseButtonClicked`)이라 별도 핸들러를 만들지 않고 `_closeButton`과 같은 메서드에 리스너를 두 번 건다.

### 3. `Awake()`가 자기 자신을 비활성화 — `UIManager.Load`의 `Instantiate` 직후 항상 안전

프리팹 원본은 `m_IsActive: 1`(활성)로 저장돼 있고, `Instantiate`는 활성 프리팹이면 반환 전에 동기적으로 `Awake()`를 호출한다. 그래서 `Awake()`가 자기 GameObject를 `SetActive(false)`로 꺼도 리스너 연결은 이미 끝난 뒤이고, 호출부가 `Load` 다음 줄에서 `Configure`를 호출해도 순서가 항상 보장된다.

---

## 클래스 구조

```
OptionManager                          (신규, Core/Settings/, static class) — 볼륨 3메서드 + 진동 2메서드

OptionPanel : MonoBehaviour            (신규, Core/Settings/, OptionPanel.prefab 루트에 부착)
├── _bgmSlider / _sfxSlider / _vibrationToggle
├── _closeButton / _dimButton / _quitButton / _surrenderButton
├── _pausesTimeScale : bool            ← private, Configure가 세팅
├── Awake()                             ← 자기 자신 비활성화 + Bind 5종 + 버튼 4개 리스너
├── Configure(OptionPanelMode mode)     ← UIManager.Load의 onCreated로 1회 호출
├── Show()                              ← Sync 2종 + 활성화 + (조건부) Time.timeScale = 0f
├── OnCloseButtonClicked()             ← Commit + 비활성화 + (조건부) Time.timeScale = 1f (나가기/딤 배경 공용)
├── OnQuitButtonClicked()              ← Commit + Application.Quit()(에디터 분기)
└── OnSurrenderButtonClicked()         ← Commit + (조건부) Time.timeScale = 1f + GameManager.ChangeState(MainMenu)
```

---

## 파일 구성

```
Assets/
├── Scripts/Core/Settings/
│   ├── OptionManager.cs   ← 신규
│   └── OptionPanel.cs     ← 신규
└── Resources/UI/
    └── OptionPanel.prefab ← 기존 프리팹을 이동+리네임(디자이너 작업물, 여기서 컴포넌트만 부착)
```

---

## 이번 범위에서 제외

- **Master 채널 슬라이더, 언어 연결** — 요청사항에 없음 / `LocalizationSystem` 미구현.
- **`GameManager._validTransitions` 수정, 씬 매니저의 `UIManager.Load` 호출** — [씬 통합 계획](plan-option-scenes.md)에서 다룬다. 이 문서의 코드만으로는 포기 버튼이 아직 동작하지 않는다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `Bind*`를 `Show()`할 때마다 호출(잘못된 사용) | 리스너 중복 등록 — 결과는 멱등하지만 불필요한 반복 호출. `Awake`에서 1회만 호출하는 규율로 지킴 |
| `Configure()`를 호출하지 않고 `Show()`부터 호출 | 부가 버튼 둘 다 보이고 `_pausesTimeScale`도 기본값(`false`)에 머묾 |
| `CommitVolumeSliders` 호출 전 씬이 파괴됨(패널이 열린 채 강제 종료) | 마지막 드래그 값이 `settings.json`에 저장되지 않음 — 다음 실행 시 이전 값으로 복원(닫기를 눌러야 확정되는 기존 정책과 동일) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `Configure(MainMenu)` 후 확인 | "종료"만 활성, "포기" 비활성 |
| 2 | `Configure(InGame)` 후 확인 | "포기"만 활성, "종료" 비활성 |
| 3 | `Show()` 호출 | 슬라이더/토글이 저장된 값과 일치 |
| 4 | 슬라이더 드래그 중 | 실시간 반영, `settings.json`은 아직 미변경 |
| 5 | "나가기" 또는 딤 배경 클릭 | 볼륨 확정 저장 + 패널 닫힘(둘 다 동일 결과) |
| 6 | (`Configure(InGame)`) "포기" 클릭, `InGame → MainMenu` 전이가 유효한 상태 | `GameManager.CurrentState`가 `MainMenu`로 전이 |
| 7 | `Configure(InGame)` 상태에서 `Show()` → 닫기 | `Time.timeScale`이 0f → 1f로 복구 |

---

## 구현 후 체크리스트

- [x] `OptionManager.cs`/`OptionPanel.cs` 작성
- [x] `OptionPanel.prefab`에 컴포넌트 부착, 필드 7개(슬라이더 2·토글 1·버튼 4) 연결
- [x] 프리팹을 `Assets/Resources/UI/OptionPanel.prefab`으로 이동+리네임
- [ ] 테스트 시나리오 7개 검증 (Unity 에디터 Play 모드)
- [ ] [씬 통합 계획](plan-option-scenes.md)으로 이동
