# InGame 결과 화면(승패)·내 턴 알림 계획

> 상위 문서: [InGame 로직 개요](plan-ingame.md) (5단계, [공격 판정 계획](plan-ingame-attack.md) 이후)
> 관련 문서: [공격 판정 계획](plan-ingame-attack.md) (`GameState.GameOver` 전이만 시키고 그 이후 UI는 범위 밖으로 남겨둔 지점을 이번 문서가 채움), [턴 진행 계획](plan-ingame-turnsystem.md) (`EnterPhase(PlayFriend)`가 유저 턴 진입점 — 내 턴 알림의 트리거)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`, `JungleDice.Core.GameManager`(`GameState.GameOver`→`MainMenu` 전이), `JungleDice.Core.User.UserManager`(`Icon`), `JungleDice.Core.Sprites.SpriteManager`, `DG.Tweening`
> 범위: 유저 진영 전용 상태 위젯(아이콘/승패 프레임/내 턴 알림/뒤로가기 버튼) 하나를 추가한다. 내 턴이 시작될 때마다 팝업 연출, 본체 파괴로 승부가 갈리면 승/패 프레임을 노출하고 뒤로가기(클릭 또는 20초 자동)로 메인메뉴로 복귀시킨다. 컴퓨터 측 동일 위젯, 합체 판정, 카드 사망 관련 승패는 범위 밖.

---

## 배경

[공격 판정 계획](plan-ingame-attack.md)은 본체 체력이 0이 되면 `GameManager.Instance.ChangeState(GameState.GameOver)`만 호출하고 끝난다 — 승패를 구분해 보여주는 화면도, 메인메뉴로 되돌아가는 수단도 없어 지금은 게임이 끝나도 InGame 씬에 멈춰 선 채로 남는다(`SceneLoader`가 `GameOver`를 씬 매핑에 두지 않으므로, 별도 UI 없이는 그대로 방치됨). 요청사항은 이 빈 자리를 채우는 것과, 그와 별개로 매 턴 "내 턴이 왔다"는 것을 알리는 연출을 요구한다.

요청자 확인 결과, 이 위젯(`backbutton`/`icon`/`my_turn`)은 **유저 진영에만 하나** 둔다 — 컴퓨터 쪽 거울상 위젯은 범위 밖이다. `icon`은 평소엔 꺼져 있다가 승부가 갈린 시점(`ShowResult`)에만 `UserManager.Current.Icon`과 함께 켜지는 결과 전용 프로필 아이콘이고, 그 위에 승리 시 `win`, 패배 시 `lose` 프레임이 얹힌다(둘 다 `icon`의 자식). `UserData.Icon`은 지금까지 코드 어디에서도 실제로 읽힌 적이 없는 필드라 이번 문서가 첫 소비 지점이다 — 아이콘 선택 UI가 아직 없어 기본값이 빈 문자열(`""`)일 수 있으므로, `SpriteManager`가 이미 `GetCard`에서 쓰는 것과 동일한 "못 찾으면 경고 로그 + null 반환" 방어가 그대로 적용된다(크래시 없음, 빈 이미지로만 보임).

`SpriteManager`는 지금 `Card` 카테고리(`Resources/Sprite/Card/{name}`)만 있다 — 이번 문서가 같은 패턴으로 `Icon` 카테고리(`Resources/Sprite/Icon/{name}`)를 추가한다.

승패 판정은 [공격 판정 계획](plan-ingame-attack.md)의 `ResolveAttackRoutine`이 이미 "어느 진영 본체가 파괴됐는지"(`targetSlot.Index <= 3`이면 컴퓨터, 아니면 유저)를 알고 있다 — 이번 문서는 그 시점에 결과를 한 번 기록해 두고, 곧바로 이어지는 `GameStateChanged(Next: GameOver)`에서 그 기록을 읽어 위젯에 반영한다(새 이벤트를 만들지 않고 `InGameSceneManager`의 기존 필드 하나로 충분 — 같은 인스턴스가 값을 쓰고 같은 프레임 안에서 곧바로 읽는다).

---

## 설계 목표

- 새 컴포넌트 `ResultPanel`이 위젯 하나(아이콘/승패 프레임/내 턴 알림/뒤로가기)를 전담 — `InGameSceneManager`는 "언제"만 지시하고 "어떻게 보여줄지"는 `ResultPanel`이 캡슐화(`Friend.SetHighlight`/`PunchScale`과 같은 위임 방식)
- 내 턴 알림은 순수 연출이다 — 상태를 바꾸지 않고, 유저 `PlayFriend` 진입마다 매번 같은 시퀀스를 처음부터 재생(`DOKill()`로 이전 재생 중이면 끊고 다시 시작)
- 승패 판정 자체는 이미 [공격 판정 계획](plan-ingame-attack.md)에서 끝나 있다 — 이번 문서는 "어느 쪽이 이겼는가"라는 `bool` 하나만 `GameOver` 직전에 기록해 재사용할 뿐, 새 판정 로직을 추가하지 않는다
- 뒤로가기는 클릭과 20초 타임아웃 두 경로 모두 같은 메서드(`GoToMainMenu`)로 합류 — `GameManager.ChangeState`가 이미 `CurrentState == next`면 아무 것도 안 하므로 두 경로가 동시에 발생해도 안전하지만, 그래도 타임아웃 코루틴은 클릭 시점에 정지시켜 불필요한 대기를 남기지 않는다
- `SpriteManager`에 `Icon` 카테고리를 추가하되 `Load` 헬퍼는 그대로 재사용 — `Card`와 동일한 폴더 규칙(`Resources/Sprite/{category}/{name}`)

---

## 핵심 설계 결정

### 1. `SpriteManager` — `Icon` 카테고리 추가

```csharp
public enum SpriteCategory
{
    Card,
    Icon,
}

public static Sprite GetIcon(string name) => Load(SpriteCategory.Icon, name);
```

`Load(SpriteCategory, string)`는 이미 카테고리를 폴더 이름으로 매핑하는 범용 헬퍼라 그대로 재사용한다 — `GetCard`와 나란히 `GetIcon`만 추가.

### 2. `ResultPanel` — 유저 진영 상태 위젯 전담, 신규 컴포넌트

```csharp
public class ResultPanel : MonoBehaviour
{
    [SerializeField] private Image _icon;
    [SerializeField] private GameObject _winFrame;
    [SerializeField] private GameObject _loseFrame;
    [SerializeField] private RectTransform _myTurnImage;
    [SerializeField] private Button _backButton;

    [SerializeField] private float _myTurnPunchScale = 1.1f;
    [SerializeField] private float _myTurnPunchDuration = 0.2f;
    [SerializeField] private float _myTurnHoldDuration = 0.5f;
    [SerializeField] private float _myTurnHideDuration = 0.2f;
    [SerializeField] private float _autoExitDelay = 20f;

    private Sequence _myTurnSequence;
    private Coroutine _autoExitRoutine;

    private void Awake()
    {
        _icon.sprite = SpriteManager.GetIcon(UserManager.Current.Icon);
        _icon.gameObject.SetActive(false);
        _myTurnImage.localScale = Vector3.zero;
        _myTurnImage.gameObject.SetActive(false);

        _winFrame.SetActive(false);
        _loseFrame.SetActive(false);
        _backButton.gameObject.SetActive(false);
        _backButton.onClick.AddListener(OnBackButtonClicked);
    }

    // 유저 PlayFriend 진입마다 호출 — 이미 재생 중이면 끊고 처음부터 다시 재생
    public void PlayMyTurnAlert()
    {
        _myTurnSequence?.Kill();
        _myTurnImage.gameObject.SetActive(true);
        _myTurnSequence = DOTween.Sequence()
            .Append(_myTurnImage.DOScale(_myTurnPunchScale, _myTurnPunchDuration))
            .Append(_myTurnImage.DOScale(1f, _myTurnPunchDuration))
            .AppendInterval(_myTurnHoldDuration)
            .Append(_myTurnImage.DOScale(0f, _myTurnHideDuration))
            .OnComplete(() => _myTurnImage.gameObject.SetActive(false));
    }

    // GameOver 전이 시 한 번만 호출
    public void ShowResult(bool userWon)
    {
        _icon.gameObject.SetActive(true);
        (userWon ? _winFrame : _loseFrame).SetActive(true);
        _backButton.gameObject.SetActive(true);
        _autoExitRoutine = StartCoroutine(AutoExitAfterDelay());
    }

    private IEnumerator AutoExitAfterDelay()
    {
        yield return new WaitForSeconds(_autoExitDelay);
        GoToMainMenu();
    }

    private void OnBackButtonClicked()
    {
        if (_autoExitRoutine != null) StopCoroutine(_autoExitRoutine);
        GoToMainMenu();
    }

    private void GoToMainMenu() => GameManager.Instance.ChangeState(GameState.MainMenu);

    private void OnDestroy() => _myTurnSequence?.Kill();
}
```

- `_myTurnImage`는 `Image`가 아니라 `RectTransform`을 받는다 — 스케일만 다루므로 이미지 컴포넌트 자체는 필요 없고, `Friend`/`FriendCard`가 이미 `transform`을 직접 스케일하는 것과 같은 방식.
- `_myTurnImage`도 `icon`/`win`/`lose`와 같은 원칙으로 평소엔 `SetActive(false)`로 꺼둔다 — 스케일만 0으로 둬도 시각적으로는 안 보이지만, 비활성 오브젝트는 레이아웃/레이캐스트 계산에서 아예 제외되므로 더 저렴하다. `PlayMyTurnAlert()` 시작 시 `SetActive(true)`로 켜고, 시퀀스의 마지막 단계(0%로 축소)가 끝나는 `OnComplete`에서 다시 `SetActive(false)`로 끈다.
- `0% → 110% → 100% → (0.5초 유지) → 0%`를 `DOTween.Sequence()` 4단계로 그대로 옮긴다 — 별도 이징 지정 없이 기본 이징 사용(요청사항에 이징 언급 없음, 필요해지면 나중에 `SetEase`만 추가).
- `_myTurnSequence?.Kill()`을 매번 재생 전 호출 — 유저가 매우 짧은 텀으로 연속 턴을 받는 경우(이론상 없음, 방어 차원)에도 시퀀스가 겹치지 않는다(`MainMenuTabSlideController`의 `Tween?.Kill()` 관례와 동일). 재생 도중 `Kill()`로 끊기면 그 시퀀스의 `OnComplete`(비활성화)는 실행되지 않지만, 바로 다음 줄에서 `SetActive(true)`를 다시 호출하므로 상태가 어긋나지 않는다.
- `ShowResult`는 `InGameSceneManager`가 정확히 한 번만 호출한다(`GameOver`는 `GameManager`의 `_validTransitions`상 `InGame`에서만 진입 가능하고 편도이므로 중복 호출 경로 없음) — 그래도 `AutoExitAfterDelay`가 두 번 걸리는 사고를 막기 위해 굳이 가드하지 않는다(YAGNI, 호출부가 한 곳뿐).
- `Awake()`에서 `UserManager.Current.Icon`을 한 번만 읽는다 — 인게임 도중 유저가 아이콘을 바꿀 수단이 없으므로(설정 화면은 InGame 밖) 갱신 이벤트 구독 불필요.
- `_icon`은 `_winFrame`/`_loseFrame`과 마찬가지로 `Awake()`에서 `SetActive(false)`로 시작한다 — 평소엔 보이지 않다가 `ShowResult`에서 함께 켜지는 결과 화면 전용 요소이기 때문. 스프라이트 자체는 `Awake()`에서 미리 `UserManager.Current.Icon`으로 세팅해 두고, 활성화만 `ShowResult` 시점으로 미룬다(매번 다시 로드할 필요 없음).

### 3. `InGameSceneManager` — 결과 기록 + 두 호출 지점 연결

```csharp
[SerializeField] private ResultPanel _resultPanel;

private bool _userWon; // GameOver 직전에 세팅, OnGameStateChanged(GameOver)에서 읽음
```

유저 턴 진입 시 내 턴 알림(기존 `EnterPhase(PlayFriend)`의 유저 분기에 한 줄 추가):

```csharp
case TurnPhase.PlayFriend:
    Debug.Log($"[InGame] {_currentOwner} 턴 - 친구카드 플레이");
    if (_currentOwner == TurnOwner.User)
    {
        DrawHandCards();
        _resultPanel.PlayMyTurnAlert();
    }
    _actionButtonText.text = "roll attacker";
    _actionButton.interactable = _currentOwner == TurnOwner.User;
    break;
```

본체 파괴 시점에 승패 기록(`ResolveAttackRoutine`의 기존 분기 수정):

```csharp
if (targetFriend == null && GetBase(targetSlot.Index).CurrentHp <= 0)
{
    _userWon = targetSlot.Index <= 3; // 컴퓨터(1~3) 본체가 파괴되면 유저 승리
    Debug.Log($"[InGame] {(targetSlot.Index <= 3 ? "Computer" : "User")} 본체 파괴 — {(_userWon ? "승리" : "패배")}");
    GameManager.Instance.ChangeState(GameState.GameOver);
    yield break;
}
```

`OnGameStateChanged`(기존, 지금은 `Pause` 전이만 다룸)에 분기 추가:

```csharp
private void OnGameStateChanged(GameStateChanged e)
{
    if (e.Next == GameState.Pause)
    {
        // ShowPauseOverlay();
    }
    else if (e.Previous == GameState.Pause && e.Next == GameState.InGame)
    {
        // HidePauseOverlay();
    }
    else if (e.Next == GameState.GameOver)
    {
        _resultPanel.ShowResult(_userWon);
    }
}
```

- `_userWon`은 `GameOver` 전이가 편도(다시 `InGame`으로 돌아오지 않고 `MainMenu`로만 나감)이므로 리셋할 필요가 없다 — 다음 판은 씬이 다시 로드되며 `InGameSceneManager` 인스턴스 자체가 새로 생성된다.
- `_resultPanel.PlayMyTurnAlert()`는 `DrawHandCards()` 바로 다음 줄에 추가 — [핸드/필드 배치 계획](plan-ingame-handfield.md)이 세운 "유저 `PlayFriend` 진입마다 한 번" 지점을 그대로 재사용한다.

---

## 클래스 구조

```
SpriteManager (기존 파일 수정, Core/Sprites/)
├── SpriteCategory.Icon                          ← 신규 enum 값
└── GetIcon(string name) : Sprite                ← 신규, GetCard와 동일 패턴

ResultPanel : MonoBehaviour                      (신규, InGame/)
├── PlayMyTurnAlert()                            ← 유저 PlayFriend 진입마다 호출
├── ShowResult(bool userWon)                     ← GameOver 전이 시 한 번 호출
├── AutoExitAfterDelay() : IEnumerator            ← private, 20초 후 GoToMainMenu
├── OnBackButtonClicked()                        ← private, 클릭 시 타이머 정지 후 GoToMainMenu
├── GoToMainMenu()                                ← private, GameManager.ChangeState(MainMenu)
└── _icon/_winFrame/_loseFrame/_myTurnImage/_backButton : [SerializeField]

InGameSceneManager (기존 파일 수정, InGame/)
├── _resultPanel : ResultPanel [SerializeField]   ← 신규
├── _userWon : bool                                ← 신규, private, GameOver 직전 기록
├── EnterPhase(TurnPhase.PlayFriend) 유저 분기       ← 기존 코드에 한 줄 추가(_resultPanel.PlayMyTurnAlert())
├── ResolveAttackRoutine 본체 파괴 분기               ← 기존 코드에 한 줄 추가(_userWon 대입)
└── OnGameStateChanged                             ← 기존 메서드에 GameOver 분기 추가
```

---

## 파일 구성

```
Assets/Scripts/
├── Core/Sprites/
│   └── SpriteManager.cs        ← 기존 파일 수정 (Icon 카테고리 추가)
└── InGame/
    ├── ResultPanel.cs           ← 신규
    └── InGameSceneManager.cs    ← 기존 파일 수정 (_resultPanel 연결, 호출 2곳 추가)
```

---

## Unity 씬/오브젝트 구성

```
[Scene: InGame.unity, Canvas (1) 하위, 유저 진영 쪽에 신규 배치]
└── (가칭) PlayerStatus                          ← 신규 GameObject, ResultPanel.cs 부착
    ├── icon                                     ← Image, 기본 비활성화(결과 화면에서만 노출)
    │   ├── win                                  ← Image(win.png), 기본 비활성화
    │   └── lose                                 ← Image(lose.png), 기본 비활성화
    ├── my_turn                                  ← Image(myturn.png), 기본 비활성화(알림 재생 중에만 활성화)
    └── backbutton                               ← Button, 기본 비활성화

[ResultPanel(PlayerStatus) 인스펙터]
├── _icon        ← 위 icon의 Image
├── _winFrame    ← 위 win GameObject
├── _loseFrame   ← 위 lose GameObject
├── _myTurnImage ← 위 my_turn의 RectTransform
└── _backButton  ← 위 backbutton의 Button

[IngameSceneManager GameObject]
└── InGameSceneManager.cs
    └── _resultPanel ← 위 PlayerStatus의 ResultPanel

[Resources 폴더, 신규]
└── Resources/Sprite/Icon/{UserData.Icon 값}.png  ← 유저 아이콘 스프라이트 배치 위치(아이콘 선택 UI가 없어 지금은 빈 문자열일 수 있음 — 최소 하나는 등록해 두면 좋음)
```

`win`/`lose`는 `icon`의 자식이라 `icon`이 꺼지면(현재 범위에서는 꺼질 일 없음) 같이 꺼진다 — 요청사항의 프리팹 구조를 그대로 따른다. `my_turn`은 `icon`의 형제(같은 부모 `PlayerStatus` 하위)로 둔다 — 요청사항 프리팹 구조상 `icon`과 동급.

---

## 이번 범위에서 제외

- 컴퓨터 진영의 거울상 위젯(아이콘/승패 프레임) — 요청자 확인, 유저 쪽 하나만
- 아이콘 선택 UI, `UserData.Icon` 값을 실제로 채워 넣는 화면 — `UserData.Icon`은 여전히 빈 문자열일 수 있고, 이 경우 `SpriteManager.GetIcon`이 경고 로그만 남기고 빈 이미지로 보임(크래시 없음)
- 카드 사망에 따른 승패 판정 — 여전히 본체 체력 0만이 유일한 승패 조건([공격 판정 계획](plan-ingame-attack.md)에서 이미 확정)
- 합체 판정(`CardCondition`/`CardTarget`) — 별도 후속 계획 문서
- 승/패 프레임 등장 시 사운드·추가 연출(컨페티 등) — `SetActive(true)`로 그냥 노출
- `GameState.Pause` 중 20초 타이머의 일시정지 — `Pause`가 InGame 씬 전환 없이 오버레이로만 처리된다는 점은 [턴 진행 계획](plan-ingame-turnsystem.md)에서 이미 알려진 제약이고, `GameOver`는애초에 `Pause`로 전이할 수 없는 상태(`_validTransitions`에 없음)라 이 문제 자체가 발생하지 않음
- `my_turn` 알림 재생 중 유저 조작 제한 — 요청사항의 "이때는 backbutton 사용안함"은 게임 진행 중엔 애초에 `backbutton`이 비활성 상태(`GameOver` 전에는 `ShowResult`가 호출되지 않음)라는 사실을 재확인하는 문장으로 해석, 별도 코드 불필요

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `UserData.Icon`이 빈 문자열(아이콘 미설정) | `SpriteManager.GetIcon("")`이 `Resources.Load` 실패 → 경고 로그 + `null` 반환 → `_icon.sprite = null`로 빈 이미지 표시, 예외 없음 |
| 유저가 매 턴 반복해서 `PlayFriend`에 진입(정상 플로우) | `PlayMyTurnAlert()`가 매번 `DOKill()` 후 처음부터 재생 — 겹쳐 재생되지 않음 |
| 승패 결정 직후 유저가 `backbutton`을 누르지 않고 20초 방치 | `AutoExitAfterDelay`가 자동으로 `GoToMainMenu()` 호출 |
| `backbutton`을 20초 이전에 클릭 | `OnBackButtonClicked`가 `_autoExitRoutine`을 `StopCoroutine`으로 정지 후 즉시 `GoToMainMenu()` — 이후 자동 전이 없음 |
| `GameOver` 전이 시점에 `_userWon`이 세팅되지 않은 경로로 호출됨 | 발생하지 않음 — `GameState.GameOver`로의 전이는 `_validTransitions`상 `InGame`에서만 가능하고, 코드상 유일한 호출부(`ResolveAttackRoutine`의 본체 파괴 분기)가 `ChangeState` 호출 직전에 항상 `_userWon`을 먼저 대입 |
| `_resultPanel`/`_icon`/`_winFrame`/`_loseFrame`/`_myTurnImage`/`_backButton` 인스펙터 연결 누락 | `NullReferenceException` — 기존 관례와 동일하게 방어 코드 없이 즉시 드러냄 |
| `Resources/Sprite/Icon/` 폴더 자체가 없음 | `Resources.Load`가 `null` 반환(폴더 부재는 예외 상황이 아니라 단순 실패) → 위 "빈 문자열" 케이스와 동일하게 처리 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 게임 시작 직후(유저 선공, `StartMatch()` → `EnterPhase(PlayFriend)`) | `icon`/`win`/`lose`는 계속 비활성 상태, `my_turn`만 0%→110%→100%로 확대된 뒤 0.5초 유지, 이후 0%로 축소 |
| 2 | 컴퓨터 턴이 끝나고 다시 유저 턴(`PlayFriend`)으로 돌아옴 | `my_turn` 알림이 동일하게 재생(반복마다 처음부터) |
| 3 | 유저가 컴퓨터 본체를 파괴 | 승리 — `icon`이 켜지며 `win` 프레임 활성화, `lose`는 비활성 유지, `backbutton` 노출 |
| 4 | 컴퓨터가 유저 본체를 파괴 | 패배 — `icon`이 켜지며 `lose` 프레임 활성화, `win`은 비활성 유지, `backbutton` 노출 |
| 5 | 시나리오 3 또는 4 이후 `backbutton` 클릭 | 즉시 `GameState.MainMenu`로 전이, `SceneLoader`가 MainMenu 씬 로드 |
| 6 | 시나리오 3 또는 4 이후 아무 조작 없이 20초 경과 | 자동으로 `GameState.MainMenu`로 전이 |
| 7 | `UserData.Icon`이 빈 문자열인 상태로 게임이 끝남(`ShowResult` 호출) | 콘솔에 `SpriteManager` 경고 로그만 남고 `icon`은 빈 이미지로 켜짐, 이후 정상 진행에 영향 없음 |

---

## 구현 시 주의사항

- **`ResultPanel.Awake()`는 `_myTurnImage.localScale = Vector3.zero`를 명시적으로 강제**: 씬에서 에디터 작업 중 실수로 100%로 남겨두더라도 런타임 시작 시 항상 0%에서 출발하도록.
- **`_userWon` 대입은 반드시 `GameManager.Instance.ChangeState(GameState.GameOver)` 호출 전에**: 순서가 바뀌면 `OnGameStateChanged`가 `EventBus.Publish` 콜백 안에서 곧바로 실행되므로(동기 이벤트), 대입 전 값(이전 판의 결과 또는 기본값 `false`)을 읽어버리는 버그가 생긴다.
- **`PlayMyTurnAlert` 호출 위치는 `DrawHandCards()`와 같은 유저 분기 안**: 컴퓨터 턴에서 호출되면 "내 턴" 알림이 컴퓨터 턴에도 뜨는 버그.
- **`_myTurnSequence`/코루틴은 `OnDestroy`에서 정리**: 게임오버 직후 곧바로 씬이 전환되지는 않지만(`GameOver`는 씬 매핑 없음), `MainMenu` 전이 시 InGame 씬이 언로드되며 `ResultPanel`도 파괴되므로 `DOTween` 시퀀스가 남지 않도록 `Kill()`.
- **`SpriteManager.GetIcon`은 `Card`와 동일하게 예외를 던지지 않는다**: 아이콘 미설정을 정상 상태로 취급 — 별도 `try/catch`나 null 체크를 호출부(`ResultPanel.Awake`)에 추가하지 않는다(`Image.sprite = null`이 이미 안전).

---

## 구현 후 체크리스트

- [ ] `SpriteManager.cs`: `SpriteCategory.Icon` 추가, `GetIcon(string)` 추가
- [ ] `ResultPanel.cs` 신규 작성 (`Assets/Scripts/InGame/`)
- [ ] `InGameSceneManager.cs`: `_resultPanel` 필드 추가, `EnterPhase(PlayFriend)` 유저 분기에 `PlayMyTurnAlert()` 호출 추가, `ResolveAttackRoutine` 본체 파괴 분기에 `_userWon` 대입 추가, `OnGameStateChanged`에 `GameOver` 분기 추가
- [ ] `PlayerStatus` GameObject 신설(`icon`/`win`/`lose`/`my_turn`/`backbutton`) 및 `ResultPanel` 부착 (Unity 에디터 작업)
- [ ] `win.png`/`lose.png`/`myturn.png` 스프라이트를 각 `Image`에 연결 (Unity 에디터 작업)
- [ ] `Resources/Sprite/Icon/` 폴더 신설 및 최소 1개 아이콘 스프라이트 등록 (Unity 에디터 작업)
- [ ] `InGameSceneManager`에 `_resultPanel` 인스펙터 연결 (Unity 에디터 작업)
- [ ] 테스트 시나리오 7개 검증 (특히 #3~#4: 승/패 프레임 구분, #6: 20초 자동 전이)
- [ ] (추후) 컴퓨터 진영 거울상 위젯 별도 계획 문서(필요해지면)
- [ ] (추후) 아이콘 선택 UI 구현 계획 문서
