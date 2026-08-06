# InGame 공격 판정(roll attacker/roll target) 계획

> 상위 문서: [InGame 로직 개요](plan-ingame.md) (4단계, [핸드/필드 배치 계획](plan-ingame-handfield.md) 이후)
> 관련 문서: [턴 진행 계획](plan-ingame-turnsystem.md) (`TurnPhase.RollAttacker`/`RollTarget`가 지금은 주사위 값만 로그로 남기는 스텁 — 이번 문서가 실제 동작을 채움), [핸드/필드 배치 계획](plan-ingame-handfield.md) (`FieldSlot`/`Friend`를 이번 문서가 그대로 재사용, `FieldSlot`을 처음으로 `InGameSceneManager`에 배열로 연결)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`, `JungleDice.InGame.Friend`, `JungleDice.InGame.FieldSlot`, `JungleDice.Data.Table.CardTable`(`att`), `JungleDice.Core.GameManager`(`GameState.GameOver` 전이), `DG.Tweening`
> 범위: `RollAttacker`/`RollTarget` 주사위 값(1~6)으로 필드 6칸(전체) 중 공격자/타겟 슬롯을 선택 → 하이라이트(빨강/파랑) + 선택 펀치 스케일 연출 → 공격자가 타겟(또는 타겟 슬롯이 비어있으면 그 진영의 본체)으로 이동해 공격하는 연출. 본체 체력(기본 30)을 두어, 슬롯이 비어 본체가 맞으면 데미지를 주고 0이 되면 `GameState.GameOver`로 전이시킨다. 카드(`Friend`) 대 카드의 실제 피해/사망 판정, 컴퓨터 측 핸드/필드 실제 배치(`_computerDeck` 소비)는 여전히 범위 밖 — 이 문서는 그 둘 사이의 간극(필드가 비어있을 수 있는 상태)을 본체 공격으로 메운다.

---

## 배경

[턴 진행 계획](plan-ingame-turnsystem.md)의 `EnterPhase(RollAttacker)`/`EnterPhase(RollTarget)`는 지금 `Random.Range(1, 7)` 값을 로그로 남기기만 하는 스텁이다. 요청사항은 이 값을 실제 필드 슬롯 선택에 연결하고, 선택된 두 친구가 실제로 "공격하는 것처럼 보이는" 연출을 요구한다.

문제는 [핸드/필드 배치 계획](plan-ingame-handfield.md)이 유저 필드(4/5/6번)만 다루고 컴퓨터 필드(1/2/3번, `oppobase`)는 아직 손대지 않았다는 점이다 — 컴퓨터의 실제 `PlayFriend`는 여전히 로그만 남기는 스텁이므로, 게임을 실행해 봐도 컴퓨터 필드는 항상 비어 있다. 주사위 값이 그대로 슬롯 절대 번호를 가리키는 이상, "롤로 뽑힌 슬롯에 친구가 없는" 상황은 예외가 아니라 지금 시점엔 오히려 흔한 경우다. 요청자 확인 결과:

- **공격자 슬롯이 비어 있으면**: 공격 자체가 성립하지 않으므로 `RollTarget`(타겟 주사위)까지 갈 필요 없이 `RollAttacker`에서 곧바로 턴을 종료한다(요청자 확인) — 애니메이션 없이 턴만 넘어간다.
- **타겟 슬롯이 비어 있으면**: 그 슬롯이 속한 진영의 **본체**를 공격한다. 본체는 기본 체력 30을 가지며, 0이 되면 패배(`GameState.GameOver`)로 이어진다.

이 결정에 따라 이번 문서는 "필드 6칸 전체"를 다뤄야 한다 — 컴퓨터 필드(`oppobase`)에도 `FieldSlot`을 붙이지만, 거기에 실제 `Friend`가 놓이는 것은 여전히 별도 후속 문서(컴퓨터 핸드/필드) 몫이다. 그래서 실전에서는 당분간 컴퓨터 쪽 공격/피격 대부분이 본체 공격으로 귀결되는데, 이는 이번 범위에서 의도된 동작이다(컴퓨터 필드가 채워지면 자연히 카드 대 카드 상황이 늘어난다).

씬에는 이미 각 진영 본체 쪽에 `bastone`(유저)/`bastone (1)`(컴퓨터) 하위로 숫자 텍스트(`Text (TMP)`, 현재 플레이스홀더 값)가 있다 — 정확한 용도가 확인된 것은 아니지만 위치·구조상 본체 체력 표시 용도로 가장 유력하다. 이번 문서가 그 텍스트를 본체 체력 표시로 재사용한다(에디터에서 실제로 그 오브젝트가 맞는지 확인 필요, 아래 "구현 시 주의사항" 참고).

하이라이트는 "material로 만들 것"(요청자 확인, 아직 미제작)이라 이번 문서는 `Friend`에 별도 오버레이 `Image`를 추가하고 코드는 그 오버레이를 켜고 끄는 것과 색을 정하는 것까지만 담당한다 — 실제 아웃라인/글로우 머티리얼을 그 오버레이의 `Material`에 꽂는 작업은 아트가 준비되는 대로 에디터에서 진행(코드 변경 불필요).

---

## 설계 목표

- 주사위 값(1~6)은 필드 절대 번호(1~6)를 그대로 가리킨다 — 별도 매핑 테이블 없음
- `InGameSceneManager`가 `FieldSlot` 6개를 배열로 들고 인덱스로 바로 조회한다 — [핸드/필드 배치 계획](plan-ingame-handfield.md)의 "매니저는 `FieldSlot`을 연결하지 않는다" 결정을 이번 문서에서 뒤집는다(사유는 아래 핵심 설계 결정 1번)
- 하이라이트 on/off와 색 지정은 `Friend`가 스스로 담당 — `InGameSceneManager`는 "누구를, 무슨 색으로"만 지시
- 펀치 스케일(선택 연출 5%/0.2초, 공격자 확대 15%/1초)은 하나의 재사용 가능한 메서드로 처리 — 강도/시간만 매개변수로 다름
- 공격 연출은 순차 진행: 타겟 하이라이트+펀치(0.2초) → 공격자 펀치(1초) → 공격자가 타겟(또는 본체) 위치로 가속 이동 → 타격(주석 처리된 사운드/이펙트 지점) → 등속으로 복귀
- 공격자가 없으면 `RollTarget`까지 갈 이유가 없다 — `RollAttacker`에서 그 즉시 턴을 종료한다(타겟 주사위를 굴리지 않음)
- 카드 대 카드 피해/사망 판정은 여전히 범위 밖 — 애니메이션은 항상 재생되지만 HP가 실제로 깎이는 것은 "타겟 슬롯이 비어 본체를 때리는 경우"뿐
- 본체 체력이 0이 되면 `GameManager.Instance.ChangeState(GameState.GameOver)`로 전이하고 턴 진행을 멈춘다 — 결과 화면 등 그 이후는 범위 밖
- 기존에 이미 구현된 `EnterPhase`/`OnActionButtonClicked`/컴퓨터 자동 진행 구조는 최대한 그대로 두고, `RollAttacker`/`RollTarget`의 내부 동작만 스텁에서 실제 로직으로 교체한다

---

## 핵심 설계 결정

### 1. `InGameSceneManager`가 `FieldSlot` 6개를 배열로 보유 — 인덱스 기반 조회가 처음 필요해짐

[핸드/필드 배치 계획](plan-ingame-handfield.md)은 "각 슬롯이 스스로 점유 여부를 알면 충분하고, 매니저가 중복 상태를 들 필요 없다"고 결정했다. 그 결정은 드롭이 "자기 자신에게" 일어나는 상황(`OnDrop`)에서는 맞지만, 이번 문서는 "주사위 값 N이 가리키는 슬롯을 달라"는 인덱스 기반 조회가 필요하다 — `FieldSlot` 스스로는 다른 슬롯을 모르므로 이 조회는 매니저(또는 별도 레지스트리)만 할 수 있다. 그래서 `_fieldSlots` 배열을 새로 연결한다.

```csharp
[SerializeField] private FieldSlot[] _fieldSlots; // 필드 6칸, 배열 인덱스 0~5 = 절대 번호 1~6 (1/2/3 컴퓨터, 4/5/6 유저)

private FieldSlot GetFieldSlot(int rollValue) => _fieldSlots[rollValue - 1];
```

`oppobase`의 `Image (2)`/`Image (3)`/`Image (4)`에도 `mybase`와 동일하게 `FieldSlot` 컴포넌트를 붙이고 `_index`를 1/2/3으로 지정한다(좌→우 실제 배치 확인 후 배정, `mybase`의 4/5/6과 동일한 절차).

### 2. `Friend` — 하이라이트 오버레이 + 펀치 스케일

```csharp
[SerializeField] private Image _highlightImage; // 카드 전체를 덮는 오버레이, 신규 자식, 기본 비활성화

public void SetHighlight(bool on, Color color)
{
    _highlightImage.color = color;
    _highlightImage.gameObject.SetActive(on);
}

// vibrato를 1로 둬 "커졌다 바로 돌아오는" 단일 펀치로 — 기본값(10)은 여러 번 진동해 목적에 맞지 않음
public void PunchScale(float strength, float duration)
{
    transform.DOKill();
    transform.DOPunchScale(Vector3.one * strength, duration, vibrato: 1, elasticity: 0.3f);
}

public void MoveTo(Vector3 worldPosition, float duration, Ease ease)
{
    transform.DOKill();
    transform.DOMove(worldPosition, duration).SetEase(ease);
}
```

- `_highlightImage`는 `Friend.prefab`에 새로 추가하는 자식(기존 "Eff"와 같은 위치 — 카드 전체를 덮는 크기, 처음엔 비활성화). 아직 전용 머티리얼이 없으므로 당장은 `Image`의 기본 머티리얼로 단색 사각형처럼 보인다 — 머티리얼이 준비되면 그 오버레이의 `Material` 슬롯만 에디터에서 교체하면 되고 코드는 그대로다.
- `PunchScale`/`MoveTo` 모두 `DOKill()`부터 호출 — 이전 턴의 트윈이 남아있는 극단적 케이스(예: 애니메이션 도중 씬 재진입)에서 트윈이 겹치지 않도록 방어.
- `Friend`는 `RectTransform` 기반 UI 오브젝트이므로 `DOMove`는 월드 좌표 기준(`FriendCard.MoveToSlot`과 동일한 방식)으로 동작한다.

### 3. `BaseStone` — 진영 본체, 신규 컴포넌트

체력 표시 전용이 아니라 본체 오브젝트 자체를 대표하는 컴포넌트로 설계한다 — 지금은 체력만 다루지만, 추후 본체 이미지(파괴 연출, 상태별 스프라이트 교체 등)도 이 컴포넌트가 함께 제어할 가능성이 높아 이름과 책임 범위를 처음부터 "체력"이 아니라 "본체"로 잡는다(요청자 확인).

```csharp
public class BaseStone : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _hpText;
    [SerializeField] private int _maxHp = 30;

    public int CurrentHp { get; private set; }

    private void Awake()
    {
        CurrentHp = _maxHp;
        _hpText.text = CurrentHp.ToString();
    }

    public void TakeDamage(int amount)
    {
        CurrentHp = Mathf.Max(0, CurrentHp - amount);
        _hpText.text = CurrentHp.ToString();
    }
}
```

`mybase`/`oppobase`의 `bastone` 하위 기존 `Text (TMP)`에 각각 부착(정확히는 그 상위 오브젝트에 `BaseStone` 컴포넌트를 붙이고 `_hpText`를 연결). `InGameSceneManager`가 두 인스턴스를 들고, 슬롯 인덱스로 어느 쪽인지 판단한다.

```csharp
[SerializeField] private BaseStone _userBase;
[SerializeField] private BaseStone _computerBase;

private BaseStone GetBase(int slotIndex) => slotIndex <= 3 ? _computerBase : _userBase;
```

### 4. 공격 판정 상태: 공격자가 없으면 `RollAttacker`에서 곧바로 턴 종료, 있으면 `RollTarget`으로 진행

공격자가 없는데 `RollTarget`까지 진행하는 것은 의미가 없다(요청자 확인) — `RollAttacker`에서 공격자 슬롯이 비어있는 것으로 확인되면, `RollTarget`으로 넘어가지 않고 그 자리에서 곧바로 턴을 종료한다. 버튼 텍스트/활성화 로직은 [턴 진행 계획](plan-ingame-turnsystem.md)에서 이미 확립된 그대로 유지하되, `RollAttacker`가 상황에 따라 "다음은 `RollTarget`"이 아니라 "턴 종료"로 직접 분기할 수 있다는 점이 새로 추가된다.

```csharp
private FieldSlot _attackerSlot; // 이번 턴에 뽑힌 공격자 슬롯, 비어있으면 null

case TurnPhase.RollAttacker:
    int attackerRoll = Random.Range(1, 7);
    Debug.Log($"[InGame] {_currentOwner} 턴 - 공격 주사위: {attackerRoll}");

    var attackerSlot = GetFieldSlot(attackerRoll);
    _attackerSlot = attackerSlot.IsOccupied ? attackerSlot : null;

    if (_attackerSlot == null)
    {
        // 공격자가 없으면 RollTarget으로 넘어가지 않고 곧바로 턴 종료
        Debug.Log($"[InGame] {_currentOwner} 턴 - 공격자 없음, 턴 종료");
        _actionButtonText.text = "상대 턴";
        _actionButton.interactable = false;
        StartCoroutine(SwitchTurnAfterDelay());
        return; // 아래의 컴퓨터 자동 진행도 걸지 않음 — 이미 턴 종료 코루틴을 시작함
    }

    var attacker = _attackerSlot.GetComponentInChildren<Friend>();
    attacker.SetHighlight(true, Color.red);
    attacker.PunchScale(_selectPunchScale, _selectPunchDuration);

    _actionButtonText.text = "roll target";
    _actionButton.interactable = _currentOwner == TurnOwner.User;
    break;

case TurnPhase.RollTarget:
    int targetRoll = Random.Range(1, 7);
    Debug.Log($"[InGame] {_currentOwner} 턴 - 타겟 주사위: {targetRoll}");

    _actionButtonText.text = "상대 턴";
    _actionButton.interactable = false;
    StartCoroutine(ResolveAttackRoutine(GetFieldSlot(targetRoll)));
    break;
```

- `_attackerSlot`은 인스턴스 필드로 남겨 `RollTarget`(정확히는 `ResolveAttackRoutine`)에서 그대로 참조한다 — 두 단계가 한 턴 안에서 하나의 공격을 이어서 만들기 때문에, [턴 진행 계획](plan-ingame-turnsystem.md)의 "상태는 `InGameSceneManager` 필드로 보관"과 같은 원칙을 그대로 따른다.
- `return`으로 `EnterPhase` 본문 끝의 "컴퓨터 자동 진행" 코드(`if (_currentOwner == TurnOwner.Computer && phase != TurnPhase.RollTarget) StartCoroutine(ComputerAdvanceAfterDelay(phase));`)를 건너뛴다 — 건너뛰지 않으면 `SwitchTurnAfterDelay`와 `ComputerAdvanceAfterDelay`가 동시에 걸려 턴이 이중으로 진행되는 버그가 생긴다.
- 이 분기 이후로는 `RollTarget`이 `_attackerSlot == null`인 채로 시작되는 경로가 없다 — 유저는 버튼이 "상대 턴"으로 바뀌고 비활성화된 상태만 보고, 컴퓨터는 애초에 `ComputerAdvanceAfterDelay`가 걸리지 않으므로 `RollTarget`에 진입하지 않는다.

### 5. 공격 연출 코루틴: `SwitchTurnAfterDelay`를 대체

기존 `RollTarget`은 고정 2초 대기(`SwitchTurnAfterDelay`) 후 턴을 넘겼다. 이제 그 2초는 "공격 연출에 걸리는 실제 시간"으로 대체된다 — 연출이 끝나는 시점이 곧 턴이 넘어가는 시점이다.

```csharp
[SerializeField] private float _selectPunchScale = 0.05f;
[SerializeField] private float _selectPunchDuration = 0.2f;
[SerializeField] private float _attackerPunchScale = 0.15f;
[SerializeField] private float _attackerPunchDuration = 1f;
[SerializeField] private float _moveToTargetDuration = 0.3f;
[SerializeField] private float _moveBackDuration = 0.3f;

// RollAttacker에서 공격자가 없으면 이 코루틴 자체가 시작되지 않으므로, _attackerSlot은 항상 점유된 슬롯이다.
private IEnumerator ResolveAttackRoutine(FieldSlot targetSlot)
{
    var attacker = _attackerSlot.GetComponentInChildren<Friend>();
    var targetFriend = targetSlot.IsOccupied ? targetSlot.GetComponentInChildren<Friend>() : null;

    if (targetFriend != null)
    {
        targetFriend.SetHighlight(true, Color.blue);
        targetFriend.PunchScale(_selectPunchScale, _selectPunchDuration);
        yield return new WaitForSeconds(_selectPunchDuration);
    }

    attacker.PunchScale(_attackerPunchScale, _attackerPunchDuration);
    yield return new WaitForSeconds(_attackerPunchDuration);

    Vector3 originalPosition = attacker.transform.position;
    Vector3 targetPosition = targetFriend != null
        ? targetFriend.transform.position
        : GetBase(targetSlot.Index).transform.position;

    attacker.MoveTo(targetPosition, _moveToTargetDuration, Ease.InQuad); // 서서히 → 빠르게
    yield return new WaitForSeconds(_moveToTargetDuration);

    // 타격음, 타격 이펙트 재생 지점

    if (targetFriend == null)
    {
        int damage = CardTable.Instance.GetAtt(attacker.Key);
        GetBase(targetSlot.Index).TakeDamage(damage);
    }
    // targetFriend != null인 경우 카드 대 카드 피해 판정은 범위 밖 — 연출만 재생

    attacker.MoveTo(originalPosition, _moveBackDuration, Ease.Linear); // 등속 복귀
    yield return new WaitForSeconds(_moveBackDuration);

    attacker.SetHighlight(false, Color.clear);
    if (targetFriend != null) targetFriend.SetHighlight(false, Color.clear); // Unity 오브젝트에 null 전파(?.) 대신 명시적 null 체크

    if (targetFriend == null && GetBase(targetSlot.Index).CurrentHp <= 0)
    {
        Debug.Log($"[InGame] {(targetSlot.Index <= 3 ? "Computer" : "User")} 본체 파괴 — 패배");
        GameManager.Instance.ChangeState(GameState.GameOver);
        yield break; // 턴 교대 없이 종료
    }

    yield return SwitchTurnAfterDelay();
}
```

- `SwitchTurnAfterDelay`는 [턴 진행 계획](plan-ingame-turnsystem.md)에 이미 있는 그대로 재사용(내부에서 `WaitForSeconds(2f)` 후 턴 교대) — 공격자가 없어 `RollAttacker`에서 곧바로 턴이 끝나는 경우와, 공격 연출이 끝난 뒤 두 경우 모두 동일하게 호출해 "연출(또는 스킵) 후 2초 뒤 턴 교대"라는 기존 리듬을 유지한다.
- 이동 트윈은 `WaitForSeconds(duration)`로 페이싱한다 — `MoveToSlot`처럼 `OnComplete` 콜백에 다음 단계를 묶지 않는 이유는, 이 코루틴이 이미 순차적인 여러 단계(하이라이트→펀치→이동→피해→복귀)를 갖고 있어 콜백 체인보다 코루틴 내 순차 `yield`가 흐름을 그대로 코드 순서로 드러내기 때문(핸드/필드 문서의 `DrawHandCardsRoutine`이 `WaitForSeconds(_drawInterval)`로 페이싱하는 것과 같은 관례).
- 본체 파괴 시 `yield break`로 `SwitchTurnAfterDelay`를 건너뛴다 — 게임이 끝났는데 턴이 계속 넘어가면 안 됨.

---

## 클래스 구조

```
Friend (기존 파일 수정, InGame/)
├── _highlightImage : Image [SerializeField]     ← 신규, 하이라이트 오버레이
├── SetHighlight(bool on, Color color)            ← 신규
├── PunchScale(float strength, float duration)    ← 신규
└── MoveTo(Vector3 worldPosition, float duration, Ease ease)  ← 신규

FieldSlot (기존 파일, 변경 없음)
└── (컴퓨터 필드 3장에도 그대로 부착, `_index` = 1/2/3)

BaseStone : MonoBehaviour                         (신규, InGame/)
├── CurrentHp : int { get; }
├── TakeDamage(int amount)
└── _hpText : TextMeshProUGUI / _maxHp : int = 30 [SerializeField]

InGameSceneManager (기존 파일 수정, InGame/)
├── _fieldSlots : FieldSlot[] [SerializeField]           ← 신규, 6개(절대 번호 1~6 순서)
├── _userBase / _computerBase : BaseStone [SerializeField]  ← 신규
├── _selectPunchScale/_selectPunchDuration : float        ← 신규, 0.05f/0.2f
├── _attackerPunchScale/_attackerPunchDuration : float     ← 신규, 0.15f/1f
├── _moveToTargetDuration/_moveBackDuration : float        ← 신규, 0.3f/0.3f
├── _attackerSlot : FieldSlot                              ← 신규, private, 턴 내 상태
├── GetFieldSlot(int rollValue) : FieldSlot                ← 신규, private
├── GetBase(int slotIndex) : BaseStone                      ← 신규, private
├── ResolveAttackRoutine(FieldSlot targetSlot) : IEnumerator  ← 신규, private
└── EnterPhase(RollAttacker/RollTarget) 분기                ← 기존 스텁을 실제 로직으로 교체
```

---

## 파일 구성

```
Assets/Scripts/
└── InGame/
    ├── BaseStone.cs                ← 신규
    ├── Friend.cs                  ← 기존 파일 수정 (하이라이트/펀치/이동 메서드 추가)
    └── InGameSceneManager.cs      ← 기존 파일 수정 (RollAttacker/RollTarget 로직, 신규 필드 추가)
```

`FieldSlot.cs`/`FieldSlot` 사용법은 변경 없음 — 컴퓨터 필드에 인스턴스를 추가 배치할 뿐.

---

## Unity 씬/오브젝트 구성

```
[Assets/Prefabs/Friend.prefab]
└── Friend(루트)
    └── Highlight(신규 자식)              ← 카드 전체를 덮는 크기(Friend 자식 Image와 동일 앵커/크기)
        ├── Image 부착(신규)               ← Material은 당장 비워둠(추후 전용 머티리얼 교체)
        └── 기본 비활성화(m_IsActive: 0)
    Friend.cs의 _highlightImage ← 위 Image 연결

[Scene: InGame.unity, Canvas (1) 하위]
├── Interface > my_area > area(?) > oppobase                 ← Image (2)/(3)/(4)에 FieldSlot 부착(_index 1/2/3)
│                                                                (좌→우 실제 화면 배치 확인 후 배정)
├── mybase > bases                                            ← 기존 FieldSlot 3개(_index 4/5/6), 변경 없음
├── mybase > bastone > (Text (TMP) 보유 오브젝트)               ← BaseStone 부착(_maxHp=30), _hpText 연결 → _userBase
└── oppobase > bastone (1) > (Text (TMP) (1) 보유 오브젝트)     ← BaseStone 부착(_maxHp=30), _hpText 연결 → _computerBase

[IngameSceneManager GameObject]
└── InGameSceneManager.cs
    ├── _fieldSlots  ← [oppobase 1, 2, 3, mybase 4, 5, 6]의 FieldSlot, 배열 순서 = 절대 번호 1~6
    ├── _userBase     ← mybase 쪽 BaseStone
    └── _computerBase ← oppobase 쪽 BaseStone
```

`bastone`/`bastone (1)` 하위 `Text (TMP)`가 실제로 본체 체력 표시 용도인지는 에디터에서 먼저 확인해야 한다(현재 플레이스홀더 값 `38`이 들어있어 다른 용도일 가능성을 완전히 배제할 수 없음) — 아니라면 새 텍스트 오브젝트를 만들어야 한다.

---

## 이번 범위에서 제외

- 카드(`Friend`) 대 카드의 실제 피해 계산·HP 감소·사망(필드에서 제거) — 애니메이션만 재생되고 스탯은 변하지 않음
- 컴퓨터 측 실제 핸드/필드 배치(`_computerDeck` 소비) — 이번 문서는 `oppobase`에 `FieldSlot`만 붙일 뿐, 그 위에 `Friend`를 실제로 놓는 것은 별도 후속 문서
- `GameState.GameOver` 이후의 결과 화면/승패 구분 UI — 상태 전이만 발생시키고 그 이후는 범위 밖(누가 이겼는지는 로그로만 남김)
- 하이라이트 전용 머티리얼 제작(아트 작업) — 코드는 오버레이를 켜고 끄는 것까지만, 실제 비주얼은 머티리얼이 준비된 뒤 에디터에서 교체
- 타격음/타격 이펙트의 실제 재생 — 코드 상 주석으로 지점만 표시
- 컴퓨터 필드가 채워진 이후의 "여러 슬롯 중 전략적으로 고르는" AI 판단 — 지금도 이후로도 순수 주사위 값 그대로 사용
- 공격 애니메이션 도중 유저 조작(드래그 등) 차단 — 버튼은 이미 `RollTarget` 진입 시 비활성화되지만, 핸드 카드 드래그는 별도로 막지 않음(YAGNI, 현재 `PlayFriend` 단계가 아니므로 애초에 핸드 조작 동선과 겹치지 않음)

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 공격자 슬롯이 비어 있음 | `_attackerSlot = null` → `RollAttacker`에서 `RollTarget`으로 넘어가지 않고 그 자리에서 곧장 `SwitchTurnAfterDelay`(주사위는 다시 굴리지 않음) |
| 타겟 슬롯이 비어 있음(공격자는 있음) | 하이라이트/펀치 없이 곧장 공격자 펀치 → 본체 위치로 이동 → `BaseStone.TakeDamage` |
| 타겟 슬롯에 실제 `Friend`가 있음 | 하이라이트+펀치(파랑) 후 공격자가 그 위치로 이동·복귀, HP 변화 없음(범위 밖) |
| 본체 체력이 데미지로 0 이하가 됨 | `Mathf.Max(0, ...)`로 음수 방지, 애니메이션 종료 후 `GameState.GameOver` 전이, 턴 교대 생략 |
| 동일 턴에서 공격자와 타겟이 같은 슬롯(주사위 두 값이 같음) | 별도 방어 없음 — 공격자가 자기 자신을 향해 이동했다 돌아오는 것으로 보임(요청사항에 명시 없음, YAGNI) |
| 연출 코루틴 도중 씬 전환/일시정지 | `MonoBehaviour` 파괴로 코루틴 자동 중단(기존 관례와 동일) — `GameState.Pause` 중 진행 정지는 [턴 진행 계획](plan-ingame-turnsystem.md)에서 이미 알려진 제약으로 범위 밖 |
| `_fieldSlots`/`_userBase`/`_computerBase` 인스펙터 연결 누락 | `NullReferenceException` — 기존 관례와 동일하게 방어 코드 없이 즉시 드러냄 |
| `bastone` 하위 텍스트가 본체 체력 용도가 아닌 것으로 판명 | 별도 텍스트 오브젝트 신설 필요(에디터 작업, 코드 영향 없음) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 유저 필드 4번에 카드가 있는 상태에서 공격 주사위가 4가 나옴 | 4번 슬롯 친구가 빨간 하이라이트 + 0.2초간 5% 확대 |
| 2 | 시나리오 1 이후 타겟 주사위가 유저 필드 5번(카드 있음)이 나옴 | 5번 친구가 파란 하이라이트 + 0.2초 5% 확대 → 0.2초 후 4번 친구가 1초간 15% 확대 → 5번 위치로 가속 이동 → 등속 복귀, HP 텍스트 변화 없음 |
| 3 | 시나리오 1 이후 타겟 주사위가 1(컴퓨터 필드, 비어있음)이 나옴 | 타겟 하이라이트 없이 곧장 4번 친구 1초 확대 → 컴퓨터 본체 위치로 이동 → 컴퓨터 본체 HP가 4번 친구의 `att`만큼 감소, 텍스트 갱신 → 등속 복귀 |
| 4 | 공격 주사위가 빈 슬롯(예: 컴퓨터 필드, 아직 아무것도 없음)을 가리킴 | 버튼이 곧바로 "상대 턴"으로 바뀌며 비활성화 — `RollTarget`(타겟 주사위/"roll target" 버튼 상태)을 거치지 않고 2초 후 턴 교대 |
| 5 | 컴퓨터 본체 HP가 이미 낮은 상태(예: 3)에서 유저가 `att` 5 이상인 친구로 본체를 공격 | HP가 0으로 클램프, 애니메이션 종료 후 콘솔에 "Computer 본체 파괴 — 패배" 로그, `GameManager.CurrentState == GameOver`, 이후 턴 교대 없음 |
| 6 | 컴퓨터 턴에서 동일 시나리오 진행(유저 관여 없이 2초 대기로 자동 진행) | 유저 턴과 동일한 연출·판정이 컴퓨터 소유 친구 기준으로 재생 |
| 7 | `GameState.GameOver` 전이 이후 | 이전 씬(Pause 등)과 달리 씬 전환 없음(`SceneLoader`가 `GameOver`를 매핑하지 않음) — `InGameSceneManager`가 계속 InGame 씬에 남아있지만 더 이상 `EnterPhase` 호출이 없어 정지 |

---

## 구현 시 주의사항

- **`_fieldSlots` 배열 순서 = 절대 번호 1~6**: 순서가 틀리면 주사위 값과 실제 슬롯이 어긋나 엉뚱한 친구가 공격/피격당한다.
- **`bastone` 하위 텍스트가 본체 체력용인지 에디터에서 먼저 확인**: 플레이스홀더 값(`38`)이 다른 용도일 가능성이 있으므로, 실제로 연결하기 전에 씬에서 확인.
- **하이라이트 색은 지금은 `Color.red`/`Color.blue`를 그대로 오버레이 `Image.color`에 대입**: 전용 머티리얼이 준비되면 셰이더가 자체적으로 색을 다르게 표현할 수도 있으므로, 그 시점에 이 부분을 다시 검토(지금은 최소 구현).
- **`PunchScale`은 `vibrato: 1`로 단일 펀치를 만든다**: 기본값(10)을 그대로 쓰면 여러 번 진동해 "0.2초간 5% 확대"라는 요청과 다른 느낌이 된다.
- **`RollAttacker`/`ResolveAttackRoutine` 모두 기존 `SwitchTurnAfterDelay`를 그대로 재사용**: 새 "턴 종료" 경로를 따로 만들지 않고 [턴 진행 계획](plan-ingame-turnsystem.md)의 기존 함수를 호출 — 턴 교대 로직이 여러 곳에 중복되지 않도록.
- **공격자가 없을 때 `RollAttacker`의 `return`을 빠뜨리면 안 됨**: `return` 없이 `SwitchTurnAfterDelay`만 호출하면, 컴퓨터 턴일 경우 `EnterPhase` 끝의 자동 진행 코드가 `ComputerAdvanceAfterDelay`까지 함께 걸어 턴이 이중으로 진행되는 버그가 생긴다.
- **본체 파괴 시 `SwitchTurnAfterDelay`를 호출하지 않고 `yield break`**: 호출해버리면 게임오버 이후에도 턴이 계속 진행되는 버그가 생긴다.
- **`Friend.MoveTo`/`PunchScale`은 `DOKill()`부터 호출**: 같은 프레임에 이전 트윈이 남아있으면 겹쳐서 튐 — 특히 같은 슬롯이 연속으로 공격자/타겟이 되는 극단적 케이스 방어.
- **`CardTable.Instance.GetAtt(attacker.Key)`로 데미지 산출**: `Friend`가 이미 `Key`를 갖고 있으므로 별도로 공격력을 캐싱할 필요 없음.

---

## 구현 후 체크리스트

- [ ] `Friend.cs`: `_highlightImage` 필드, `SetHighlight`/`PunchScale`/`MoveTo` 추가
- [ ] `BaseStone.cs` 신규 작성 (`Assets/Scripts/InGame/`)
- [ ] `InGameSceneManager.cs`: `_fieldSlots`/`_userBase`/`_computerBase`/펀치·이동 시간 필드 6종 추가, `GetFieldSlot`/`GetBase`/`ResolveAttackRoutine` 추가, `EnterPhase`의 `RollAttacker`/`RollTarget` 분기 교체
- [ ] `Friend.prefab`에 `Highlight` 자식(Image, 기본 비활성화) 추가 + 인스펙터 연결 (Unity 에디터 작업)
- [ ] `oppobase`의 `Image (2)/(3)/(4)`에 `FieldSlot` 부착(_index 1/2/3) (Unity 에디터 작업)
- [ ] `mybase`/`oppobase`의 `bastone` 하위 텍스트 오브젝트 확인 후 `BaseStone` 부착 + `_hpText` 연결 (Unity 에디터 작업)
- [ ] `IngameSceneManager`에 `_fieldSlots`(6개, 순서 확인)/`_userBase`/`_computerBase` 인스펙터 연결 (Unity 에디터 작업)
- [ ] 테스트 시나리오 7개 검증 (특히 #3, #5: 본체 피해·게임오버 전이)
- [ ] (추후) 하이라이트 전용 머티리얼 제작 후 `Highlight` 오버레이의 `Material` 교체 (아트 작업, 코드 변경 없음)
- [ ] (추후) 컴퓨터 핸드/필드(1/2/3번) 실제 배치를 다루는 별도 계획 문서
- [ ] (추후) 카드 대 카드 피해/사망 판정을 다루는 후속 계획 문서
- [ ] (추후) `GameState.GameOver` 결과 화면
