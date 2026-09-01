# 메인메뉴 고정형 레이아웃 전환 구현 계획

> 상위 문서: [메인메뉴 슬라이드/탭 구현 계획](./plan-mainmenuscene-tabslide.md) (이 문서가 만든 4탭 `ScrollRect` 슬라이드 구조를 대체 — 구현 완료 후 [tabslide-dotween](./plan-mainmenuscene-tabslide-dotween.md)/[tabslide-selectedscale](./plan-mainmenuscene-tabslide-selectedscale.md)와 함께 `deprecated/`로 이동 대상)
> 범위: MainMenu 씬을 슬라이드 4탭 구조에서 고정형 구조로 전환한다 — "모험" 탭 콘텐츠(1인모드/정글탐험 버튼, 덱 미리보기)를 화면에 상시 고정 표시하고, "친구"/"상품"은 버튼 클릭 시 뜨는 패널로, "미정" 탭은 완전히 제거한다. 각 패널 내부의 실제 콘텐츠 로직 변경(친구 교체 로직 자체, 상품 판매 로직)은 다루지 않는다 — 진입 방식(슬라이드 페이지 → 패널)만 바꾼다.

---

## 배경 / 문제 인식

[plan-mainmenuscene-tabslide.md](./plan-mainmenuscene-tabslide.md)가 만든 4탭 `ScrollRect` 슬라이드 구조는 상품/친구/모험/미정 4페이지를 좌우로 넘기는 방식이었다. 요구사항 변경으로 이 구조를 버리고:

- **모험**: 메인메뉴의 유일한 고정 화면이 된다 (1인모드/정글탐험 버튼 + 덱 미리보기 유지)
- **친구**, **상품**: 모험 화면 위에서 버튼으로 열고 닫는 패널이 된다
- **미정**: 콘텐츠가 없는 자리표시자였으므로 제거한다

마침 [plan-uimanager-popupstack.md](../../01-core-systems/uimanager/plan-uimanager-popupstack.md)가 만든 `UIPanel`/`UIManager.Show<T>`/`HideTop`/`HideAll`/`Register<T>`(팝업 스택 + 백버튼 자동 처리 + 씬 배치 패널 등록)이 이미 있으나 실제 소비자가 없었다 — 이번 전환이 그 첫 실사용처가 된다.

---

## 변경 요약

```
[Before: 슬라이드 4탭]                    [After: 고정형]
Canvas                                    Canvas
└─ TabSlide                               ├─ AdventureView (상시 고정)
   ├─ Tabs(4)                             │  ├─ Mode Solo / Mode Battle
   └─ ScrollView(스냅 스크롤)              │  ├─ DeckPreview(3장)
      └─ Content                          │  ├─ [친구] 버튼 → FriendPanel
         ├─ 상품(빈 자리표시자)            │  ├─ [상품] 버튼 → StorePanel
         ├─ 친구(FriendTabController)      │  └─ 옵션/공용 닫기 버튼(같은 자리, 패널 열림 여부로 토글)
         ├─ 모험(Solo/Battle)              ├─ FriendPanel (UIPanel, 씬 배치, 자가 Register)
         └─ 미정(빈 자리표시자)            ├─ StorePanel (UIPanel, 씬 배치, 자가 Register, 신규 스텁)
                                           └─ MainMenuHudView (변경 없음)
```

---

## 하위 문서

| # | 문서 | 다루는 것 |
|---|------|-----------|
| 1 | [plan-mainmenuscene-fixedlayout-friendpanel.md](./plan-mainmenuscene-fixedlayout-friendpanel.md) | `FriendTabController` → `FriendPanel`(`UIPanel`) 전환, MainMenu 씬에 배치한 채 자가 `Register` |
| 2 | [plan-mainmenuscene-fixedlayout-storepanel.md](./plan-mainmenuscene-fixedlayout-storepanel.md) | `StorePanel`(`UIPanel`) 신규 스텁 — 실제 상품 콘텐츠는 범위 밖 |
| 3 | [plan-mainmenuscene-fixedlayout-slideremoval.md](./plan-mainmenuscene-fixedlayout-slideremoval.md) | `MainMenuTabSlideController`/미정 탭 제거, 모험 콘텐츠 고정 배치, 친구/상품 오픈 버튼과 옵션/공용 닫기 버튼 연결 |

## 작업 순서

1번, 2번은 서로 독립적이라 순서 무관하며 병행 가능하다. 3번은 1·2번이 만든 `FriendPanel`/`StorePanel` 타입을 코드에서 참조하므로 **반드시 마지막**에 진행한다.

---

## 이번 범위에서 제외

- **친구/상품 패널 내부의 실제 도메인 로직 변경** — 친구 교체 규칙, 상품 목록/구매 로직 자체는 다루지 않는다(상품은 애초에 미구현 상태라 신규 스텁만 만든다).
- **탭 전환 애니메이션 관련 후속 문서 정리(`deprecated/` 이동)** — 문서 정리는 별도 작업(`/plan-organize`)으로 이번 구현 완료 후 진행한다.
- **`FriendPanel`/`StorePanel` 동시에 두 개 이상 열렸을 때의 우선순위 조정** — `UIManager` 팝업 스택이 이미 "마지막에 연 것부터 닫힘"을 보장하므로 별도 처리 없이 그대로 둔다.

---

## 구현 후 체크리스트

- [ ] [friendpanel](./plan-mainmenuscene-fixedlayout-friendpanel.md) 구현
- [ ] [storepanel](./plan-mainmenuscene-fixedlayout-storepanel.md) 구현
- [ ] [slideremoval](./plan-mainmenuscene-fixedlayout-slideremoval.md) 구현
- [ ] (추후) `plan-mainmenuscene-tabslide*.md` 3개 문서를 `deprecated/`로 이동
