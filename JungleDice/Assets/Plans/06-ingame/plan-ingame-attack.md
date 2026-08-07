# InGame 공격 판정(roll attacker/roll target) 계획

> 상위 문서: [InGame 로직 개요](plan-ingame.md) (4단계, [핸드/필드 배치 계획](plan-ingame-handfield.md) 이후)
> 관련 문서: [턴 진행 계획](plan-ingame-turnsystem.md) (`TurnPhase.RollAttacker`/`RollTarget`가 지금은 주사위 값만 로그로 남기는 스텁 — 이번 문서가 실제 동작을 채움), [핸드/필드 배치 계획](plan-ingame-handfield.md) (`FieldSlot`/`Friend`를 이번 문서가 그대로 재사용, `FieldSlot`을 처음으로 `InGameSceneManager`에 배열로 연결)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`, `JungleDice.InGame.Friend`, `JungleDice.InGame.FieldSlot`, `JungleDice.Data.Table.CardTable`(`att`/`hp`), `JungleDice.Core.GameManager`(`GameState.GameOver` 전이), `DG.Tweening`
> 범위: `RollAttacker`/`RollTarget` 주사위 값(1~6)으로 필드 6칸(전체) 중 공격자/타겟 슬롯을 선택 → 하이라이트(빨강/파랑) + 선택 펀치 스케일 연출 → 공격자가 타겟(또는 타겟 슬롯이 비어있으면 그 진영의 본체)으로 이동해 공격하는 연출. 타겟이 실제 `Friend`면 서로의 공격력만큼 쌍방 피해를 주고받고, 체력이 0 이하가 되면 필드에서 제거(사망)한다. 공격자·타겟이 동일한 카드(공격/타겟 주사위가 같은 슬롯)면 서로 공격하는 대신 공격력이 2배로 오르는 어드밴티지를 받는다. 타겟 슬롯이 비어 본체가 맞으면 데미지를 주고, 본체 체력(기본 30)이 0이 되면 `GameState.GameOver`로 전이시킨다. 컴퓨터 측 핸드/필드 실제 배치(`_computerDeck` 소비), 합체 판정(`CardCondition`/`CardTarget`)은 범위 밖.

---

## 배경

[턴 진행 계획](plan-ingame-turnsystem.md)의 `EnterPhase(RollAttacker)`/`EnterPhase(RollTarget)`는 지금 `Random.Range(1, 7)` 값을 로그로 남기기만 하는 스텁이다. 요청사항은 이 값을 실제 필드 슬롯 선택에 연결하고, 선택된 두 친구가 실제로 "공격하는 것처럼 보이는" 연출과 함께 실제 피해를 주고받는 판정을 요구한다.

문제는 [핸드/필드 배치 계획](plan-ingame-handfield.md)이 유저 필드(4/5/6번)만 다루고 컴퓨터 필드(1/2/3번, `oppobase`)는 아직 손대지 않았다는 점이다 — 컴퓨터의 실제 `PlayFriend`는 여전히 로그만 남기는 스텁이므로, 게임을 실행해 봐도 컴퓨터 필드는 항상 비어 있다. 주사위 값이 그대로 슬롯 절대 번호를 가리키는 이상, "롤로 뽑힌 슬롯에 친구가 없는" 상황은 예외가 아니라 지금 시점엔 오히려 흔한 경우다. 요청자 확인 결과:

- **공격자 슬롯이 비어 있으면**: 공격 자체가 성립하지 않으므로 `RollTarget`(타겟 주사위)까지 갈 필요 없이 `RollAttacker`에서 곧바로 턴을 종료한다(요청자 확인) — 애니메이션 없이 턴만 넘어간다.
- **타겟 슬롯이 비어 있으면**: 그 슬롯이 속한 진영의 **본체**를 공격한다. 본체는 기본 체력 30을 가지며, 0이 되면 패배(`GameState.GameOver`)로 이어진다.
- **타겟 슬롯에 실제 `Friend`가 있으면**: 무조건 쌍방 피해 — attacker는 target의 공격력만큼, target은 attacker의 공격력만큼 동시에 체력이 깎인다. base와의 교전도 같은 규칙을 타되, base의 공격력이 0으로 고정돼 있어 attacker 쪽 피해가 결과적으로 0이 될 뿐이다("base도 마찬가지(하지만 base의 기본 공격력은 0)"). 체력이 0 이하가 된 쪽은 필드에서 제거(사망)하는데, 피해 판정 직후 곧바로 지우면 "맞거나 부딪히는 순간 카드가 툭 사라지는" 것처럼 보이므로, 공격자가 제자리로 돌아오는 복귀 연출까지 재생한 뒤 그 시점에 죽은 쪽(들)을 한번에 제거한다. 죽지 않고 살아남았다면 체력 텍스트 색을 갱신한다 — 고정된 "최초값"이 아니라 이번 피해로 인한 직전 값 대비로 판정해, 올랐으면 초록·떨어졌으면 빨강·변화 없으면 흰색으로 표시한다.
- **공격 주사위와 타겟 주사위가 같은 슬롯을 가리켜 attacker와 target이 동일한 카드가 되는 경우**: 서로 공격하는 상황 자체가 성립하지 않으므로 피해를 주고받지 않는다. 대신 그 카드는 공격력이 2배로 오르는 어드밴티지를 받는다(요청자 확인 — 처음엔 "자기 자신에게 2배 피해"로 잘못 구현했다가, 실제로는 피해가 아니라 공격력이 오르는 보상이라는 점을 확인해 수정). 공격력 텍스트도 체력과 같은 규칙(직전 값 대비 흰/초록/빨강)으로 색이 갱신된다.

이 결정에 따라 이번 문서는 "필드 6칸 전체"를 다뤄야 한다 — 컴퓨터 필드(`oppobase`)에도 `FieldSlot`을 붙이지만, 거기에 실제 `Friend`가 놓이는 것은 여전히 별도 후속 문서(컴퓨터 핸드/필드) 몫이다. 그래서 실전에서는 당분간 컴퓨터 쪽 공격/피격 대부분이 본체 공격으로 귀결되는데, 이는 이번 범위에서 의도된 동작이다(컴퓨터 필드가 채워지면 자연히 카드 대 카드 상황이 늘어난다).

씬에는 이미 각 진영 본체 쪽에 `bastone`(유저)/`bastone (1)`(컴퓨터) 하위로 숫자 텍스트(`Text (TMP)`, 현재 플레이스홀더 값)가 있다 — 정확한 용도가 확인된 것은 아니지만 위치·구조상 본체 체력 표시 용도로 가장 유력하다. 이번 문서가 그 텍스트를 본체 체력 표시로 재사용한다(에디터에서 실제로 그 오브젝트가 맞는지 확인 필요, 아래 "구현 시 주의사항" 참고).

하이라이트는 "material로 만들 것"(요청자 확인, 아직 미제작)이라 이번 문서는 `Friend`에 별도 오버레이 `Image`를 추가하고 코드는 그 오버레이를 켜고 끄는 것과 색을 정하는 것까지만 담당한다 — 실제 아웃라인/글로우 머티리얼을 그 오버레이의 `Material`에 꽂는 작업은 아트가 준비되는 대로 에디터에서 진행(코드 변경 불필요).

---

## 설계 목표

- 주사위 값(1~6)은 필드 절대 번호(1~6)를 그대로 가리킨다 — 별도 매핑 테이블 없음
- `InGameSceneManager`가 `FieldSlot` 6개를 배열로 들고 인덱스로 바로 조회한다 — [핸드/필드 배치 계획](plan-ingame-handfield.md)의 "매니저는 `FieldSlot`을 연결하지 않는다" 결정을 이번 문서에서 뒤집는다(사유는 아래 핵심 설계 결정 1번)
- 하이라이트 on/off와 색 지정은 `Friend`가 스스로 담당 — `InGameSceneManager`는 "누구를, 무슨 색으로"만 지시
- 펀치 스케일(선택 연출 5%/0.2초, 공격자 확대 15%/1초)은 하나의 재사용 가능한 메서드로 처리 — 강도/시간만 매개변수로 다름
- 공격 연출은 순차 진행: 타겟 하이라이트+펀치(0.2초) → 공격자 펀치(1초) → `_attackLayer`로 옮겨 타겟(또는 본체) 위치로 가속 이동 → 타격(주석 처리된 사운드/이펙트 지점, 이 시점에 피해 계산·사망 판정) → 등속으로 복귀 → 원래 슬롯으로 복귀(생존 시) → 복귀가 끝난 뒤 죽은 쪽을 제거
- 공격자는 이동하는 동안 다른 슬롯/카드에 가려지지 않도록 `_attackLayer`(핸드/필드 문서의 `_dragLayer`와 같은 역할)로 잠깐 옮겨졌다가, 살아서 복귀하면 원래 `FieldSlot`의 자식으로 되돌아간다
- 공격자가 없으면 `RollTarget`까지 갈 이유가 없다 — `RollAttacker`에서 그 즉시 턴을 종료한다(타겟 주사위를 굴리지 않음)
- 타겟이 `Friend`면 무조건 쌍방 피해 — attacker는 target의 공격력만큼, target은 attacker의 공격력만큼 동시에 깎인다. `BaseStone`에는 공격력 필드를 두지 않고, 타겟이 `Friend`가 아니면(= base) 상수 0을 쓰는 것으로 "base의 기본 공격력은 0"을 표현한다.
- 공격자와 타겟이 동일한 카드(공격/타겟 주사위가 같은 슬롯을 가리킴)면 서로 공격하지 않고, 대신 공격력이 2배로 오르는 어드밴티지를 받는다 — `targetFriend == attacker` 분기로 명시하며, 이 경우 `TakeDamage`는 호출하지 않는다(피해 없음).
- 사망(체력 ≤ 0)한 쪽은 그 즉시가 아니라, 공격자가 제자리로 돌아오는 복귀 연출이 끝난 시점에 `Destroy`로 필드에서 제거한다 — `FieldSlot.IsOccupied`가 `childCount > 0`로 판정하므로 별도 상태 플래그 없이 자동으로 그 슬롯이 다시 빈 슬롯이 된다. 사망 여부 자체는 피해 판정 직후 확정해 두지만, 실제 제거는 복귀 연출 뒤로 미뤄 "맞는 순간 사라지는" 부자연스러움을 없앤다.
- 체력 텍스트 색상은 `TakeDamage` 호출 직전의 값(직전 값) 대비로 판정하는 재사용 가능한 헬퍼로 처리한다 — 오르면 초록, 떨어지면 빨강, 변화 없으면 흰색. 공격력 텍스트에도 같은 규칙이 적용되도록 일반화해 두지만, 이번 범위에는 공격력을 변화시키는 수단(합체 판정 등)이 없어 실제로 색이 바뀌는 것은 체력뿐이다.
- 본체 체력이 0이 되면 `GameManager.Instance.ChangeState(GameState.GameOver)`로 전이하고 턴 진행을 멈춘다 — 카드 사망은 승패와 무관, 결과 화면 등 그 이후는 범위 밖
- 기존에 이미 구현된 `EnterPhase`/`OnActionButtonClicked`/컴퓨터 자동 진행 구조는 최대한 그대로 두고, `RollAttacker`/`RollTarget`의 내부 동작만 스텁에서 실제 로직으로 교체한다
- 핸드 카드 드래그는 "내 차례의 `PlayFriend`" 상태에서만 시작된다 — `InGameSceneManager.CanPlayFriend`를 `FriendCard.OnBeginDrag`가 참조해, 컴퓨터 턴 전체와 내 턴의 `RollAttacker`/`RollTarget`(공격 연출 도중 포함) 동안에는 드래그 자체가 시작되지 않는다

---

## 핵심 설계 결정

### 1. `InGameSceneManager`가 `FieldSlot` 6개를 배열로 보유 — 인덱스 기반 조회가 처음 필요해짐

[핸드/필드 배치 계획](plan-ingame-handfield.md)은 "각 슬롯이 스스로 점유 여부를 알면 충분하고, 매니저가 중복 상태를 들 필요 없다"고 결정했다. 그 결정은 드롭이 "자기 자신에게" 일어나는 상황(`OnDrop`)에서는 맞지만, 이번 문서는 "주사위 값 N이 가리키는 슬롯을 달라"는 인덱스 기반 조회가 필요하다 — `FieldSlot` 스스로는 다른 슬롯을 모르므로 이 조회는 매니저(또는 별도 레지스트리)만 할 수 있다. 그래서 `_fieldSlots` 배열을 새로 연결한다.

```csharp
[SerializeField] private FieldSlot[] _fieldSlots; // 필드 6칸, 배열 인덱스 0~5 = 절대 번호 1~6 (1/2/3 컴퓨터, 4/5/6 유저)

private FieldSlot GetFieldSlot(int rollValue) => _fieldSlots[rollValue - 1];
```

`oppobase`의 `Image (2)`/`Image (3)`/`Image (4)`에도 `mybase`와 동일하게 `FieldSlot` 컴포넌트를 붙이고 `_index`를 1/2/3으로 지정한다(좌→우 실제 배치 확인 후 배정, `mybase`의 4/5/6과 동일한 절차).

### 2. `Friend` — 하이라이트 오버레이 + 펀치 스케일 + 이동

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

public void SetParent(Transform parent) => transform.SetParent(parent, worldPositionStays: true);
```

- `_highlightImage`는 `Friend.prefab`에 새로 추가하는 자식(기존 "Eff"와 같은 위치 — 카드 전체를 덮는 크기, 처음엔 비활성화). 아직 전용 머티리얼이 없으므로 당장은 `Image`의 기본 머티리얼로 단색 사각형처럼 보인다 — 머티리얼이 준비되면 그 오버레이의 `Material` 슬롯만 에디터에서 교체하면 되고 코드는 그대로다.
- `PunchScale`/`MoveTo` 모두 `DOKill()`부터 호출 — 이전 턴의 트윈이 남아있는 극단적 케이스(예: 애니메이션 도중 씬 재진입)에서 트윈이 겹치지 않도록 방어.
- `Friend`는 `RectTransform` 기반 UI 오브젝트이므로 `DOMove`는 월드 좌표 기준(`FriendCard.MoveToSlot`과 동일한 방식)으로 동작한다.
- `SetParent`는 `worldPositionStays: true`로 고정 — 부모만 바뀌고 현재 화면상 위치는 그대로 유지된다(핸드/필드 문서의 `FriendCard`가 드래그 시작 시 `_dragLayer`로 옮겨갈 때와 동일한 방식). `ResolveAttackRoutine`이 공격 연출 도중 attacker를 `_attackLayer`로 옮겼다가 되돌리는 데 사용한다(아래 결정 6).

### 3. `Friend` — 공격력/체력 상태와 `TakeDamage`

```csharp
public int Key { get; private set; }
public int Att { get; private set; }
public int CurrentHp { get; private set; }
public bool IsDead => CurrentHp <= 0;

public void SetKey(int key)
{
    Key = key;

    var data = CardTable.Instance?.Get(key);
    if (data == null) return; // CardTable.Get이 이미 LogError를 남김

    Att = data.att;
    CurrentHp = data.hp;

    _cardImage.sprite = SpriteManager.GetCard(key.ToString());
    _attText.text = Att.ToString();
    _attText.color = Color.white;
    _hpText.text = CurrentHp.ToString();
    _hpText.color = Color.white;
}

public void TakeDamage(int amount)
{
    int previousHp = CurrentHp;
    CurrentHp = Mathf.Max(0, CurrentHp - amount);
    _hpText.text = CurrentHp.ToString();
    _hpText.color = GetStatColor(CurrentHp, previousHp);
}

public void DoubleAtt()
{
    int previousAtt = Att;
    Att *= 2;
    _attText.text = Att.ToString();
    _attText.color = GetStatColor(Att, previousAtt);
}

// 직전 값 대비로 판정 — 오르면 초록, 떨어지면 빨강, 변화 없으면 흰색(최초값 같은 고정 기준값과 비교하지 않음)
private static Color GetStatColor(int current, int previous)
{
    if (current == previous) return Color.white;
    return current > previous ? Color.green : Color.red;
}
```

- `Friend`가 `Att`/`CurrentHp`를 직접 보유한다 — 지금까지는 `_attText`/`_hpText`에 표시용 문자열만 넣고 실제 값은 `CardTable`에서 그때그때 조회했지만, 피해 누적과 색상 판정에는 "현재 체력"이라는 상태가 필요하다. 캐싱해 둔 `Att` 덕분에 `ResolveAttackRoutine`에서 매번 `CardTable.Instance.GetAtt(attacker.Key)`를 다시 조회할 필요가 없다.
- `CurrentHp`는 `Mathf.Max(0, ...)`로 음수 방지(기존 `BaseStone.TakeDamage`와 동일한 관례).
- 사망 여부는 `IsDead`(`CurrentHp <= 0`)로 노출 — `ResolveAttackRoutine`이 `TakeDamage` 직후 이 값을 읽어 제거 여부를 판단한다.
- `DoubleAtt`는 공격/타겟 주사위가 같은 슬롯을 가리켜 attacker와 target이 동일한 카드일 때 호출된다(아래 결정 6) — 피해를 주고받는 대신 `Att`를 두 배로 올리고, `_attText` 색도 `TakeDamage`와 같은 `GetStatColor` 헬퍼로 갱신한다. 처음엔 이 상황을 "자기 자신에게 2배 피해"로 잘못 구현했었는데, 실제로는 피해가 아니라 공격력이 오르는 어드밴티지라는 점을 확인해 바로잡았다 — 공격력 텍스트 색상 갱신 헬퍼가 실제로 호출되는 유일한 지점이기도 하다(체력 색상 규칙이 애초에 공격력에도 적용되도록 일반화해 둔 이유).
- `GetStatColor`는 "최초 체력" 같은 고정 기준값을 따로 보유하지 않고, 호출 직전의 값(직전 값)과 비교한다 — 여러 번 값이 바뀌어도 "이번에 올랐는지/내려갔는지"만으로 색이 정해진다. `private static` 순수 함수라 인스턴스 상태에 의존하지 않고 `TakeDamage`/`DoubleAtt` 둘 다에서 재사용한다.

### 4. `BaseStone` — 진영 본체, 신규 컴포넌트

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

`BaseStone`에는 `Att` 필드를 두지 않는다 — base는 카드가 아니라서 `CardTable`에 대응하는 행이 없고, "공격력 0"이라는 사실 하나만 있으면 되므로 `ResolveAttackRoutine`의 `else`(타겟이 `Friend`가 아닌 경우) 분기 자체가 그 상수를 대신한다. 필드를 추가하면 "왜 항상 0인 필드가 존재하는가"를 설명해야 하는 불필요한 API가 생긴다.

### 5. 공격 판정 상태: 공격자가 없으면 `RollAttacker`에서 곧바로 턴 종료, 있으면 `RollTarget`으로 진행

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

### 6. 공격 연출 코루틴: 쌍방 피해·사망 판정을 포함해 `SwitchTurnAfterDelay`를 대체

기존 `RollTarget`은 고정 2초 대기(`SwitchTurnAfterDelay`) 후 턴을 넘겼다. 이제 그 2초는 "공격 연출(과 그 안에서 벌어지는 피해/사망 판정)에 걸리는 실제 시간"으로 대체된다 — 연출이 끝나는 시점이 곧 턴이 넘어가는 시점이다.

```csharp
[SerializeField] private Transform _attackLayer; // 공격 연출 도중 attacker가 다른 슬롯/카드 위로 그려지도록 임시로 옮겨가는 레이어(_dragLayer와 같은 역할)
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

    attacker.SetParent(_attackLayer); // 공격 연출 도중 다른 슬롯/카드 위로 그려지도록 임시로 옮김
    attacker.MoveTo(targetPosition, _moveToTargetDuration, Ease.InQuad); // 서서히 → 빠르게
    yield return new WaitForSeconds(_moveToTargetDuration);

    // 타격음, 타격 이펙트 재생 지점

    bool attackerDied = false;
    bool targetDied = false;

    if (targetFriend != null)
    {
        if (targetFriend == attacker)
        {
            // 공격 주사위/타겟 주사위가 같은 슬롯을 가리켜 attacker와 target이 동일한 카드인 경우 — 서로 공격하는 대신 공격력이 2배로 오르는 어드밴티지
            attacker.DoubleAtt();
        }
        else
        {
            // 친구카드끼리는 무조건 쌍방 피해 — attacker는 target의 공격력만큼, target은 attacker의 공격력만큼
            targetFriend.TakeDamage(attacker.Att);
            attacker.TakeDamage(targetFriend.Att);
        }

        targetDied = targetFriend.IsDead;
        attackerDied = attacker.IsDead;
    }
    else
    {
        GetBase(targetSlot.Index).TakeDamage(attacker.Att); // base의 공격력은 0으로 고정 — 되돌아오는 피해 없음
    }

    // 죽더라도 공격자가 제자리로 돌아오는 연출까지는 재생 — 그 뒤에 죽은 쪽을 한번에 제거
    attacker.MoveTo(originalPosition, _moveBackDuration, Ease.Linear); // 등속 복귀
    yield return new WaitForSeconds(_moveBackDuration);

    if (attackerDied)
    {
        Destroy(attacker.gameObject);
    }
    else
    {
        attacker.SetParent(_attackerSlot.transform); // 공격 레이어에서 원래 슬롯으로 복귀
        attacker.SetHighlight(false, Color.clear);
    }

    if (targetFriend != null)
    {
        if (targetDied) Destroy(targetFriend.gameObject);
        else targetFriend.SetHighlight(false, Color.clear);
    }

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
- `attacker.SetParent(_attackLayer)`는 타겟을 향해 이동을 시작하기 직전(펀치 연출 이후, 이동 직전)에 호출한다 — attacker가 자기 `FieldSlot`의 자식으로 남아있으면 다른 슬롯/카드의 그리기 순서(UI sibling index)에 가려질 수 있어, `FriendCard`가 드래그 시작 시 `_dragLayer`로 옮겨가는 것과 같은 이유로 공격 도중에만 임시로 최상위 레이어로 옮긴다.
- 복귀 후 `_attackerSlot.transform`으로 다시 `SetParent`하는 것은 **생존한 경우에만** 한다 — attacker가 죽으면 어차피 `Destroy`될 것이므로 슬롯으로 되돌릴 필요가 없고, 살아남았는데 되돌리지 않으면 `FieldSlot.IsOccupied`(`childCount > 0` 판정)가 그 슬롯을 계속 "비어있음"으로 잘못 판단해 이후 턴의 공격/피격 판정이 전부 어긋난다.
- 이동 트윈은 `WaitForSeconds(duration)`로 페이싱한다 — `MoveToSlot`처럼 `OnComplete` 콜백에 다음 단계를 묶지 않는 이유는, 이 코루틴이 이미 순차적인 여러 단계(하이라이트→펀치→이동→피해→복귀→제거)를 갖고 있어 콜백 체인보다 코루틴 내 순차 `yield`가 흐름을 그대로 코드 순서로 드러내기 때문(핸드/필드 문서의 `DrawHandCardsRoutine`이 `WaitForSeconds(_drawInterval)`로 페이싱하는 것과 같은 관례).
- `attackerDied`/`targetDied`는 `TakeDamage` 직후(타격 시점)에 바로 계산해 변수에 저장해 둔다 — 실제 `Destroy`는 공격자 복귀 연출이 끝난 뒤로 미루지만, "죽었는가"라는 판정 자체는 연출 도중 상태가 바뀔 일이 없으므로 미리 확정해 둔 값을 그대로 쓴다. `targetFriend == null`(base가 타겟)인 경우 둘 다 초기값 `false`를 유지 — base를 때려서 attacker가 죽는 일은 없다. `targetFriend == attacker`(자기 자신)인 경우도 `TakeDamage`를 호출하지 않으므로 `IsDead`는 항상 `false` — 이 경로에서는 죽거나 필드에서 제거되는 일이 없다.
- `MoveTo`(복귀)는 사망 여부와 무관하게 항상 호출한다 — 죽은 공격자도 시각적으로는 "타격 후 제자리로 돌아왔다가" 사라지는 것으로 보인다. target은 애초에 이동하지 않으므로(공격자만 이동) attacker의 복귀 대기(`WaitForSeconds(_moveBackDuration)`) 하나로 두 쪽의 제거 시점을 맞춘다.
- 본체 파괴 시(`targetFriend == null && GetBase(...).CurrentHp <= 0`) `yield break`로 `SwitchTurnAfterDelay`를 건너뛴다 — 게임이 끝났는데 턴이 계속 넘어가면 안 됨. 카드 사망은 이 분기와 무관하다(패배 조건은 여전히 본체 체력뿐).

### 7. 내 차례의 `PlayFriend`가 아니면 핸드 카드 드래그 자체가 시작되지 않음

기존엔 "roll attacker"/"roll target" 단계에서 액션 버튼만 비활성화될 뿐, 핸드의 `FriendCard`는 여전히 드래그 가능한 상태로 남아있었다(원본 문서가 "핸드 조작 동선과 겹치지 않아 YAGNI"로 남겨둔 부분). 요청사항은 "내 차례의 `PlayFriend`가 아니면" 드래그 자체를 막으라는 것이다 — 즉 컴퓨터 턴 전체와, 내 턴이라도 `RollAttacker`/`RollTarget` 단계에서는 카드가 손 안에서 꼼짝하지 않아야 한다.

```csharp
public bool CanPlayFriend => _currentOwner == TurnOwner.User && _currentPhase == TurnPhase.PlayFriend;
```

`FriendCard`는 `InGameSceneManager`를 참조하지 않던 컴포넌트지만, `FieldSlot.OnDrop`이 이미 `InGameSceneManager.Instance.TryPlaceFriendCard(...)`로 싱글턴을 직접 참조하는 것과 같은 방식으로, 드래그 시작 시점에 싱글턴에게 "지금 플레이해도 되는가"를 묻는다.

```csharp
public void OnBeginDrag(PointerEventData eventData)
{
    if (!InGameSceneManager.Instance.CanPlayFriend)
    {
        eventData.pointerDrag = null; // 드래그 자체를 취소 — 이후 OnDrag/OnEndDrag가 이 오브젝트에 호출되지 않음
        return;
    }

    _canvasGroup.blocksRaycasts = false; // 이 카드 자신이 아래 FieldSlot의 레이캐스트를 가로막지 않도록

    transform.SetParent(_dragLayer, worldPositionStays: true);
    transform.SetAsLastSibling();
}
```

- `eventData.pointerDrag = null`은 Unity UI 이벤트 시스템이 드래그를 취소하는 표준적인 방법이다 — `OnBeginDrag`가 호출되는 시점엔 이미 `pointerDrag`가 이 오브젝트로 설정돼 있지만, 그 안에서 `null`로 되돌리면 이후 프레임의 `OnDrag`/`OnEndDrag`가 이 오브젝트에 전달되지 않는다. 별도의 "드래그 가능 여부" 플래그를 두고 `OnDrag`/`OnEndDrag`에서도 매번 체크하는 것보다 시작점 한 곳만 막는 편이 간단하다.
- 이 게이트는 공격 연출 도중(`RollTarget` 진행 중)도 자동으로 포함한다 — `RollTarget`이 진행되는 동안 `_currentPhase`는 `PlayFriend`가 아니므로, 원본 문서가 "공격 애니메이션 도중 드래그 차단"으로 따로 다루려던 범위가 이 게이트 하나로 함께 해결된다.
- `CanPlayFriend`는 `InGameSceneManager`의 다른 프로퍼티(`GetFieldSlot`/`GetBase` 등)와 달리 `public`으로 노출한다 — `FriendCard`가 매니저 외부(다른 스크립트)에 있으므로 `private`으로는 접근할 수 없다.

---

## 클래스 구조

```
Friend (기존 파일 수정, InGame/)
├── _highlightImage : Image [SerializeField]        ← 신규, 하이라이트 오버레이
├── Att : int { get; }                               ← 신규, SetKey에서 CardTable 값으로 세팅
├── CurrentHp : int { get; }                         ← 신규
├── IsDead : bool { get; }                           ← 신규, CurrentHp <= 0
├── SetHighlight(bool on, Color color)                ← 신규
├── PunchScale(float strength, float duration)        ← 신규
├── MoveTo(Vector3 worldPosition, float duration, Ease ease)  ← 신규
├── TakeDamage(int amount)                            ← 신규
├── DoubleAtt()                                        ← 신규, 자기 자신 대상 시 공격력 2배 어드밴티지
├── SetParent(Transform parent)                        ← 신규, worldPositionStays: true
└── GetStatColor(int current, int previous) : Color   ← 신규, private static, 직전 값 대비 판정

FieldSlot (기존 파일, 변경 없음)
└── (컴퓨터 필드 3장에도 그대로 부착, `_index` = 1/2/3)

BaseStone : MonoBehaviour                         (신규, InGame/)
├── CurrentHp : int { get; }
├── TakeDamage(int amount)
└── _hpText : TextMeshProUGUI / _maxHp : int = 30 [SerializeField]

InGameSceneManager (기존 파일 수정, InGame/)
├── _fieldSlots : FieldSlot[] [SerializeField]           ← 신규, 6개(절대 번호 1~6 순서)
├── _attackLayer : Transform [SerializeField]             ← 신규, `_dragLayer`와 같은 역할의 공격 연출용 레이어
├── _userBase / _computerBase : BaseStone [SerializeField]  ← 신규
├── _selectPunchScale/_selectPunchDuration : float        ← 신규, 0.05f/0.2f
├── _attackerPunchScale/_attackerPunchDuration : float     ← 신규, 0.15f/1f
├── _moveToTargetDuration/_moveBackDuration : float        ← 신규, 0.3f/0.3f
├── _attackerSlot : FieldSlot                              ← 신규, private, 턴 내 상태
├── CanPlayFriend : bool { get; }                          ← 신규, public, `FriendCard`가 드래그 시작 시 참조
├── GetFieldSlot(int rollValue) : FieldSlot                ← 신규, private
├── GetBase(int slotIndex) : BaseStone                      ← 신규, private
├── ResolveAttackRoutine(FieldSlot targetSlot) : IEnumerator  ← 신규, private (쌍방 피해/사망 판정 포함)
└── EnterPhase(RollAttacker/RollTarget) 분기                ← 기존 스텁을 실제 로직으로 교체

FriendCard (기존 파일 수정, InGame/)
└── OnBeginDrag(PointerEventData eventData)               ← 수정, `InGameSceneManager.Instance.CanPlayFriend`가 `false`면 드래그 취소
```

---

## 파일 구성

```
Assets/Scripts/
└── InGame/
    ├── BaseStone.cs                ← 신규
    ├── Friend.cs                  ← 기존 파일 수정 (하이라이트/펀치/이동, 공격력/체력 상태, TakeDamage, 색상 헬퍼 추가)
    ├── FriendCard.cs               ← 기존 파일 수정 (`OnBeginDrag`에 `CanPlayFriend` 게이트 추가)
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
├── oppobase > bastone (1) > (Text (TMP) (1) 보유 오브젝트)     ← BaseStone 부착(_maxHp=30), _hpText 연결 → _computerBase
└── AttackLayer(신규, `_dragLayer`와 형제)                      ← 빈 `RectTransform`, Canvas 최상단(다른 필드/카드보다 나중에 그려지도록 형제 목록 맨 아래) — InGameSceneManager._attackLayer에 연결

[IngameSceneManager GameObject]
└── InGameSceneManager.cs
    ├── _fieldSlots    ← [oppobase 1, 2, 3, mybase 4, 5, 6]의 FieldSlot, 배열 순서 = 절대 번호 1~6
    ├── _attackLayer   ← 위 AttackLayer
    ├── _userBase      ← mybase 쪽 BaseStone
    └── _computerBase  ← oppobase 쪽 BaseStone
```

`bastone`/`bastone (1)` 하위 `Text (TMP)`가 실제로 본체 체력 표시 용도인지는 에디터에서 먼저 확인해야 한다(현재 플레이스홀더 값 `38`이 들어있어 다른 용도일 가능성을 완전히 배제할 수 없음) — 아니라면 새 텍스트 오브젝트를 만들어야 한다. 카드 대 카드 피해/사망 판정은 기존 `_attText`/`_hpText` 컴포넌트를 그대로 재사용하므로 이 부분에는 씬/프리팹 변경이 필요 없다. `AttackLayer`는 `_dragLayer`처럼 위치 자체는 의미 없는 빈 컨테이너이므로, 어디에 두든 Canvas 하위에서 다른 카드/슬롯보다 나중에 그려지는 자리(형제 목록 맨 아래)에만 있으면 된다.

---

## 이번 범위에서 제외

- 컴퓨터 측 실제 핸드/필드 배치(`_computerDeck` 소비) — 이번 문서는 `oppobase`에 `FieldSlot`만 붙일 뿐, 그 위에 `Friend`를 실제로 놓는 것은 별도 후속 문서
- `GameState.GameOver` 이후의 결과 화면/승패 구분 UI — 상태 전이만 발생시키고 그 이후는 범위 밖(누가 이겼는지는 로그로만 남김)
- 하이라이트 전용 머티리얼 제작(아트 작업) — 코드는 오버레이를 켜고 끄는 것까지만, 실제 비주얼은 머티리얼이 준비된 뒤 에디터에서 교체
- 타격음/타격 이펙트의 실제 재생 — 코드 상 주석으로 지점만 표시
- 컴퓨터 필드가 채워진 이후의 "여러 슬롯 중 전략적으로 고르는" AI 판단 — 지금도 이후로도 순수 주사위 값 그대로 사용
- ~~공격 애니메이션 도중 유저 조작(드래그 등) 차단 — 버튼은 이미 `RollTarget` 진입 시 비활성화되지만, 핸드 카드 드래그는 별도로 막지 않음(YAGNI, 현재 `PlayFriend` 단계가 아니므로 애초에 핸드 조작 동선과 겹치지 않음)~~ → 아래 결정 7에서 "내 차례의 `PlayFriend`가 아니면 드래그 자체가 시작되지 않음"으로 해결(공격 연출 도중도 자동으로 포함)
- 합체 판정(`CardCondition`/`CardTarget`) — 별도 후속 계획 문서
- 카드 사망 이펙트/사운드, 파괴 애니메이션 — `Destroy` 직전에 아무 연출 없이 즉시 사라짐(위 "타격음/이펙트 재생 지점" 주석과 같은 성격의 후속 작업)
- 카드 사망에 따른 승패 판정 — 패배 조건은 여전히 본체 체력 0뿐, 필드의 카드가 전부 죽어도 게임은 계속됨
- `DoubleAtt`로 오른 공격력이 다음 턴에도 유지되는지에 대한 별도 밸런스 검증 — 지금은 `Att`가 한번 오르면 그 카드가 필드에 남아있는 한 계속 유지된다(별도로 되돌리는 로직 없음), 과도한 상승에 대한 상한선 등은 범위 밖

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 공격자 슬롯이 비어 있음 | `_attackerSlot = null` → `RollAttacker`에서 `RollTarget`으로 넘어가지 않고 그 자리에서 곧장 `SwitchTurnAfterDelay`(주사위는 다시 굴리지 않음) |
| 타겟 슬롯이 비어 있음(공격자는 있음) | 하이라이트/펀치 없이 곧장 공격자 펀치 → 본체 위치로 이동 → `BaseStone.TakeDamage`, attacker는 항상 생존·정상 복귀 |
| 공격자와 타겟 모두 생존(둘 다 `Friend`) | 둘 다 `TakeDamage` 후 체력 텍스트 색만 갱신, 공격자 복귀 후 양쪽 하이라이트 해제 |
| 타겟만 사망 | 공격자가 제자리로 돌아올 때까지 살아있는 것처럼 보이다가, 복귀 완료 시점에 `Destroy(targetFriend.gameObject)` → 해당 슬롯 `IsOccupied`가 자동으로 `false`, attacker는 정상 복귀 후 하이라이트만 해제 |
| 공격자만 사망 | 복귀 애니메이션은 그대로 재생되고, 복귀 완료 시점에 `Destroy(attacker.gameObject)` — "죽는 순간 사라지는" 대신 제자리로 돌아온 뒤 사라지는 것으로 보임 |
| 둘 다 사망 | 서로의 피해량 계산 자체는 죽음과 무관하게 먼저 끝나 있고(둘 다 고정된 `Att` 값으로 서로에게 피해), 공격자 복귀 연출이 끝난 같은 시점에 attacker/target이 함께 `Destroy`됨 |
| 공격 주사위와 타겟 주사위가 같은 슬롯을 가리킴 | `attacker`와 `targetFriend`가 같은 인스턴스 — `targetFriend == attacker` 분기를 타 `DoubleAtt()`만 호출하고 `TakeDamage`는 호출하지 않는다. 피해가 없으므로 `attackerDied`/`targetDied`는 항상 `false`, 정상적으로 복귀 연출 후 하이라이트만 해제(제거되지 않음) |
| base가 타겟, base 체력이 낮아 이번 공격으로 파괴됨 | `Mathf.Max(0, ...)`로 음수 방지, 애니메이션 종료 후 `GameState.GameOver` 전이, 턴 교대 생략. `attackerDied`는 base 분기에서 항상 `false`라 attacker는 정상 복귀 |
| 연출 코루틴 도중 씬 전환/일시정지 | `MonoBehaviour` 파괴로 코루틴 자동 중단(기존 관례와 동일) — `GameState.Pause` 중 진행 정지는 [턴 진행 계획](plan-ingame-turnsystem.md)에서 이미 알려진 제약으로 범위 밖 |
| `_fieldSlots`/`_attackLayer`/`_userBase`/`_computerBase` 인스펙터 연결 누락 | `NullReferenceException` — 기존 관례와 동일하게 방어 코드 없이 즉시 드러냄 |
| `bastone` 하위 텍스트가 본체 체력 용도가 아닌 것으로 판명 | 별도 텍스트 오브젝트 신설 필요(에디터 작업, 코드 영향 없음) |
| 컴퓨터 턴 또는 내 턴의 `RollAttacker`/`RollTarget` 중 핸드 카드를 드래그 시도 | `FriendCard.OnBeginDrag`에서 `CanPlayFriend`가 `false`라 `eventData.pointerDrag = null` → 카드가 손 안에서 전혀 움직이지 않음(부모 변경도, `OnDrag`/`OnEndDrag` 호출도 없음) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 유저 필드 4번에 카드가 있는 상태에서 공격 주사위가 4가 나옴 | 4번 슬롯 친구가 빨간 하이라이트 + 0.2초간 5% 확대 |
| 2 | 시나리오 1 이후 타겟 주사위가 유저 필드 5번(카드 있음, 서로 다른 카드)이 나옴 | 5번 친구가 파란 하이라이트 + 0.2초 5% 확대 → 0.2초 후 4번 친구가 1초간 15% 확대 → 5번 위치로 가속 이동 → 서로의 공격력만큼 쌍방 체력 감소(색상 갱신) → 등속 복귀 → 복귀 완료 시점에 죽은 쪽(있다면) 제거, 생존한 쪽은 하이라이트 해제 |
| 3 | 시나리오 1 이후 타겟 주사위가 1(컴퓨터 필드, 비어있음)이 나옴 | 타겟 하이라이트 없이 곧장 4번 친구 1초 확대 → 컴퓨터 본체 위치로 이동 → 컴퓨터 본체 HP가 4번 친구의 `att`만큼 감소, 텍스트 갱신 → 등속 복귀, attacker는 피해 없음 |
| 4 | 공격 주사위가 빈 슬롯(예: 컴퓨터 필드, 아직 아무것도 없음)을 가리킴 | 버튼이 곧바로 "상대 턴"으로 바뀌며 비활성화 — `RollTarget`(타겟 주사위/"roll target" 버튼 상태)을 거치지 않고 2초 후 턴 교대 |
| 5 | attacker(att 5) vs target(hp 3) | target 체력 3→0, 공격자 복귀 연출이 끝난 뒤 `Destroy`로 필드에서 제거(슬롯 다시 빈 자리), attacker는 target 공격력만큼만 깎이고 생존 시 정상 복귀 |
| 6 | attacker(hp 2) vs target(att 5) | attacker 체력 2→0이지만 제자리로 돌아오는 복귀 연출까지 재생된 뒤 `Destroy`, target은 attacker 공격력만큼 깎여 생존 시 하이라이트 해제 |
| 7 | attacker(hp 2, att 2) vs target(hp 2, att 2) — 서로가 서로를 죽일 조합 | 둘 다 체력 0, 공격자 복귀 연출이 끝나는 시점에 둘 다 동시에 `Destroy`, 양쪽 슬롯 모두 빈 자리로 돌아감, `GameOver` 전이 없음(카드 사망은 승패와 무관) |
| 8 | 공격 주사위와 타겟 주사위가 같은 슬롯(att 4, hp 10)을 가리킴 | 체력 변화 없음(10 유지), 공격력 4→8(초록)로 표시 갱신, 죽지 않고 정상 복귀 |
| 9 | 컴퓨터 본체 HP가 이미 낮은 상태(예: 3)에서 유저가 `att` 5 이상인 친구로 본체를 공격 | HP가 0으로 클램프, 애니메이션 종료 후 콘솔에 "Computer 본체 파괴 — 패배" 로그, `GameManager.CurrentState == GameOver`, 이후 턴 교대 없음 |
| 10 | 컴퓨터 턴에서 시나리오 2~9 재현(유저 관여 없이 자동 진행) | 유저 턴과 동일한 연출·판정이 컴퓨터 소유 친구 기준으로도 동작 |
| 11 | `GameState.GameOver` 전이 이후 | 이전 씬(Pause 등)과 달리 씬 전환 없음(`SceneLoader`가 `GameOver`를 매핑하지 않음) — `InGameSceneManager`가 계속 InGame 씬에 남아있지만 더 이상 `EnterPhase` 호출이 없어 정지 |
| 12 | 유저 턴 `RollAttacker`/`RollTarget` 단계 또는 컴퓨터 턴 전체에서 핸드 카드를 드래그 시도 | 카드가 손 안에서 전혀 움직이지 않음 — `PlayFriend` 단계로 돌아오면(내 턴이 다시 왔을 때) 다시 정상적으로 드래그 가능 |

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
- **데미지 산출은 `attacker.Att`/`targetFriend.Att`로**: `Friend`가 `SetKey` 시점에 `CardTable` 값을 캐싱해 두므로, `ResolveAttackRoutine`에서 `CardTable.Instance.GetAtt(...)`를 매번 다시 조회할 필요가 없다.
- **`attackerDied`/`targetDied`는 `TakeDamage` 직후 바로 계산해 변수에 저장해 둔다**: `Destroy` 자체는 공격자 복귀 연출이 끝난 뒤로 미루지만, "죽었는가"라는 판정은 연출 도중 상태가 바뀔 일이 없으므로 미리 확정해 둔 값을 그대로 쓴다.
- **`Destroy`는 공격자의 복귀 이동(`MoveTo` + `WaitForSeconds(_moveBackDuration)`)이 끝난 뒤에만 호출한다**: 피해 판정 직후 바로 `Destroy`하면 "맞는/부딪히는 순간 사라지는" 것처럼 보인다 — 죽은 쪽이라도 공격자가 제자리로 돌아올 때까지는 그대로 두었다가, 복귀가 끝나는 시점에 죽은 쪽(들)을 한번에 제거한다.
- **죽은 쪽에는 `SetHighlight`를 호출하지 않고 `Destroy`로 대체한다**: 파괴된(또는 파괴 예약된) `GameObject`에 접근하면 `MissingReferenceException`으로 이어진다. attacker는 죽어도 `MoveTo`까지는 항상 호출된다는 점에 주의(target만 이동하지 않으므로 애초에 대상이 아님).
- **base 공격력은 필드로 만들지 않고 `else` 분기의 상수 0으로 표현**: `BaseStone`에 항상 0인 `Att` 필드를 추가하는 것은 불필요한 API — "타겟이 `Friend`가 아니면 되돌아오는 피해가 없다"는 사실을 분기 자체로 표현하는 편이 더 간단하다.
- **`GetStatColor`는 최초값이 아니라 직전 값과 비교한다**: `TakeDamage` 호출 직전의 `CurrentHp`를 넘겨 판정하며, `private static`으로 상태를 갖지 않게 작성한다.
- **공격/타겟 주사위가 같은 슬롯을 가리키는 경우는 `targetFriend == attacker`로 명시적으로 분기한다**: 이 경우는 "자기 자신을 공격해 피해를 입는" 것이 아니라 "서로 공격할 대상이 없어 공격력이 2배로 오르는 어드밴티지"다 — `TakeDamage`가 아니라 `DoubleAtt()`를 호출해야 하며, 반대로 구현하면(자기 자신에게 피해) 약한 카드가 스스로를 공격해 죽는 것처럼 보이는 버그가 된다.
- **`attacker.SetParent(_attackLayer)`는 이동 시작 직전, `attacker.SetParent(_attackerSlot.transform)`는 복귀 이동이 끝난 뒤(그리고 생존한 경우에만) 호출한다**: 순서를 반대로 하거나 죽은 경우에도 슬롯으로 되돌리려 하면 이미 `Destroy` 예약된 오브젝트를 건드리게 되거나, 살아있는데도 슬롯 복귀를 빠뜨려 `FieldSlot.IsOccupied`가 계속 `false`를 반환하는 버그로 이어진다.

---

## 구현 후 체크리스트

- [x] `Friend.cs`: `_highlightImage` 필드, `SetHighlight`/`PunchScale`/`MoveTo`/`SetParent` 추가; `Att`/`CurrentHp`/`IsDead` 추가, `SetKey`에서 세팅, `TakeDamage`/`DoubleAtt`/`GetStatColor`(직전 값 대비) 추가
- [x] `BaseStone.cs` 신규 작성 (`Assets/Scripts/InGame/`)
- [x] `InGameSceneManager.cs`: `_fieldSlots`/`_attackLayer`/`_userBase`/`_computerBase`/펀치·이동 시간 필드 6종 추가, `CanPlayFriend` 프로퍼티 추가, `GetFieldSlot`/`GetBase`/`ResolveAttackRoutine`(쌍방 피해/사망 판정, `_attackLayer` 이동/복귀 포함) 추가, `EnterPhase`의 `RollAttacker`/`RollTarget` 분기 교체
- [x] `FriendCard.cs`: `OnBeginDrag`에 `CanPlayFriend` 게이트 추가(내 차례의 `PlayFriend`가 아니면 드래그 취소)
- [ ] `Friend.prefab`에 `Highlight` 자식(Image, 기본 비활성화) 추가 + 인스펙터 연결 (Unity 에디터 작업)
- [ ] `oppobase`의 `Image (2)/(3)/(4)`에 `FieldSlot` 부착(_index 1/2/3) (Unity 에디터 작업)
- [ ] `mybase`/`oppobase`의 `bastone` 하위 텍스트 오브젝트 확인 후 `BaseStone` 부착 + `_hpText` 연결 (Unity 에디터 작업)
- [ ] `_dragLayer`와 같은 형태로 `AttackLayer` 오브젝트 신설 후 `IngameSceneManager._attackLayer`에 연결 (Unity 에디터 작업)
- [ ] `IngameSceneManager`에 `_fieldSlots`(6개, 순서 확인)/`_attackLayer`/`_userBase`/`_computerBase` 인스펙터 연결 (Unity 에디터 작업)
- [ ] 테스트 시나리오 12개 검증 (특히 #5~#7: 카드 사망·필드 제거, #8: 자기 자신 대상 시 공격력 2배 어드밴티지, #9: 본체 피해·게임오버 전이, #12: 내 턴 아닐 때 드래그 차단)
- [ ] (추후) 하이라이트 전용 머티리얼 제작 후 `Highlight` 오버레이의 `Material` 교체 (아트 작업, 코드 변경 없음)
- [ ] (추후) 컴퓨터 핸드/필드(1/2/3번) 실제 배치를 다루는 별도 계획 문서
- [ ] (추후) `GameState.GameOver` 결과 화면
- [ ] (추후) 합체 판정(`CardCondition`/`CardTarget`)을 다루는 후속 계획 문서
- [ ] (추후) 카드 사망 이펙트/사운드
- [ ] (추후) 카드 사망이 승패에 영향을 주는지 여부(현재는 본체 체력만 승패 기준) 재검토
