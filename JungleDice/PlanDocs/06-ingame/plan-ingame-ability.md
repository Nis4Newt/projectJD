# InGame 친구카드 능력(CardCondition 발동 효과) 구현 계획

> 상위 문서: [친구카드 합체 계획](plan-ingame-merge.md) ("이번 범위에서 제외"에 있던 "(추후) `CardCondition`(Merge/Except) 발동 효과" 항목을 요청자 확인 후 범위에 포함시키며 분리한 후속 문서)
> 관련 문서: [친구카드 합체 계획](plan-ingame-merge.md) (`TryPlaceFriendCard`의 병합 분기 직후에 이 문서의 발동 효과 훅을 연결, 까마귀 배수 병합은 그 병합 공식 자체를 이 문서가 확장), [공격 판정 계획](plan-ingame-attack.md) (`Friend.TakeDamage`/사망 판정·제거 지점(`ResolveAttackRoutine`)을 1010/1018의 사망 트리거가 확장 — 이 문서는 그 확장 지점의 설계만 다루고, 실제 `ResolveAttackRoutine` 코드 반영은 이 문서 작업 시 함께 진행)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`(`TryPlaceFriendCard`, `GetFieldSlot`, `_userBase`/`_computerBase`), `JungleDice.InGame.Friend`, `JungleDice.InGame.FieldSlot`, `JungleDice.InGame.BaseStone`, `JungleDice.Data.Table.CardTable`(`cond`/`target`/`explain`)
> 범위: `CardTable` 20종 중 `cond=Merge`인 카드가 병합 성공 시 실제로 발동하는 효과, `cond=Die`인 카드(1018)의 사망 시 부활. `CardTableData`에 아직 없는 `animal`/`sheets` 필드와 `CardCondition.Die` enum 추가도 이 문서에서 선행 처리한다. `target=All`(1004)/`target=Any`(1019) 이종 합체 판정과 드래그 중 병합 가능 슬롯 미리보기(초록 하이라이트)는 `TryPlaceFriendCard`/`FriendCard`를 이미 소유한 [친구카드 합체 계획](plan-ingame-merge.md) 쪽에서 다룬다(그 문서에서 `CardTarget.Any` enum 멤버도 추가). 1012(`sheets=15`)는 전투 능력이 아니라 덱 구성 파라미터라 범위 밖. 컴퓨터 측 카드가 능력을 발동하는 경우는 컴퓨터가 아직 실제로 필드에 카드를 놓지 않아 범위 밖([핸드/필드 배치 계획](plan-ingame-handfield.md)부터 이어지는 기존 제외).

---

## 배경

[친구카드 합체 계획](plan-ingame-merge.md)은 같은 종류 친구카드가 필드에서 겹칠 때 공격력/체력만 누적하는 최소 규칙만 다루고, `CardTable`에 이미 정의된 `cond`(`None`/`Merge`/`Except`)를 이용한 실제 발동 효과는 "상대 필드에서 무작위 대상을 고르는 로직, 플레이어/모험가 체력 시스템이 아직 코드베이스에 없다"는 이유로 후속 문서로 미뤘다. 그 사이 [공격 판정 계획](plan-ingame-attack.md)에서 본체 체력이 `BaseStone`(`_userBase`/`_computerBase`)으로 이미 구현됐고, 요청자 확인 결과 이번 문서에서 남은 인프라(무작위 대상 선택)를 마저 채우고 실제 발동 효과까지 구현한다.

작업 시작 전 코드 조사로 두 가지 선행 이슈를 확인했다:

1. **`CardTable.csv`가 최근 10종에서 20종으로 확장되며 `animal`/`sheets` 컬럼이 추가됐지만, `CardTableData`(`CardTable.cs`)에는 대응 필드가 없다.** `TableBase<T>.PopulateFromText`는 헤더 이름과 일치하는 `public` 필드가 없으면 에러 로그도 없이 그냥 `continue`한다(`TableBase.cs:45-46`) — 지금 상태로는 두 컬럼 값이 조용히 버려진다.
2. **1018(고사리)의 `cond` 값이 `die`인데, `CardCondition` enum에는 `None`/`Merge`/`Except`만 있다.** `TableValueParser.TryParse`가 `Enum.Parse(type, raw, true)` 실패를 잡아 `false`를 반환하면(`TableValueParser.cs:14`), 호출부는 `LogError` 후 그 필드를 기본값(`None`)으로 남긴다(`TableBase.cs:48-53`) — 게임이 죽지는 않지만 "사망 시 부활"이 조용히 동작하지 않는다.

두 이슈 모두 능력을 구현하기 전에 먼저 고쳐야 하는 데이터 계층 문제라 아래 핵심 설계 결정 1번에서 함께 처리한다.

**설계 방향 확정 — 카드 key로도, 이름 붙은 effect enum으로도 분기하지 않는다.** 처음에는 `effect` 컬럼에 `DoubleAtt`/`HalveStats`/`AddAtt`처럼 이름 붙은 값을 넣고 C# enum으로 파싱하는 안을 검토했지만, 요청자 확인 결과 이 방식은 "공격력을 2배로" "체력을 2배로"처럼 **대상 스탯만 다르고 동작은 같은 효과**마다 별도 enum 값과 별도 `switch case`가 필요해진다는 문제가 있었다. 이후 요청자가 실제 사용할 문법을 다음과 같이 확정했다:

| 의미 | 문법 | 예 |
|---|---|---|
| 공격력/체력 증감(성장·저하, `MaxHp`도 함께 변함) | `Att<연산자><값>` / `Hp<연산자><값>`(연산자: `+` `-` `*` `/`) | `Att*2`, `Hp/2`, `Att+1` |
| 데미지(전투와 같은 피해 — 방어막 소모, `MaxHp` 불변) | `dmg+n` | `dmg+2` |
| 회복(고정량, `MaxHp`까지만 — `MaxHp` 자체는 불변) | `heal+n` | `heal+2` |
| 최대치까지 회복 | `heal+max` | `heal+max` |
| 부활/포자감염(카드를 새로 낳음 — 죽었을 때 key/att/hp로 새 카드 생성) | `spawn+key,att=n,hp=n` | `spawn+1010,att=2,hp=2` |

스탯 증감(`Att`/`Hp`)과 전투식 피해/회복(`dmg`/`heal`)을 분리한 것이 핵심이다 — 전자는 카드의 "기본기가 세지거나 약해지는 것"(`MaxHp`도 같이 움직임), 후자는 "그 시점의 체력만 오르내리는 것"(전투 피해와 동일한 성격, 방어막이 막을 수 있고 `MaxHp`는 그대로)이라 서로 다른 코드 경로가 필요해서다. `spawn`은 콤마를 자기 내부 구분자로 쓰므로(`key,att=n,hp=n`) 다른 조각과 콤마로 나열해 섞어 쓰지 않는다 — 지금 20종 중 `spawn`을 쓰는 두 카드(1010/1018) 모두 `spawn` 하나가 `effect`의 전부다. `Shield`/`MultiplierMerge`처럼 값이 필요 없는 상태 변경만 키워드로 남긴다.

`explain` 20종을 실제 동작 단위로 분류하면 아래와 같다. `effect`/`scope`는 이 문서가 `CardTable.csv`에 새로 추가한 컬럼이다(1번 결정 참고) — **카드 key로 분기하지 않고 이 두 컬럼만으로 발동 효과를 완전히 결정**하는 것이 이번 설계의 핵심이다.

| key | cardname | cond | effect | scope | 효과 요약 |
|---|---|---|---|---|---|
| 1000 | 대지의 왕 | Merge | `Att*2` | AllyRandom | 내 필드 무작위 대상 공격력 2배 |
| 1001 | 9개의 다리 | Merge | `Att/2,Hp/2` | EnemyRandom | 상대 필드 무작위 대상 능력치 절반 |
| 1002 | 늪지의 송곳니 | Merge | `dmg+2` | EnemyBase | 상대 본체에 2 데미지 |
| 1003 | 땅의 그림자 | Except | `None` | None | 능력 대상에서 제외(능동 효과 없음) |
| 1005 | 운반자 | Merge | `dmg+1` | EnemyRandom | 상대 필드 무작위 대상에 1 데미지 |
| 1006 | 바다의 수호자 | Merge | `heal+2` | AllyBase | 내 본체 체력 2 회복 |
| 1007 | 물렁한 돌 | Merge | `Shield` | Self | 이 카드에 방어막 1회 |
| 1009 | 아를라스 | Merge | `Hp*2` | AllyRandom | 내 필드 무작위 대상 체력 2배 |
| 1010 | 가짜 풀 | Merge | `spawn+1010,att=2,hp=2` | AllyRandom | 내 필드 무작위 대상에 포자감염 부여 — 그 대상이 죽으면 그 자리에 1010(기본 2/2) 카드 생성 |
| 1011 | 흰머리 | Merge | `MultiplierMerge` | None | 병합 시 필드의 같은 종류 수만큼 배수 병합(4번 결정에서 병합 공식 자체를 처리 — `scope` 미사용) |
| 1013 | 뒤집힌 별 | Merge | `Att+1` | AllyAll | 내 필드 전체 공격력 +1 |
| 1014 | 검은 거인 | Merge | `dmg+1` | EnemyAll | 상대 필드 전체에 1 데미지(1005와 같은 `dmg+1`, `scope`만 다름) |
| 1015 | 루 카르콜 | Merge | `Shield` | AllyAll | 내 필드 전체에 방어막 1회(1007과 같은 `Shield`, `scope`만 다름) |
| 1016 | 표류 잼 | Merge | `Hp+1` | AllyAll | 내 필드 전체 체력 +1 |
| 1017 | 미운 백조 | Merge | `heal+max` | AllyRandom | 내 필드 무작위 대상 체력을 최대치까지 회복 |
| 1018 | 같은 손 | **Die** | `spawn+1018,att=1,hp=1` | None | 사망 시 자기 자신을 1/1로 1회 부활(트리거는 `cond=Die`, `scope`는 자기 자신 고정이라 미사용) |

(1004/1008/1012/1019는 이번 문서에서 다루지 않음 — 위 범위 참고. 이 넷도 `effect`/`scope`를 `None`으로 채워 컬럼을 항상 완전하게 유지한다.)

1005와 1014가 같은 `dmg+1`을, 1007과 1015가 같은 `Shield`를 `scope`만 다르게 쓰는 것에서 보이듯, "무엇을"(스탯/키워드/스폰)과 "누구에게"(`scope`)가 완전히 분리돼 있어 새 카드가 기존 조합을 재사용하면 CSV 값만 채우면 된다.

---

## 설계 목표

- "내 필드"/"상대 필드"는 유저/컴퓨터 고정이 아니라 병합이 실제로 일어난 슬롯 절대 번호(`slotIndex`) 기준으로 판정한다 — `GetBase(int slotIndex)`([공격 판정 계획](plan-ingame-attack.md))와 같은 축(발견 경위는 2번 결정 참고).
- 무작위 대상 선택은 `cond=Except`(1003류) 카드를 항상 후보에서 제외하는 공용 헬퍼 하나로 처리한다.
- `effect`는 스탯 증감(`Att`/`Hp` + 연산자 + 값), 전투식 피해/회복(`dmg`/`heal`), 스폰(`spawn`), 고정 키워드(`Shield`/`MultiplierMerge`) 네 갈래로만 구성된다. C# 쪽은 이 네 갈래를 해석하는 인터프리터 하나만 갖고, "비슷하지만 대상 스탯만 다른" 효과(공격력 2배 vs 체력 2배)나 "값만 다른" 효과(`dmg+1` vs `dmg+2`)를 위해 별도 분기를 늘리지 않는다.
- 발동 효과는 최대한 `Friend`/`BaseStone`의 기존 메서드(`TakeDamage`)를 재사용하고, 새로 추가하는 메서드도 기존 스타일(직전 값 대비 흰/초록/빨강 색상 갱신)을 따른다.
- 능력으로 인한 피해가 대상을 죽일 수 있는 경우, 전투처럼 복귀 연출을 기다리지 않고 그 자리에서 즉시 제거한다 — 능력 발동에는 애초에 이동 연출이 없다.
- `TryPlaceFriendCard`의 병합 분기 이후에 발동 효과를 트리거하는 훅을 하나만 추가한다 — 카드 key로 분기하지 않고 `CardTable`의 `effect`("무엇을")/`scope`("누구에게") 데이터로 분기한다. `InGameSceneManager`는 이 값들을 해석하는 범용 디스패처만 갖고, `Friend`는 여전히 "무엇을 할지"만 알 뿐 "누구에게 할지"는 모른다([공격 판정 계획](plan-ingame-attack.md)의 책임 분리 패턴 재사용).
- 1018/1010처럼 "사망 시점"에 걸리는 효과는 병합 시점 훅과 다른 지점(전투 사망 처리)이 필요하므로, `Friend`에는 상태/조회 API만 두고 실제 트리거 지점은 [공격 판정 계획](plan-ingame-attack.md)의 `ResolveAttackRoutine`을 확장한다. 부활(1018)과 포자감염(1010)은 둘 다 "죽었을 때 key/att/hp로 카드를 새로 낳는다"는 같은 `spawn` 문법을 쓰지만, 부활은 **자기 자신의 `effect`를 자기 사망 시점에** 읽고, 포자감염은 **병합 시점에 다른 대상에게 걸어 둔 예약을(그 대상의 사망 시점에)** 읽는다는 차이가 있다 — 평가 시점이 다를 뿐 "카드를 새로 낳는다"는 동작 자체는 같은 코드 경로(`SpawnFriendDirectly`)를 공유한다.

---

## 핵심 설계 결정

### 1. 데이터 계층 선행 정리 — `CardTableData` 필드 추가, `CardCondition.Die` 추가, `effect` 문법 파서 신설

```csharp
// CardTable.cs
public enum CardCondition
{
    None,
    Merge,
    Except,
    Die,
}

// effect 문법의 대상 스탯 — Att/Hp 두 축만 존재
public enum CardStat
{
    Att,
    Hp,
}

// effect 조각의 종류
public enum CardEffectClauseKind
{
    Stat,       // Att/Hp + 사칙연산 + 값 — 영구적인 스탯 증감(MaxHp도 함께 변함)
    Damage,     // dmg+n — 전투와 같은 피해(방어막 소모, MaxHp 불변)
    Heal,       // heal+n — 고정량 회복(MaxHp까지만, MaxHp 자체는 불변)
    HealToMax,  // heal+max — 최대치까지 회복
    Keyword,    // Shield/MultiplierMerge처럼 값이 없는 상태 변경
    Spawn,      // spawn+key,att=n,hp=n — 부활/포자감염처럼 카드를 새로 생성
}

// effect 조각 하나. Kind로 어떤 필드가 유효한지 정해진다(정적 팩터리로만 생성)
public readonly struct CardEffectClause
{
    public readonly CardEffectClauseKind Kind;
    public readonly CardStat Stat;   // Kind == Stat일 때만 유효
    public readonly char Op;         // Kind == Stat일 때만 유효: '+' '-' '*' '/'
    public readonly int Value;       // Kind == Stat/Damage/Heal일 때만 유효
    public readonly string Keyword;  // Kind == Keyword일 때만 유효: "Shield"/"MultiplierMerge"
    public readonly int SpawnKey;    // Kind == Spawn일 때만 유효
    public readonly int SpawnAtt;    // Kind == Spawn일 때만 유효
    public readonly int SpawnHp;     // Kind == Spawn일 때만 유효

    public static CardEffectClause StatOp(CardStat stat, char op, int value) => ...;
    public static CardEffectClause Damage(int value) => ...;
    public static CardEffectClause Heal(int value) => ...;
    public static CardEffectClause HealToMax() => ...;
    public static CardEffectClause KeywordOf(string keyword) => ...;
    public static CardEffectClause Spawn(int key, int att, int hp) => ...;
}

public class CardTableData : TableDataBase<int>
{
    public int key;
    public string animal;   // 신규 — 지금까지 조용히 버려지던 컬럼
    public string cardname;
    public int sheets;      // 신규 — 지금까지 조용히 버려지던 컬럼(사용은 이번 범위 밖, 필드만 추가)
    public int att;
    public int hp;
    public CardCondition cond;
    public CardTarget target;
    public string effect;            // 신규 — CSV 원본 문자열, 예: "Att*2" / "dmg+1" / "spawn+1010,att=2,hp=2"
    public CardAbilityScope scope;   // 신규
    public string explain;

    // effect 문자열을 파싱한 결과 캐시 — CardTable.OnLoaded()가 채운다
    [NonSerialized] public List<CardEffectClause> EffectClauses;
}

// "누구에게" — 대상 범위
public enum CardAbilityScope
{
    None,
    Self,        // 병합된 이 카드 자신 (예: 1007)
    AllyRandom,  // 내 필드 무작위 한 장
    AllyAll,     // 내 필드 전체
    EnemyRandom, // 상대 필드 무작위 한 장
    EnemyAll,    // 상대 필드 전체
    AllyBase,    // 내 본체
    EnemyBase,   // 상대 본체
}

// "무엇을" 문자열을 해석하는 전용 파서 — 테이블의 다른 컬럼과 달리 TableValueParser의 범용 파싱으로 표현할 수 없어 별도로 둔다
public static class CardEffectParser
{
    private static readonly Regex StatOpPattern = new(@"^(Att|Hp)([+\-*/])(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DamagePattern = new(@"^dmg\+(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HealPattern = new(@"^heal\+(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HealMaxPattern = new(@"^heal\+max$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // spawn 조각은 콤마를 자기 내부 구분자로 쓰므로(부활/포자감염 전용) 전체 문자열 단위로 따로 검사한다
    private static readonly Regex SpawnPattern = new(@"^spawn\+(\d+),att=(\d+),hp=(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 알려진 키워드만 통과시키고 대소문자를 정규화 — CSV에 "shield"처럼 casing이 달라도 항상 같은 문자열로 취급
    private static readonly Dictionary<string, string> KnownKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Shield"] = "Shield",
        ["MultiplierMerge"] = "MultiplierMerge",
    };

    public static List<CardEffectClause> Parse(string raw, int key)
    {
        var result = new List<CardEffectClause>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        var trimmedRaw = raw.Trim();
        if (trimmedRaw.Equals("None", StringComparison.OrdinalIgnoreCase)) return result; // "능력 없음"을 CSV에 명시적으로 적어둔 것

        var spawnMatch = SpawnPattern.Match(trimmedRaw);
        if (spawnMatch.Success)
        {
            result.Add(CardEffectClause.Spawn(
                int.Parse(spawnMatch.Groups[1].Value),
                int.Parse(spawnMatch.Groups[2].Value),
                int.Parse(spawnMatch.Groups[3].Value)));
            return result;
        }

        foreach (var token in trimmedRaw.Split(','))
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0) continue; // 콤마 뒤 빈 조각(트레일링 콤마 등)은 조용히 건너뜀

            var statMatch = StatOpPattern.Match(trimmed);
            var dmgMatch = DamagePattern.Match(trimmed);
            var healMaxMatch = HealMaxPattern.Match(trimmed);
            var healMatch = HealPattern.Match(trimmed);

            if (statMatch.Success)
            {
                var stat = statMatch.Groups[1].Value.Equals("Att", StringComparison.OrdinalIgnoreCase) ? CardStat.Att : CardStat.Hp;
                result.Add(CardEffectClause.StatOp(stat, statMatch.Groups[2].Value[0], int.Parse(statMatch.Groups[3].Value)));
            }
            else if (dmgMatch.Success)
            {
                result.Add(CardEffectClause.Damage(int.Parse(dmgMatch.Groups[1].Value)));
            }
            else if (healMaxMatch.Success)
            {
                result.Add(CardEffectClause.HealToMax());
            }
            else if (healMatch.Success)
            {
                result.Add(CardEffectClause.Heal(int.Parse(healMatch.Groups[1].Value)));
            }
            else if (KnownKeywords.TryGetValue(trimmed, out var canonical))
            {
                result.Add(CardEffectClause.KeywordOf(canonical));
            }
            else
            {
                Debug.LogError($"[Table] CardTableData.effect 알 수 없는 조각(key={key}): '{trimmed}'");
            }
        }
        return result;
    }
}

public class CardTable : TableBase<CardTable, CardTableData, int>
{
    protected override void OnLoaded()
    {
        foreach (var row in Rows)
            row.EffectClauses = CardEffectParser.Parse(row.effect, row.key);
    }

    // ... 기존 Get/GetAtt/GetHp/GetCond/GetTarget/GetExplain은 그대로 유지
}
```

- 필드 이름을 CSV 헤더(`animal`/`sheets`/`effect`/`scope`)와 그대로 맞춘다 — `PopulateFromText`가 대소문자 무시 이름 매칭이라 그 외엔 손댈 곳이 없다(`TableBase.cs:45`). `effect`는 `string` 필드라 `TableValueParser`가 그대로 원본 텍스트를 담아준다 — 문법 해석은 별도로 `CardEffectParser`가 담당한다("테이블 파싱 시 개별적인 기능 필요"라는 요구가 정확히 이 지점).
- `Die`를 `CardCondition` 마지막에 추가한다 — 기존 값(`None=0`/`Merge=1`/`Except=2`)의 정수값이 바뀌면 안 되므로 반드시 끝에 붙인다.
- `spawn+key,att=n,hp=n`은 콤마를 내부 구분자로 쓰므로, 다른 조각과 콤마로 나열해 함께 쓰면 안 된다 — 파서가 전체 문자열을 `SpawnPattern`으로 먼저 통째로 검사하고, 매치되면 그 즉시 단일 `Spawn` 조각만 반환한다(뒤의 콤마 분리 루프로 넘어가지 않음).
- `EffectClauses`는 CSV 로드 직후 `CardTable.OnLoaded()`(기존 `TableBase<T>.Instance` getter가 `Resources.Load` 후 자동 호출하는 훅, `TableBase.cs:78`)에서 전 카드에 대해 한 번만 파싱해 캐싱한다 — 능력 발동마다 문자열을 다시 파싱하지 않고, CSV에 오타가 있으면 게임 시작 시점에 바로 `LogError`로 드러난다(기존 enum 파싱 실패 로그와 같은 시점·같은 성격).
- `MultiplierMerge`는 5번 결정의 발동 효과 디스패처가 아니라 4번 결정(병합 공식 자체)에서만 참조한다 — "발동 효과"가 아니라 "병합 수치 계산 방식"이라 성격이 달라서다. 1011의 `scope`는 그래서 `None`(미사용).
- 능력이 없는 카드(1003/1004/1008/1012/1019)의 `effect` 셀은 빈 문자열이 아니라 리터럴 `None`으로 채운다(요청자 확인) — CSV를 훑어볼 때 "값을 빠뜨린 것"과 "의도적으로 능력이 없는 것"을 구분하기 위함이다. `CardEffectParser.Parse`는 `None`을 빈 문자열과 동일하게(조각을 만들지 않고, 에러도 남기지 않고) 건너뛴다.
- `CardTable.csv`의 실제 컬럼 추가·값 채우기는 이번 문서 구현 시 CSV 파일도 함께 수정한다(위 "능력 분류" 표가 곧 채워 넣을 값).

### 2. 대상 선택 인프라 — 필드 범위 상수 + "내 필드/상대 필드"를 실제 슬롯 위치로 판정 + 무작위 선택(Except 제외)

**버그 수정([치트 에디터](plan-ingame-cheat.md)로 발견)**: "내 필드"=유저 고정, "상대 필드"=컴퓨터 고정으로 설계했으나, 컴퓨터 필드(1~3)에서 병합을 테스트해보니 `EnemyRandom`/`EnemyAll` 효과가 반대로 유저 필드(4~6)에 적용되는 버그가 드러났다. `existing`이 놓인 슬롯 절대 번호(`slotIndex`) 기준으로 "내 필드"/"상대 필드"를 그때그때 판정하도록 고쳐, 컴퓨터 배치가 나중에 구현되거나 치트로 테스트해도 항상 정확하게 만들었다.

```csharp
// InGameSceneManager.cs
private const int ComputerFieldStart = 1, ComputerFieldEnd = 3;
private const int UserFieldStart = 4, UserFieldEnd = 6;

// slotIndex가 속한 진영의 필드 범위("내 필드") — 유저 고정이 아니라 실제 슬롯 위치 기준
private (int Start, int End) OwnFieldRange(int slotIndex) =>
    slotIndex <= ComputerFieldEnd ? (ComputerFieldStart, ComputerFieldEnd) : (UserFieldStart, UserFieldEnd);

// slotIndex 반대 진영의 필드 범위("상대 필드")
private (int Start, int End) OpponentFieldRange(int slotIndex) =>
    slotIndex <= ComputerFieldEnd ? (UserFieldStart, UserFieldEnd) : (ComputerFieldStart, ComputerFieldEnd);

private List<Friend> GetFieldFriends(int fromIndex, int toIndex)
{
    var result = new List<Friend>();
    for (int i = fromIndex; i <= toIndex; i++)
    {
        var slot = GetFieldSlot(i);
        if (slot.IsOccupied) result.Add(slot.GetComponentInChildren<Friend>());
    }
    return result;
}

private Friend PickRandomTargetable(List<Friend> candidates)
{
    candidates.RemoveAll(f => CardTable.Instance.GetCond(f.Key) == CardCondition.Except);
    return candidates.Count == 0 ? null : candidates[Random.Range(0, candidates.Count)];
}
```

- `GetBase(int slotIndex)`([공격 판정 계획](plan-ingame-attack.md)에 이미 있음, `slotIndex <= 3 ? _computerBase : _userBase`)와 같은 판정 축을 그대로 따른다 — "이 슬롯이 컴퓨터 쪽인가"를 기준으로 삼는 판정이 이미 검증된 패턴이라는 근거.
- `AllyRandom`(무작위 단일 대상)은 방금 병합된 카드 자기 자신을 후보에서 제외한다(요청자 확인, 5번 결정 참고) — `AllyAll`(전체 적용)에는 자기 자신도 포함된다. `PickRandomTargetable` 자체는 이 제외를 하지 않으므로(범용 헬퍼라 "누가 방금 병합됐는지" 모름), 호출하는 쪽(`TriggerMergeAbility`)이 후보 목록에서 미리 걷어낸다.

### 3. Merge 발동 훅 — `MergeCardIntoSlot`에 한 줄 연결

```csharp
// InGameSceneManager.cs, MergeCardIntoSlot(existing, mergeKey, slotIndex) — plan-ingame-cheat.md가 TryPlaceFriendCard에서 추출한 실행 메서드
existing.MergeWith(addAtt, addHp);
existing.PunchScale(_mergePunchScale, _mergePunchDuration);
TriggerMergeAbility(existing, slotIndex); // 신규 — slotIndex로 "내 필드"/"상대 필드"를 판정
```

(`addAtt`/`addHp` 계산은 [친구카드 합체 계획](plan-ingame-merge.md)의 이종 합체 확장을 그대로 따르되, 4번 결정의 배수 병합만 이 문서가 그 계산에 끼어든다. `slotIndex`는 [친구카드 합체 계획](plan-ingame-merge.md)의 `CanMerge`/`MergeCardIntoSlot` 분리, [치트 에디터 계획](plan-ingame-cheat.md)의 `CheatMergeIntoSlot` 모두를 거쳐 흘러 들어온다 — 드래그든 치트든 항상 "실제로 합쳐지는 슬롯"이 기준이다.)

### 4. 까마귀(1011) 배수 병합 — 낸 카드 기본값 × 내 필드의 같은 종류 수

요청자 확인: "낸 카드(1011)의 기본 att/hp × 병합 직전 필드에 있던 같은 종류(1011) 수"로 계산한다. 예: 기본 2/2 카드이고 필드에 이미 1011이 2장 있는 상태에서 1장을 더 내면 `2×2=4`/`2×2=4`가 더해진다. 여기서 "필드"도 2번 결정과 마찬가지로 `existing`이 실제로 놓인 슬롯의 진영 기준이다(유저 고정 아님).

```csharp
// InGameSceneManager.cs, MergeCardIntoSlot(existing, mergeKey, slotIndex) — addAtt/addHp를 정하는 지점
var existingData = CardTable.Instance.Get(existing.Key);

int addAtt = data.att;
int addHp = data.hp;
if (existingData.EffectClauses.Any(c => c.Keyword == "MultiplierMerge"))
{
    var (ownStart, ownEnd) = OwnFieldRange(slotIndex);
    int sameCount = GetFieldFriends(ownStart, ownEnd).Count(f => f.Key == existing.Key);
    addAtt *= sameCount;
    addHp *= sameCount;
}
```

- `existing.Key`가 아니라 `EffectClauses`에 `MultiplierMerge` 키워드가 있는지로 판정한다 — 1011이라는 특정 key를 코드에 박아 넣지 않고, "이 카드는 배수 병합 효과를 갖는다"는 데이터를 그대로 읽는다. 나중에 같은 효과를 갖는 카드가 추가돼도 CSV에 `effect=MultiplierMerge`만 채우면 이 코드는 그대로 재사용된다.
- `sameCount`도 `existing.Key`(필드에 실제로 있는 카드의 key, 예: 1011)와 같은 카드 수를 센다 — 필드 카드의 정체성은 병합 후에도 `existing`이 유지하므로([친구카드 합체 계획](plan-ingame-merge.md)의 이종 합체 확장과 일관됨) "배수 병합 카드가 합쳐질 때"는 곧 "그 카드 위에 무언가가 합쳐질 때"다.
- `sameCount`는 병합 **직전** 상태를 센다 — 이번에 낸 카드는 아직 `existing`에 반영되지 않은 시점이라 자동으로 "기존 개수"가 된다.

### 5. 발동 효과 디스패처 — `scope`로 대상을, `EffectClauses`의 `Kind`로 동작을 결정

`TriggerMergeAbility`는 카드 key도, 이름 붙은 effect enum도 모른다. `scope`로 "누구에게"를 정해 대상(들)을 모으고, `EffectClauses`(1번 결정에서 미리 파싱해 둔 조각 목록)를 순서대로 적용한다. `slotIndex`(병합이 실제로 일어난 슬롯)로 2번 결정의 `OwnFieldRange`/`OpponentFieldRange`를 구해 `Ally`/`Enemy`를 판정한다 — 유저 고정이 아니다.

```csharp
// InGameSceneManager.cs
private void TriggerMergeAbility(Friend existing, int slotIndex)
{
    var data = CardTable.Instance.Get(existing.Key);
    if (data.cond != CardCondition.Merge) return;
    if (data.EffectClauses.Any(c => c.Keyword == "MultiplierMerge")) return; // 4번 결정(병합 공식 자체)에서 이미 처리됨

    var (ownStart, ownEnd) = OwnFieldRange(slotIndex);
    var (oppStart, oppEnd) = OpponentFieldRange(slotIndex);

    switch (data.scope)
    {
        case CardAbilityScope.Self:
            ApplyClausesToFriend(data.EffectClauses, existing);
            break;
        case CardAbilityScope.AllyRandom:
        {
            var candidates = GetFieldFriends(ownStart, ownEnd);
            candidates.Remove(existing); // 무작위 단일 대상에서는 자기 자신 제외(AllyAll처럼 전체 적용일 때는 포함됨)
            ApplyClausesToFriend(data.EffectClauses, PickRandomTargetable(candidates));
            break;
        }
        case CardAbilityScope.EnemyRandom:
            ApplyClausesToFriend(data.EffectClauses, PickRandomTargetable(GetFieldFriends(oppStart, oppEnd)));
            break;
        case CardAbilityScope.AllyAll:
            foreach (var f in GetFieldFriends(ownStart, ownEnd)) ApplyClausesToFriend(data.EffectClauses, f);
            break;
        case CardAbilityScope.EnemyAll:
            foreach (var f in GetFieldFriends(oppStart, oppEnd)) ApplyClausesToFriend(data.EffectClauses, f);
            break;
        case CardAbilityScope.AllyBase:
            ApplyClausesToBase(data.EffectClauses, GetBase(slotIndex)); // GetBase: slotIndex가 속한 진영의 본체(공격 판정 계획에 이미 있는 메서드 재사용)
            break;
        case CardAbilityScope.EnemyBase:
            ApplyClausesToBase(data.EffectClauses, slotIndex <= ComputerFieldEnd ? _userBase : _computerBase);
            break;
    }
}

// Friend 대상 — 조각의 종류(Kind)별로 적용. 적용 후 죽었으면(능력엔 복귀 연출이 없으므로) 그 자리에서 즉시 제거(7번 결정)
private void ApplyClausesToFriend(List<CardEffectClause> clauses, Friend target)
{
    if (target == null) return;

    foreach (var clause in clauses)
    {
        switch (clause.Kind)
        {
            case CardEffectClauseKind.Stat:
                switch (clause.Stat, clause.Op)
                {
                    case (CardStat.Att, '+'): target.AddAtt(clause.Value); break;
                    case (CardStat.Att, '-'): target.AddAtt(-clause.Value); break;
                    case (CardStat.Att, '*'): target.MultiplyAtt(clause.Value); break;
                    case (CardStat.Att, '/'): target.DivideAtt(clause.Value); break;
                    case (CardStat.Hp, '+'): target.AddHp(clause.Value); break;    // 성장 — MaxHp도 같이 오름
                    case (CardStat.Hp, '-'): target.AddHp(-clause.Value); break;   // 저하 — MaxHp도 같이 내려감(전투 피해와 다름, dmg 사용)
                    case (CardStat.Hp, '*'): target.MultiplyHp(clause.Value); break;
                    case (CardStat.Hp, '/'): target.DivideHp(clause.Value); break;
                }
                break;
            case CardEffectClauseKind.Damage:
                target.TakeDamage(clause.Value); // 방어막 소모 대상 — 전투와 같은 피해
                break;
            case CardEffectClauseKind.Heal:
                target.Heal(clause.Value); // MaxHp까지만, MaxHp 자체는 불변
                break;
            case CardEffectClauseKind.HealToMax:
                target.HealToMax();
                break;
            case CardEffectClauseKind.Keyword:
                if (clause.Keyword == "Shield") target.AddShield();
                // "MultiplierMerge"는 TriggerMergeAbility 진입 시점에 이미 걸러져 여기 도달하지 않음
                break;
            case CardEffectClauseKind.Spawn:
                target.ApplySpawnMark(clause.SpawnKey, clause.SpawnAtt, clause.SpawnHp); // 포자감염류 — 이 대상이 나중에 죽을 때 스폰
                break;
        }
    }

    if (target.IsDead) Destroy(target.gameObject);
}

// BaseStone 대상 — 피해/회복만 의미가 있다(Att, 스폰 등은 본체를 대상으로 하는 카드가 없어 무시)
private void ApplyClausesToBase(List<CardEffectClause> clauses, BaseStone target)
{
    foreach (var clause in clauses)
    {
        switch (clause.Kind)
        {
            case CardEffectClauseKind.Damage: target.TakeDamage(clause.Value); break;
            case CardEffectClauseKind.Heal: target.Heal(clause.Value); break;
        }
    }
}
```

- `data.cond != CardCondition.Merge`면 즉시 반환 — `Except`(1003)나 `None`(1008 등)인 카드가 병합돼도 아무 일도 일어나지 않는다.
- `scope`가 `AllyRandom`/`EnemyRandom`인데 `PickRandomTargetable`이 `null`을 반환하면(대상 후보가 전혀 없거나 전부 `Except`) `ApplyClausesToFriend`가 `target == null`로 조용히 스킵한다 — 별도 방어 코드 불필요.
- `AllyRandom`은 후보 목록에서 `existing`(방금 병합된 카드 자신)을 먼저 제거한 뒤 무작위로 뽑는다(요청자 확인) — "내 필드 무작위 친구"가 매번 자기 자신을 뽑는 것을 방지한다. `EnemyRandom`은 애초에 `existing`이 상대 필드 후보에 들어갈 수 없어 같은 처리가 필요 없고, `AllyAll`(전체 적용)은 자기 자신도 그대로 포함된다 — "전체"와 "무작위 하나"는 의미가 다르므로 제외 대상도 다르다.
- `Hp+`/`Hp-`(스탯 증감)와 `dmg`/`heal`(전투식 피해/회복)은 언뜻 비슷해 보이지만 **완전히 다른 메서드**로 보낸다 — `AddHp`는 `MaxHp`도 함께 움직이는 성장/저하, `TakeDamage`/`Heal`은 그 시점 체력만 바꾸는 전투식 변화(`TakeDamage`는 방어막도 소모)라 서로 대체할 수 없다. `Att`는 방어막/최대치 개념이 없어 `+`/`-` 모두 `AddAtt(±value)` 하나로 충분하다.
- `ApplyClausesToFriend`는 모든 조각 적용이 끝난 뒤 공통으로 `IsDead`를 확인한다 — `dmg`나 `Att/`,`Hp/`처럼 체력을 깎는 조각만 실제로 죽음을 유발할 수 있고, 나머지는 `IsDead`가 항상 `false`라 안전하게 넘어간다.
- 새로운 카드가 기존 `effect` 문법(예: `dmg+1`)과 기존 `scope` 조합을 그대로 쓴다면 `CardTable.csv`에 행만 추가하면 끝난다 — 이 스위치문에 손댈 일이 없다. 완전히 새로운 스탯이나 문법이 필요할 때만 코드를 늘린다.

### 6. 신규 `Friend`/`BaseStone` 메서드 — 기존 스타일(직전 값 대비 색상 갱신) 유지

```csharp
// BaseStone.cs
public void Heal(int amount)
{
    CurrentHp = Mathf.Min(_maxHp, CurrentHp + amount); // _maxHp(30)를 넘지 않음
    _hpText.text = CurrentHp.ToString();
}
```

```csharp
// Friend.cs
public int MaxHp { get; private set; }
public bool HasShield { get; private set; }
public bool HasRevived { get; private set; }

// 사망 시 새 카드를 낳는 예약(부활/포자감염 공용) — key/att/hp는 spawn+key,att=n,hp=n 조각에서 옴
public SpawnMarkInfo SpawnMark { get; } = new SpawnMarkInfo();

public class SpawnMarkInfo
{
    public bool HasMark { get; private set; }
    public int Key { get; private set; }
    public int Att { get; private set; }
    public int Hp { get; private set; }

    public void Set(int key, int att, int hp)
    {
        HasMark = true;
        Key = key;
        Att = att;
        Hp = hp;
    }
}

public void SetKey(int key) // 기존 메서드 수정 — MaxHp 초기화 추가
{
    ...
    CurrentHp = data.hp;
    MaxHp = data.hp; // 신규
    ...
}

public void TakeDamage(int amount) // 기존 메서드 수정 — 방어막 최우선 소모
{
    if (HasShield)
    {
        HasShield = false;
        return; // 이번 피해 전부 무효, 텍스트/색 변화 없음
    }
    int previousHp = CurrentHp;
    CurrentHp = Mathf.Max(0, CurrentHp - amount);
    _hpText.text = CurrentHp.ToString();
    _hpText.color = GetStatColor(CurrentHp, previousHp);
}

public void MergeWith(int addAtt, int addHp) // 기존 메서드 수정 — MaxHp도 함께 누적
{
    ...
    CurrentHp += addHp;
    MaxHp += addHp; // 신규
    ...
}

public void AddAtt(int amount) // 5번 결정의 Att '+'/'-' 공용
{
    int previous = Att;
    Att = Mathf.Max(0, Att + amount);
    _attText.text = Att.ToString();
    _attText.color = GetStatColor(Att, previous);
}

public void MultiplyAtt(int factor) // 5번 결정의 Att '*'
{
    int previous = Att;
    Att *= factor;
    _attText.text = Att.ToString();
    _attText.color = GetStatColor(Att, previous);
}

public void DivideAtt(int divisor) // 5번 결정의 Att '/'
{
    int previous = Att;
    Att = Mathf.Max(0, Att / divisor);
    _attText.text = Att.ToString();
    _attText.color = GetStatColor(Att, previous);
}

// 스탯 증감(성장/저하) — MaxHp도 같이 바뀜. 전투 피해(TakeDamage)·회복(Heal)과 달리 방어막과 무관하고 최대치 자체가 변한다
public void AddHp(int amount)
{
    int previousHp = CurrentHp;
    CurrentHp = Mathf.Max(0, CurrentHp + amount);
    MaxHp = Mathf.Max(1, MaxHp + amount);
    _hpText.text = CurrentHp.ToString();
    _hpText.color = GetStatColor(CurrentHp, previousHp);
}

// 고정량 회복 — MaxHp를 넘지 않고, MaxHp 자체는 바꾸지 않는다(성장인 AddHp와 구분)
public void Heal(int amount)
{
    int previousHp = CurrentHp;
    CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
    _hpText.text = CurrentHp.ToString();
    _hpText.color = GetStatColor(CurrentHp, previousHp);
}

public void MultiplyHp(int factor) // 5번 결정의 Hp '*'
{
    int previousHp = CurrentHp;
    CurrentHp *= factor;
    MaxHp *= factor;
    _hpText.text = CurrentHp.ToString();
    _hpText.color = GetStatColor(CurrentHp, previousHp);
}

public void DivideHp(int divisor) // 5번 결정의 Hp '/'
{
    int previousHp = CurrentHp;
    CurrentHp = Mathf.Max(0, CurrentHp / divisor);
    MaxHp = Mathf.Max(1, MaxHp / divisor);
    _hpText.text = CurrentHp.ToString();
    _hpText.color = GetStatColor(CurrentHp, previousHp);
}

public void HealToMax()
{
    int previousHp = CurrentHp;
    CurrentHp = MaxHp;
    _hpText.text = CurrentHp.ToString();
    _hpText.color = GetStatColor(CurrentHp, previousHp);
}

public void AddShield() => HasShield = true; // 이미 있어도 그대로 유지(스택 없음)

public void ApplySpawnMark(int key, int att, int hp) => SpawnMark.Set(key, att, hp);

// 사망 시 1회 부활(CardCondition.Die) — att/hp는 자신의 effect(spawn+key,att=n,hp=n)에서 읽어온 값
public bool TryRevive(int att, int hp)
{
    if (HasRevived) return false;
    HasRevived = true;
    OverrideStats(att, hp);
    return true;
}

// SetKey로 채워진 기본 스탯을 명시적인 값으로 덮어쓴다 — 부활/포자감염처럼 카드 기본값이 아닌 수치로 등장할 때 사용
public void OverrideStats(int att, int hp)
{
    Att = att;
    CurrentHp = hp;
    MaxHp = hp;
    _attText.text = Att.ToString();
    _attText.color = Color.white;
    _hpText.text = CurrentHp.ToString();
    _hpText.color = Color.white;
}
```

- `Att`/`Hp`의 사칙연산 메서드(`AddAtt`/`MultiplyAtt`/`DivideAtt`/`AddHp`/`MultiplyHp`/`DivideHp`) 6개만 있으면 지금 20종의 모든 "스탯 증감" 효과를 조합으로 표현할 수 있다 — 이름 붙은 복합 메서드(`HalveStats`/`DoubleHp` 같은)는 만들지 않는다. `Att/2,Hp/2`는 `DivideAtt(2)`와 `DivideHp(2)`를 디스패처가 순서대로 두 번 호출하는 것으로 표현된다.
- 기존 `Friend.DoubleAtt()`([공격 판정 계획](plan-ingame-attack.md)에서 자기 자신 대상 공격 시 어드밴티지로 이미 사용 중)는 그대로 둔다 — 이 문서의 능력 시스템은 그 메서드를 호출하지 않고 `MultiplyAtt(2)`를 쓴다.
- `MaxHp`는 "이 카드가 지금까지 커진(혹은 줄어든) 최대치"다 — 병합(`MergeWith`)/곱(`MultiplyHp`)/성장(`AddHp`)으로 오르고 나눔(`DivideHp`)/저하(`AddHp(-n)`)로 내려가지만, 전투 피해(`TakeDamage`)·회복(`Heal`)으로는 바뀌지 않는다. `HealToMax`(1017)는 이 값까지만 회복한다 — 카드 기본값으로 고정하지 않은 이유는 "3번 겹쳐 12/6인 카드"처럼 병합으로 커진 카드가 그 커진 상태를 최대치로 인식해야 자연스럽기 때문(합체 계획의 누적 철학과 동일).
- `DivideAtt`/`DivideHp`는 0까지 허용한다 — `CurrentHp`가 0이 되는 경우는 아래 7번에서 즉시 제거로 이어지지만, `Att`가 0이 되는 것 자체는 죽음과 무관하므로 막을 이유가 없다.
- `OverrideStats`는 `SetKey`가 채운 카드 기본값을 명시적인 수치로 덮어쓴다 — 부활(1018)은 같은 인스턴스에 바로 호출하고(`TryRevive` 내부), 포자감염(1010류)은 새로 `Instantiate`한 인스턴스에 `SetKey` 직후 호출한다(8번 결정).

### 7. 능력으로 죽는 경우 즉시 제거

전투(`ResolveAttackRoutine`)는 "죽어도 공격자가 복귀하는 연출까지 재생한 뒤 제거"하지만, 능력 발동에는 애초에 이동 연출이 없다 — `ApplyClausesToFriend`(5번 결정)가 모든 조각을 적용한 직후 `IsDead`를 확인해 그 자리에서 바로 `Destroy`한다. `FieldSlot.IsOccupied`가 `childCount > 0`로 판정하므로 별도 상태 정리 없이 슬롯이 자동으로 빈다.

### 8. 사망 트리거 효과(1010/1018) — `ResolveAttackRoutine` 확장 지점

1010(포자감염→사망 시 카드 생성)과 1018(사망 시 1회 부활)은 둘 다 `spawn+key,att=n,hp=n` 문법을 쓰지만 **평가 시점이 다르다**:

- **1018(부활)**: `cond=Die`라 병합과 무관하게 언제든 죽을 수 있다. **자기 자신이 죽는 바로 그 순간**, 자기 자신의 `EffectClauses`에서 `Spawn` 조각을 찾아 그 att/hp로 자신을 되살린다(`Friend.TryRevive`, 새 오브젝트를 만들지 않고 같은 인스턴스를 재활용).
- **1010(포자감염)**: `cond=Merge`, `scope=AllyRandom`이라 **병합 시점에** 무작위 아군 한 장에게 "죽으면 이 key/att/hp로 카드가 생긴다"는 예약(`SpawnMark`)을 걸어 둔다(5번 결정의 `Spawn` 케이스). 그 마킹된 대상이 **훗날 실제로 죽는 시점에** 예약된 정보로 새 `Friend`를 그 슬롯에 생성한다 — 마킹된 대상이 1010 자신일 필요는 없다(다른 종류 카드가 마킹된 채 죽어도 항상 1010이 태어난다).

`Friend`에는 이미 6번 결정에서 `SpawnMark`/`TryRevive()` API를 정의했으므로, `ResolveAttackRoutine` 쪽 변경은 아래 헬퍼 하나로 정리된다.

```csharp
// InGameSceneManager.cs
// 사망 확정된 Friend를 부활/포자감염 규칙에 따라 처리한다. true를 반환하면 부활 성공 — 파괴하지 않고 필드에 남긴다.
private bool TryHandleDeath(Friend friend, Transform slotTransform)
{
    var data = CardTable.Instance.Get(friend.Key);
    if (data.cond == CardCondition.Die)
    {
        foreach (var clause in data.EffectClauses)
        {
            if (clause.Kind != CardEffectClauseKind.Spawn) continue;
            if (!friend.TryRevive(clause.SpawnAtt, clause.SpawnHp)) break; // 이미 한 번 부활했으면 그대로 사망 처리로 진행

            friend.PunchScale(_mergePunchScale, _mergePunchDuration); // 부활 연출은 별도로 만들지 않고 병합 펀치 재사용
            return true;
        }
    }

    bool hasSpawnMark = friend.SpawnMark.HasMark;
    int spawnKey = friend.SpawnMark.Key, spawnAtt = friend.SpawnMark.Att, spawnHp = friend.SpawnMark.Hp;
    Destroy(friend.gameObject);
    if (hasSpawnMark) SpawnFriendDirectly(spawnKey, spawnAtt, spawnHp, slotTransform);
    return false;
}

private void SpawnFriendDirectly(int key, int att, int hp, Transform slotTransform)
{
    var friend = Instantiate(_friendPrefab, slotTransform);
    friend.SetKey(key);
    friend.OverrideStats(att, hp); // CardTable 기본값이 아니라 spawn 조각에 적힌 att/hp로 덮어씀
}
```

`ResolveAttackRoutine`의 기존 제거 코드(`Destroy(attacker.gameObject)`, `Destroy(targetFriend.gameObject)`)를 `TryHandleDeath` 호출로 바꾸고, 반환값이 `true`(부활)면 기존의 "생존 시" 분기(원래 슬롯으로 복귀 + 하이라이트 해제)를 그대로 타도록 연결한다. **이 변경은 [공격 판정 계획](plan-ingame-attack.md)이 소유한 파일을 건드리므로, 실제 구현 시 그 문서에도 변경 사항을 기록한다.**

`SpawnFriendDirectly`가 항상 `spawn` 조각에 적힌 `key`(1010이든 1018이든)로 카드를 낳는 것은 5번 결정의 "카드 key로 분기하지 않는다" 원칙에 대한 예외가 아니다 — 무엇을 낳을지는 여전히 `effect` 데이터(`spawn+key,...`)가 결정하고, 코드는 그 key를 그대로 읽어 `Instantiate`할 뿐 특정 key를 하드코딩하지 않는다.

---

## 클래스 구조

```
Friend (기존 파일 수정, InGame/)
├── MaxHp : int { get; }                              ← 신규
├── HasShield : bool { get; }                         ← 신규
├── HasRevived : bool { get; }                        ← 신규
├── SpawnMark : SpawnMarkInfo (HasMark/Key/Att/Hp)     ← 신규, 중첩 클래스로 묶음
├── SetKey(int)                                       ← 수정, MaxHp 초기화 추가
├── TakeDamage(int)                                   ← 수정, 방어막 소모 우선 처리
├── MergeWith(int, int)                               ← 수정, MaxHp 누적 추가
├── AddAtt(int)                                       ← 신규 (Att '+'/'-')
├── MultiplyAtt(int) / DivideAtt(int)                 ← 신규 (Att '*'/'/')
├── AddHp(int)                                        ← 신규 (Hp '+'/'-', 성장·저하)
├── Heal(int)                                         ← 신규 (heal+n, MaxHp까지만)
├── MultiplyHp(int) / DivideHp(int)                   ← 신규 (Hp '*'/'/')
├── HealToMax()                                       ← 신규 (heal+max)
├── AddShield()                                       ← 신규
├── ApplySpawnMark(int, int, int)                     ← 신규
├── TryRevive(int, int) : bool                        ← 신규(호출은 8번 결정에서, plan-ingame-attack.md 몫)
└── OverrideStats(int, int)                           ← 신규

BaseStone (기존 파일 수정, InGame/)
└── Heal(int)                          ← 신규

InGameSceneManager (기존 파일 수정, InGame/)
├── OwnFieldRange(int slotIndex) / OpponentFieldRange(int slotIndex) : (int, int) ← 신규, "내/상대 필드"를 슬롯 위치 기준으로 판정
├── GetFieldFriends(int, int) : List<Friend>          ← 신규
├── PickRandomTargetable(List<Friend>) : Friend       ← 신규
├── TriggerMergeAbility(Friend, int slotIndex)        ← 신규, `MergeCardIntoSlot`이 병합 직후 호출, `scope`+`slotIndex`로 분기
├── ApplyClausesToFriend(List<CardEffectClause>, Friend) ← 신규, Kind별로 적용 + 사망 시 즉시 제거
├── ApplyClausesToBase(List<CardEffectClause>, BaseStone) ← 신규
├── TryHandleDeath(Friend, Transform) : bool          ← 신규(호출은 plan-ingame-attack.md 몫)
└── SpawnFriendDirectly(int, int, int, Transform)     ← 신규

(`CanMerge`/`MergeCardIntoSlot`으로의 `TryPlaceFriendCard` 분리는 [InGame 필드 슬롯 치트 에디터 계획](plan-ingame-cheat.md)이 수행 — `MergeCardIntoSlot`이 `existingData.EffectClauses`의 `MultiplierMerge` 배수 반영과 `TriggerMergeAbility` 호출을 담당)

CardTable.cs (기존 파일 수정, Data/Table/)
├── CardCondition.Die                          ← 신규 enum 멤버
├── CardStat(Att/Hp)                            ← 신규 enum
├── CardEffectClauseKind(Stat/Damage/Heal/HealToMax/Keyword/Spawn) ← 신규 enum
├── CardEffectClause                            ← 신규 struct("무엇을" 한 조각, 정적 팩터리)
├── CardEffectParser                            ← 신규 static class(effect 문자열 → List<CardEffectClause>)
├── CardAbilityScope(7종)                       ← 신규 enum("누구에게")
├── CardTableData.animal, .sheets, .effect, .scope, .EffectClauses  ← 신규 필드
└── CardTable.OnLoaded()                        ← 신규 override, 로드 직후 전 카드 effect를 파싱해 캐싱
```

---

## 파일 구성

```
Assets/
├── Scripts/
│   ├── Data/Table/
│   │   └── CardTable.cs             ← 기존 파일 수정(CardCondition.Die, CardStat/CardEffectClauseKind/CardEffectClause/CardEffectParser/CardAbilityScope 신설, CardTableData 필드, OnLoaded)
│   └── InGame/
│       ├── BaseStone.cs              ← 기존 파일 수정(Heal 추가)
│       ├── Friend.cs                 ← 기존 파일 수정(6번 결정)
│       └── InGameSceneManager.cs     ← 기존 파일 수정(2/3/4/5/8번 결정)
└── Tables/Source/
    └── CardTable.csv                 ← 기존 파일 수정(effect/scope 컬럼 추가 및 20종 값 채움 — "능력 분류" 표 참고)
```

씬/프리팹 변경 없음 — 방어막/포자감염 마킹 상태의 시각 표시(아이콘 등)는 이번 범위에 없다(아래 "이번 범위에서 제외").

---

## 이번 범위에서 제외

- 방어막(`HasShield`)·포자감염 마킹(`SpawnMark.HasMark`)의 시각적 표시(아이콘, 오버레이 등) — 지금은 상태값만 존재하고 카드 위에 드러나지 않는다. 상태를 눈으로 확인하려면 로그나 디버거에 의존해야 한다(요청자 확인 후 아트 리소스가 준비되면 후속 문서에서 추가).
- 컴퓨터 측 카드가 능력을 발동하는 경우 — 컴퓨터가 아직 실제로 필드에 카드를 놓지 않는다(기존 제외 범위 유지). 2번 결정의 필드 범위 상수(유저 고정)를 `TurnOwner` 기준으로 바꾸는 일반화는 컴퓨터 배치 문서에서 함께 처리한다.
- 1004(`target=All`)/1019(`target=Any`) 이종 합체 판정, 드래그 중 병합 가능 슬롯 초록 하이라이트 미리보기 — [친구카드 합체 계획](plan-ingame-merge.md)에서 다룸.
- 1012(`sheets=15`) 반영 — `DeckBuilder.Build`가 카드마다 고정 10장 대신 `CardTableData.sheets`를 쓰도록 바꾸는 일은 전투 능력이 아니라 덱 구성 문제라 범위 밖.
- `effect` 문법의 확장(괄호, 조건식, 스탯 외 다른 대상, `spawn` 다중 콤보 등) — 지금 20종은 이 문서의 다섯 갈래(스탯/데미지/회복/최대회복/스폰) 문법만으로 전부 표현되므로, 더 복잡한 문법은 실제로 필요해질 때(YAGNI) 후속 문서에서 다룬다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 무작위 대상 후보가 없음(내/상대 필드가 비었거나 후보 전부 `Except`) | `PickRandomTargetable`이 `null` 반환 → `ApplyClausesToFriend`가 `target == null`로 조용히 스킵 |
| `AllyRandom`인데 내 필드에 방금 병합된 카드 자신 말고는 대상이 없음(필드에 그 카드 한 장뿐) | `existing` 제거 후 후보가 빈 리스트가 됨 → `PickRandomTargetable`이 `null` 반환 → 조용히 스킵(효과 발동 안 됨) |
| `AllyAll`(전체 적용)에 방금 병합된 카드 자신이 포함되는지 | 포함됨 — `AllyRandom`만 자기 자신을 제외하고, `AllyAll`은 `GetFieldFriends` 결과를 그대로 순회하므로 자신도 대상이 됨(설계 목표 참고) |
| `Att/2,Hp/2`(1001)로 대상 `CurrentHp`가 0이 됨 | 두 조각이 순서대로 적용된 뒤 `ApplyClausesToFriend`가 `IsDead` 확인 후 즉시 `Destroy` — 전투와 달리 복귀 연출 없음 |
| `dmg+n`(대상이 `Friend`)로 대상 `CurrentHp`가 0이 됨 | 위와 동일 |
| `Shield` 조각으로 이미 방어막이 있는 카드에 다시 방어막 부여 | `AddShield`는 단순 대입이라 중첩되지 않고 그대로 유지 |
| 방어막이 있는 카드가 전투에서 공격받음([공격 판정 계획](plan-ingame-attack.md)의 쌍방 피해) | `TakeDamage`가 최우선으로 방어막을 소모 — 공격 판정 코드 변경 없이 자동 적용 |
| `heal+max` 대상이 이미 `CurrentHp == MaxHp` | `HealToMax`가 그대로 대입 — 변화 없으므로 `GetStatColor`가 흰색 유지 |
| `heal+n` 대상이 이미 `MaxHp`에 근접해 `n`을 다 못 채움 | `Heal`이 `Mathf.Min(MaxHp, ...)`으로 자동으로 자름 |
| `MultiplierMerge` 키워드가 없는 카드에 배수 로직이 적용되는지 | `EffectClauses`에 그 키워드가 있을 때만 배수 — 카드 key와 무관하게 데이터로만 판정, 그 외엔 기존 단순 합산 그대로 |
| `cond=Die`(1018)인 카드가 이미 한 번 부활한 뒤 다시 사망 | `HasRevived == true`라 `TryRevive()`가 `false` 반환 → 일반 사망 처리로 진행(포자감염 마킹 여부만 추가 확인 후 제거) |
| 포자감염(1010)으로 마킹된 대상이 부활 카드(1018)인 경우 | `SpawnMark.HasMark`(포자감염 예약)와 `cond=Die`(부활)는 서로 다른 트리거라 함께 걸릴 수 있다 — `TryHandleDeath`는 `cond=Die` 부활을 먼저 시도하고, 부활에 성공하면 그 자리에서 `return true`하므로 포자감염 마킹은 그 시점엔 소비되지 않고 남아있는다(다음에 진짜로 죽을 때 스폰) |
| `CardTable.csv`의 `effect`에 오타/미지원 문법을 넣은 행(예: `Att%2`, `spawn1010`처럼 형식이 깨짐) | `CardEffectParser.Parse`가 어떤 패턴에도 매칭 안 되면 `LogError` 후 그 조각을 버림 — 나머지 정상 조각은 그대로 적용되고, CSV 편집 후에는 항상 콘솔에서 이 로그가 없는지 확인 |
| `effect`가 완전히 빈 문자열이거나 `cond≠Merge` | `EffectClauses`가 빈 리스트이거나 `TriggerMergeAbility`가 `cond` 확인에서 조기 반환 — 둘 다 아무 일도 일어나지 않음 |
| `CardTable.Instance.Get(existing.Key)`가 `null`(테이블에 없는 key) | 기존 관례(합체 계획 등)와 동일하게 방어 코드 없이 신뢰 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 유저 필드에 1000(대지의 왕) 한 장, 다른 자리에 아무 카드나 있는 상태에서 같은 key(1000) 카드를 병합 | `effect="Att*2"` 해석 → `existing`(방금 병합된 1000)은 후보에서 제외되므로, 대상은 항상 "다른 자리의 카드"로 결정됨 — 그 카드의 `Att`가 2배(초록 표시) |
| 2 | 컴퓨터 필드에 카드가 있는 상태(테스트용으로 임의 배치)에서 1001 병합 | `effect="Att/2,Hp/2"` 두 조각이 순서대로 적용 — 컴퓨터 필드 무작위 한 장의 `Att`/`CurrentHp`가 각각 절반(정수 나눗셈), 0이 되면 그 자리에서 즉시 `Destroy` |
| 3 | 1002 병합 | `effect="dmg+2"`, `scope=EnemyBase` → `_computerBase.CurrentHp`가 2 감소 |
| 4 | `_userBase.CurrentHp`가 30 미만인 상태에서 1006 병합 | `effect="heal+2"`, `scope=AllyBase` → `_userBase.CurrentHp`가 2 증가(30 초과 불가) |
| 5 | 1007 병합 후 그 카드가 전투에서 피해를 받음 | `effect="Shield"` → 첫 피해는 무효(방어막 소모, `HasShield`가 `false`로 전환), `CurrentHp` 불변 |
| 6 | 1009 병합 | `effect="Hp*2"` → 유저 필드 무작위 한 장의 `CurrentHp`/`MaxHp` 모두 2배 |
| 7 | 유저 필드에 1011이 2장 있는 상태(합쳐서 4/4)에서 1011 한 장 더 병합 | `MultiplierMerge` 키워드로 4번 결정 분기 — 낸 카드 기본 2/2 × 필드의 기존 1011 수(2) = 4/4가 추가로 더해져 8/8 |
| 8 | 1013/1014/1016 각각 병합 | `effect`가 순서대로 `Att+1`/`dmg+1`/`Hp+1`, `scope`는 `AllyAll`/`EnemyAll`/`AllyAll` — 유저 필드 전체 `Att` +1, 컴퓨터 필드 전체 피해 1(0 되는 카드는 즉시 제거), 유저 필드 전체 `CurrentHp`/`MaxHp` +1 |
| 9 | 전투로 `CurrentHp`가 깎인 카드에 1017 병합 결과가 그 카드를 무작위로 선택 | `effect="heal+max"` → `CurrentHp`가 `MaxHp`까지 회복(초록 표시) |
| 10 | 유저 필드 무작위 한 장이 1010 병합으로 포자감염 마킹된 뒤 전투에서 사망 | `effect="spawn+1010,att=2,hp=2"` → 마킹된 대상의 종류와 무관하게 그 슬롯에 1010(2/2)이 새로 생성됨 |
| 11 | 1018 카드가 전투에서 사망 | `effect="spawn+1018,att=1,hp=1"` → 같은 인스턴스가 `Att`/`CurrentHp`/`MaxHp` 모두 1로 바뀌어 필드에 그대로 남음(제거되지 않음), `HasRevived`가 `true`로 전환 |
| 12 | 시나리오 11 이후 그 카드가 다시 사망 | `HasRevived == true`라 `TryRevive`가 `false` → 이번엔 정상적으로 `Destroy` |
| 13 | `CardTable` 로드 시 콘솔 확인 | 1018의 `cond`가 `Die`로 정상 파싱되어 더 이상 파싱 실패 로그가 남지 않음, `animal`/`sheets`/`effect`/`scope`가 채워지고 `EffectClauses`가 20종 전부에서 `CardEffectParser.Parse` 에러 없이 채워짐 |
| 14 | (가상) 테스트용으로 CSV에 `effect="dmg+3"`/`scope=EnemyRandom`인 새 key를 추가하고 병합 | `InGameSceneManager.cs` 코드 변경 없이 상대 필드 무작위 대상에 3 데미지가 들어감 — 1005/1014와 같은 `dmg` 분기를 값만 다르게 재사용, 데이터만으로 새 카드가 동작하는지 확인하는 회귀 시나리오 |
| 15 | (가상) CSV `effect`에 `Att%2`처럼 지원하지 않는 연산자를 넣고 로드 | 콘솔에 `[Table] CardTableData.effect 알 수 없는 조각` 로그가 남고, 그 카드는 해당 조각만 빠진 채(다른 정상 조각이 있다면 그것만) 동작 — 게임이 죽지 않음 |
| 16 | [치트 에디터](plan-ingame-cheat.md)로 컴퓨터 필드(1~3번) 중 한 슬롯에 1001(`Att/2,Hp/2`, `EnemyRandom`)을 배치하고 같은 key로 "머지" | `slotIndex`가 1~3이므로 `OwnFieldRange`=컴퓨터(1~3), `OpponentFieldRange`=유저(4~6) — `EnemyRandom` 효과가 유저 필드(4~6)의 무작위 대상에 적용됨(컴퓨터 자신의 필드에는 적용되지 않음) |
| 17 | 유저 필드에 1000(대지의 왕) 단 한 장만 있는 상태에서(다른 슬롯은 비어 있음) 같은 key로 병합 | `existing` 제외 후 후보가 없음 → `Att*2`가 적용되지 않음(자기 자신에게도 적용되지 않고 조용히 스킵) |
| 18 | 유저 필드에 1013(뒤집힌 별, `AllyAll`) 한 장만 있는 상태에서 같은 key로 병합 | `AllyAll`은 자기 자신을 제외하지 않으므로 `existing`도 `Att+1` 대상에 포함됨 |

---

## 구현 시 주의사항

- `TriggerMergeAbility`/`MergeCardIntoSlot`은 `slotIndex`(병합이 실제로 일어난 필드 절대 번호)를 반드시 받아 `OwnFieldRange`/`OpponentFieldRange`로 "내 필드"/"상대 필드"를 판정한다 — `UserFieldStart`/`ComputerFieldStart`를 직접 하드코딩해 "Ally=유저"로 단정하면 컴퓨터 필드에서 병합이 일어났을 때 상대/아군이 뒤바뀐다(2번 결정 참고).
- `TriggerMergeAbility`/`ApplyClausesToFriend`/`ApplyClausesToBase`는 어디에도 카드 key를 하드코딩하지 않는다 — 새 카드를 추가할 때 기존 `effect` 문법과 `scope` 조합으로 표현되면 `CardTable.csv`만 고치고, 표현이 안 되면 그때 `CardEffectClauseKind`/`CardAbilityScope`나 `CardEffectParser`의 패턴을 늘린다. 이 구분을 지키지 않고 카드 key나 이름 붙은 effect enum으로 분기하는 코드를 추가하면 5번 결정의 설계 의도가 깨진다.
- `Att`/`Hp`(스탯 증감)와 `dmg`/`heal`(전투식 변화)을 절대 같은 메서드로 합치지 않는다 — `dmg`를 `AddHp(-value)`로 처리하면 방어막이 피해를 막지 못하고 `MaxHp`가 피해로도 깎이는 버그가 되고, 반대로 `Hp-n`(저하)을 `TakeDamage`로 처리하면 방어막이 스탯 저하까지 막아버리는 의도치 않은 상호작용이 생긴다.
- `spawn+key,att=n,hp=n`은 콤마를 자기 내부 구분자로 쓴다 — 다른 조각과 콤마로 나열해 함께 쓰지 않는다(파서가 전체 문자열 단위로 먼저 매치하므로, 섞어 쓰면 의도와 다르게 해석되거나 파싱 실패로 이어진다).
- 이종 합체(`target=All`은 낸 카드 역할, `target=Any`는 필드 카드 역할 — [친구카드 합체 계획](plan-ingame-merge.md) 참고)로 서로 다른 두 카드가 합쳐져도 발동하는 능력은 항상 "필드에 남는 카드(`existing`)"의 `effect`/`scope`다 — `existing.Key`로 조회한 `CardTableData`를 그대로 쓰면 자동으로 성립한다.
- `AddHp`/`MultiplyHp`/`DivideHp`는 `MaxHp`도 함께 갱신해야 한다 — 빠뜨리면 `HealToMax`가 잘못된 값으로 회복시킨다(스탯 증감 계열은 `MaxHp`도 같이, `TakeDamage`/`Heal`은 `MaxHp` 불변).
- 1010/1018은 이 문서만으로 끝나지 않는다 — `ResolveAttackRoutine`(`plan-ingame-attack.md` 소유 파일)을 실제로 고쳐야 동작한다. 이 문서의 8번 결정은 설계이고, 구현 체크리스트에 그 문서 쪽 작업을 별도로 남긴다.
- `ApplyClausesToFriend`로 능력 피해를 적용할 때 대상이 이미 다른 효과로 죽어 `Destroy` 대기 중인 프레임이면(같은 프레임에 여러 전체 효과가 겹치는 극단적 경우) `GetFieldFriends`가 반환한 스냅샷 리스트를 순회 중이라 문제 없다 — `Destroy`는 다음 프레임에 실제 파괴되므로 `IsDead` 체크와 상태 갱신 자체는 즉시 유효하다.
- `CardTable.csv`의 `effect`에 오타를 내면(철자가 틀리거나 지원하지 않는 문법) 게임이 죽지 않고 그 조각만 조용히 버려진다 — CSV를 고친 뒤에는 항상 콘솔에서 `[Table] CardTableData.effect 알 수 없는 조각` 로그가 없는지 확인한다. `CardEffectParser.Parse`가 `CardTable.OnLoaded()` 시점에 전부 실행되므로 게임 시작 직후 콘솔만 봐도 CSV 오타를 전부 잡을 수 있다.

---

## 구현 후 체크리스트

- [x] `CardTable.cs`: `CardCondition.Die`, `CardStat`, `CardEffectClauseKind`, `CardEffectClause`, `CardEffectParser`, `CardAbilityScope`(7종) 추가, `CardTableData.animal`/`.sheets`/`.effect`/`.scope`/`.EffectClauses` 필드 추가, `CardTable.OnLoaded()` 오버라이드 추가
- [x] `CardTable.csv`: `effect`/`scope` 컬럼 추가, 20종 전체에 값 채움("능력 분류" 표 기준 — cond=Except/None 카드도 `None`으로 명시)
- [x] `BaseStone.cs`: `Heal(int)` 추가
- [x] `Friend.cs`: `MaxHp`/`HasShield`/`HasRevived`/`SpawnMark`(`SpawnMarkInfo` 중첩 클래스)류, `AddAtt`/`MultiplyAtt`/`DivideAtt`/`AddHp`/`Heal`/`MultiplyHp`/`DivideHp`/`HealToMax`/`AddShield`/`ApplySpawnMark`/`TryRevive`/`OverrideStats` 추가, `SetKey`/`TakeDamage`/`MergeWith` 수정
- [x] `InGameSceneManager.cs`: `GetFieldFriends`/`PickRandomTargetable`/`TriggerMergeAbility`/`ApplyClausesToFriend`/`ApplyClausesToBase`/`TryHandleDeath`/`SpawnFriendDirectly` 추가, `MergeCardIntoSlot`에 `MultiplierMerge` 키워드 기반 배수 반영 + `TriggerMergeAbility` 호출 연결
- [x] `plan-ingame-attack.md`의 `ResolveAttackRoutine`을 `TryHandleDeath` 호출로 확장(1010/1018 실제 동작에 필요) — 해당 문서에도 변경 기록
- [x] `CardTable.csv` → `CardTable.asset` 재생성 — 실제 Play 모드 테스트로 확인됨
- [x] "내 필드"/"상대 필드" 고정 버그 수정 — `slotIndex` 기준 동적 판정 도입(2번 결정)
- [x] `AllyRandom`이 방금 병합된 카드 자기 자신을 무작위 대상으로 뽑던 문제 수정 — 요청자 확인 후 `candidates.Remove(existing)`로 후보에서 제외(5번 결정 참고). `AllyAll`(전체 적용)은 그대로 자기 자신을 포함
- [ ] 테스트 시나리오 18개 재검증(Unity Play 모드에서 확인 필요 — `slotIndex` 기반 수정, 자기 자신 제외 수정 모두 반영 후. 14번은 데이터 추가만으로 새 카드가 동작하는지, 15번은 잘못된 데이터가 게임을 죽이지 않는지, 16번은 컴퓨터 필드에서 병합해도 Ally/Enemy가 뒤바뀌지 않는지, 17/18번은 자기 자신 제외가 `AllyRandom`/`AllyAll`에서 다르게 동작하는지 확인하는 회귀 테스트)
- [ ] (추후) 방어막/포자감염 마킹 상태 시각 표시
- [ ] (추후) `effect` 문법이 부족해지면(괄호, 조건식, `spawn` 다중 콤보 등) 그때 `CardEffectParser` 확장
