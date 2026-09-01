# StorePanel 신규 구현 계획

> 상위 문서: [메인메뉴 고정형 레이아웃 전환 개요](./plan-mainmenuscene-fixedlayout.md) (하위 문서 #2)
> 관련 문서: [UIManager 팝업 스택 구현 계획](../../01-core-systems/uimanager/plan-uimanager-popupstack.md) (`UIPanel`/`UIManager.Show<T>`/`Register<T>` 재사용 — 씬에 배치된 패널은 자기 `Awake()`에서 스스로 `Register`, 결정 11), [친구탭 → FriendPanel 전환 계획](./plan-mainmenuscene-fixedlayout-friendpanel.md) (동일 패턴 — 패널 자신은 닫기 버튼을 갖지 않음, 씬에 배치해두고 자가 `Register`), [탭슬라이드 제거 및 모험 화면 고정화 계획](./plan-mainmenuscene-fixedlayout-slideremoval.md) (닫기는 이 문서의 공용 버튼/백버튼이 담당 — `MainMenuSceneManager`는 `StorePanel`을 몰라도 됨)
> 의존 관계: `JungleDice.Core.UI.UIPanel`/`UIManager`(`Show<T>`/`Register<T>`)
> 범위: "상품" 버튼을 눌렀을 때 열리는 빈 패널(제목만)을 만든다. 실제 상품 목록/구매/재화 차감 로직은 상품 시스템 자체가 아직 설계되지 않아 범위 밖 — 이 문서는 "패널을 열고 닫는 틀"만 완성한다. 닫기 버튼은 패널 자신이 아니라 [slideremoval 문서](./plan-mainmenuscene-fixedlayout-slideremoval.md)의 `MainMenuSceneManager` 공용 버튼과 백버튼/ESC가 담당한다. **`FriendPanel`과 동일하게 `Resources/UI/` 프리팹이 아니라 MainMenu 씬에 직접 배치한다** — `Awake()`에서 스스로 `UIManager.Register(this)`로 캐시에 등록.

---

## 배경 / 문제 인식

기존 4탭 슬라이드 구조에는 "상품" 탭 아이콘(`IconStore.png`)만 있고 실제 콘텐츠나 스크립트는 없었다 — 자리표시자 페이지였다. 고정형 전환 후 "상품" 버튼이 눌리는 것 자체는 요구사항이므로, 콘텐츠는 비어있더라도 [친구탭 → FriendPanel 전환 계획](./plan-mainmenuscene-fixedlayout-friendpanel.md)과 동일한 `UIPanel` 패턴으로 열고 닫히는 패널을 신규로 만든다.

---

## 설계 목표

- "상품" 버튼 클릭 → `StorePanel`이 열리고, `MainMenuSceneManager`의 공용 닫기 버튼 또는 백버튼/ESC로 닫힌다
- 실제 상품 콘텐츠(목록, 구매 흐름)는 만들지 않는다 — 나중에 상품 시스템이 설계되면 이 패널 내부만 채우면 되는 구조로 남긴다
- [plan-uimanager-popupstack.md](../../01-core-systems/uimanager/plan-uimanager-popupstack.md)의 `ToastPanel`/`ConfirmPanel` 스텁과 동일한 성격 — 코드/프리팹은 최소한으로 준비하고 실제 소비 콘텐츠는 후속 문서로 미룬다

---

## 핵심 설계 결정

### 1. `StorePanel : UIPanel` — 타입 존재만 목적인 빈 스텁

닫기 버튼을 패널 자신이 갖지 않기로 한 결정([friendpanel 문서 결정 3](./plan-mainmenuscene-fixedlayout-friendpanel.md))을 그대로 따르고, 실제 상품 콘텐츠도 아직 없으므로 이 클래스는 `UIManager.Show<StorePanel>(parent)`이 타입 인자로 잡을 수 있는 것 외에 할 일이 거의 없다. `Open()`/`Close()` 오버라이드도 불필요 — `UIPanel`의 기본 구현(`SetActive` 토글) 그대로 사용한다. `Awake()`에서 `gameObject.SetActive(false)`만 `OptionPanel`/`FriendPanel`과 동일하게 방어적으로 걸고, [popupstack 결정 11](../../01-core-systems/uimanager/plan-uimanager-popupstack.md)에 따라 `UIManager.Register(this)`로 스스로 캐시에 등록한다 — 씬을 소비하는 `MainMenuSceneManager`는 `StorePanel`을 인스펙터로 알 필요가 없다.

```csharp
using JungleDice.Core.UI;

namespace JungleDice.MainMenu
{
    public class StorePanel : UIPanel
    {
        private void Awake()
        {
            gameObject.SetActive(false);
            UIManager.Register(this);
        }
    }
}
```

---

## 클래스 구조

```
StorePanel : UIPanel                          (신규, MainMenu/, 빈 스텁 — UIPanel 기본 구현만 사용)
└── Awake()                                    ← gameObject.SetActive(false) + UIManager.Register(this)
```

---

## 파일 구성

```
Assets/
└── Scripts/
    └── MainMenu/
        └── StorePanel.cs                      ← 신규
```

`Resources/UI/`에는 아무것도 추가하지 않는다 — `StorePanel`은 MainMenu 씬에 배치한다(핵심 설계 결정 1, `FriendPanel`과 동일한 이유).

---

## Unity 씬/오브젝트 구성

```
MainMenu.unity
└── StorePanel (StorePanel.cs 부착)
    ├── Dim (배경, 선택)
    └── Panel
        └── Title ("상품", TextMeshProUGUI)
```

`FriendPanel`과 마찬가지로 씬에 새 GameObject를 만들어 `StorePanel.cs`를 붙이기만 하면 된다 — `Awake()`가 스스로 `Register`하므로 `MainMenuSceneManager` 쪽 인스펙터 연결은 불필요하다. 씬에 이미 있는 Canvas 하위에 두면 되므로 별도 `Canvas`/`CanvasScaler`/`GraphicRaycaster`도 필요 없다.

---

## 이번 범위에서 제외

- **실제 상품 목록/구매/재화 차감 로직** — 상품 시스템 자체가 아직 설계되지 않았다. 설계가 나오면 이 패널 내부에 콘텐츠만 추가하면 되는 구조(`UIPanel` 상속, 씬에 배치)는 이미 갖춰진다.
- **상품 아이콘(`IconStore.png`)을 패널 어디에 배치할지** — 실제 상품 콘텐츠 설계 시점에 함께 결정.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `StorePanel` GameObject가 씬에서 비활성 부모 밑에 있어 `Awake()`가 실행되지 않음 | `Register(this)`가 안 불려 캐시가 비어있는 채로 남음 → "상품" 버튼 클릭 시 `Show<T>`가 캐시 미스로 `Resources.Load`를 시도하다 실패([friendpanel 문서](./plan-mainmenuscene-fixedlayout-friendpanel.md)와 동일한 관례) |
| "상품" 버튼과 "친구" 버튼을 연달아 클릭 | 두 패널이 각각 스택에 쌓임 — 둘 다 같은 MainMenu 씬 Canvas 밑이므로 그리기 순서는 씬 하이어라키상 형제 순서를 따름([개요 문서](./plan-mainmenuscene-fixedlayout.md)에서 이미 범위 밖으로 명시), 백버튼/공용 닫기 버튼을 눌러야 모험 화면으로 완전히 복귀 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 모험 화면에서 "상품" 버튼 클릭 | `StorePanel`이 열림, 스택 1개 |
| 2 | 시나리오 1 이후 공용 닫기 버튼 클릭 | 패널 닫힘, 스택 0개 |
| 3 | 시나리오 1 이후 백버튼(Escape) 입력 | 패널 닫힘, 스택 0개 |

---

## 구현 시 주의사항

- **씬에 `StorePanel` GameObject를 배치하지 않으면(또는 비활성 부모 밑에 두면) "상품" 버튼이 예외를 던진다** — `Awake()`의 자가 `Register`가 전제.
- **`Resources/UI/`에 프리팹을 만들지 않는다** — 씬 배치 + 자가 `Register`가 최종 설계다(핵심 설계 결정 1).
- **내부 콘텐츠를 미리 설계하려 하지 않는다** — 요구사항이 없는 상태에서 상품 그리드/아이템 프리팹 등을 미리 만들면 나중에 실제 설계와 어긋날 가능성이 크다(YAGNI).

---

## 구현 후 체크리스트

- [x] `StorePanel.cs` 작성 (`Assets/Scripts/MainMenu/`) — `Awake()`에 `UIManager.Register(this)` 포함
- [ ] MainMenu 씬에 `StorePanel` GameObject 배치 — 배경 딤 + 패널 박스 + 제목 텍스트 (Unity 에디터 작업)
- [ ] 테스트 시나리오 3개 검증 — Unity 에디터 Play 모드 필요
- [ ] (추후) 실제 상품 시스템 설계 시 이 패널 내부에 콘텐츠 추가
