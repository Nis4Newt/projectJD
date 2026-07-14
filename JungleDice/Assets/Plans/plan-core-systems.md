# 공용 코어 시스템 설계 계획

> 플랫폼: Android  
> 엔진: Unity (URP, Input System 1.x)  
> 목적: 장르와 무관하게 어떤 게임에서도 재사용 가능한 공통 인프라 구성

---

## 시스템 목록

| # | 시스템 | 우선순위 | 의존 관계 |
|---|--------|----------|-----------|
| 1 | GameManager | 최고 | 없음 |
| 2 | SceneLoader | 최고 | GameManager |
| 3 | AudioSystem | 높음 | GameManager |
| 4 | UIManager | 높음 | SceneLoader |
| 5 | SaveSystem | 높음 | 없음 |
| 6 | EventBus | 높음 | 없음 |
| 7 | ObjectPool | 중간 | 없음 |
| 8 | InputManager | 중간 | EventBus |
| 9 | SettingsSystem | 중간 | SaveSystem, AudioSystem |
| 10 | LocalizationSystem | 낮음 | SaveSystem |

---

## 1. GameManager

**역할**: 게임 전체 생명주기 관리, 각 시스템의 루트 진입점

### 책임
- 게임 상태(State) 관리: `Logo → MainMenu → InGame → Pause → GameOver`
- 앱 포커스/정지/재개 처리 (`OnApplicationPause`, `OnApplicationFocus`)
- 코어 시스템 초기화 순서 조율
- `DontDestroyOnLoad` 루트 오브젝트 역할

### 설계 방식
```
싱글턴 MonoBehaviour (씬 간 유지)
GameState enum + 상태 전이 이벤트
```

### 주요 인터페이스
```csharp
GameManager.Instance.ChangeState(GameState.InGame);
GameManager.Instance.CurrentState
```

---

## 2. SceneLoader (씬 전환 시스템)

**역할**: 씬 전환 + 로딩 화면 + 전환 연출

### 책임
- 비동기 씬 로드 (`LoadSceneAsync`)
- 로딩 진행률 UI 표시
- 전환 연출 (Fade In/Out, 슬라이드 등)
- 씬 전환 중 입력 차단
- 이전 씬 언로드 처리

### 설계 방식
```
SceneLoader (싱글턴) + LoadingScreen (독립 캔버스)
Addressables 또는 Build Settings 씬 이름 기반 로드
전환 연출은 DOTween 또는 Unity Animator
```

### 주요 인터페이스
```csharp
SceneLoader.Instance.LoadScene("MainMenu", transition: FadeType.Black);
SceneLoader.Instance.LoadSceneAsync("GamePlay", onProgress, onComplete);
```

---

## 3. AudioSystem (사운드 시스템)

**역할**: BGM / SFX 통합 관리

### 책임
- BGM 재생/정지/페이드 전환 (씬 전환 시 자동 크로스페이드)
- SFX 재생 (풀링 기반, 동시 재생 제한)
- 볼륨 채널별 제어 (Master / BGM / SFX)
- 설정값 저장/복원 연동 (SettingsSystem)
- 모바일 포커스 잃을 때 자동 Mute

### 설계 방식
```
AudioSource 풀 기반 SFX 플레이어
AudioMixer 채널 3개 (Master / BGM / SFX)
오디오 클립은 ScriptableObject로 등록 (AudioClipRegistry)
```

### 주요 인터페이스
```csharp
AudioSystem.Instance.PlayBGM(AudioID.MainTheme, fadeIn: 1.0f);
AudioSystem.Instance.PlaySFX(AudioID.ButtonClick);
AudioSystem.Instance.SetVolume(AudioChannel.BGM, 0.8f);
```

---

## 4. UIManager (UI 관리 시스템)

**역할**: 팝업/패널/HUD의 스택 기반 생명주기 관리

### 책임
- 팝업 스택 관리 (열기/닫기/뒤로가기)
- Android 백 버튼 → 최상단 팝업 닫기 자동 처리
- 레이어 정렬 (HUD < Panel < Popup < Toast < SystemModal)
- 로딩/토스트/확인 팝업 공통 제공
- UI 풀링 (자주 쓰이는 팝업 재사용)

### 설계 방식
```
캔버스 레이어 분리 (World / HUD / Popup / Overlay)
UIPanel 베이스 클래스 + Open/Close 애니메이션 인터페이스
Stack<UIPanel> 기반 팝업 히스토리
```

### 주요 인터페이스
```csharp
UIManager.Instance.Show<SettingsPopup>();
UIManager.Instance.ShowToast("저장되었습니다");
UIManager.Instance.ShowConfirm("나가시겠습니까?", onYes, onNo);
UIManager.Instance.HideTop();
```

---

## 5. SaveSystem (저장 시스템)

**역할**: 플레이어 데이터 영속화

### 책임
- 세이브 데이터 직렬화/역직렬화 (JSON)
- 암호화 (AES, 모바일 부정행위 방지 기초)
- 슬롯 개념 지원 (설정/유저데이터 분리)
- 비동기 저장 (메인 스레드 블로킹 방지)
- 저장 실패 시 백업 파일 복원

### 저장 경로
```
Application.persistentDataPath/save/userdata.json  (플레이어 진행 데이터)
Application.persistentDataPath/save/settings.json  (게임 설정)
```

### 주요 인터페이스
```csharp
SaveSystem.Save<UserData>(userData, SlotKey.UserData);
UserData data = SaveSystem.Load<UserData>(SlotKey.UserData);
SaveSystem.Delete(SlotKey.UserData);
```

---

## 6. EventBus (이벤트 버스)

**역할**: 시스템 간 결합도를 낮추는 전역 메시지 브로커

### 책임
- 타입 기반 이벤트 발행/구독
- 구독 해제 누락 방지 (약참조 또는 토큰 방식)
- 이벤트 큐잉 (프레임 지연 발행 옵션)

### 설계 방식
```
제네릭 이벤트 딕셔너리 (Dictionary<Type, Delegate>)
구독 시 IDisposable 토큰 반환 → using 또는 OnDestroy 해제
```

### 주요 인터페이스
```csharp
// 이벤트 정의
public record PlayerGoldChanged(int Before, int After);

// 발행
EventBus.Publish(new PlayerGoldChanged(100, 150));

// 구독
_subscription = EventBus.Subscribe<PlayerGoldChanged>(e => UpdateGoldUI(e.After));

// 해제
_subscription.Dispose();
```

---

## 7. ObjectPool (오브젝트 풀)

**역할**: 잦은 생성/파괴로 인한 GC 스파이크 방지

### 책임
- 프리팹 기반 풀 자동 생성 및 크기 확장
- Get / Release 인터페이스
- 씬 전환 시 풀 초기화

### 설계 방식
```
PoolManager (싱글턴) + Pool<T> 제네릭
Unity 내장 ObjectPool<T> (UnityEngine.Pool) 래핑
```

### 주요 인터페이스
```csharp
var obj = PoolManager.Get<DamageText>(prefab);
PoolManager.Release(obj);
```

---

## 8. InputManager (입력 관리)

**역할**: 터치/클릭 입력 추상화 및 전달

### 책임
- New Input System 이벤트 래핑
- UI 레이캐스트 히트 여부 필터링 (UI 위 터치 무시)
- 스와이프/핀치줌 제스처 인식
- 입력 잠금 (씬 전환, 팝업 열림 등)

### 설계 방식
```
InputSystem_Actions.inputactions (이미 존재) 기반
InputManager가 Raw 입력을 받아 EventBus로 변환 발행
```

### 주요 인터페이스
```csharp
InputManager.Instance.LockInput();
InputManager.Instance.UnlockInput();
// 게스처 이벤트는 EventBus를 통해 수신
EventBus.Subscribe<SwipeEvent>(OnSwipe);
```

---

## 9. SettingsSystem (설정 시스템)

**역할**: 게임 설정 UI와 실제 시스템 연결 + 저장

### 책임
- 설정 항목: BGM 볼륨 / SFX 볼륨 / 진동 / 언어
- 변경 즉시 적용 + 자동 저장 (SaveSystem 사용)
- 앱 시작 시 저장된 설정 자동 로드/적용

### 데이터 구조
```csharp
public class SettingsData
{
    public float MasterVolume;
    public float BgmVolume;
    public float SfxVolume;
    public bool  Vibration;
    public string Language;
}
```

---

## 10. LocalizationSystem (다국어 지원)

**역할**: 텍스트 다국어 처리

### 책임
- 언어 키 → 현재 언어 텍스트 반환
- CSV 또는 ScriptableObject 기반 번역 테이블
- 런타임 언어 변경 → 전체 UI 갱신

### 설계 방식
```
Unity Localization 패키지 활용 권장
경량 구현 시: Dictionary<string, string> + ScriptableObject 테이블
```

### 주요 인터페이스
```csharp
LocalizationSystem.Get("btn_start");       // "게임 시작" / "Start"
LocalizationSystem.SetLanguage("en");
```

---

## 폴더 구조 (제안)

```
Assets/
└── Scripts/
    └── Core/
        ├── GameManager.cs
        ├── Scene/
        │   ├── SceneLoader.cs
        │   └── LoadingScreen.cs
        ├── Audio/
        │   ├── AudioSystem.cs
        │   └── AudioClipRegistry.cs
        ├── UI/
        │   ├── UIManager.cs
        │   ├── UIPanel.cs
        │   └── Popups/
        │       ├── ConfirmPopup.cs
        │       └── ToastPopup.cs
        ├── Save/
        │   └── SaveSystem.cs
        ├── Event/
        │   └── EventBus.cs
        ├── Pool/
        │   └── PoolManager.cs
        ├── Input/
        │   └── InputManager.cs
        ├── Settings/
        │   └── SettingsSystem.cs
        └── Localization/
            └── LocalizationSystem.cs
```

---

## 구현 순서 (권장)

```
Phase 1 — 최소 실행 가능 기반
  EventBus → GameManager → SceneLoader → UIManager(기초)

Phase 2 — 데이터 계층
  SaveSystem → SettingsSystem → AudioSystem

Phase 3 — 성능 / 편의
  ObjectPool → InputManager → LocalizationSystem
```

---

## 메모

- URP 17.x / Unity 6 LTS 기준
- `com.unity.inputsystem` 이미 패키지에 포함됨
- `com.unity.modules.audio` 이미 포함됨
- Addressables 도입 여부는 리소스 규모 확정 후 결정
- DOTween 또는 LeanTween 중 하나를 전환 연출용으로 추가 검토
