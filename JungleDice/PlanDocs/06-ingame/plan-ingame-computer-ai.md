# PvE 컴퓨터 AI 알고리즘 구현 계획

> **⚠️ 대체됨 — 이 문서의 설계(6카테고리/3그룹, 조건 A+B/C, `CategoryValue` 가치 공식)는 실제로 구현되지 않았다.** 실제 구현은 [PvE AI 카드 우선순위 설계](../99-요청문서/PvE_AI_카드_우선순위_설계.md)의 11카테고리 순위 매트릭스(`UrgencyState`/`ActionPriorityTable`) 방식을 따른다 — 코드는 `Assets/Scripts/InGame/ComputerAI.cs`, `Assets/Scripts/Data/Table/ActionPriorityTable.cs`, `Assets/Tables/Source/ActionPriorityTable.csv`. 이 문서는 그 이전 설계안이 어떤 모양이었는지 참고용으로만 남겨둔다.
>
> 상위 문서(원안, 보류): [PvE AI 알고리즘 설계(초안)](../99-요청문서/PvE_AI_알고리즘_설계.md) — 1/2/3그룹 판단 순서, 위급 신호 A+B/C 케이스, 카테고리별 가치 공식을 정의한 설계 원안. 이 문서는 그 설계를 실제 `InGameSceneManager`/`CardTable` 코드 구조로 옮기는 구현 계획이었다.
> 관련 문서: [턴 진행 계획](plan-ingame-turnsystem.md) (`ComputerAdvanceAfterDelay`의 `PlayFriend` 분기가 이 문서의 삽입 지점), [핸드/필드 배치 계획](plan-ingame-handfield.md), [친구카드 합체 계획](plan-ingame-merge.md) (`CanMerge`/`MergeCardIntoSlot` 재사용), [친구카드 능력 계획](plan-ingame-ability.md) (`CardCondition`/`CardTarget`/`CardAbilityScope`/`CardEffectClause` 정의를 카테고리 분류·가치 공식의 입력으로 사용), [필드 슬롯 치트 에디터 계획](plan-ingame-cheat.md) (위급 신호 A+B/C 케이스를 재현할 필드 상태를 강제로 세팅하는 데 활용)
> 의존 관계: `JungleDice.Data.Table.CardTable`(카드 능력 데이터), `JungleDice.InGame.InGameSceneManager`(필드/덱/베이스 상태, `GetFieldFriends`/`CanMerge`/`MergeCardIntoSlot`/`OwnFieldRange`/`OpponentFieldRange`), `JungleDice.InGame.Friend`/`BaseStone`(스탯 조회)
> 범위: 컴퓨터의 `PlayFriend` 단계에서 원 설계 문서의 1/2/3그룹 순서를 그대로 따라 "무엇을 어디에 낼 것인가"를 결정하는 로직만 다룬다. `RollAttacker`/`RollTarget` 자체(이미 소유자 무관 완전 랜덤)는 범위 밖. 위급 신호 임계값(예: C 케이스의 확률 임계치)의 최종 수치 튜닝은 원 설계 문서의 "미결 사항"과 동일하게 이번 문서에서도 확정하지 않고 플레이테스트로 넘긴다.

---

## 배경

[턴 진행 계획](plan-ingame-turnsystem.md)이 비워둔 지점은 정확히 하나다 — `EnterPhase(TurnPhase.PlayFriend)`에서 컴퓨터 턴이면 `DrawHandCards()`조차 호출되지 않고, `ComputerAdvanceAfterDelay`는 2초 뒤 다음 단계로 넘어갈 뿐이다. 이 문서는 [PvE AI 알고리즘 설계(초안)](../99-요청문서/PvE_AI_알고리즘_설계.md)이 정의한 판단 순서를 그 지점에 그대로 구현한다.

```
매 턴 AI 행동 결정:
  1. 위급 신호 체크 (A+B / C) → 해당되면 각 케이스 알고리즘 실행, 종료
  2. 평시: a. 2그룹(확정 이득 정렬) → b. 3그룹(슬롯 배분)
```

원 설계 문서가 전제하는 "정보 제한"도 코드 레벨에서 지켜야 한다 — 상대(유저) 손패는 **장수만** 읽고 내용(`key`)은 읽지 않는다. 기술적으로는 같은 프로세스 안에 있어 `_userDeck`/핸드 슬롯의 실제 카드를 얼마든지 들여다볼 수 있지만, 그렇게 하면 원 설계의 "상대 손패 내용은 모름 → 초기하분포로 추정" 이라는 판단 축 자체가 무의미해진다. 이 제약은 아래 전 구간에서 반복해 강조한다.

---

## 설계 목표

- 원 문서의 그룹/케이스 구조(1그룹 위급 A+B·C, 2그룹 확정 이득, 3그룹 슬롯 배분)를 코드에서도 동일한 이름의 메서드로 분리해 — 문서를 읽으면 코드 진입점을 바로 찾을 수 있게 한다.
- 카드 20종을 원 문서가 정의한 6개 카테고리(즉발 데미지/방어/회복/견제/면역/성장) + 3그룹 전용 2개 그룹(조커형/필러형)으로 **데이터 기반 규칙**에 의해 자동 분류한다 — 카드마다 `switch(key)` 분기를 만들지 않는다.
- 가치 공식은 원 문서의 표를 그대로 코드로 옮기되, 공식에 필요한 값(상대 최강 카드 공격력, 내 체력 비율, 예상 피격 확률 등)은 실제 게임 상태에서 조회한 값을 쓴다.
- 상대 손패 관련 확률(C 케이스, 면역 카테고리)은 초기하분포로 계산하고, 계산에 쓰는 입력은 "장수"와 "이미 필드에 드러난 카드 수"뿐이다 — 손패 내용을 직접 읽지 않는다.
- 판단 로직은 Unity 컴포넌트에 최소한으로 의존한다 — 관찰(observation) 스냅샷을 만든 뒤에는 순수 계산만 하도록 분리해, 유닛 테스트와 수치 튜닝을 쉽게 한다.
- 실행(실제 배치/병합)은 기존 정상 경로(`CanMerge`/`MergeCardIntoSlot`/`Instantiate`+`SetKey`)를 그대로 재사용한다.

---

## 핵심 설계 결정

### 0. 관찰 스냅샷 — 원 문서의 "고려 요소"를 값 객체로 그대로 옮김

원 문서의 "고려 요소(관찰 대상)" 절을 그대로 필드로 옮긴다. 매 `PlayFriend` 진입 시 한 번 스냅샷을 만들고, 이후 모든 판단 함수는 이 스냅샷만 참조한다(게임 상태를 직접 다시 조회하지 않음 — 판단 도중 상태가 바뀌지 않는다는 보장).

```csharp
public readonly struct FriendSnapshot
{
    public readonly int SlotIndex;
    public readonly int Key;
    public readonly int Att;
    public readonly int CurrentHp;
    public readonly int MaxHp;

    public FriendSnapshot(int slotIndex, int key, int att, int currentHp, int maxHp)
    {
        SlotIndex = slotIndex; Key = key; Att = att; CurrentHp = currentHp; MaxHp = maxHp;
    }
}

public readonly struct ComputerObservation
{
    // AI 쪽
    public readonly int AiHp;
    public readonly int AiMaxHp;
    public readonly List<FriendSnapshot> AiField;       // 컴퓨터 필드(1~3)에 실제로 있는 카드
    public readonly List<int> AiEmptySlotIndices;       // 비어있는 컴퓨터 슬롯의 절대 번호(1~3) — ChooseBestFillAction이 그대로 배치 대상으로 씀
    public readonly List<int> AiHand;                   // AI 손패는 내용까지 안다(당연히 자기 패)
    public readonly int AiDeckRemainingCount;

    // 플레이어 쪽 — 손패는 "장수"만, 필드는 눈에 보이므로 전부
    public readonly int PlayerHp;
    public readonly List<FriendSnapshot> PlayerField;
    public readonly int PlayerHandCount;                 // 내용 모름, 장수만
    public readonly int PlayerDeckRemainingCount;         // 카드 수는 알 수 있음(내용은 모름)

    public readonly int InitialComputerDeckSize;                // "예상 잔여 턴수 가중치" 정규화 기준

    public int AiEmptySlotCount => AiEmptySlotIndices.Count; // 계산 프로퍼티 — 별도 필드로 중복 저장하지 않음(값 불일치 방지)
}
```

`InGameSceneManager`가 이 스냅샷을 만드는 코드 — `GetComponentInParent<FieldSlot>()`로 슬롯 번호를 역으로 찾지 않고, 슬롯 인덱스를 직접 순회하며 스냅샷을 만든다(공격 연출 중 `Friend`가 `_attackLayer`로 일시 재부모화되는 타이밍과 겹칠 여지를 원천 차단):

```csharp
private ComputerObservation BuildObservation()
{
    var aiField = SnapshotFieldRange(ComputerFieldStart, ComputerFieldEnd);
    var playerField = SnapshotFieldRange(UserFieldStart, UserFieldEnd);

    var aiEmptySlots = new List<int>();
    for (int i = ComputerFieldStart; i <= ComputerFieldEnd; i++)
        if (!GetFieldSlot(i).IsOccupied) aiEmptySlots.Add(i);

    int playerHandCount = _handSlots.Count(s => s.IsOccupied); // 내용은 읽지 않고 개수만

    return new ComputerObservation(
        aiHp: _computerBase.CurrentHp, aiMaxHp: _computerBase.MaxHp,
        aiField: aiField, aiEmptySlotIndices: aiEmptySlots,
        aiHand: new List<int>(_computerHand), aiDeckRemainingCount: _computerDeck.Count,
        playerHp: _userBase.CurrentHp, playerField: playerField,
        playerHandCount: playerHandCount, playerDeckRemainingCount: _userDeck.Count,
        initialComputerDeckSize: _computerInitialDeckSize);
}

private List<FriendSnapshot> SnapshotFieldRange(int fromIndex, int toIndex)
{
    var result = new List<FriendSnapshot>();
    for (int i = fromIndex; i <= toIndex; i++)
    {
        var slot = GetFieldSlot(i);
        if (!slot.IsOccupied) continue;
        var friend = slot.GetComponentInChildren<Friend>();
        result.Add(new FriendSnapshot(i, friend.Key, friend.Att, friend.CurrentHp, friend.MaxHp));
    }
    return result;
}
```

`_computerInitialDeckSize`는 `SetupDecks()`에서 `_computerDeck = DeckBuilder.Build(...)` 직후 `_computerDeck.Count`를 캡처해두는 필드 하나만 추가하면 된다(성장 카테고리 가치식에서만 쓰임, 5번 항목 참고). `PlayerMaxHp`/`EnemyEmptySlotCount`/`TotalOccupiedSlots`는 실제로 어떤 가치 공식에서도 쓰이지 않아(방어형 가치식은 3그룹이 정의한 고정값 `1/6`을 그대로 재사용) 스냅샷에서 뺐다 — 안 쓰는 필드를 미리 만들어두지 않는다.

### 1. 1그룹 — 위급 신호 A+B 케이스

**조건 A**: 필드 전체(내 + 상대) 친구 중 최댓값 Att ≥ 내 체력의 절반.
**조건 B**: 내 필드 빈 슬롯 ≥ 2.

```csharp
private const float ConditionAHpRatio = 0.5f;
private const int ConditionBMinEmptySlots = 2;

private static bool ConditionA(ComputerObservation obs)
{
    int maxAtt = obs.AiField.Concat(obs.PlayerField).Select(f => f.Att).DefaultIfEmpty(0).Max();
    return maxAtt >= obs.AiHp * ConditionAHpRatio;
}

private static bool ConditionB(ComputerObservation obs) => obs.AiEmptySlotCount >= ConditionBMinEmptySlots;
```

대응 순서(원 문서 그대로 — 필드 채우기 최우선 → 상대 필드 정리 → 체력관리):

```csharp
private ComputerAction? HandleUrgentAB(ComputerObservation obs) =>
    ChooseBestFillAction(obs)          // 1. 내 필드 슬롯 채우기(3그룹 배치 로직 재사용)
    ?? ChooseBestPlayerClearAction(obs) // 2. 능력으로 상대 필드 정리(즉발데미지/견제 카테고리)
    ?? ChooseBestHealAction(obs);      // 3. 모험가 체력관리(회복 카테고리)
```

`ChooseBestFillAction`/`ChooseBestPlayerClearAction`/`ChooseBestHealAction`은 각각 3번(2그룹 카테고리 가치)과 5번(3그룹 배분) 항목에서 정의하는 헬퍼를 그대로 재사용한다 — A+B 케이스가 "따로" 구현해야 할 새 로직은 조건 판정뿐이고, 실제 행동 선택은 2/3그룹 헬퍼에 위임한다.

### 2. 1그룹 — 위급 신호 C 케이스와 초기하분포

**조건**: 내 체력 ≤ 10 **AND** 상대가 "모험가 직접 공격 능력"(`scope == EnemyBase` + `Damage` 조각, 현재 테이블에서는 개구리 1002 하나뿐)을 보유했거나 보유 가능성이 높음.

"보유 가능성"은 상대 손패 내용을 모르는 채로 판단해야 하므로 초기하분포를 쓴다 — 모집단은 "아직 우리가 확인하지 못한 상대측 카드 전체"(상대 덱 잔여 + 상대 손패, 장수만 앎), 그 안의 목표 카드 수는 "스테이지 카드풀 총 장수 − 이미 상대 필드에 드러난 장수", 표본 크기는 "상대 손패 장수".

```csharp
private const int DirectAttackDangerHpThreshold = 10;
private const int DirectAttackKey = 1002; // scope=EnemyBase + Damage인 유일한 카드(개구리) — CardTable에서 이런 카드가 늘어나면 아래 필터로 자동 확장
private const double DirectAttackThreatThreshold = 0.5; // 미결 사항 — 플레이테스트로 조정

// scope=EnemyBase + Damage 조각을 가진 카드 key를 CardTable에서 동적으로 찾는다(하드코딩 대신 데이터 기반 판정)
private static bool IsDirectAttackKey(int key)
{
    var data = CardTable.Instance.Get(key);
    return data.cond == CardCondition.Merge && data.scope == CardAbilityScope.EnemyBase
        && data.EffectClauses.Any(c => c.Kind == CardEffectClauseKind.Damage);
}

private bool ConditionC(ComputerObservation obs)
{
    if (obs.AiHp > DirectAttackDangerHpThreshold) return false;
    if (obs.PlayerField.Any(f => IsDirectAttackKey(f.Key))) return true; // 이미 필드에 나와 있으면 확률 계산 없이 즉시 위협 확정

    double threatProbability = EstimatePlayerHasAtLeast(DirectAttackKey, wantCount: 2, obs);
    return threatProbability >= DirectAttackThreatThreshold;
}
```

초기하분포 계산(면역 카테고리에서도 재사용, 4번 항목):

```csharp
// N=population(미확인 상대 카드 전체), K=그 안의 key 카드 수, n=표본 크기(상대 손패 장수), k=원하는 최소 장수
private double EstimatePlayerHasAtLeast(int key, int wantCount, ComputerObservation obs)
{
    int totalCopies = CardTable.Instance.Get(key).sheets; // 카드별 총 장수(나비류처럼 10이 아닐 수 있음, 아래 주의사항 참고)
    int observedOnField = obs.PlayerField.Count(f => f.Key == key); // 이미 드러난 장수 — 더 이상 "미확인"이 아님
    int unseenKeyCount = Mathf.Max(0, totalCopies - observedOnField);
    int unseenPopulation = obs.PlayerDeckRemainingCount + obs.PlayerHandCount;
    int sampleSize = obs.PlayerHandCount;

    double pZero = HypergeometricPmf(unseenPopulation, unseenKeyCount, sampleSize, 0);
    double pOne = wantCount >= 2 ? HypergeometricPmf(unseenPopulation, unseenKeyCount, sampleSize, 1) : 0.0;
    return 1.0 - pZero - pOne;
}

private static double HypergeometricPmf(int population, int successStates, int sampleSize, int successCount)
{
    if (successCount < 0 || successCount > successStates || successCount > sampleSize) return 0.0;
    if (sampleSize - successCount > population - successStates) return 0.0;
    return Combination(successStates, successCount)
         * Combination(population - successStates, sampleSize - successCount)
         / Combination(population, sampleSize);
}

// 큰 값 오버플로를 피하려고 (n-i)/(i+1) 비율을 누적 곱하는 방식으로 계산 — 결과가 크지 않아 double로 충분
private static double Combination(int n, int k)
{
    if (k < 0 || k > n) return 0.0;
    k = Mathf.Min(k, n - k);
    double result = 1.0;
    for (int i = 0; i < k; i++)
        result *= (double)(n - i) / (i + 1);
    return result;
}
```

대응 순서(원 문서 그대로 — 확률/필드 관찰로 위협을 판단한 뒤, 정리 → 체력관리 → 슬롯 채우기 순서. **A+B와 정반대로 슬롯 채우기가 맨 마지막**이라는 점에 주의):

```csharp
private ComputerAction? HandleUrgentC(ComputerObservation obs) =>
    ChooseBestPlayerClearAction(obs)  // 3. 능력으로 상대 필드 정리
    ?? ChooseBestHealAction(obs)     // 4. 모험가 체력관리
    ?? ChooseBestFillAction(obs);    // 5. 내 필드 슬롯 채우기(뒤로 미룸)
```

(1~2단계인 "확률 계산"과 "상대 필드 이미 있는지 체크"는 `ConditionC` 자체가 이미 수행하므로 별도 메서드가 없다.)

### 3. 카드 20종 → 카테고리 자동 분류

원 문서의 6개 카테고리 + 3그룹 전용 2개(조커형/필러형)를 카드 key가 아니라 `CardCondition`/`CardTarget`/`CardAbilityScope`/`CardEffectClauseKind` 조합으로 판정한다 — 새 카드가 테이블에 추가돼도 이 함수는 수정할 필요가 없다.

```csharp
public enum AbilityCategory { InstantDamage, Defense, Heal, Debuff, Immunity, Growth, Joker, Filler }

public static AbilityCategory Classify(CardTableData data)
{
    if (data.cond == CardCondition.Except) return AbilityCategory.Immunity;

    if (data.cond == CardCondition.None || data.cond == CardCondition.Die)
        return data.target is CardTarget.All or CardTarget.Any ? AbilityCategory.Joker : AbilityCategory.Filler;

    // cond == Merge
    if (data.EffectClauses.Any(c => c.Kind == CardEffectClauseKind.Keyword && c.Keyword == "Shield"))
        return AbilityCategory.Defense;
    if (data.EffectClauses.Any(c => c.Kind is CardEffectClauseKind.Heal or CardEffectClauseKind.HealToMax))
        return AbilityCategory.Heal;
    if (data.EffectClauses.Any(c => c.Kind == CardEffectClauseKind.Damage))
        return AbilityCategory.InstantDamage;
    if (data.EffectClauses.Any(c => c.Kind == CardEffectClauseKind.Stat && c.Op is '-' or '/'))
        return AbilityCategory.Debuff;
    // 남은 경우: Att/Hp 증가(+,*), MultiplierMerge, Spawn — 전부 성장형으로 묶는다(아래 표/근거 참고)
    return AbilityCategory.Growth;
}

// 병합 가능 판정 — key만으로 계산되는 순수 로직이라 ComputerAI가 직접 갖는다.
// InGameSceneManager.CanMerge(Friend, int)는 이 메서드에 위임하도록 리팩터링해 판정 로직이 하나만 존재하게 한다(치트 에디터 문서의 CanMerge/MergeCardIntoSlot 추출과 같은 이유).
public static bool CanMerge(int existingKey, int mergeKey)
{
    var existingData = CardTable.Instance.Get(existingKey);
    var data = CardTable.Instance.Get(mergeKey);

    bool sameKind = existingKey == mergeKey;
    bool existingAcceptsAnything = existingData.target == CardTarget.Any; // 필드 카드가 베이스 역할(하이에나류)
    bool mergeJoinsAnything = data.target == CardTarget.All; // 낼 카드가 무엇에든 합쳐지는 역할(블루베리류)
    return sameKind || existingAcceptsAnything || mergeJoinsAnything;
}
```

| key | 동물 | cond/target/scope 요약 | 분류 결과 | 비고 |
|---|---|---|---|---|
| 1000 | 코끼리 | merge, AllyRandom, `Att*2` | 성장 | |
| 1001 | 거미 | merge, EnemyRandom, `Att/2,Hp/2` | 견제 | |
| 1002 | 개구리 | merge, EnemyBase, `dmg+2` | 즉발 데미지 | C 케이스의 `DirectAttackKey` |
| 1003 | 독수리 | except | 면역 | |
| 1004 | 블루베리 | none, target=All | 조커형 | |
| 1005 | 박쥐 | merge, EnemyRandom, `dmg+1` | 즉발 데미지 | |
| 1006 | 고래 | merge, AllyBase, `heal+2` | 회복 | |
| 1007 | 거북이 | merge, Self, `Shield` | 방어 | |
| 1008 | 코뿔소 | none, target=Same | 필러형 | 발동 효과 없음, 스탯만 높음 |
| 1009 | 바오밥나무 | merge, AllyRandom, `Hp*2` | 성장 | |
| 1010 | 버섯 | merge, AllyRandom, `spawn` | 성장 | 사후 조건부 가치라 "성장"만큼 불확실 — 4번 항목 `SpawnUncertaintyDiscount` |
| 1011 | 까마귀 | merge, `MultiplierMerge` | 성장 | 배수 병합 자체가 스탯 증가이므로 성장으로 분류 |
| 1012 | 나비 | none, target=Same(15장) | 필러형 | |
| 1013 | 염소 | merge, AllyAll, `Att+1` | 성장 | |
| 1014 | 고릴라 | merge, EnemyAll, `dmg+1` | 즉발 데미지 | |
| 1015 | 달팽이 | merge, AllyAll, `Shield` | 방어 | |
| 1016 | 해파리 | merge, AllyAll, `Hp+1` | 성장 | |
| 1017 | 오리 | merge, AllyRandom, `heal+max` | 회복 | |
| 1018 | 고사리 | **die**, `spawn`(부활) | 필러형 | 합체 시 발동 효과 없음(=필러) + `HasFreeRevive` 플래그(5번 항목에서 슬롯 배치 우선순위에만 별도 반영) |
| 1019 | 하이에나 | none, target=Any | 조커형 | |

**분류 결과 검증**: 원 문서가 명시적으로 예시를 든 카드(즉발 데미지: 개구리·박쥐·고릴라 / 방어: 거북이·달팽이 / 회복: 고래·오리 / 견제: 거미 / 면역: 독수리 / 성장: 코끼리·바오밥나무·염소·해파리·까마귀 / 조커: 블루베리·하이에나) 전부와 위 표가 정확히 일치한다. 원 문서에 언급되지 않은 4종(코뿔소·나비·버섯·고사리)만 이번 분류 규칙으로 새로 확정했다.

### 4. 2그룹 — 카테고리별 가치 공식

원 문서의 공식을 그대로 옮기되, "예상 피격 확률"은 3그룹 절이 정의한 `1/6`을 그대로 재사용한다(원 문서: "이 값은 2그룹의 방어형 가치 공식에도 재사용된다").

```csharp
private const float SlotHitProbability = 1f / 6f;     // "슬롯_피격_확률 = 1/6" (원 문서 3그룹)
private const float FinisherPriorityMultiplier = 1000f; // "상대 체력이 데미지 이하면 최우선"을 값 스케일로 강제
private const float EagleImmunityBaseValue = 4f;        // 견제형(거미) 평균 스탯 손실 근사치
private const float SpawnUncertaintyDiscount = 0.5f;    // 포자감염류는 사망을 전제로 해 성장보다도 불확실

private static float CategoryValue(int handKey, AbilityCategory category, ComputerObservation obs, FriendSnapshot mergeTarget)
{
    var data = CardTable.Instance.Get(handKey);
    return category switch
    {
        AbilityCategory.InstantDamage => InstantDamageValue(data, obs),
        AbilityCategory.Defense => DefenseValue(mergeTarget, obs),
        AbilityCategory.Heal => HealValue(data, obs),
        AbilityCategory.Debuff => DebuffValue(data, obs),
        AbilityCategory.Immunity => (float)ImmunityValue(obs),
        AbilityCategory.Growth => GrowthValue(data, mergeTarget, obs),
        _ => 0f, // 조커형/필러형은 2그룹 대상이 아님(3그룹 전용)
    };
}

// 즉발 데미지: 기본_데미지 × 막타_보너스(상대 체력이 데미지 이하면 최우선)
private static float InstantDamageValue(CardTableData data, ComputerObservation obs)
{
    int baseDamage = data.EffectClauses.Where(c => c.Kind == CardEffectClauseKind.Damage).Sum(c => c.Value);
    bool finishesAdventurer = data.scope == CardAbilityScope.EnemyBase && obs.PlayerHp <= baseDamage;
    return baseDamage * (finishesAdventurer ? FinisherPriorityMultiplier : 1f);
}

// 방어: max(예상_다음_피격_데미지, 생존_보너스) × 예상_피격_확률
private static float DefenseValue(FriendSnapshot existing, ComputerObservation obs)
{
    float expectedNextHitDamage = obs.PlayerField.Count == 0 ? 0f : (float)obs.PlayerField.Average(f => f.Att);
    float survivalBonus = existing.Att + existing.CurrentHp; // 이 카드 자체의 보존 가치
    return Mathf.Max(expectedNextHitDamage, survivalBonus) * SlotHitProbability;
}

// 회복: 기본_회복량 × (1 - 내_체력_비율) — AllyBase면 모험가 체력, AllyRandom이면 대상 친구 체력 기준
private static float HealValue(CardTableData data, ComputerObservation obs)
{
    bool healsBase = data.scope == CardAbilityScope.AllyBase;
    var targetable = FilterTargetable(obs.AiField);
    float ratio = healsBase ? SafeRatio(obs.AiHp, obs.AiMaxHp) : AverageHpRatio(targetable);
    int baseHeal = data.EffectClauses.Any(c => c.Kind == CardEffectClauseKind.HealToMax)
        ? (healsBase ? obs.AiMaxHp - obs.AiHp : (int)AverageMissingHp(targetable))
        : data.EffectClauses.Where(c => c.Kind == CardEffectClauseKind.Heal).Sum(c => c.Value);
    return baseHeal * (1f - ratio);
}

// 견제: 기본_감소량 × (상대_최강카드_공격력 / 내_체력)
private static float DebuffValue(CardTableData data, ComputerObservation obs)
{
    var targetable = FilterTargetable(obs.PlayerField);
    float baseReduction = data.EffectClauses.Where(c => c.Kind == CardEffectClauseKind.Stat && c.Op is '-' or '/')
        .Sum(c => c.Op == '-' ? c.Value : AverageStatOf(targetable, c.Stat) * (1f - 1f / c.Value));
    float playerStrongestAtt = obs.PlayerField.Count == 0 ? 0f : obs.PlayerField.Max(f => f.Att);
    return baseReduction * (playerStrongestAtt / Mathf.Max(1, obs.AiHp));
}

// 면역: 기본값 × 상대_견제형_카드_보유_추정확률(초기하분포) — 견제형은 현재 거미(1001) 하나뿐
private static double ImmunityValue(ComputerObservation obs) => EagleImmunityBaseValue * EstimatePlayerHasAtLeast(1001, wantCount: 1, obs);

// 성장: 기본_증가량 × 예상_잔여_턴수_가중치(덱+손패 잔여량 / 초기 덱 크기)
private static float GrowthValue(CardTableData data, FriendSnapshot mergeTarget, ComputerObservation obs)
{
    var targetable = FilterTargetable(obs.AiField);
    float baseGrowth = 0f;
    foreach (var c in data.EffectClauses)
    {
        baseGrowth += c.Kind switch
        {
            CardEffectClauseKind.Stat when c.Op == '+' => c.Value,
            CardEffectClauseKind.Stat when c.Op == '*' => AverageStatOf(targetable, c.Stat) * (c.Value - 1),
            CardEffectClauseKind.Spawn => (c.SpawnAtt + c.SpawnHp) * SpawnUncertaintyDiscount,
            _ => 0f,
        };
    }
    if (data.EffectClauses.Any(c => c.Kind == CardEffectClauseKind.Keyword && c.Keyword == "MultiplierMerge"))
        baseGrowth += mergeTarget.Att + mergeTarget.CurrentHp; // 배수 병합 자체의 증가분 근사

    float remainingTurnWeight = obs.InitialComputerDeckSize <= 0 ? 0f
        : Mathf.Clamp01((obs.AiDeckRemainingCount + obs.AiHand.Count) / (float)obs.InitialComputerDeckSize);
    return baseGrowth * remainingTurnWeight;
}
```

`mergeTarget`은 `FriendSnapshot`(비-nullable)이다 — `CategoryValue`는 항상 실제 병합 후보(`GenerateGroup2Candidates`/`ChooseBestMergeAction`이 순회 중인 `slotFriend`)와 함께 호출되므로 값이 없는 경우 자체가 없다(3그룹의 빈 슬롯 배치는 `ChooseBestFillAction`이 전담하며 `CategoryValue`를 호출하지 않음).

```csharp
// AllyRandom/AllyAll/EnemyRandom/EnemyAll 발동 효과의 대상에서 CardCondition.Except(면역) 카드를 제외한다.
// PickRandomTargetable(InGameSceneManager, 친구카드 능력 계획)이 실제 발동 시 적용하는 필터와 반드시 같은 기준이어야 한다.
private static List<FriendSnapshot> FilterTargetable(List<FriendSnapshot> friends) =>
    friends.Where(f => CardTable.Instance.GetCond(f.Key) != CardCondition.Except).ToList();

private static float AverageHpRatio(List<FriendSnapshot> friends) =>
    friends.Count == 0 ? 0f : friends.Average(f => SafeRatio(f.CurrentHp, f.MaxHp));

private static float AverageMissingHp(List<FriendSnapshot> friends) =>
    friends.Count == 0 ? 0f : friends.Average(f => (float)(f.MaxHp - f.CurrentHp));

private static float AverageStatOf(List<FriendSnapshot> friends, CardStat stat) =>
    friends.Count == 0 ? 0f : friends.Average(f => stat == CardStat.Att ? (float)f.Att : f.CurrentHp);

private static float SafeRatio(int value, int max) => max <= 0 ? 0f : (float)value / max;
```

`HealValue`/`DebuffValue`/`GrowthValue` 모두 `AllyRandom`/`AllyAll`/`EnemyRandom`/`EnemyAll` 스코프의 평균을 낼 때 `FilterTargetable`을 먼저 거친다 — 독수리(1003, `CardCondition.Except`)가 대상 평균 계산에 섞이지 않도록 하기 위함(3번 결정 검증 표, 아래 엣지 케이스 8번 참고). `SafeRatio`는 `MaxHp`가 0인 비정상 상태에서도 0으로 나누지 않도록 하는 방어용 헬퍼다.

### 5. 2그룹 — 정렬과 실행

원 문서: "가치 계산 및 정렬, 상위부터 실행" — 값으로 우선 정렬하고, **동점일 때만** 카테고리 순서(즉발 데미지 > 방어 > 회복 > 견제 > 면역 > 성장)로 타이브레이크한다.

```csharp
private static readonly AbilityCategory[] Group2TieBreakOrder =
{
    AbilityCategory.InstantDamage, AbilityCategory.Defense, AbilityCategory.Heal,
    AbilityCategory.Debuff, AbilityCategory.Immunity, AbilityCategory.Growth,
};

private static ComputerAction? DecideGroup2Action(ComputerObservation obs) =>
    BestCandidate(GenerateGroup2Candidates(obs));

private static ComputerAction? ChooseBestPlayerClearAction(ComputerObservation obs) =>
    BestCandidate(GenerateGroup2Candidates(obs).Where(c => c.category is AbilityCategory.InstantDamage or AbilityCategory.Debuff));

private static ComputerAction? ChooseBestHealAction(ComputerObservation obs) =>
    BestCandidate(GenerateGroup2Candidates(obs).Where(c => c.category == AbilityCategory.Heal));

// 핸드 × 필드 슬롯을 순회하며 CanMerge를 통과한 조합만 후보로 만든다 — DecideGroup2Action과 1그룹의 정리/체력관리 헬퍼가 이 하나의 생성 로직을 공유한다(중복 방지)
private static IEnumerable<(ComputerAction action, AbilityCategory category, float value)> GenerateGroup2Candidates(ComputerObservation obs)
{
    foreach (int handKey in obs.AiHand)
    {
        var data = CardTable.Instance.Get(handKey);
        if (data == null) continue;
        var category = Classify(data);
        if (category is AbilityCategory.Joker or AbilityCategory.Filler) continue; // 확정 발동 효과가 있는 카드만 2그룹 대상

        foreach (var slotFriend in obs.AiField)
        {
            if (!CanMerge(slotFriend.Key, handKey)) continue;
            float value = CategoryValue(handKey, category, obs, slotFriend);
            yield return (new ComputerAction(handKey, slotFriend.SlotIndex, isMerge: true), category, value);
        }
    }
}

private static ComputerAction? BestCandidate(IEnumerable<(ComputerAction action, AbilityCategory category, float value)> candidates)
{
    var ordered = candidates
        .OrderByDescending(c => c.value)
        .ThenBy(c => Array.IndexOf(Group2TieBreakOrder, c.category))
        .ToList();
    return ordered.Count == 0 ? null : ordered[0].action;
}
```

`CanMerge(int existingKey, int mergeKey)`는 3번 결정에서 정의한 key 기반 버전이다 — `FriendSnapshot.Key`끼리 비교하며, `Friend` 컴포넌트를 다시 조회하지 않는다.

### 6. 3그룹 — 슬롯 배분

원 문서: 빈 슬롯은 조커형/필러형(약한 순)으로 우선 채우고 → 빈 슬롯이 없으면 성장/방어형 위주로 병합 → 그래도 없으면 패스.

```csharp
private ComputerAction? ChooseBestFillAction(ComputerObservation obs)
{
    if (obs.AiEmptySlotCount == 0 || obs.AiHand.Count == 0) return null;

    // 조커형/필러형을 스탯 합이 낮은 순으로 우선(강한 카드를 방어용으로 낭비하지 않기 위함), 없으면 손패 맨 앞 카드
    var fillerCandidates = obs.AiHand
        .Where(key => { var d = CardTable.Instance.Get(key); return d != null && Classify(d) is AbilityCategory.Joker or AbilityCategory.Filler; })
        .OrderBy(key => { var d = CardTable.Instance.Get(key); return d.att + d.hp; })
        .ToList();

    int chosenKey = fillerCandidates.Count > 0 ? fillerCandidates[0] : obs.AiHand[0];
    return new ComputerAction(chosenKey, obs.AiEmptySlotIndices[0], isMerge: false);
}
```

빈 슬롯 인덱스는 `ComputerAI`가 직접 찾지 않는다 — `ComputerAI`는 `GetFieldSlot`/`ComputerFieldStart` 같은 `InGameSceneManager`의 필드·상수에 접근할 수 없는 순수 계산 계층이므로(0번 결정), `BuildObservation`이 이미 채워둔 `obs.AiEmptySlotIndices[0]`을 그대로 쓴다.

```csharp
private ComputerAction? DecideGroup3Action(ComputerObservation obs) =>
    ChooseBestFillAction(obs) ?? ChooseBestMergeAction(obs, AbilityCategory.Growth, AbilityCategory.Defense);
    // 둘 다 null이면 "패스" — 호출부(7번)가 null을 그대로 반환해 이번 턴은 배치 없이 넘어감

// AbilityCategory[] 로 넘긴 카테고리 안에서만 병합 후보를 찾아 가치순 1위를 고른다. 1그룹의 정리/체력관리 헬퍼와 동일한 형태.
private ComputerAction? ChooseBestMergeAction(ComputerObservation obs, params AbilityCategory[] preferredCategories)
{
    ComputerAction? best = null;
    float bestValue = float.NegativeInfinity;
    foreach (int handKey in obs.AiHand)
    {
        var data = CardTable.Instance.Get(handKey);
        if (data == null) continue;
        var category = Classify(data);
        if (!preferredCategories.Contains(category)) continue;

        foreach (var slotFriend in obs.AiField)
        {
            if (!CanMerge(slotFriend.Key, handKey)) continue;
            float value = CategoryValue(handKey, category, obs, slotFriend);
            if (value > bestValue) { bestValue = value; best = new ComputerAction(handKey, slotFriend.SlotIndex, isMerge: true); }
        }
    }
    return best;
}
```

- 고사리(1018, `HasFreeRevive` 성격)는 `ChooseBestFillAction`의 "필러형" 후보 안에 이미 포함돼 있다 — 별도 우대 로직을 추가하려면 정렬 키에 "죽어도 1회 무료 생존"을 반영해 `OrderBy(... ).ThenByDescending(key => IsDirectAttackKey... )`식으로 확장 가능하지만, 이번 1차 구현에서는 원 문서의 "필러형 우선 채우기"만 그대로 따르고 세부 우대는 다루지 않는다(아래 "이번 범위에서 제외" 참고).

### 7. 최종 진입점

```csharp
private ComputerAction? DecideComputerAction()
{
    var obs = BuildObservation();

    if (ConditionA(obs) && ConditionB(obs)) return HandleUrgentAB(obs);
    if (ConditionC(obs)) return HandleUrgentC(obs);

    return DecideGroup2Action(obs) ?? DecideGroup3Action(obs);
}
```

`EnterPhase(TurnPhase.PlayFriend)`의 컴퓨터 분기:

```csharp
else // _currentOwner == TurnOwner.Computer
{
    RefillComputerHand();
    var action = DecideComputerAction();
    if (action.HasValue) ExecuteComputerAction(action.Value);
}
```

`RefillComputerHand`/`ExecuteComputerAction`/`ComputerAction` struct(`Key`/`SlotIndex`/`IsMerge`)는 손패 관리·실행 전용 로직으로, 판단 로직(1~6번)과 분리해 [파일 구성](#파일-구성)에서 서로 다른 파일에 둔다.

---

## 클래스 구조

```
ComputerAI (신규 파일, InGame/) — Unity 컴포넌트 아님, CardTable 데이터만으로 동작하는 순수 판단 로직
├── AbilityCategory : enum                         ← 3번 결정
├── FriendSnapshot / ComputerObservation : readonly struct  ← 0번 결정
├── ComputerAction : readonly struct                ← 실행 지시(Key/SlotIndex/IsMerge)
├── Classify(CardTableData) : AbilityCategory       ← 3번 결정
├── CanMerge(int existingKey, int mergeKey) : bool  ← 3번 결정, key만으로 계산되는 순수 판정
├── DecideComputerAction(ComputerObservation) : ComputerAction?   ← 7번 결정, 최종 진입점
├── ConditionA/ConditionB/ConditionC(...)            ← 1·2번 결정
├── HandleUrgentAB/HandleUrgentC(...)                ← 1·2번 결정
├── DecideGroup2Action/ChooseBestPlayerClearAction/ChooseBestHealAction/GenerateGroup2Candidates/BestCandidate(...)  ← 5번 결정
├── DecideGroup3Action/ChooseBestFillAction/ChooseBestMergeAction(...)  ← 6번 결정
├── CategoryValue + Instant/Defense/Heal/Debuff/Immunity/Growth Value(...)  ← 4번 결정
├── EstimatePlayerHasAtLeast/HypergeometricPmf/Combination(...)  ← 2번 결정
└── (private) FilterTargetable/AverageHpRatio/AverageMissingHp/AverageStatOf/SafeRatio     ← 공용 수치 헬퍼

InGameSceneManager (기존 파일 수정, InGame/)
├── _computerHand : List<int>                       ← 신규
├── _computerInitialDeckSize : int                   ← 신규, SetupDecks() 직후 캡처
├── RefillComputerHand()                              ← 신규, private
├── BuildObservation() / SnapshotFieldRange(int, int)  ← 신규, private (0번 결정 — Unity 상태 조회는 여기서만)
├── ExecuteComputerAction(ComputerAction)             ← 신규, private
├── CanMerge(Friend, int)                             ← 기존 메서드, 본문을 `ComputerAI.CanMerge(existing.Key, mergeKey)` 위임 한 줄로 교체(3번 결정 — 판정 로직 중복 제거)
├── MergeCardIntoSlot / GetFieldFriends / OwnFieldRange / OpponentFieldRange / GetFieldSlot  ← 기존 그대로, ExecuteComputerAction/BuildObservation이 재사용
└── EnterPhase(TurnPhase.PlayFriend) 컴퓨터 분기      ← 기존 수정, 7번 결정의 3줄 추가
```

`ComputerAI`를 별도 파일/네임스페이스로 완전히 분리하는 이유는 [턴 진행 계획](plan-ingame-turnsystem.md)이 턴 상태 머신을 분리하지 않은 이유("재사용 근거 없음")와 반대다 — 이번엔 판단 메서드가 15개 이상이고 전부 `ComputerObservation` 값만으로 완결되므로(Unity `MonoBehaviour`/필드 접근이 전혀 필요 없음), `InGameSceneManager`에 얹으면 그 거대한 파일이 더 비대해질 뿐 아니라 판단 로직만 따로 유닛 테스트할 방법이 없어진다. `BuildObservation`(관찰)과 `ExecuteComputerAction`(실행)만 Unity 상태에 접근하고, 그 사이(판단)는 순수 함수로 완전히 격리한다.

---

## 파일 구성

```
Assets/Scripts/
└── InGame/
    ├── ComputerAI.cs             ← 신규 (판단 전용, Unity 비의존)
    └── InGameSceneManager.cs     ← 기존 파일 수정 (손패 관리, 관찰 스냅샷 생성, 실행)
```

---

## 이번 범위에서 제외

- **C 케이스 확률 임계값(`DirectAttackThreatThreshold = 0.5`)과 A 조건 비율(`0.5`)의 최종 튜닝**: 원 문서의 "미결 사항"과 동일 — 실제 플레이 데이터 없이 확정할 수 없어 초기값만 잡고 이후 조정한다.
- **"당장 머지 vs 한 턴 더 모아서 큰 효과 노리기" 트레이드오프**: 원 문서가 명시적으로 1차 구현에서 제외했다 — 3그룹은 항상 "가능하면 바로 병합"으로 단순화한다.
- **고사리(1018)의 `HasFreeRevive`를 슬롯 배치 우선순위에 정량 반영**: 3그룹 필러 채우기에서 "위험한 슬롯에 우선 배치"하는 세부 로직은 이번 1차 구현에 넣지 않는다(원 문서에 명시된 요구가 아님, 필러형으로만 취급).
- **독수리(1003, 면역)의 방어적 부가가치를 자기 카드 평가에 반영**: `Classify`가 "면역" 카테고리로 분류하고 2그룹 가치식(`ImmunityValue`)도 정의하지만, 이건 어디까지나 "독수리 자체를 낼 가치"를 계산하는 것이지 "다른 카드가 견제당할 확률을 낮추는" 효과까지 다른 카드들의 점수에 되먹임하지는 않는다 — 원 문서에 그런 상호작용까지는 정의돼 있지 않음.
- **`DeckBuilder`가 `sheets` 컬럼을 무시하고 `CopiesPerFriend=10`으로 고정하는 기존 동작**: `EstimatePlayerHasAtLeast`는 `CardTable.Instance.Get(key).sheets`(CSV상 나비만 15, 나머지 10)를 정확한 총 장수로 가정하지만, 실제 `DeckBuilder.Build`는 전부 10장으로 찍어낸다(사전 존재하던 불일치, 이번 기능과 무관). 나비는 어차피 필러형이라 이 문서의 확률 계산에 영향이 없어 당장은 무시하되, `DeckBuilder`가 나중에 `sheets`를 반영하도록 고쳐지면 이 문서의 가정과 자동으로 맞아떨어진다(별도 대응 불필요).
- **컴퓨터 손패 UI 표시**: 유저 손패처럼 화면에 그리지 않는다 — `_computerHand`는 순수 데이터.
- **대전(`GameType.Battle`) 모드**: 이 문서는 Solo 모드의 PvE 컴퓨터 전용이다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `_computerDeck`이 소진돼 `RefillComputerHand`가 3장을 못 채움 | 채울 수 있는 만큼만 채워짐 — `obs.AiHand`가 비어 있으면 `ChooseBestFillAction`/`DecideGroup2Action` 모두 즉시 `null` 반환 |
| 조건 A와 조건 C가 동시에 성립(내 체력이 매우 낮고 필드 최댓값 Att도 높음) | A+B 우선 확인 후(`ConditionA && ConditionB`) 성립하면 즉시 `HandleUrgentAB` 실행·반환 — C는 확인조차 하지 않음(원 문서의 순서 그대로: "1. 위급 신호 체크(A+B/C)"는 A+B를 먼저 검사) |
| 컴퓨터 필드 3칸 전부 점유 + 핸드 카드 전부 병합 불가 | `ChooseBestFillAction`이 `null`(빈 슬롯 없음), `ChooseBestMergeAction`도 `null` → `DecideGroup3Action`이 `null` 반환, 이번 턴 배치 없이 진행 |
| 상대 필드(4~6)가 완전히 비어 있는데 견제/즉발데미지 카드만 손패에 있음 | `DebuffValue`/`InstantDamageValue`가 `AverageAtt`/`sum` 등에서 자연히 0에 가까운 값 산출(상대 최강 Att 0이면 견제 값도 0) — 별도 예외처리 불필요 |
| `EstimatePlayerHasAtLeast` 호출 시 `unseenPopulation`이 0(상대 덱과 손패가 모두 0장, 사실상 발생 안 함) | `HypergeometricPmf`가 `Combination(0, sampleSize=0)`으로 1을 반환해 `pZero=1` → 확률 0 — 0으로 나누는 상황은 없음(조합 계산이 나눗셈을 쓰지 않는 누적 곱 방식이라 안전) |
| 상대 필드에 이미 개구리(1002)가 있는 상태에서 C 케이스 재확인 | `ConditionC`가 확률 계산 없이 `obs.PlayerField.Any(f => IsDirectAttackKey(f.Key))`에서 바로 `true` — 불필요한 확률 계산 생략 |
| `AiHp`가 0에 가까워 `DebuffValue`의 분모(`obs.AiHp`)가 매우 작아짐 | 값이 매우 커질 수 있음(의도된 동작 — 체력이 낮을수록 견제의 상대적 가치가 커진다는 원 문서 공식 그대로) — `AiHp`가 정확히 0이면 애초에 `GameState.GameOver`로 전이돼 이 로직 자체가 호출되지 않음(공격 판정 계획 참고) |
| 조커형/필러형 카드가 손패에 하나도 없는데 빈 슬롯이 있음 | `ChooseBestFillAction`의 `fillerCandidates.Count > 0 ? fillerCandidates[0] : obs.AiHand[0]`가 손패 맨 앞 카드(성장/방어/즉발데미지 등 무엇이든)로 대체 채움 — "조커/필러 없으면 아무 카드나 채운다"는 원 문서 3번 배치 원칙의 자연스러운 확장 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 컴퓨터 체력 15, 필드 전체에 Att 8짜리 카드 존재(15의 절반=7.5 이상), 컴퓨터 필드 빈 슬롯 2개 | 조건 A/B 모두 성립 → `HandleUrgentAB` 호출, 빈 슬롯부터 채움 확인 |
| 2 | 컴퓨터 체력 8, 유저 필드에 개구리(1002) 이미 존재 | `ConditionC`가 확률 계산 없이 즉시 `true`, `HandleUrgentC` 실행(정리→체력관리→슬롯채우기 순서로 시도) |
| 3 | 컴퓨터 체력 8, 유저 필드는 비어 있지만 유저 손패 4장, 유저 덱 잔여 20장 중 개구리 미확인 10장 | `EstimatePlayerHasAtLeast(1002, 2, obs)`가 초기하분포로 계산된 확률 반환, 임계값(0.5) 비교 결과에 따라 C 케이스 진입 여부 결정 — 표본 크기(손패 4)와 모집단(24) 대입해 수기로 계산한 값과 일치하는지 단위 테스트로 검증 |
| 4 | 위급 신호 없음(체력 충분, 빈 슬롯 1개), 손패에 개구리(즉발데미지)와 코끼리(성장) 동시 존재, 둘 다 병합 가능 | `DecideGroup2Action`이 두 값 계산 후 더 큰 쪽 선택 — 유저 체력이 개구리 데미지 이하라면 `FinisherPriorityMultiplier`로 개구리가 항상 승리 |
| 5 | 2그룹 후보 없음(손패 전부 조커형/필러형이거나 병합 불가), 빈 슬롯 1개 존재 | `DecideGroup3Action`이 `ChooseBestFillAction`으로 조커/필러 중 스탯 합이 가장 낮은 카드를 빈 슬롯에 배치 |
| 6 | 빈 슬롯 없음, 손패에 성장형(코끼리)과 방어형(거북이) 병합 가능 대상 존재 | `ChooseBestMergeAction(Growth, Defense)`가 둘 중 `CategoryValue`가 더 높은 쪽 선택 |
| 7 | 빈 슬롯 없음, 손패 전부 어떤 필드 카드와도 병합 불가 | `DecideGroup3Action`이 `null` 반환 → 이번 턴 배치 스킵(로그로 확인) |
| 8 | 독수리(1003)만 내 필드에 있고 손패에 `AllyRandom` 성장 카드(코끼리) 병합 시도 | `GrowthValue`가 `FilterTargetable(obs.AiField)`로 독수리를 걸러낸 뒤 평균을 계산 — 대상이 0명이면 `AverageStatOf`가 0을 반환해 성장 가치도 0 |
| 9 | 컴퓨터 덱 잔여 0, 손패도 0 | `RefillComputerHand` 무동작, `DecideComputerAction`이 모든 단계에서 `null` 반환, 배치 없이 턴 진행 |
| 10 | 조건 A만 성립(빈 슬롯 0~1개, B 불성립), 체력도 10 초과(C 불성립) | 위급 신호 모두 미해당 → 평시 2/3그룹 경로로 정상 진행 |

---

## 구현 시 주의사항

- **`AllyRandom`/`EnemyRandom` 평균 계산은 `CardCondition.Except` 카드를 제외해야 한다**: [친구카드 능력 계획](plan-ingame-ability.md)의 `PickRandomTargetable`이 이미 독수리류를 걸러내고 실제 발동하므로, `CategoryValue`의 `AverageStatOf`/`AverageAtt` 등도 같은 필터를 적용하지 않으면 "점수는 매겨졌는데 실제로는 대상이 아니라 발동 안 되는" 불일치가 생긴다(치트 에디터 문서에서 반복 강조된 것과 같은 종류의 함정).
- **`ComputerObservation`은 스냅샷이지 실시간 참조가 아니다**: `DecideComputerAction` 내부에서 게임 상태를 다시 조회하지 않는다 — 판단 도중 상태가 바뀔 일이 없는 동기 실행 구조이므로(턴 상태 머신이 단일 진행), 스냅샷 하나로 전 판단을 끝내는 것이 맞다. 실행(`ExecuteComputerAction`)만 실제 상태를 변경한다.
- **상대 손패 "내용"을 절대 읽지 않는다**: `BuildObservation`에서 `_handSlots.Count(s => s.IsOccupied)`처럼 개수만 세고, `GetComponentInChildren<FriendCard>().Key`를 호출하지 않는다 — 기술적으로는 가능하지만 원 문서의 정보 제한 설계를 깨는 것이므로, 코드 리뷰에서 반드시 확인해야 할 지점.
- **초기하분포 계산의 `sheets` 필드 의존은 `DeckBuilder`의 현재 동작과 어긋난다는 것을 인지한 채로 구현한다**(이번 범위에서 제외 참고) — 지금 당장 버그를 고치라는 뜻이 아니라, 두 값이 다르다는 걸 알고 있어야 나중에 확률 계산이 안 맞을 때 헤매지 않는다.
- **`FinisherPriorityMultiplier`(1000)는 "무조건 그 행동을 고른다"를 값 스케일로 흉내 낸 것**: 진짜 우선순위 강제(예: `if` 분기)로 처리하지 않고 값에 큰 배수를 곱하는 이유는, 2그룹의 정렬 로직(`OrderByDescending(value)`)을 그대로 재사용하기 위함이다 — 다른 카테고리 값이 실수로 1000을 넘는 극단적 상황이 생기지 않도록 각 카테고리 공식의 스케일을 주기적으로 점검한다.
- **`ChooseBestMergeAction`/`DecideGroup2Action`의 후보 생성 로직이 중복되지 않도록 실제 구현 시 공통 private 헬퍼로 뽑는다**: 이번 문서의 코드 스니펫은 설명을 위해 별도로 적었지만, "핸드 × 필드 슬롯을 순회하며 `CanMerge`를 확인하고 카테고리로 필터링"하는 부분은 한 곳에만 존재해야 한다.

---

## 구현 후 체크리스트

- [ ] `ComputerAI.cs` 작성(`InGame/`) — `AbilityCategory`/`FriendSnapshot`/`ComputerObservation`/`ComputerAction`, `Classify`, 1~7번 결정의 판단 메서드 전체
- [ ] `InGameSceneManager.cs`: `_computerHand`/`_computerInitialDeckSize` 필드, `RefillComputerHand`/`BuildObservation`/`ExecuteComputerAction` 추가
- [ ] `EnterPhase(TurnPhase.PlayFriend)`의 컴퓨터 분기에 `RefillComputerHand` → `DecideComputerAction` → `ExecuteComputerAction` 연결
- [ ] `HypergeometricPmf`/`Combination`에 대한 순수 단위 테스트(알려진 손 계산값과 비교) 작성
- [ ] 테스트 시나리오 10개 검증(Unity Play 모드, [필드 슬롯 치트 에디터](plan-ingame-cheat.md)로 체력/필드/덱 잔여 상태를 강제 세팅해 A+B/C 케이스 재현)
- [ ] 여러 판을 실제로 플레이해 `ConditionAHpRatio`/`DirectAttackDangerHpThreshold`/`DirectAttackThreatThreshold`/`FinisherPriorityMultiplier` 등 상수 1차 튜닝
- [ ] (추후) "당장 병합 vs 다음 턴까지 보유" 트레이드오프 고도화 — 원 문서의 미결 사항
- [ ] (추후) 난이도 단계별 상수 프리셋 도입 여부 검토
