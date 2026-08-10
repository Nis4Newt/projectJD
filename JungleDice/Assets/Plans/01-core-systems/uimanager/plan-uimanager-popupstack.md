# UIManager 팝업 스택·레이어·백버튼 구현 계획

> 상위 문서: [UIManager 구현 계획](plan-uimanager.md) (2단계, `Load<T>` 캐시 이후)
> 관련 문서: [공용 코어 시스템 설계 계획](../plan-core-systems.md)(시스템 목록 #4 `UIManager`가 요구한 나머지 책임 — 팝업 스택 관리, Android 백버튼, 레이어 정렬, 로딩/토스트/확인 팝업 공통 제공 — 을 이 문서가 마저 구현)
> 의존 관계: [UIManager 구현 계획](plan-uimanager.md)의 `UIManager.Load<T>`(재사용), `UnityEngine.Input`(백버튼 폴링), `GameManagers`(`DontDestroyOnLoad` 루트, 드라이버 부착 대상)
> 범위: `UIPanel` 베이스 클래스, 레이어 5단(HUD/Panel/Popup/Toast/SystemModal) 루트 관리, 팝업 스택(`Show<T>`/`HideTop`), Android 백버튼 자동 처리까지 구현한다. `ShowToast`/`ShowConfirm` API와 그 대상인 `ToastPanel`/`ConfirmPanel`(코드·프리팹 모두)은 설계만 문서에 남기고 이번 구현에서는 제외한다 — 아직 이 API를 실제로 호출하는 소비자가 없어, 생기는 시점에 함께 만든다. `LoadingScreen`도 범위 밖.

---

## 배경 / 문제 인식

[UIManager 구현 계획](plan-uimanager.md)(1단계)은 `plan-core-systems.md`가 `UIManager`(#4)에게 맡긴 다섯 책임 중 "UI 풀링(자주 쓰이는 팝업 재사용)"만 `Load<T>`로 구현했다. 나머지 네 가지가 남아있다:

- 팝업 스택 관리 (열기/닫기/뒤로가기)
- Android 백버튼 → 최상단 팝업 닫기 자동 처리
- 레이어 정렬 (HUD < Panel < Popup < Toast < SystemModal)
- 로딩/토스트/확인 팝업 공통 제공

이 문서가 이 네 가지를 `UIManager`에 마저 얹는다. `OptionPanel`([plan-option-panel.md](../../07-option/plan-option-panel.md))은 이 시스템에 편입시키지 않는다 — 볼륨 확정 저장·`Time.timeScale` 전환처럼 자기만의 여닫힘 로직이 이미 있고, 여러 팝업이 겹쳐 쌓이는 상황도 아니라(각 씬에 하나뿐, 전용 버튼으로만 여닫음) 스택/레이어 관리 대상으로 삼을 이유가 없다. 이 문서가 새로 만드는 `UIPanel` 베이스는 앞으로 생길 "여러 개가 겹쳐 쌓일 수 있는" 팝업 종류에만 적용된다.

---

## 설계 목표

- 화면에 여러 팝업이 동시에 열려도 "가장 최근에 연 것부터 닫는" 순서가 자동으로 보장돼야 한다.
- Android 백버튼을 누르면 최상단 팝업 하나만 닫히고, 스택이 비어있으면 아무 일도 일어나지 않아야 한다(앱을 직접 종료시키지 않음).
- 팝업 종류(HUD/Panel/Popup/Toast/SystemModal)에 따라 항상 올바른 그리기 순서로 보여야 한다 — 개별 팝업이 자기 정렬 순서를 직접 관리하지 않는다.
- 토스트/확인 팝업처럼 자주 쓰이는 패턴은 한 줄 API(`ShowToast`/`ShowConfirm`)로 띄울 수 있어야 한다.
- 1단계의 `UIManager.Load<T>` 캐시 메커니즘을 그대로 재사용한다 — 팝업도 결국 "타입별로 하나씩 재사용하는 패널"이라는 점은 동일하다.

---

## 핵심 설계 결정

### 5. `UIPanel` 추상 베이스 클래스 — Open/Close 공통 인터페이스 + 레이어 선언

**후보 검토:**

| 후보 | 기각/채택 사유 |
|------|----------------|
| `interface IUIPanel` | 기각 — 공통 동작(기본 `Open`/`Close` 구현)을 담을 곳이 없어 구현체마다 `gameObject.SetActive` 같은 뻔한 코드가 중복됨 |
| **`abstract class UIPanel : MonoBehaviour`** | **채택** — 기본 `Open`/`Close` 구현을 제공하고, 필요한 패널만 오버라이드. `Layer`도 기본값(Popup)을 가진 `virtual` 프로퍼티로 둬서 대부분의 팝업은 아무것도 안 적어도 되게 함 |

```csharp
public abstract class UIPanel : MonoBehaviour
{
    public virtual UILayer Layer => UILayer.Popup;

    public virtual void Open() => gameObject.SetActive(true);
    public virtual void Close() => gameObject.SetActive(false);
}
```

### 6. `UILayer` enum + 레이어 루트는 `UIManagerDriver`가 인스펙터로 공급

```csharp
public enum UILayer
{
    HUD,
    Panel,
    Popup,
    Toast,
    SystemModal,
}
```

`UIManager`는 정적 클래스라 씬과 무관하게 살아있는 Canvas 계층을 스스로 만들 수 없다 — 코드로 `new GameObject()` + `Canvas`/`CanvasScaler`/`GraphicRaycaster`를 즉석에서 붙이는 방법도 있지만, 그러면 인스펙터에서 아무것도 보이지 않아 디버깅이 어려워지고 프로젝트 전반의 "인스펙터 연결 기반" 관례(`AudioSystem`의 믹서 참조, `SettingsSystem`의 없음도 포함해 대부분의 시스템이 `SerializeField`로 연결)와 어긋난다. 그래서 `GameManagers`에 얇은 `MonoBehaviour`(`UIManagerDriver`)를 하나 붙여 레이어 루트 5개를 인스펙터로 받고, `Awake()`에서 `UIManager.Initialize(...)`로 한 번 전달한다.

```csharp
public class UIManagerDriver : MonoBehaviour
{
    [SerializeField] private Transform[] _layerRoots; // UILayer enum 순서와 동일하게 5개(HUD, Panel, Popup, Toast, SystemModal)

    private void Awake() => UIManager.Initialize(_layerRoots);
    private void Update() => UIManager.HandleBackButton();
}
```

```csharp
// UIManager.cs 추가분
private static Transform[] _layerRoots;

public static void Initialize(Transform[] layerRoots) => _layerRoots = layerRoots;
```

### 7. 팝업 스택은 `Load<T>` 위에 얇게 얹는다 — `Show<T>`/`HideTop`

```csharp
private static readonly Stack<UIPanel> _popupStack = new();

public static T Show<T>(Action<T> onCreated = null) where T : UIPanel
{
    var panel = Load<T>(_layerRoots[(int)UILayer.Popup], onCreated); // 최초 생성 시 임시 부모
    panel.transform.SetParent(_layerRoots[(int)panel.Layer], false); // 실제 레이어로 재배치(매번 확인)
    panel.Open();
    _popupStack.Push(panel);
    return panel;
}

public static void HideTop()
{
    if (_popupStack.Count == 0) return;
    _popupStack.Pop().Close();
}
```

레이어 재배치를 캐시 히트에도 매번 수행하는 이유: `Layer`는 인스턴스 프로퍼티라 인스턴스가 생기기 전에는 읽을 수 없다(제네릭 타입 인자만으로는 알 수 없음). 그래서 일단 `Popup` 레이어 밑에 생성한 뒤 실제 `Layer` 값으로 옮긴다 — 이미 올바른 부모 밑에 있으면 `SetParent`가 사실상 아무 일도 하지 않으므로 캐시 히트 경로에서 매번 호출해도 비용은 무시할 만하다.

### 8. Android 백버튼: `UIManagerDriver.Update()`에서 매 프레임 폴링, 스택 최상단만 닫음

```csharp
public static void HandleBackButton()
{
    if (_popupStack.Count > 0 && Input.GetKeyDown(KeyCode.Escape))
        HideTop();
}
```

`KeyCode.Escape`가 Android 백버튼과 매핑되는 것은 Unity의 기존 관례다. `plan-core-systems.md`의 `InputManager`(#8)가 아직 미구현이라, 새 Input System 액션을 추가하는 대신 레거시 `Input` API로 최소 구현한다 — `InputManager` 도입 시 이 폴링을 그쪽으로 옮기는 것을 재검토한다. 스택이 비어있으면(열린 팝업이 없으면) 아무것도 하지 않는다 — 앱 종료는 OS/Unity 기본 동작에 맡기고 직접 처리하지 않는다.

### 9. `ShowToast`/`ShowConfirm`: 토스트는 스택에 안 쌓이고, 확인 팝업은 쌓인다

```csharp
public static void ShowToast(string message)
{
    var toast = Load<ToastPanel>(_layerRoots[(int)UILayer.Toast]);
    toast.transform.SetParent(_layerRoots[(int)UILayer.Toast], false);
    toast.Show(message);
}

public static ConfirmPanel ShowConfirm(string message, Action onYes, Action onNo)
{
    var confirm = Show<ConfirmPanel>();
    confirm.Setup(message, onYes, onNo);
    return confirm;
}
```

`ShowToast`는 `Show<T>`가 아니라 `Load<T>`를 직접 쓴다 — 토스트는 몇 초 뒤 스스로 사라지는 알림이라 "뒤로가기로 닫는 스택 대상"이 아니다. 스택에 넣으면 토스트가 자동으로 사라진 뒤에도 스택에는 항목이 남아, 다음 백버튼이 이미 사라진 토스트를 대상으로 또 `Close()`를 호출하는 불일치가 생긴다. `ShowConfirm`은 반대로 사용자가 반드시 응답해야 하는 모달이라 스택에 참여시킨다(`Layer`는 `SystemModal`로 둬서 항상 최상단에 그려지게 한다).

---

## 클래스 구조

```
UILayer : enum                                        (신규, Core/UI/)
├── HUD
├── Panel
├── Popup
├── Toast
└── SystemModal

UIPanel : MonoBehaviour, abstract                     (신규, Core/UI/)
├── Layer : UILayer                                    ← virtual, 기본값 Popup
├── Open()                                              ← virtual
└── Close()                                             ← virtual

UIManager                                             (기존, Core/UI/, static class — 1단계에 이어 확장)
├── _instances : Dictionary<Type, MonoBehaviour>       ← 기존(1단계)
├── _layerRoots : Transform[]                          ← 신규, Initialize로 세팅
├── _popupStack : Stack<UIPanel>                        ← 신규
├── Load<T>(...)                                        ← 기존(1단계), 변경 없음
├── Initialize(Transform[] layerRoots)                  ← 신규, UIManagerDriver.Awake에서 1회
├── Show<T>(Action<T> onCreated = null) : T where T : UIPanel   ← 신규
├── HideTop()                                           ← 신규
├── HandleBackButton()                                  ← 신규, UIManagerDriver.Update에서 매 프레임
├── ShowToast(string message)                           ← 신규, Load 기반(스택 미참여)
└── ShowConfirm(string message, Action onYes, Action onNo) : ConfirmPanel   ← 신규, Show 기반(스택 참여)

UIManagerDriver : MonoBehaviour                        (신규, Core/UI/, GameManagers에 부착)
├── _layerRoots : Transform[5]                          ← [SerializeField], UILayer 순서와 동일
├── Awake() → UIManager.Initialize(_layerRoots)
└── Update() → UIManager.HandleBackButton()

ToastPanel : UIPanel                                    (신규, Core/UI/, 코드 스텁만 — 프리팹은 범위 밖)
├── Layer => UILayer.Toast
└── Show(string message)                                ← 일정 시간 후 스스로 Close

ConfirmPanel : UIPanel                                  (신규, Core/UI/, 코드 스텁만 — 프리팹은 범위 밖)
├── Layer => UILayer.SystemModal
└── Setup(string message, Action onYes, Action onNo)
```

---

## 파일 구성

```
Assets/
├── Scripts/
│   └── Core/
│       └── UI/
│           ├── UIManager.cs         ← 기존, 확장(Initialize/Show/HideTop/HandleBackButton/ShowToast/ShowConfirm 추가)
│           ├── UILayer.cs           ← 신규
│           ├── UIPanel.cs           ← 신규
│           ├── UIManagerDriver.cs   ← 신규
│           ├── ToastPanel.cs        ← 신규(코드 스텁 — 프리팹 없이는 동작 안 함)
│           └── ConfirmPanel.cs      ← 신규(코드 스텁 — 프리팹 없이는 동작 안 함)
└── Resources/
    └── UI/
        └── (ToastPanel.prefab, ConfirmPanel.prefab — 실제 제작은 범위 밖)
```

---

## 상세 구현 명세

### UIManager.cs (기존 파일, 확장)

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace JungleDice.Core.UI
{
    public static class UIManager
    {
        private static readonly Dictionary<Type, MonoBehaviour> _instances = new();
        private static readonly Stack<UIPanel> _popupStack = new();
        private static Transform[] _layerRoots;

        public static T Load<T>(Transform parent, Action<T> onCreated = null) where T : MonoBehaviour
        {
            if (_instances.TryGetValue(typeof(T), out var cached) && cached != null)
                return (T)cached;

            var prefab = Resources.Load<T>($"UI/{typeof(T).Name}");
            var instance = UnityEngine.Object.Instantiate(prefab, parent);
            _instances[typeof(T)] = instance;
            onCreated?.Invoke(instance);
            return instance;
        }

        public static void Initialize(Transform[] layerRoots) => _layerRoots = layerRoots;

        public static T Show<T>(Action<T> onCreated = null) where T : UIPanel
        {
            var panel = Load<T>(_layerRoots[(int)UILayer.Popup], onCreated);
            panel.transform.SetParent(_layerRoots[(int)panel.Layer], false);
            panel.Open();
            _popupStack.Push(panel);
            return panel;
        }

        public static void HideTop()
        {
            if (_popupStack.Count == 0) return;
            _popupStack.Pop().Close();
        }

        public static void HandleBackButton()
        {
            if (_popupStack.Count > 0 && Input.GetKeyDown(KeyCode.Escape))
                HideTop();
        }

        public static void ShowToast(string message)
        {
            var toast = Load<ToastPanel>(_layerRoots[(int)UILayer.Toast]);
            toast.transform.SetParent(_layerRoots[(int)UILayer.Toast], false);
            toast.Show(message);
        }

        public static ConfirmPanel ShowConfirm(string message, Action onYes, Action onNo)
        {
            var confirm = Show<ConfirmPanel>();
            confirm.Setup(message, onYes, onNo);
            return confirm;
        }
    }
}
```

### UILayer.cs / UIPanel.cs (신규)

```csharp
namespace JungleDice.Core.UI
{
    public enum UILayer
    {
        HUD,
        Panel,
        Popup,
        Toast,
        SystemModal,
    }
}
```

```csharp
using UnityEngine;

namespace JungleDice.Core.UI
{
    public abstract class UIPanel : MonoBehaviour
    {
        public virtual UILayer Layer => UILayer.Popup;

        public virtual void Open() => gameObject.SetActive(true);
        public virtual void Close() => gameObject.SetActive(false);
    }
}
```

### UIManagerDriver.cs (신규)

```csharp
using UnityEngine;

namespace JungleDice.Core.UI
{
    public class UIManagerDriver : MonoBehaviour
    {
        [SerializeField] private Transform[] _layerRoots; // UILayer 순서와 동일: HUD, Panel, Popup, Toast, SystemModal

        private void Awake() => UIManager.Initialize(_layerRoots);
        private void Update() => UIManager.HandleBackButton();
    }
}
```

### ToastPanel.cs / ConfirmPanel.cs (신규, 코드 스텁)

```csharp
using TMPro;
using UnityEngine;

namespace JungleDice.Core.UI
{
    public class ToastPanel : UIPanel
    {
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private float _duration = 2f;

        public override UILayer Layer => UILayer.Toast;

        public void Show(string message)
        {
            _messageText.text = message;
            Open();
            CancelInvoke(nameof(Close));
            Invoke(nameof(Close), _duration);
        }
    }
}
```

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.Core.UI
{
    public class ConfirmPanel : UIPanel
    {
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private Button _yesButton;
        [SerializeField] private Button _noButton;

        public override UILayer Layer => UILayer.SystemModal;

        private Action _onYes;
        private Action _onNo;

        private void Awake()
        {
            _yesButton.onClick.AddListener(() => { Close(); _onYes?.Invoke(); });
            _noButton.onClick.AddListener(() => { Close(); _onNo?.Invoke(); });
        }

        public void Setup(string message, Action onYes, Action onNo)
        {
            _messageText.text = message;
            _onYes = onYes;
            _onNo = onNo;
        }
    }
}
```

`ToastPanel`/`ConfirmPanel`은 `[SerializeField]` 참조(`_messageText`, `_yesButton`, `_noButton`)를 실제로 연결해줄 프리팹이 없으면 인스펙터 연결이 비어있는 채로 `Resources.Load`조차 실패한다(프리팹 자체가 `Resources/UI/`에 없으므로) — 이 두 클래스는 코드만 준비해두고, 프리팹은 실제 소비자가 생기는 시점에 함께 제작한다.

---

## 이번 범위에서 제외

- **`ToastPanel`/`ConfirmPanel`의 실제 프리팹(비주얼) 제작** — 디자인 리소스와 Unity 에디터 작업이 필요하고, 현재 이 API를 실제로 호출하는 곳이 없어 급하지 않다. 소비자가 생기는 시점에 함께 만든다.
- **`LoadingScreen`(로딩 화면)** — 로드맵의 `UIManager` 책임 목록에 있었지만, `SceneLoader`(`plan-sceneloader.md`)가 이미 씬 전환 로딩을 다루고 있어 겹치는 부분을 어떻게 나눌지 조율이 필요하다. 이 문서에서 함께 설계하지 않는다.
- **`SystemModal`이 항상 최상단에 그려지도록 실제 Canvas `Sort Order` 값을 배정하는 것** — 씬/프리팹 구성(에디터) 작업이라 범위 밖. 이 문서는 "어떤 레이어가 있어야 하는가"까지만 다룬다.
- **여러 `SystemModal`이 동시에 뜨는 경우의 우선순위** — 확인 팝업은 한 번에 하나씩만 띄우는 게 일반적인 UX라는 전제로, 지금은 다루지 않는다.
- **New Input System으로의 백버튼 처리 이관** — `InputManager`(#8) 도입 시 재검토.
- **`Show<T>()` 중복 호출 시 스택에 같은 인스턴스가 여러 번 쌓이는 것에 대한 방어** — 아직 실제 소비자가 없어 이게 실제로 문제가 되는지 확인되지 않았다. 아래 "엣지 케이스"에 남겨두고, 실제로 발생하면 그때 가드를 추가한다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 팝업이 하나도 없는 상태에서 백버튼 입력 | 아무 일도 일어나지 않음(앱이 종료되지 않음, OS/Unity 기본 동작에 맡김) |
| 같은 타입에 대해 `Show<T>()`를 연속 두 번 호출 | `Load<T>`가 같은 인스턴스를 반환하지만, `_popupStack`에는 같은 인스턴스가 두 번 쌓임 — `HideTop()`을 한 번 눌러도 완전히 닫히지 않고 스택에 남은 복제 항목 때문에 한 번 더 눌러야 함(위 "이번 범위에서 제외" 참고, 아직 방어 없음) |
| `UIManagerDriver.Awake()`가 실행되기 전에(즉 `Initialize` 호출 전에) `Show<T>()`가 먼저 호출됨 | `_layerRoots`가 `null`이라 `NullReferenceException` — 방어 코드 없음, 기존 관례(초기화 순서 실수는 즉시 드러남)와 동일 |
| `ToastPanel`이 화면에 떠있는 도중 같은 메시지로 `ShowToast`를 다시 호출 | 캐시된 동일 인스턴스를 재사용, `CancelInvoke` → `Invoke` 재예약으로 사라지는 타이머만 리셋되고 텍스트는 최신 메시지로 갱신됨 |
| `ToastPanel.prefab`/`ConfirmPanel.prefab`이 `Resources/UI/`에 없는 상태에서 `ShowToast`/`ShowConfirm` 호출 | `Load<T>` 내부의 `Resources.Load<T>`가 `null` 반환 → `Instantiate(null, parent)`에서 예외 — 1단계 문서와 동일한 관례(방어 없음) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `UIManagerDriver.Awake()` 실행 후 `Show<ConfirmPanel>()` 호출 | `SystemModal` 레이어 루트 밑에 생성되어 화면에 보임, 스택에 1개 |
| 2 | 시나리오 1 이후 백버튼(Escape) 입력 | `ConfirmPanel`이 `Close()`되어 사라짐, 스택 0개 |
| 3 | 스택이 빈 상태에서 백버튼 입력 | 아무 일도 일어나지 않음(예외 없음) |
| 4 | `ShowToast("저장됨")` 호출 | `Toast` 레이어에 표시되고, 지정 시간 뒤 자동으로 사라짐 — 표시되는 동안 백버튼을 눌러도(스택에 없으므로) 토스트에 영향 없음 |
| 5 | `ShowConfirm("나가시겠습니까?", onYes, onNo)` 호출 후 "예" 버튼 클릭 | `onYes` 콜백이 호출되고 패널이 닫힘, 스택에서도 제거됨(다음 백버튼이 다른 팝업을 대상으로 함) |

---

## 구현 시 주의사항

- **`Show<T>()`가 `Initialize()`보다 먼저 호출되지 않게 한다.** `UIManagerDriver`가 `GameManagers`에 부착되어 있는지, 그리고 그 `Awake()`가 실제로 실행되는 시점(씬 로드 이후 언제든 `Show<T>`가 호출될 수 있음을 감안)을 확인할 것.
- **`ShowToast`는 `Show<T>`가 아니라 `Load<T>`를 쓴다.** 토스트를 스택에 넣으면 백버튼 처리와 어긋난다(위 "핵심 설계 결정 9" 참고) — 새 팝업 종류를 추가할 때 "이게 스스로 사라지는 알림인지, 사용자가 닫아야 하는 모달인지"를 먼저 판단해서 `Load`/`Show` 중 맞는 쪽을 고를 것.
- **`ToastPanel`/`ConfirmPanel`의 프리팹이 없는 동안은 `ShowToast`/`ShowConfirm`을 호출하지 않는다** — 호출하면 즉시 예외가 난다.
- **레이어 루트 5개(`UIManagerDriver._layerRoots`)의 순서가 `UILayer` enum 순서(HUD, Panel, Popup, Toast, SystemModal)와 정확히 일치해야 한다** — 인덱스로 접근하므로 순서가 어긋나면 엉뚱한 레이어에 패널이 배치된다.

---

## 구현 후 체크리스트

- [x] `UIManager.cs`에 `Initialize`/`Show`/`HideTop`/`HandleBackButton` 추가
- [x] `UILayer.cs`, `UIPanel.cs` 작성
- [x] `UIManagerDriver.cs` 작성(스크립트만 — `GameManagers` 부착과 레이어 루트 5개 인스펙터 연결은 에디터 작업으로 남음)
- [ ] **(이번 구현에서 제외)** `ToastPanel.cs`/`ConfirmPanel.cs`, `UIManager.ShowToast`/`ShowConfirm` — 문서 설계만 남기고 코드는 작성하지 않음
- [ ] `UIManagerDriver`를 `GameManagers`에 부착, 레이어 루트 5개 인스펙터 연결 (에디터 작업)
- [ ] 테스트 시나리오 1~3 검증(`Show<T>`/`HideTop`/백버튼 — 임시 `UIPanel` 구현체로 검증 가능) — Unity 에디터 Play 모드 필요
- [ ] (추후) `ToastPanel`/`ConfirmPanel` 코드 + 프리팹 제작, `ShowToast`/`ShowConfirm` 구현 — 실제 소비자가 생기는 시점
- [ ] (추후) `LoadingScreen` 설계, `SceneLoader`와 책임 분담 조율
- [ ] (추후) `InputManager` 도입 시 백버튼 폴링을 그쪽으로 이관 검토
