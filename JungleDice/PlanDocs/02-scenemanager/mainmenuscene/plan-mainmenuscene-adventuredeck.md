# 모험탭 덱 미리보기 구현 계획

> 상위 문서: [메인메뉴 슬라이드/탭 구현 계획](./plan-mainmenuscene-tabslide.md) (`TabSlide`가 정의한 페이지 중 모험/Solo·Battle 탭 페이지 콘텐츠로 파생)
> 관련 문서: [메인메뉴 친구 선택/교체 구현 계획](./plan-mainmenuscene-friendselect.md) (`UserData.Friends`/`SetFriends`를 변경하는 주체), [메인메뉴 유저 정보 HUD 연결 계획](./plan-mainmenuscene-userdata-hud.md) (`UserDataChanged` 구독 → 재바인딩 패턴 재사용)
> 의존 관계: `JungleDice.Core.User.UserManager`/`UserData`(`Friends`), `JungleDice.Core.Event.EventBus`/`UserDataChanged`, `JungleDice.InGame.Friend`(표시 전용 카드 컴포넌트)
> 범위: 모험탭(Solo/Battle 페이지)에 현재 친구 덱 3장을 읽기 전용으로 보여주고, 친구탭에서 덱이 바뀌면 즉시 갱신되게 한다. 새 컴포넌트/씬 오브젝트를 만들지 않고 기존 `MainMenuSceneManager`에 합친다.

---

## 배경 / 문제 인식

친구탭(`FriendTabController`)에서 `UserData.SetFriends`로 덱을 바꾸면 `UserDataChanged`가 발행되고 친구탭 자신의 슬롯은 즉시 갱신된다(`plan-mainmenuscene-friendselect.md`). 하지만 모험탭(Solo/Battle 버튼이 있는 페이지)에는 현재 덱을 보여주는 UI가 아예 없었다 — 실제 대전 시작 시점(`InGameSceneManager`가 `UserManager.Current.Friends`로 덱 빌드)엔 항상 최신값을 읽으므로 게임 로직상 문제는 없지만, 메인메뉴에서 "지금 어떤 카드로 나가는지" 미리 볼 방법이 없었다.

---

## 설계 목표

- 모험탭 진입/앱 실행 시 현재 `UserData.Friends` 3장을 그대로 보여준다
- 친구탭에서 덱이 바뀌면(다른 시스템이 바꿔도) 별도 폴링 없이 이벤트로 즉시 반영한다
- 새 컴포넌트를 만들지 않고 `MainMenuSceneManager`(이미 이 씬의 진입점/버튼 로직을 담당)에 합친다 — 표시만 하는 3칸짜리 뷰를 위해 별도 클래스+구독+Dispose 인프라를 또 만드는 건 과함

---

## 핵심 설계 결정

### 1. 표시는 `Friend`(InGame 표시 전용 컴포넌트) 재사용, `FriendCard`가 아니다

| 후보 | 기각/채택 사유 |
|------|----------------|
| `FriendCard`(이미지+이름+설명+att+hp, 친구탭 카드 목록/교체용) | 기각 — 모험탭 미리보기는 이름/설명까지 보여줄 필요가 없고, 이 정도 정보량이면 필드 카드용 `Friend`로 충분 |
| **`Friend`(이미지+att+hp만, InGame 필드 배치 카드 표시용) 재사용** | **채택** — 이미 `SetKey(int)`로 이미지/att/hp만 그리는 표시 전용 컴포넌트가 있어 그대로 재사용. 드래그/핸드슬롯 책임(`FriendCardBattleControl` 상당)은 붙이지 않는다 — 모험탭 미리보기는 상호작용이 없다 |

### 2. `MainMenuHudView`와 동일한 구독 패턴 — `Awake`에서 1회 반영 + `UserDataChanged` 구독

```csharp
[SerializeField] private Friend[] _deckCards; // 모험탭 덱 미리보기 3개, UserData.Friends 인덱스와 1:1

protected override void OnAwake()
{
    // ... 기존 버튼 연결 ...

    RefreshDeckCards();
    _subs.Add(EventBus.Subscribe<UserDataChanged>(_ => RefreshDeckCards()));
}

private void RefreshDeckCards()
{
    var friends = UserManager.Current.Friends; // 항상 3개(UserData 기본값이자 SetFriends 호출부의 불변 조건)
    for (int i = 0; i < _deckCards.Length; i++)
        _deckCards[i].SetKey(friends[i]);
}
```

`MainMenuSceneManager`가 이미 갖고 있던 `_subs`(`CompositeDisposable`)를 그대로 재사용한다 — `OnDestroy()`의 `_subs.Dispose()` 호출도 기존과 동일, 추가 해제 로직 불필요.

### 3. 별도 클래스로 분리하지 않는다

처음엔 `AdventureDeckView`라는 별도 `MonoBehaviour`로 만들었으나, 표시 로직이 3줄짜리 루프 하나뿐이고 이 씬에 이미 진입점 성격의 `MainMenuSceneManager`가 있어 컴포넌트를 쪼갤 이유가 없다는 판단으로 통합했다. 구독/해제 인프라(`_subs`)도 중복 없이 공유한다.

---

## 클래스 구조

```
MainMenuSceneManager : SceneSingleton<MainMenuSceneManager>   (기존 파일 수정)
├── _deckCards : Friend[]                ← [SerializeField] 신규, 모험탭 덱 미리보기 3개
├── OnAwake()                            ← 기존 버튼 연결 뒤에 RefreshDeckCards() 호출 + UserDataChanged 구독 추가
└── RefreshDeckCards()                   ← private 신규, Friends[i] → _deckCards[i].SetKey
```

---

## 파일 구성

```
Assets/
└── Scripts/
    └── MainMenu/
        └── MainMenuSceneManager.cs   ← 기존 파일 수정 (신규 파일 없음)
```

---

## Unity 씬/오브젝트 구성

```
[Scene: MainMenu]
└── Canvas
    └── TabSlide/ScrollView/Content/Page_Adventure (Solo/Battle 버튼이 있는 페이지)
        └── DeckPreview (임의 컨테이너, 레이아웃만)
            ├── Card_0 ~ Card_2 (Friend 컴포넌트가 붙은 인스턴스, 읽기 전용 — 드래그/클릭 불필요)
```

`_deckCards` 배열은 인스펙터에서 `Card_0`~`Card_2`를 `UserData.Friends` 인덱스 순서 그대로 연결한다 — `FriendTabController._slots`와 동일한 관례.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 앱 실행 직후(친구탭 진입 전) | `OnAwake()`가 즉시 `RefreshDeckCards()` 호출 — 기본값(`UserData` 초기 3종)으로 표시 |
| 친구탭에서 슬롯 교체 | `UserData.SetFriends` → `UserDataChanged` 발행 → 모험탭도 같은 이벤트를 구독 중이라 동시에 갱신 |
| 모험탭이 비활성 페이지인 동안 덱 교체 | 페이지 활성 여부와 무관하게 `MainMenuSceneManager`/구독은 항상 살아있으므로 즉시 갱신, 탭 전환 시 이미 최신 상태 |
| `_deckCards` 인스펙터 연결 누락/개수 불일치 | 방어 코드 없음 — `FriendTabController`/`MainMenuHudView`와 동일한 기존 관례(누락 시 `NullReferenceException`으로 즉시 드러남) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 메인메뉴 최초 진입 | 모험탭 `_deckCards` 3장이 `UserData.Friends` 기본값(1004/1016/1019)으로 표시 |
| 2 | 친구탭에서 슬롯 하나 교체 후 모험탭으로 스와이프 | 이동 전에 이미 `UserDataChanged`로 갱신 완료 — 모험탭 진입 시 교체된 카드가 즉시 보임 |
| 3 | 모험탭에 머무른 상태에서 친구탭 로직으로 덱 교체 | 별도 재진입 없이 `_deckCards` 3장이 즉시 갱신 |

---

## 구현 시 주의사항

- **`Friend`는 표시 전용 컴포넌트다** — `FriendCardBattleControl` 같은 드래그 책임을 가진 컴포넌트를 붙이지 않는다(모험탭 미리보기는 상호작용 없음).
- **`_deckCards` 순서 = `UserData.Friends` 인덱스** — `FriendTabController._slots`와 동일한 관례를 유지한다.
- **씬 배치(`DeckPreview`/`Friend` 인스턴스 3개, 인스펙터 연결)는 Unity 에디터 작업** — 코드만으로는 완결되지 않는다.

---

## 구현 후 체크리스트

- [x] `MainMenuSceneManager.cs`: `_deckCards` 필드, `RefreshDeckCards()`, `OnAwake()`에 초기화 호출 + `UserDataChanged` 구독 추가
- [ ] 씬: 모험탭 페이지에 `Friend` 인스턴스 3개 배치, `_deckCards` 인스펙터 연결 — Unity 에디터 작업
- [ ] 테스트 시나리오 3개 검증
