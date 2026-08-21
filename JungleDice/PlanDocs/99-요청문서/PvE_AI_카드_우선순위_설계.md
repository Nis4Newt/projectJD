# PvE AI 카드 우선순위 설계 (초안)

> [PvE AI 알고리즘 설계(초안)](PvE_AI_알고리즘_설계.md)은 보류하고, 이 문서의 조건 A/B 정의와 11개 우선순위 카테고리를 기준으로 삼는다. 이 문서는 AI 관점뿐 아니라 **플레이어 쪽 위급 여부까지 함께 고려한** 8가지 상황별 카드 우선순위와, 더 세분화된 친구 카드 능력 분류를 정리한다.
>
> **구현 현황**: 이 문서의 설계는 `Assets/Scripts/InGame/ComputerAI.cs`(판단 로직)와 `Assets/Scripts/Data/Table/ActionPriorityTable.cs` + `Assets/Tables/Source/ActionPriorityTable.csv`(순위 매트릭스 데이터)로 구현되어 있다. [plan-ingame-computer-ai.md](../06-ingame/plan-ingame-computer-ai.md)는 이 문서 이전의 다른 설계안(6카테고리/3그룹)을 다루고 있어 더 이상 유효하지 않다 — 참고 시 주의.

## AI 턴 동작 방식

1. 덱에서 친구카드를 최대 4장까지 받아 핸드를 채운다(핸드 최대 4장).
2. AI 필드, 플레이어 필드의 스냅샷을 생성한다.
3. 스냅샷으로부터 현재 AI가 처한 상황(1그룹 / 2그룹, 그리고 세부 케이스)을 도출한다.
4. 도출된 상황의 카드 우선순위와 보유 카드를 비교하며 카드를 플레이한다(0장~4장).

### 스냅샷 구성

| 구분 | AI 스냅샷 | 플레이어 스냅샷 |
|---|---|---|
| 전투 중 유지하며 갱신 | 모험가 최대/현재 체력, 전투에 가져온 카드 종류, 무덤 카드 리스트, 덱의 카드 리스트(순서는 모름 — 카드별 남은 count만 확보) | 모험가 최대/현재 체력, 현재까지 확인된 카드 종류, 무덤 카드 리스트, 덱의 카드 count |
| 스냅샷마다 새로 조회 | 필드 카드 리스트, 핸드 카드 리스트 | 필드 카드 리스트, 핸드 카드 count(내용은 모름, 장수만) |

플레이어 쪽은 손패 "내용"은 모른 채 장수만 알고, 덱은 카드 수만 알고 순서/내용은 모른다는 정보 제한을 스냅샷 단계에서부터 지킨다.

---

## 능력 친구 카드 제출 규칙

- 필드를 채워야 하는 상황이면 일단 낸다.
- 필드를 꼭 채우지 않아도 되는 상황이면:
  - 필드에 같은 카드가 1장 이상 있으면 바로 제출한다.
  - 필드에 같은 카드가 없으면 최소 2장을 모아서 제출한다.

---

## 위급 상황 판단 조건

| 조건 | 내용 |
|---|---|
| 조건 A | 필드에 존재하는 모든 친구(내 필드 + 상대 필드)의 공격력 중 최댓값 ≥ 체력의 절반, 그리고 내 필드의 빈 슬롯이 2개 이상 |
| 조건 B | 체력이 10 이하, 그리고 상대가 모험가를 직접 공격하는 친구를 가지고 있을 가능성이 높을 때 |

두 조건 모두 AI/플레이어 양쪽에 각각 적용해 판단하며, 아래 1그룹·2그룹은 "AI 조건 성립 여부 × 플레이어 조건 성립 여부" 조합에 따라 나뉜다.

---

## 우선순위 적용 공통 규칙

아래 모든 순위표는 이 3가지 규칙을 함께 적용한 결과다.

1. **확정 킬 / 위협 해소 보너스** — 손패의 카드로 (a) 상대 모험가를 확정으로 죽일 수 있거나, (b) 지금 조건 A/B를 유발한 위협 카드(공격력 최댓값 카드, 또는 직접공격 카드)를 확정으로 제거·무력화할 수 있다면, 순위표를 무시하고 그 카드를 최우선으로 실행한다. 확정은 아니지만 유력한 경우(대상이 무작위라 확률적으로만 맞는 경우 등)에는 최우선 승격 대신, 같은 카테고리 후보들 중에서만 그 카드를 우선 정렬한다.
2. **소유자 조건** — "상대 필드 약화"/"상대 필드 직접 데미지"가 *조건 A의 위협 카드(공격력 최댓값) 자체를 해소하는 목적*으로 표에 들어간 경우엔, 그 위협 카드가 실제로 **상대 소유일 때만** 적용한다(내 카드가 원인이면 스킵하고 다음 순위로). 반대로 *상대의 구조적 취약점(빈 슬롯 등)을 압박하는 목적*으로 들어간 경우엔 이 조건을 적용하지 않는다 — 각 표의 비고에 목적을 표시했다.
3. **약화 우선** — 상대 필드 약화와 상대 필드 직접 데미지가 같은 표에 함께 있으면, 항상 상대 필드 약화가 더 높은 순위를 가진다.

그리고 실드는 "같은 친구와 합쳐질 때 필드 내 전투가 있어도 필드(카드)를 보존시킬 수 있다"는 전제 아래, **AI 자신이 조건 A(빈슬롯+큰 공격력 위협)에 해당하는 시나리오에서만** 필드 채우기 바로 다음 순위로 올린다 — 조건 B(능력 기반 직접 데미지)는 필드 전투 자체와 무관해 실드가 막지 못하므로 그대로 하위권에 둔다.

---

## 1그룹: AI 위급 상황 (최우선 대응)

### AI만 위급 — 조건 A

| 순위 | 카드 우선순위 | 비고 |
|---|---|---|
| 1 | 필드 채우기 | |
| 2 | 실드 | 필드 내 전투로부터 슬롯 보존 — AI 자신이 조건 A라 최상위권으로 승격 |
| 3 | 모험가 성장 | |
| 4 | 모험가 회복 | |
| 5 | 상대 필드 약화 → 상대 필드 직접 데미지 | 위협 해소 목적 — 공격력 최댓값을 가진 친구가 **상대 소유일 때만** |
| 6 | 특수 효과 | |
| 7 | 상대 모험가 직접 데미지 | |
| 8 | 필드 회복 | |
| 9 | 같이 성장 | |
| 10 | 홀로 성장 | |

### AI만 위급 — 조건 B

| 순위 | 카드 우선순위 | 비고 |
|---|---|---|
| 1 | 모험가 성장 | |
| 2 | 모험가 회복 | |
| 3 | 상대 필드 약화 | |
| 4 | 특수 효과 | |
| 5 | 상대 필드 직접 데미지 | 압박 목적 — 플레이어 필드에 모험가를 직접 공격하는 친구가 **이미 나와있을 때만** |
| 6 | 필드 채우기 | |
| 7 | 상대 모험가 직접 데미지 | |
| 8 | 필드 회복 | |
| 9 | 실드 | 필드 내 전투가 아닌 능력 기반 위협이라 하위권 유지 |
| 10 | 같이 성장 | |
| 11 | 홀로 성장 | |

### AI + 플레이어 동시 위급

#### AI 조건 A & 플레이어 조건 A

| 순위 | 카드 우선순위 | 비고 |
|---|---|---|
| 1 | 필드 채우기 | |
| 2 | 실드 | AI 자신이 조건 A라 승격 |
| 3 | 모험가 성장 | |
| 4 | 모험가 회복 | |
| 5 | 상대 모험가 직접 데미지 | 마무리 압박 |
| 6 | 상대 필드 약화 | 위협 해소 목적 — 위협 카드가 **상대 소유일 때만** |
| 7 | 상대 필드 직접 데미지 | 동일 조건 |
| 8 | 특수 효과 | |
| 9 | 필드 회복 | |
| 10 | 같이 성장 | |
| 11 | 홀로 성장 | |

#### AI 조건 A & 플레이어 조건 B

| 순위 | 카드 우선순위 | 비고 |
|---|---|---|
| 1 | 필드 채우기 | AI 자신도 조건 A(빈슬롯 위험)라 공격보다 자기 방어를 우선 |
| 2 | 실드 | AI 자신이 조건 A라 승격 |
| 3 | 상대 모험가 직접 데미지 | 플레이어 조건 B 마무리 압박 |
| 4 | 모험가 성장 | |
| 5 | 모험가 회복 | |
| 6 | 상대 필드 약화 | 위협 해소 목적 — 위협 카드가 **상대 소유일 때만** |
| 7 | 상대 필드 직접 데미지 | 동일 조건 |
| 8 | 특수 효과 | |
| 9 | 필드 회복 | |
| 10 | 같이 성장 | |
| 11 | 홀로 성장 | |

#### AI 조건 B & 플레이어 조건 A

| 순위 | 카드 우선순위 | 비고 |
|---|---|---|
| 1 | 상대 필드 약화 | 압박 목적 — 플레이어의 구조적 취약점을 노림, 소유자 조건 불필요 |
| 2 | 모험가 성장 | |
| 3 | 모험가 회복 | |
| 4 | 상대 모험가 직접 데미지 | |
| 5 | 필드 채우기 | AI 자신은 조건 B라 후순위 유지 |
| 6 | 상대 필드 직접 데미지 | |
| 7 | 특수 효과 | |
| 8 | 필드 회복 | |
| 9 | 실드 | AI 자신이 조건 A가 아니라 하위권 유지 |
| 10 | 같이 성장 | |
| 11 | 홀로 성장 | |

#### AI 조건 B & 플레이어 조건 B

| 순위 | 카드 우선순위 | 비고 |
|---|---|---|
| 1 | 상대 모험가 직접 데미지 | |
| 2 | 상대 필드 약화 | 압박 목적 |
| 3 | 모험가 성장 | |
| 4 | 모험가 회복 | |
| 5 | 필드 채우기 | AI 자신은 조건 B라 후순위 유지 |
| 6 | 상대 필드 직접 데미지 | |
| 7 | 특수 효과 | |
| 8 | 필드 회복 | |
| 9 | 실드 | AI 자신이 조건 A가 아니라 하위권 유지 |
| 10 | 같이 성장 | |
| 11 | 홀로 성장 | |

---

## 2그룹: 일반 상황

### 플레이어 위급상황

#### 플레이어만 조건 A

| 순위 | 카드 우선순위 | 비고 |
|---|---|---|
| 1 | 필드 채우기 | |
| 2 | 상대 필드 약화 | 압박 목적 — AI 자신은 위급이 아니라 소유자 조건 불필요 |
| 3 | 상대 필드 직접 데미지 | |
| 4 | 같이 성장 | |
| 5 | 홀로 성장 | |
| 6 | 상대 모험가 직접 데미지 | |
| 7 | 특수 효과 | |
| 8 | 모험가 성장 | |
| 9 | 모험가 회복 | |
| 10 | 필드 회복 | |
| 11 | 실드 | AI 자신이 조건 A가 아니라 최하위 유지 |

#### 플레이어만 조건 B

| 순위 | 카드 우선순위 | 비고 |
|---|---|---|
| 1 | 상대 모험가 직접 데미지 | |
| 2 | 필드 채우기 | |
| 3 | 상대 필드 약화 | 압박 목적 |
| 4 | 상대 필드 직접 데미지 | |
| 5 | 같이 성장 | |
| 6 | 홀로 성장 | |
| 7 | 특수 효과 | |
| 8 | 모험가 성장 | |
| 9 | 모험가 회복 | |
| 10 | 필드 회복 | |
| 11 | 실드 | |

### 모두 일반 상황

| 순위 | 카드 우선순위 |
|---|---|
| 1 | 같이 성장 |
| 2 | 홀로 성장 |
| 3 | 실드 |
| 4 | 상대 모험가 직접 데미지 |
| 5 | 필드 채우기 |
| 6 | 상대 필드 약화 |
| 7 | 상대 필드 직접 데미지 |
| 8 | 특수 효과 |
| 9 | 모험가 성장 |
| 10 | 모험가 회복 |
| 11 | 필드 회복 |

---

## 우선순위 데이터 테이블 (관리용)

위 9개 표는 사람이 읽기 위한 형태다. 실제로는 카테고리가 늘거나 위급 조건이 추가될 때마다 표를 통째로 다시 쓰지 않도록, 아래 하나의 매트릭스로 관리한다. 카테고리가 늘어나면 `AbilityPriorityCategory`에 멤버 하나, 위급 조건이 늘어나면 `UrgencyState`에 멤버 하나만 추가하면 된다.

> **구현됨**: 아래 CSV 그대로 `Assets/Tables/Source/ActionPriorityTable.csv`로 존재하고, `Assets/Scripts/Data/Table/ActionPriorityTable.cs`(`ActionPriorityTable : TableBase<...>`)가 `CardTable`과 동일한 패턴으로 로드해 `GetPriority(UrgencyState ai, UrgencyState player)`로 조회한다. `AbilityPriorityCategory`/`UrgencyState` enum도 이 파일에 정의돼 있다(아래 예시 스니펫과 실제 코드의 소속 파일만 다르고 정의는 동일).

### 시나리오 코드

시나리오는 코드를 하나씩 나열하지 않고, **AI 상태 × 플레이어 상태**의 조합으로 표현한다 — 상태 종류가 늘어도(예: 조건 C 추가) `UrgencyState`에 멤버 하나만 추가하면 조합은 자동으로 늘어난다.

```csharp
public enum UrgencyState
{
    None,
    FieldExposure,       // 조건 A — 필드에 큰 위협 + 내 빈 슬롯 2개 이상 (다이스 직격 위험)
    DirectAttackThreat,  // 조건 B — 체력 10 이하 + 상대의 직접공격 능력 위협
}
```

| AI 상태 \ 플레이어 상태 | `None` | `FieldExposure` | `DirectAttackThreat` |
|---|---|---|---|
| `None` | 모두 일반 상황 | 플레이어만 조건 A | 플레이어만 조건 B |
| `FieldExposure` | AI만 위급 — 조건 A | AI 조건 A & 플레이어 조건 A | AI 조건 A & 플레이어 조건 B |
| `DirectAttackThreat` | AI만 위급 — 조건 B | AI 조건 B & 플레이어 조건 A | AI 조건 B & 플레이어 조건 B |

### 카테고리 키 (Enum)

| Key | 한글 이름 |
|---|---|
| `Fill` | 필드 채우기 |
| `Shield` | 실드 |
| `AdvGrowth` | 모험가 성장 |
| `AdvHeal` | 모험가 회복 |
| `FieldHeal` | 필드 회복 |
| `AllyGrowth` | 같이 성장 |
| `SoloGrowth` | 홀로 성장 |
| `Special` | 특수 효과 |
| `EnemyBaseDmg` | 상대 모험가 직접 데미지 |
| `EnemyWeaken` | 상대 필드 약화 |
| `EnemyFieldDmg` | 상대 필드 직접 데미지 |

```csharp
public enum AbilityPriorityCategory
{
    Fill,
    Shield,
    AdvGrowth,
    AdvHeal,
    FieldHeal,
    AllyGrowth,
    SoloGrowth,
    Special,
    EnemyBaseDmg,
    EnemyWeaken,
    EnemyFieldDmg,
}
```

### 순위 매트릭스

`AI`·`Player`(둘 다 `UrgencyState` 값) 두 컬럼을 합쳐 하나의 키로 쓴다. `Priority`는 그 키에 해당하는 카테고리를 1순위부터 11순위까지 쉼표로 이어붙인 문자열이다.

```csharp
private static readonly Dictionary<(UrgencyState Ai, UrgencyState Player), AbilityPriorityCategory[]> PriorityTable = new()
{
    [(UrgencyState.FieldExposure, UrgencyState.None)] = new[] { AbilityPriorityCategory.Fill, /* ... */ },
    [(UrgencyState.None, UrgencyState.None)] = new[] { AbilityPriorityCategory.AllyGrowth, /* ... */ },
    // ...
};
```

```csv
AI|Player|Priority
FieldExposure|None|Fill,Shield,AdvGrowth,AdvHeal,EnemyWeaken,EnemyFieldDmg,Special,EnemyBaseDmg,FieldHeal,AllyGrowth,SoloGrowth
DirectAttackThreat|None|AdvGrowth,AdvHeal,EnemyWeaken,Special,EnemyFieldDmg,Fill,EnemyBaseDmg,FieldHeal,Shield,AllyGrowth,SoloGrowth
FieldExposure|FieldExposure|Fill,Shield,AdvGrowth,AdvHeal,EnemyBaseDmg,EnemyWeaken,EnemyFieldDmg,Special,FieldHeal,AllyGrowth,SoloGrowth
FieldExposure|DirectAttackThreat|Fill,Shield,EnemyBaseDmg,AdvGrowth,AdvHeal,EnemyWeaken,EnemyFieldDmg,Special,FieldHeal,AllyGrowth,SoloGrowth
DirectAttackThreat|FieldExposure|EnemyWeaken,AdvGrowth,AdvHeal,EnemyBaseDmg,Fill,EnemyFieldDmg,Special,FieldHeal,Shield,AllyGrowth,SoloGrowth
DirectAttackThreat|DirectAttackThreat|EnemyBaseDmg,EnemyWeaken,AdvGrowth,AdvHeal,Fill,EnemyFieldDmg,Special,FieldHeal,Shield,AllyGrowth,SoloGrowth
None|FieldExposure|Fill,EnemyWeaken,EnemyFieldDmg,AllyGrowth,SoloGrowth,EnemyBaseDmg,Special,AdvGrowth,AdvHeal,FieldHeal,Shield
None|DirectAttackThreat|EnemyBaseDmg,Fill,EnemyWeaken,EnemyFieldDmg,AllyGrowth,SoloGrowth,Special,AdvGrowth,AdvHeal,FieldHeal,Shield
None|None|AllyGrowth,SoloGrowth,Shield,EnemyBaseDmg,Fill,EnemyWeaken,EnemyFieldDmg,Special,AdvGrowth,AdvHeal,FieldHeal
```

이 블록을 그대로 시트/`ScriptableObject` 테이블로 옮겨 관리한다(행 키=`AI`+`Player`(각각 `UrgencyState` 값), `Priority`=순위 순서대로 나열한 카테고리 목록). 위 9개 표는 이 매트릭스에서 파생된 사람이 읽는 뷰이므로, 이후 수치를 조정할 때는 매트릭스를 기준으로 갱신하고 9개 표는 참고용 스냅샷으로만 유지한다.

### 조건부 셀 (매트릭스 값만으로 표현 안 되는 것)

매트릭스의 `EnemyWeaken`·`EnemyFieldDmg` 값은 항상 적용되는 게 아니라 시나리오별로 목적과 발동 조건이 다르다 — "위협 해소" 목적은 위협 카드가 실제로 상대 소유일 때만 발동하고, "압박" 목적은 조건 없이 항상 발동한다.

| AI | Player | `EnemyWeaken` 목적 | `EnemyWeaken` 조건 | `EnemyFieldDmg` 목적 | `EnemyFieldDmg` 조건 |
|---|---|---|---|---|---|
| `FieldExposure` | `None` | 위협 해소 | 위협 카드가 상대 소유일 때만 | 위협 해소 | 위협 카드가 상대 소유일 때만 |
| `DirectAttackThreat` | `None` | 압박 | 없음 | 압박 | 플레이어 필드에 직접공격 카드가 이미 있을 때만 |
| `FieldExposure` | `FieldExposure` | 위협 해소 | 위협 카드가 상대 소유일 때만 | 위협 해소 | 위협 카드가 상대 소유일 때만 |
| `FieldExposure` | `DirectAttackThreat` | 위협 해소 | 위협 카드가 상대 소유일 때만 | 위협 해소 | 위협 카드가 상대 소유일 때만 |
| `DirectAttackThreat` | `FieldExposure` | 압박 | 없음 | 압박 | 없음 |
| `DirectAttackThreat` | `DirectAttackThreat` | 압박 | 없음 | 압박 | 없음 |
| `None` | `FieldExposure` | 압박 | 없음 | 압박 | 없음 |
| `None` | `DirectAttackThreat` | 압박 | 없음 | 압박 | 없음 |
| `None` | `None` | — | — | — | — |

### 매트릭스보다 우선하는 전역 규칙

매트릭스 순위는 아래 상황에서는 무시되고 이 규칙이 먼저 적용된다(모든 시나리오 공통, 카테고리·시나리오가 늘어도 규칙 자체는 그대로 유지):

- **확정 킬 / 위협 해소 보너스**: 손패 카드로 상대 모험가를 확정으로 죽일 수 있거나 지금 조건 A/B를 유발한 위협 카드를 확정으로 제거·무력화할 수 있으면, 매트릭스 순위와 무관하게 그 카드를 최우선 실행한다.

---

## 친구 카드 능력 분류

| 카테고리 | 카드 | 효과 |
|---|---|---|
| 모험가 성장 | 고래 | 같은 친구와 합쳐질 때 내 모험가의 생명력을 2 회복 |
| 모험가 회복 | *(미정)* | 원문에 해당 카드가 아직 배정되지 않음 |
| 같이 성장 | 코끼리 | 같은 친구와 합쳐질 때 내 필드의 무작위 친구 공격력 2배 |
| 같이 성장 | 바오밥나무 | 같은 친구와 합쳐질 때 내 필드의 무작위 친구 체력 2배 |
| 같이 성장 | 염소 | 같은 친구와 합쳐질 때 내 필드 전체 공격력 +1 |
| 같이 성장 | 해파리 | 같은 친구와 합쳐질 때 내 필드 전체 체력 +1 |
| 홀로 성장 | 까마귀 | 같은 친구와 합쳐질 때 필드에 같은 친구 수만큼 곱해서 머지 |
| 필드 회복 | 오리 | 같은 친구와 합쳐질 때 내 필드의 무작위 친구 체력을 최대치까지 회복 |
| 실드 | 거북이 | 같은 친구와 합쳐질 때 공격을 1회 방어 |
| 실드 | 달팽이 | 같은 친구와 합쳐질 때 내 필드 전체 방어 1회 |
| 특수 효과 | 버섯 | 같은 친구와 합쳐질 때 내 필드의 무작위 친구에게 포자감염(죽은 자리에 버섯 생성) |
| 특수 효과 | 블루베리 | 모든 종류의 친구에 합쳐질 수 있음 |
| 상대 모험가 직접 데미지 | 개구리 | 같은 친구와 합쳐질 때 상대 모험가에게 2의 데미지 |
| 상대 필드 직접 데미지 | 박쥐 | 같은 친구와 합쳐질 때 상대 필드의 무작위 친구에게 1의 데미지 |
| 상대 필드 직접 데미지 | 고릴라 | 같은 친구와 합쳐질 때 상대 필드 전체에 1의 데미지 |
| 상대 필드 약화 | 거미 | 같은 친구와 합쳐질 때 상대 필드의 무작위 친구의 능력치가 절반 |
| 그냥 카드(발동 효과 없음) | 독수리 | 이 친구는 능력의 대상이 되지 않음 |
| 그냥 카드 | 코뿔소 | 높은 공격력 |
| 그냥 카드 | 고사리 | 사망 시 1/1로 1회 부활 |
| 그냥 카드 | 나비 | 이 친구는 15장이 들어감 |
| 그냥 카드 | 하이에나 | 모든 종류의 친구를 잡아먹을 수 있음(능력 발동 안 됨) |

---

## CardTable.csv 확장 제안 (채택되지 않음)

> **구현 결과**: 아래 `abilityCategory` 컬럼 추가안은 실제로는 채택되지 않았다. `ComputerAI.Classify(CardTableData)`가 기존 `cond`/`target`/`scope`/`effect` 조합만으로 20종 카드 전부를 이 문서의 "카드별 값" 표와 정확히 같은 결과로 분류하는 순수 함수로 구현됐다(예: `cond=merge` + `Heal` + `scope=AllyBase` → `AdvGrowth`, `cond=merge` + `Spawn` → `Special` 식으로 조합을 세분화해 구분). `CardTable.csv`에 컬럼을 추가하지 않아도 되므로 이 절은 검토했던 대안으로만 남겨둔다.

현재 `CardTable.csv`(`key|animal|cardname|sheets|att|hp|cond|target|effect|scope|explain`)는 `cond`/`target`/`scope`/`effect` 조합으로 카드의 능력을 표현하지만, 이 조합만으로는 위 11개 `AbilityPriorityCategory`를 다시 도출할 수 없다 — 예를 들어 코끼리·바오밥나무·염소·해파리(같이 성장)와 고래(모험가 성장)는 전부 `cond=merge`라 `cond` 하나로는 구분되지 않는다. 매 순위 계산 때마다 이걸 다시 분류하는 대신, `abilityCategory` 컬럼을 데이터에 직접 추가해 조회만으로 끝내는 것을 제안한다.

### 추가 컬럼

| 컬럼명 | 타입 | 설명 |
|---|---|---|
| `abilityCategory` | `AbilityPriorityCategory` 문자열 | 이 카드의 우선순위 카테고리 키. 순위 매트릭스의 `Priority` 문자열 안의 값과 그대로 매칭된다. |

### 카드별 값

| key | animal | abilityCategory |
|---|---|---|
| 1000 | 코끼리 | `AllyGrowth` |
| 1001 | 거미 | `EnemyWeaken` |
| 1002 | 개구리 | `EnemyBaseDmg` |
| 1003 | 독수리 | `Fill` |
| 1004 | 블루베리 | `Special` |
| 1005 | 박쥐 | `EnemyFieldDmg` |
| 1006 | 고래 | `AdvGrowth` |
| 1007 | 거북이 | `Shield` |
| 1008 | 코뿔소 | `Fill` |
| 1009 | 바오밥나무 | `AllyGrowth` |
| 1010 | 버섯 | `Special` |
| 1011 | 까마귀 | `SoloGrowth` |
| 1012 | 나비 | `Fill` |
| 1013 | 염소 | `AllyGrowth` |
| 1014 | 고릴라 | `EnemyFieldDmg` |
| 1015 | 달팽이 | `Shield` |
| 1016 | 해파리 | `AllyGrowth` |
| 1017 | 오리 | `FieldHeal` |
| 1018 | 고사리 | `Fill` |
| 1019 | 하이에나 | `Fill` |

`AdvHeal`(모험가 회복)에 해당하는 카드는 아직 없어 어떤 key에도 배정하지 않았다.

`그냥 카드`(독수리·코뿔소·고사리·나비·하이에나)는 발동 효과가 없어 다른 10개 카테고리 어디에도 속하지 않으므로, 순위 매트릭스에서 "필드 채우기" 후보로 쓰인다는 원래 취지 그대로 `Fill`로 분류한다.

### 예시 (컬럼 추가 후 행 형식)

```csv
key|animal|cardname|sheets|att|hp|cond|target|effect|scope|explain|abilityCategory
1000|코끼리|대지의 왕|10|2|2|merge|same|Att*2|AllyRandom|같은 친구와 합쳐질때 내 필드의 무작위 친구 공격력 2배|AllyGrowth
1002|개구리|늪지의 송곳니|10|2|2|merge|same|dmg+2|EnemyBase|같은 친구와 합쳐질때 상대 모험가에게 2의 데미지|EnemyBaseDmg
```

---

## 미결 사항

- "모험가 회복" 카테고리에 해당하는 카드가 아직 없음 — 카드가 추가되기 전까지 이 우선순위 슬롯은 항상 스킵되며, 아래 순위표는 카드 등장을 전제로 자리만 잡아둔 상태다.
