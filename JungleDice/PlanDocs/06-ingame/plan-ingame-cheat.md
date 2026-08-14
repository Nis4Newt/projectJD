# InGame 필드 슬롯 치트 에디터 구현 계획

> 상위 문서: 없음 (독립 에디터 편의 기능 — 특정 상위 로드맵에서 파생되지 않음)
> 관련 문서: [InGame 로직 개요](plan-ingame.md) (`InGameSceneManager`가 관리하는 필드 6칸을 이 문서가 조작), [친구카드 합체 계획](plan-ingame-merge.md) (`TryPlaceFriendCard`의 병합 스탯 계산 로직을 이 문서가 재사용), [PlayFromFirstScene 구현 계획](../08-editortools/plan-editortools.md) (Play 모드 전용 `EditorWindow`라는 점에서 같은 "개발 편의 도구" 계열)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`, `JungleDice.InGame.Friend`, `JungleDice.InGame.FieldSlot`, `JungleDice.Data.Table.CardTable`
> 범위: Unity 에디터 전용 `EditorWindow`에서 Play 모드 중인 InGame 씬의 필드 슬롯(1~6번)에 카드를 강제로 채우기/비우기/병합/데미지 적용하는 기능만 다룬다. 슬롯 6개를 한 화면에 모두 펼쳐 보여준다(슬롯 번호를 입력받지 않음). 런타임(빌드)에 포함되는 UI, 핸드/덱 조작, 본체(BaseStone) 체력 조작은 범위 밖.

---

## 배경

카드 합체([친구카드 합체 계획](plan-ingame-merge.md))나 발동 효과(`CardCondition`/`CardTarget`)를 테스트하려면 매번 정상적인 턴 진행(주사위 굴리기, 덱 소비, 핸드 드래그)을 거쳐 원하는 카드 조합을 필드에 만들어야 한다. 특정 key 조합(예: `target=All`/`Any` 카드끼리의 이종 합체, `cond=Merge`의 발동 효과)을 재현하려면 이 과정이 매번 반복되어 QA/개발 속도를 떨어뜨린다.

필드 슬롯 상태를 직접 지정할 수 있는 에디터 도구가 있으면 임의의 key를 임의의 슬롯에 즉시 배치하고, 이미 있는 카드에 임의의 key를 강제로 합쳐 넣어 원하는 시나리오를 몇 초 안에 재현할 수 있다. 실제로 도구를 사용해보니 슬롯 번호를 매번 입력/슬라이더로 바꿔가며 조작하는 것보다 6개 슬롯을 한 화면에 펼쳐 놓고 바로바로 조작하는 편이 더 빠르다 — 슬롯 개수(6개)가 고정이라 입력받을 이유가 없다. 또한 `TakeDamage` 경로(방어막 소모, 사망/부활)를 테스트하려면 슬롯별로 임의의 데미지를 즉시 넣어볼 수 있어야 한다.

---

## 설계 목표

- Play 모드 중인 InGame 씬에서만 동작 — 빌드에는 포함되지 않는 순수 Editor 전용 도구(`Assets/Scripts/Editor/` 하위)
- 창 상단에 제목 라벨을 두고, UI 자체는 Play 모드 여부와 무관하게 항상 그려진다 — 동작 불가능한 상태(Edit 모드, 또는 InGame이 아닌 씬)에서는 입력/버튼을 회색으로 비활성화(`GUI.enabled = false`)할 뿐, 통째로 숨기지 않는다
- 슬롯 번호를 입력받지 않는다 — 필드가 항상 6칸으로 고정이므로, 슬롯 1~6 각각의 조작 UI를 한 화면에 모두 펼쳐서 보여준다
- 슬롯마다 key를 입력받아 "추가"(강제 배치, 기존 점유 여부와 무관하게 덮어씀), "비우기"(강제 제거) 수행 — 이 두 동작만 정상 규칙을 무시하는 진짜 "치트"다
- 슬롯마다 이미 점유된 상태에서 key를 입력해 "머지" — [친구카드 합체 계획](plan-ingame-merge.md)이 정의한 합체 가능 조건(같은 종류, 또는 필드 카드가 `target=Any`, 또는 입력한 key가 `target=All`)을 **그대로 지킨다**. 드래그 없이 key 입력만으로 정상 합체를 재현하는 것이 목적이지, 규칙을 우회하는 것이 목적이 아니다 — 조건을 만족하지 않으면 거부하고 경고 로그만 남긴다
- 슬롯마다 데미지 값을 입력받아 "데미지" — `Friend.TakeDamage`를 그대로 호출해 방어막 소모/사망 판정까지 정상 전투와 동일하게 재현한다
- 정상 배치/합체/사망 경로(`TryPlaceFriendCard`/`TryHandleDeath`)가 이미 구현한 판정(`CanMerge`로 추출)과 스탯 계산·발동 효과(`MergeCardIntoSlot`으로 추출)를 그대로 재사용해, 치트로 만든 상태도 정상 플레이와 완전히 동일한 조건·결과(발동 효과·부활·포자감염 포함)로 동작함을 보장 — 별도의 병렬 로직을 만들지 않음
- 슬롯 조작은 `InGameSceneManager`의 공개 메서드를 통해서만 이루어진다 — 에디터 창이 `FieldSlot`/`Friend`를 직접 `Instantiate`/`Destroy`하지 않음(기존 "매니저가 지시, 컴포넌트가 실행" 책임 분리 패턴 유지)

---

## 핵심 설계 결정

### 1. `InGameSceneManager`에 치트 전용 공개 메서드 4종 추가

```csharp
// 슬롯을 강제로 비운다 — 점유돼 있지 않으면 아무 것도 하지 않는다
public void CheatClearSlot(int slotIndex)
{
    var slot = GetFieldSlot(slotIndex);
    if (!slot.IsOccupied) return;

    Destroy(slot.GetComponentInChildren<Friend>().gameObject);
}

// 슬롯에 key를 강제로 채운다 — 점유돼 있으면 기존 카드를 먼저 제거하고 새로 채운다(치트이므로 병합 규칙을 타지 않음)
public void CheatSetSlot(int slotIndex, int key)
{
    CheatClearSlot(slotIndex);

    var slot = GetFieldSlot(slotIndex);
    var friend = Instantiate(_friendPrefab, slot.transform);
    friend.SetKey(key);
}

// 점유된 슬롯에 key를 합친다 — TryPlaceFriendCard와 동일한 CanMerge 판정을 통과해야 실제로 합쳐진다
public void CheatMergeIntoSlot(int slotIndex, int mergeKey)
{
    var slot = GetFieldSlot(slotIndex);
    if (!slot.IsOccupied)
    {
        Debug.LogWarning($"[Cheat] 슬롯 {slotIndex}이 비어 있어 병합할 수 없습니다.");
        return;
    }

    var existing = slot.GetComponentInChildren<Friend>();
    if (!CanMerge(existing, mergeKey))
    {
        Debug.LogWarning($"[Cheat] 슬롯 {slotIndex}(key={existing.Key})에 key={mergeKey}를 합칠 수 없습니다 — 같은 종류가 아니고 target(All/Any) 조건도 만족하지 않습니다.");
        return;
    }

    MergeCardIntoSlot(existing, mergeKey, slotIndex);
}

// 슬롯의 카드에 데미지를 강제로 입힌다 — TakeDamage(방어막 소모 포함)를 그대로 재사용, 죽으면 TryHandleDeath로 정상 사망 처리(부활/포자감염 포함)와 동일하게 처리
public void CheatDamageSlot(int slotIndex, int amount)
{
    var slot = GetFieldSlot(slotIndex);
    if (!slot.IsOccupied)
    {
        Debug.LogWarning($"[Cheat] 슬롯 {slotIndex}이 비어 있어 데미지를 적용할 수 없습니다.");
        return;
    }

    var friend = slot.GetComponentInChildren<Friend>();
    friend.TakeDamage(amount);

    if (friend.IsDead) TryHandleDeath(friend, slot.transform);
}
```

- `GetFieldSlot(int rollValue)`은 이미 `private FieldSlot GetFieldSlot(int rollValue) => _fieldSlots[rollValue - 1];`로 존재 — 그대로 재사용(슬롯 절대 번호 1~6 규칙과 동일하게 맞춤).
- `CheatSetSlot`은 항상 "덮어쓰기"다 — 정상 플레이의 `TryPlaceFriendCard`처럼 점유 여부로 병합/거부를 가르지 않는다. 치트 목적상 "이 슬롯을 정확히 이 상태로 만들고 싶다"는 요구가 우선이므로, 기존 카드를 조용히 지우고 새로 채운다. (합체 규칙을 지키는 `CheatMergeIntoSlot`과 달리 이 메서드만 진짜로 규칙을 무시한다.)
- `CheatClearSlot`은 `TryHandleDeath`(부활/포자감염 판정)를 거치지 않고 바로 `Destroy`한다 — 전투 사망이 아니라 에디터가 강제로 치우는 것이므로 부활/스폰 마크를 트리거할 이유가 없다.
- `CheatMergeIntoSlot`은 `CanMerge`(아래 2번 결정)를 통과하지 못하면 경고만 남기고 아무 것도 바꾸지 않는다 — 정상 드래그 합체가 거부될 상황(다른 종류, `target` 조건 불만족)이면 치트에서도 똑같이 거부된다.
- `CheatDamageSlot`은 `CheatClearSlot`과 반대로 `TryHandleDeath`를 그대로 호출한다 — 이번엔 진짜 "전투로 인한 사망"을 흉내 내는 것이 목적(부활/포자감염 발동 여부까지 테스트하려는 것이 설계 목표)이므로, `ResolveAttackRoutine`이 죽음을 처리하는 방식과 동일한 경로를 탄다.

### 2. `TryPlaceFriendCard`의 병합 판정/실행을 `CanMerge`/`MergeCardIntoSlot`으로 추출해 공유

기존 `TryPlaceFriendCard`(`InGameSceneManager.cs:377`~)의 점유 슬롯 분기는 "병합 가능 여부 판정"과 "병합 실행(스탯 계산 + 연출 + 발동 효과)"이 한 메서드에 섞여 있다. `CheatMergeIntoSlot`도 정확히 같은 판정과 실행이 그대로 필요하므로(설계 목표 — 치트도 규칙을 지킴), 판정과 실행을 각각 private 메서드로 분리해 두 경로가 공유한다.

```csharp
public void TryPlaceFriendCard(FieldSlot slot, FriendCard card)
{
    if (slot.IsOccupied)
    {
        var existing = slot.GetComponentInChildren<Friend>();
        if (!CanMerge(existing, card.Key)) return; // 병합 불가 — 배치 거부, OnEndDrag가 원래 슬롯으로 복귀시킴

        MergeCardIntoSlot(existing, card.Key, slot.Index);

        card.NotifyPlaced();
        Destroy(card.gameObject);
        return;
    }

    var friend = Instantiate(_friendPrefab, slot.transform);
    friend.SetKey(card.Key);

    card.NotifyPlaced();
    Destroy(card.gameObject);
}

// 같은 종류, 또는 슬롯의 카드가 target=Any(베이스), 또는 합칠 카드가 target=All(무엇에든 합쳐짐)일 때 합체 가능
private bool CanMerge(Friend existing, int mergeKey)
{
    var existingData = CardTable.Instance.Get(existing.Key);
    var data = CardTable.Instance.Get(mergeKey);

    bool sameKind = existing.Key == mergeKey;
    bool existingAcceptsAnything = existingData.target == CardTarget.Any; // 필드 카드가 베이스 역할(하이에나류)
    bool mergeJoinsAnything = data.target == CardTarget.All; // 합칠 카드가 무엇에든 합쳐지는 역할(블루베리류)
    return sameKind || existingAcceptsAnything || mergeJoinsAnything;
}

// existing에 mergeKey 카드의 기본 스탯을 합산 + 연출 + 발동 효과. 호출 전 CanMerge로 이미 통과된 조합이라고 가정한다.
// slotIndex는 existing이 실제로 놓인 필드 절대 번호(1~6) — [친구카드 능력 계획](plan-ingame-ability.md)의 발동 효과가 "내 필드"/"상대 필드"를 이 값으로 판정하므로, 치트로 컴퓨터 필드(1~3)에서 병합해도 정확히 동작한다
private void MergeCardIntoSlot(Friend existing, int mergeKey, int slotIndex)
{
    var existingData = CardTable.Instance.Get(existing.Key);
    var data = CardTable.Instance.Get(mergeKey);

    int addAtt = data.att;
    int addHp = data.hp;
    if (existingData.EffectClauses.Any(c => c.Keyword == "MultiplierMerge"))
    {
        var (ownStart, ownEnd) = OwnFieldRange(slotIndex);
        int sameCount = GetFieldFriends(ownStart, ownEnd).Count(f => f.Key == existing.Key);
        addAtt *= sameCount;
        addHp *= sameCount;
    }

    existing.MergeWith(addAtt, addHp);
    existing.PunchScale(_mergePunchScale, _mergePunchDuration);
    TriggerMergeAbility(existing, slotIndex);
}
```

- `card.Key`를 쓰던 자리를 `mergeKey`(`int`)로 일반화한 것, `target` 판정을 역할이 있는 조건으로 확정한 것(요청자 확인 — 아래 "구현 시 주의사항" 참고), `slotIndex`를 [친구카드 능력 계획](plan-ingame-ability.md)의 필드 판정에 전달하도록 확장한 것 외에는 기존 로직 그대로 옮긴 것.
- `CheatMergeIntoSlot`은 `CanMerge`로 먼저 거부 여부를 확인한 뒤에만 `MergeCardIntoSlot`을 호출하므로, 드래그로는 거부됐을 조합(다른 종류이고 `target` 조건도 없는 경우)은 치트로도 거부된다 — [친구카드 합체 계획](plan-ingame-merge.md)의 "판정식은 반드시 같은 조건을 유지한다"(`ShowMergePreview`/`TryPlaceFriendCard` 간 관례)를 치트 경로까지 확장한 것.
- 조건을 통과하면 `MultiplierMerge` 배수 계산이나 `TriggerMergeAbility`(예: `cond=Merge`의 상대 피해/회복 등)까지 치트로 만든 상태에서도 정상 플레이와 동일하게 발동한다 — key 입력만으로 드래그 없이 정상 합체를 재현하는 것이 이 결정의 핵심.

### 3. `CheatEditorWindow` — 항상 그려지되, 동작 불가 상태에서는 `DisabledScope`로 비활성화만 하는 `EditorWindow`

```csharp
public class CheatEditorWindow : EditorWindow
{
    private const int SlotCount = 6;

    private readonly int[] _setKeys = new int[SlotCount];
    private readonly int[] _mergeKeys = new int[SlotCount];
    private readonly int[] _damages = new int[SlotCount];

    [MenuItem("Tools/InGame/Cheat Editor")]
    private static void Open() => GetWindow<CheatEditorWindow>("InGame Cheat");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("InGame 필드 슬롯 치트", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        bool canOperate = EditorApplication.isPlaying && InGameSceneManager.Instance != null;
        if (!canOperate)
            EditorGUILayout.HelpBox("Play 모드에서 InGame 씬에 진입한 뒤 사용할 수 있습니다.", MessageType.Info);

        using (new EditorGUI.DisabledScope(!canOperate))
        {
            for (int i = 0; i < SlotCount; i++)
            {
                int slotIndex = i + 1;

                EditorGUILayout.LabelField($"슬롯 {slotIndex}", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _setKeys[i] = EditorGUILayout.IntField("Key", _setKeys[i]);
                    if (GUILayout.Button("추가", GUILayout.Width(60)))
                        InGameSceneManager.Instance.CheatSetSlot(slotIndex, _setKeys[i]);
                    if (GUILayout.Button("비우기", GUILayout.Width(60)))
                        InGameSceneManager.Instance.CheatClearSlot(slotIndex);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _mergeKeys[i] = EditorGUILayout.IntField("머지 Key", _mergeKeys[i]);
                    if (GUILayout.Button("머지", GUILayout.Width(60)))
                        InGameSceneManager.Instance.CheatMergeIntoSlot(slotIndex, _mergeKeys[i]);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _damages[i] = EditorGUILayout.IntField("데미지", _damages[i]);
                    if (GUILayout.Button("데미지", GUILayout.Width(60)))
                        InGameSceneManager.Instance.CheatDamageSlot(slotIndex, _damages[i]);
                }

                EditorGUILayout.Space();
            }
        }
    }
}
```

- `OnGUI`는 매 프레임 제목 라벨과 슬롯 UI를 항상 그린다 — Edit 모드/다른 씬이어도 창이 텅 비거나 안내 문구만 남지 않고, 조작 가능한 형태 그대로 보인다.
- 동작 가능 여부(`canOperate`)만 `EditorGUI.DisabledScope`로 감싼다 — Unity IMGUI는 `GUI.enabled == false`일 때 버튼이 시각적으로 회색으로 흐려지고 클릭도 씹으므로(`GUILayout.Button`이 `true`를 반환하지 않음), `InGameSceneManager.Instance`가 `null`인 상태에서 그 안의 호출 코드가 실행될 일이 없다 — 별도의 null 체크가 각 버튼마다 필요 없다.
- `canOperate`가 `false`일 때는 `HelpBox`를 슬롯 UI 위에 추가로 띄운다 — 왜 비활성화됐는지 안내만 하고, 슬롯 UI 자체를 감추지는 않는다(이번 요청의 핵심).
- [PlayFromFirstScene의 `isPlaying` 가드](../08-editortools/plan-editortools.md)와 같은 조건식을 재사용하되, "숨기기"가 아니라 "비활성화"로 적용 방식만 다르다.
- 슬롯 번호를 입력받지 않고 `for` 루프로 1~6을 직접 순회한다 — 잘못된 인덱스가 `GetFieldSlot`에 전달될 여지 자체가 없다(입력값이 아니라 코드가 만든 값이므로).
- key/데미지 입력값은 슬롯별로 배열(`_setKeys`/`_mergeKeys`/`_damages`, 길이 6)에 보관 — 슬롯마다 별도의 입력 상태를 유지해야 여러 슬롯을 동시에 준비해뒀다가 순서대로 눌러볼 수 있다.
- key 값 자체는 검증하지 않는다 — `CardTable.Get`이 이미 없는 key에 대해 `LogError`를 남기는 기존 관례를 그대로 신뢰(아래 엣지 케이스 참고).
- `Repaint()`를 강제로 걸지 않는다 — Play 모드 중 슬롯 상태가 외부(정상 플레이)에서 바뀌어도 이 창은 입력 필드일 뿐 슬롯의 "현재 상태"를 표시하지 않으므로 갱신할 대상이 없다(아래 "이번 범위에서 제외" 참고).

---

## 클래스 구조

```
InGameSceneManager (기존 파일 수정, InGame/)
├── CheatClearSlot(int slotIndex)              ← 신규
├── CheatSetSlot(int slotIndex, int key)       ← 신규
├── CheatMergeIntoSlot(int slotIndex, int mergeKey) ← 신규
├── CheatDamageSlot(int slotIndex, int amount) ← 신규
├── CanMerge(Friend existing, int mergeKey)    ← 신규(TryPlaceFriendCard 병합 분기의 판정 부분에서 추출, target 역할 판정)
├── MergeCardIntoSlot(Friend existing, int mergeKey, int slotIndex) ← 신규(TryPlaceFriendCard 병합 분기의 실행 부분에서 추출, slotIndex는 [친구카드 능력 계획](plan-ingame-ability.md)의 필드 판정용)
└── TryPlaceFriendCard(FieldSlot, FriendCard)  ← 수정, 판정/실행을 각각 CanMerge/MergeCardIntoSlot 호출로 교체(동작은 그대로)

CheatEditorWindow (신규 파일, Editor/InGame/)
├── _setKeys/_mergeKeys/_damages : int[6]  ← 슬롯별 입력 상태
└── OnGUI()   ← 슬롯 1~6을 순회하며 각각 key/추가/비우기/머지 Key/머지/데미지/데미지 버튼 렌더링, Play 모드 가드
```

---

## 파일 구성

```
Assets/Scripts/
├── InGame/
│   └── InGameSceneManager.cs   ← 기존 파일 수정 (치트 메서드 4종 + CanMerge/MergeCardIntoSlot 추출)
└── Editor/InGame/
    └── CheatEditorWindow.cs    ← 신규 (Play 모드 전용 EditorWindow)
```

`FriendEditor.cs`와 같은 `Editor/InGame/` 폴더, 같은 `JungleDice.InGame.Editor` 네임스페이스를 사용한다.

---

## Unity 씬/오브젝트 구성

해당 없음 — 씬/프리팹 변경 없음. `CheatEditorWindow`는 독립된 에디터 창으로, `InGameSceneManager`/`_fieldSlots`/`_friendPrefab` 등 기존 씬 구성을 그대로 사용한다(새 컴포넌트를 씬에 배치하지 않음).

---

## 이번 범위에서 제외

- 슬롯의 "현재 상태"(점유된 카드의 key/이름/스탯)를 창에 실시간으로 표시 — 지금은 입력 전용 도구다. 필요해지면 후속 문서에서 `OnInspectorUpdate`/`Repaint` 기반 상태 표시를 추가
- 핸드/덱 조작(임의 key를 핸드에 꽂기, 덱 순서 조작) — 이번 문서는 필드 슬롯(1~6)만 다룬다
- 컴퓨터 필드(1~3번) 전용 UI 분기 — `CheatSetSlot`/`CheatClearSlot`/`CheatMergeIntoSlot`은 슬롯 번호만 받으므로 1~3번도 그대로 동작하지만, 컴퓨터 전용 UX(예: "내 필드"/"상대 필드" 탭 구분)는 만들지 않는다
- 본체(`BaseStone`) 체력 조작, `GameState`/턴 강제 전환 등 필드 슬롯 이외의 치트 기능
- key 유효성 사전 검증(존재하는 key 목록 드롭다운 등) — `CardTable.Get`의 기존 `LogError` 관례를 그대로 신뢰
- 빌드 포함 여부 걱정 — `Editor/` 폴더 하위 스크립트는 Unity가 빌드에서 자동으로 제외하므로 별도 조치 불필요

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| Play 모드가 아닌 상태에서 창을 열거나 조작 | 제목/슬롯 UI는 그대로 보이되 `DisabledScope`로 회색 비활성화, 위에 `HelpBox` 안내 추가 표시 — 클릭해도 아무 반응 없음(비활성 버튼은 클릭 자체가 씹힘) |
| Play 모드이지만 InGame 씬이 아님(Login/MainMenu 등) | `InGameSceneManager.Instance == null` → 위와 동일하게 비활성화 |
| 존재하지 않는 key로 "슬롯에 추가" | `Friend.SetKey`가 `CardTable.Get(key) == null`을 만나 `LogError` 남기고 스탯/스프라이트 갱신 없이 종료 — 슬롯에는 빈 스탯(0/0)의 `Friend`가 남는다(기존 `SetKey`의 방어 관례와 동일, 별도 처리 안 함) |
| 존재하지 않는 key로 "머지" | `CanMerge`가 먼저 호출되며 `CardTable.Get(mergeKey) == null` 반환 후 `data.target` 접근 시 `NullReferenceException` — 기존 `TryPlaceFriendCard`/`CardTable.Get` 관례와 동일하게 방어 코드 없이 즉시 드러남 |
| 빈 슬롯에 "머지" 클릭 | `CheatMergeIntoSlot`이 `LogWarning` 남기고 아무 것도 하지 않음 |
| 점유된 슬롯에 다른 종류이고 `target` 조건도 만족하지 않는 key로 "머지" | `CanMerge`가 `false` 반환 → `LogWarning` 남기고 거부, 필드 상태 불변(정상 드래그였다면 배치 거부되는 것과 동일한 조건) |
| 점유된 슬롯이 `target=Any`이거나 입력 key가 `target=All`인 조합으로 "머지" | `CanMerge`가 `true` 반환 → 정상 이종 합체와 동일하게 성공 |
| 점유된 슬롯이 `target=All`(예: 블루베리)인데 입력 key가 다른 종류(`target=Same`)로 "머지" | `CanMerge`가 `false` 반환(블루베리는 필드 카드 역할일 때 받아주는 힘이 없음) → `LogWarning` 남기고 거부 |
| 점유된 슬롯에 "슬롯에 추가" 클릭(덮어쓰기) | 기존 카드를 `Destroy`(부활/포자감염 판정 없이) 후 새 key로 재생성 — `CheatMergeIntoSlot`과 달리 `CanMerge` 판정 없이 항상 성공(이 메서드만 규칙을 무시하는 진짜 치트) |
| "머지"로 `MultiplierMerge` 카드(예: 특정 배수 합체 카드)에 병합 | `CanMerge` 통과 시 정상 병합과 동일하게 현재 필드의 같은 key 개수를 세어 배수 적용(`MergeCardIntoSlot` 공유 로직) |
| "머지"로 `cond=Merge` 카드에 병합 | 정상 병합과 동일하게 `TriggerMergeAbility`가 발동(상대 랜덤 대상 피해, 본체 회복 등) — 치트로 만든 상태에서도 발동 효과 테스트 가능(설계 목표) |
| 창을 열어둔 채 Play 모드 종료 후 재진입 | 다음 `OnGUI` 호출 시 새 `InGameSceneManager.Instance`를 다시 참조 — 창 자체에 씬 참조를 캐싱하지 않으므로 문제 없음 |
| 빈 슬롯에 "데미지" 클릭 | `CheatDamageSlot`이 `LogWarning` 남기고 아무 것도 하지 않음 |
| 점유된 슬롯에 `HasShield`가 `true`인 상태에서 "데미지" 클릭 | `Friend.TakeDamage`의 기존 방어막 소모 로직 그대로 — 이번 데미지는 전부 무효화되고 방어막만 소모됨 |
| "데미지"로 `CurrentHp`를 0 이하로 만듦(`cond=Die`가 아닌 카드) | `TryHandleDeath`가 부활 조건 없음을 확인 후 바로 `Destroy` — 정상 전투 사망과 동일 |
| "데미지"로 `CurrentHp`를 0 이하로 만듦(`cond=Die`인 카드, 아직 부활 안 함) | `TryHandleDeath`가 `TryRevive` 성공 → 슬롯에 그대로 남아 새 스탯으로 갱신, 부활 펀치 연출 재생 |
| 음수 데미지 입력 후 "데미지" 클릭 | `TakeDamage`가 `Mathf.Max(0, CurrentHp - amount)`를 그대로 계산하므로 음수 amount는 오히려 회복처럼 동작 — 별도 검증 없이 기존 `TakeDamage` 계약을 그대로 따름 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | Edit 모드에서 `Tools > InGame > Cheat Editor` 실행 | 제목/슬롯 1~6 UI가 모두 보이지만 회색으로 비활성화, "Play 모드에서 InGame 씬에 진입한 뒤 사용할 수 있습니다" 안내 표시, 버튼 클릭해도 반응 없음 |
| 2 | Play 모드로 MainMenu 씬에 있는 상태에서 창 열기 | 시나리오 1과 동일 — `InGameSceneManager.Instance`가 null |
| 3 | Play 모드로 InGame 씬 진입 후 슬롯 4에 빈 상태에서 key 1001 입력 후 "슬롯에 추가" | 필드 4번에 1001 카드가 즉시 생성됨(정상 배치와 동일한 시각 결과) |
| 4 | 시나리오 3 상태에서 슬롯 4에 key 1002 입력 후 "슬롯에 추가" | 기존 1001 카드가 사라지고 1002 카드로 교체됨(병합되지 않음) |
| 5 | 시나리오 4 상태에서 "슬롯 비우기" 클릭 | 필드 4번이 빈 슬롯이 됨 |
| 6 | 빈 슬롯에서 "슬롯 비우기" 클릭 | 아무 동작 없음(에러 없음) |
| 7 | 필드 4번에 key A(기본 att4/hp2) 카드가 있는 상태에서 머지 key에 A 입력 후 "머지" | `Att` 4→8, `CurrentHp` 2→4(정상 합체와 동일한 누적 결과) |
| 8 | 필드 4번에 key A(둘 다 `target=Same`) 카드가 있는 상태에서 머지 key에 전혀 다른 key B 입력 후 "머지" | `CanMerge`가 거부 — 필드 4번 스탯 불변, 콘솔에 경고 로그 |
| 9 | 빈 슬롯에 머지 key 입력 후 "머지" 클릭 | 콘솔에 경고 로그, 필드 상태 변화 없음 |
| 10 | `cond=Merge`인 카드가 필드에 있는 상태에서 같은 종류 key로 "머지" | 정상 병합과 동일하게 발동 효과(상대 랜덤 피해 등)가 함께 실행됨 |
| 11 | 필드 4번에 다른 key(`target=Same`) 카드가 있는 상태에서 머지 key에 1004(블루베리, `target=All`) 입력 후 "머지" | `CanMerge` 통과(`mergeJoinsAnything`) — 필드 카드(`existing`)의 스탯에 블루베리 기본값이 더해짐, 필드 카드 정체성 유지 |
| 12 | 필드 4번에 1019(하이에나, `target=Any`)가 있는 상태에서 다른 key로 "머지" | `CanMerge` 통과(`existingAcceptsAnything`) — 정상 이종 합체와 동일하게 성공, 필드에 남는 카드는 여전히 하이에나 |
| 13 | 슬롯 1~6 각각의 "추가" 버튼으로 임의 key 배치 | 컴퓨터 필드(1~3)/유저 필드(4~6) 모두 동일하게 정상 동작, 6개 행이 한 화면에 모두 보임 |
| 14 | 필드 4번에 hp4 카드가 있는 상태에서 데미지 3 입력 후 "데미지" | `CurrentHp` 4→1(빨간색 텍스트), 카드는 그대로 필드에 남음 |
| 15 | 시나리오 14 상태에서 데미지 1 입력 후 "데미지" 재클릭(정확히 사망) | `cond=Die`가 아닌 카드라면 카드가 필드에서 제거됨, `cond=Die`이고 부활 전이라면 새 스탯으로 부활해 필드에 남음(`plan-ingame-attack.md`의 사망 처리와 동일) |
| 16 | 빈 슬롯에 데미지 값 입력 후 "데미지" 클릭 | 콘솔에 경고 로그, 필드 상태 변화 없음 |
| 17 | `HasShield`가 걸린 카드에 데미지 입력 후 "데미지" 클릭 | 이번 데미지는 무효화되고 방어막만 소모(`CurrentHp` 불변) |
| 18 | 창을 열어둔 채 Edit 모드에서 Play 버튼을 눌러 InGame 씬에 진입 | 다음 `OnGUI` 프레임에서 UI가 자동으로 활성화(회색 해제)됨, 별도로 창을 다시 열 필요 없음 |

---

## 구현 시 주의사항

- `CanMerge`/`MergeCardIntoSlot` 추출은 `TryPlaceFriendCard`의 기존 판정·실행 로직을 절대 바꾸지 않는다 — 정상 플레이의 병합 가능 조건·결과는 이 리팩터링 전후로 동일해야 한다(순수 로직 이동).
- `CheatMergeIntoSlot`은 `CanMerge`를 반드시 거쳐야 한다 — 판정을 건너뛰고 곧바로 `MergeCardIntoSlot`을 호출하면 드래그로는 거부될 조합이 치트로는 합쳐지는 불일치가 생긴다(이번 문서의 핵심 요구사항).
- 치트 중 정상 규칙을 실제로 무시하는 것은 `CheatSetSlot`(강제 덮어쓰기)뿐이다 — `CheatMergeIntoSlot`을 "치트"라고 해서 판정을 생략하지 않도록 주의.
- `CheatSetSlot`이 기존 점유 카드를 지울 때 `TryHandleDeath`를 호출하면 안 된다 — 전투 사망이 아니므로 부활/포자감염이 트리거되면 안 된다. 단순 `Destroy`만 사용.
- `CheatEditorWindow`는 `InGameSceneManager.Instance`를 필드에 캐싱하지 말고 `OnGUI`마다 새로 참조한다 — Play 모드 재진입 시 씬 매니저 인스턴스가 바뀌므로 캐싱하면 stale 참조로 `MissingReferenceException`이 날 수 있다.
- 치트 메서드 4종은 `public`이지만 이름에 `Cheat` 접두사를 붙여 정상 게임 로직 API(`TryPlaceFriendCard` 등)와 명확히 구분한다 — 다른 런타임 코드가 실수로 호출하지 않도록 하는 최소한의 관례적 방어(강제 접근 제한은 하지 않음, `SceneSingleton` 공개 API 관례와 동일).
- `CheatDamageSlot`은 `CheatClearSlot`과 사망 처리 방식이 다르다는 점에 유의 — `CheatClearSlot`(단순 제거)과 헷갈려 `TryHandleDeath` 호출을 빠뜨리면 데미지 치트로는 부활/포자감염을 테스트할 수 없게 된다.
- 슬롯별 입력 배열(`_setKeys`/`_mergeKeys`/`_damages`)은 인덱스 `i`(0~5)를 그대로 쓰고, `InGameSceneManager` 메서드를 호출할 때만 `slotIndex = i + 1`로 변환한다 — 배열 인덱스와 슬롯 절대 번호를 섞어 쓰지 않도록 주의.
- 동작 불가 상태를 "숨기기"(`return` 후 UI 미출력)가 아니라 "비활성화"(`EditorGUI.DisabledScope`)로 구현해야 한다 — 조건 검사 후 `return`해버리면 슬롯 UI가 아예 그려지지 않아 이번 요청과 어긋난다. `DisabledScope`는 `using` 블록으로 감싼 범위 전체에 적용되므로 제목 라벨/`HelpBox`는 그 바깥에 둬야 회색으로 흐려지지 않는다.
- 컴퓨터 필드(1~3)를 굳이 숨기지 않은 설계 목표 덕분에, 이 도구로 실제 버그 두 건(이종 합체 조건 반전, 컴퓨터 필드 병합 시 Ally/Enemy 뒤바뀜)을 QA 단계에서 미리 잡아냈다 — 수정 내역은 [친구카드 합체 계획](plan-ingame-merge.md)/[친구카드 능력 계획](plan-ingame-ability.md) 참고.

---

## 구현 후 체크리스트

- [x] `InGameSceneManager.cs`: `CanMerge`/`MergeCardIntoSlot` 추출, `TryPlaceFriendCard`가 이를 호출하도록 수정(리팩터링, 동작 변화 없음 확인)
- [x] `InGameSceneManager.cs`: `CheatClearSlot`/`CheatSetSlot`/`CheatMergeIntoSlot`/`CheatDamageSlot` 추가
- [x] `CheatEditorWindow.cs` 작성 (`Editor/InGame/`, 슬롯 1~6 고정 UI, Play 모드/씬 가드 포함)
- [x] 도구를 실제로 사용해 이종 합체·컴퓨터 필드 병합 버그 발견 및 수정 확인([친구카드 합체 계획](plan-ingame-merge.md)/[친구카드 능력 계획](plan-ingame-ability.md))
- [ ] 테스트 시나리오 18개 재검증(Unity Play 모드에서 확인 필요)
- [ ] (추후) 슬롯 현재 상태(key/스탯) 표시 — 필요성이 확인되면 별도 문서
