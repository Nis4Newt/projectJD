# InGame 로직(Solo 모드) 구현 계획

> 상위 문서: 없음 (신규 최상위 시스템, `03-table`/`04-userdata`/`05-prefab`과 동일한 위계)
> 관련 문서: [메인메뉴 게임 시작 계획](../02-scenemanager/mainmenuscene/plan-mainmenuscene-gamestart.md) (`GameSession.CurrentGameType`을 InGame 씬에서 실제로 소비하는 첫 지점이 이 문서), [씬별 매니저 구현 계획](../02-scenemanager/plan-scenemanager.md) (`InGameSceneManager` 스켈레톤에 실제 로직을 채움), [UserData 구현 계획](../04-userdata/plan-userdata.md) (`_friends`를 덱 구성의 출처로 사용, 이번 문서에서 `icon`/`nextStage` 필드 추가), [테이블 리더 시스템 구현 계획](../03-table/plan-table.md) (`StageTable.GetFriends`로 컴퓨터 덱 구성)
> 범위: `GameType.Solo`(1인모드)로 InGame 씬에 진입했을 때의 초기 덱 구성과 턴 진행 상태 머신. 실제 카드 합체/피격 등 전투 판정, `GameType.Battle`(대전) 모드의 InGame 로직은 범위 밖.

---

## 배경

이 시스템은 세 단계로 나뉜다 — 하나의 문서에 다 담기엔 "덱을 어떻게 만드는가", "턴을 어떻게 진행하는가", "핸드/필드를 어떻게 다루는가"가 서로 다른 관심사이고, 실제로 각 구현 단계에서는 이전 단계가 스텁으로 남겨둔 부분을 다음 단계가 이어받는 구조이기 때문이다.

1. [덱 구성 계획](plan-ingame-decksetup.md) — `UserData` 필드 확장(`icon`, `nextStage`) + 유저/컴퓨터 30매 덱 생성·셔플·로그 출력
2. [턴 진행 계획](plan-ingame-turnsystem.md) — 유저/컴퓨터가 번갈아 진행하는 3단계 턴 상태 머신(친구카드 플레이 → 공격 주사위 → 타겟 주사위) + 액션 버튼 텍스트 전환
3. [핸드/필드 배치 계획](plan-ingame-handfield.md) — `_userDeck`을 처음 실제로 소비해 핸드에 카드를 뽑아 오는 연출 + 핸드의 친구카드를 드래그해 유저 필드(4/5/6번)에 놓는 상호작용
4. [공격 판정 계획](plan-ingame-attack.md) — `RollAttacker`/`RollTarget` 주사위 값으로 필드 6칸 중 공격자/타겟을 선택해 하이라이트·공격 연출을 재생하고, 타겟 슬롯이 비어있으면 본체(기본 체력 30)를 공격해 0이 되면 `GameState.GameOver`로 전이

네 문서는 각각 독립적으로 구현·커밋 가능한 단위다. 합체 판정(`CardCondition`/`CardTarget`)·카드 대 카드 공격/피격·컴퓨터 측 실제 핸드/필드 배치는 네 문서 모두에서 범위 밖으로 명시하고, 이후 별도 문서에서 다룬다(본체 공격/승패 판정은 4단계에서 다룸).

---

## 작업 순서

1. [plan-ingame-decksetup.md](plan-ingame-decksetup.md) — `UserData`에 `icon`/`nextStage` 추가, `InGameSceneManager`에서 유저/컴퓨터 덱 생성 및 로그 출력
2. [plan-ingame-turnsystem.md](plan-ingame-turnsystem.md) — `InGameSceneManager`에 턴 상태 머신 추가, 액션 버튼 연결
3. [plan-ingame-handfield.md](plan-ingame-handfield.md) — `FriendCard`/`FieldSlot` 컴포넌트 추가, 덱→핸드 드로우 연출과 핸드→필드 드래그 앤 드롭 구현
4. [plan-ingame-attack.md](plan-ingame-attack.md) — `Friend` 하이라이트/펀치/이동 연출, `BaseHp` 컴포넌트 추가, `RollAttacker`/`RollTarget`에 실제 공격 판정 연결

---

## 흐름도

```
InGame 씬 진입 (GameSession.CurrentGameType == Solo)
        │
        ▼
[덱 구성]  UserData.Friends           → 유저 30매 셔플
           StageTable.GetFriends(UserData.NextStage) → 컴퓨터 30매 셔플
        │
        ▼
[턴 시작]  유저 선공, TurnPhase.PlayFriend
        │
        ▼
   ┌───────────────────────────────┐
   │ PlayFriend → RollAttacker →   │  유저: 버튼 클릭으로 진행
   │ RollTarget → (공격 연출)      │  컴퓨터: 매 단계 2초 대기로 진행
   └───────────────────────────────┘
        │        (유저 PlayFriend 진입 시) 덱→핸드 드로우(최대 4장),
        │        핸드 친구카드 드래그 → 필드(4/5/6번) 드롭
        │        RollAttacker/RollTarget 값(1~6) = 필드 절대 번호,
        │        비어있으면 그 진영 본체(기본 체력 30) 공격
        ▼
   턴 교대 (User ↔ Computer), PlayFriend로 리셋 후 반복
   (단, 본체 체력이 0이 되면 GameState.GameOver로 전이하고 반복 중단)
```

---

## 이번 범위에서 제외

- 합체 판정(`CardCondition`)/카드 대 카드 공격·피격(HP 감소) — 필드에 `Friend`가 놓이는 것과 본체 공격까지만 다룸(4단계)
- 컴퓨터 측 핸드 연출/필드(1/2/3번), `_computerDeck` 소비 — 컴퓨터의 `PlayFriend`는 로그만 남기는 스텁 그대로(4단계에서 `FieldSlot`만 미리 배치)
- `GameState.GameOver` 이후의 결과 화면/승패 구분 UI — 상태 전이만 발생
- `GameType.Battle`(대전) 모드의 InGame 로직 — 이 문서는 Solo 전용

---

## 구현 후 체크리스트

- [x] [plan-ingame-decksetup.md](plan-ingame-decksetup.md) 구현
- [x] [plan-ingame-turnsystem.md](plan-ingame-turnsystem.md) 구현
- [ ] [plan-ingame-handfield.md](plan-ingame-handfield.md) 구현
- [ ] [plan-ingame-attack.md](plan-ingame-attack.md) 구현
- [ ] (추후) 컴퓨터 핸드/필드(1/2/3번) 별도 계획 문서
- [ ] (추후) 합체 판정/카드 대 카드 공격·피격을 다루는 후속 계획 문서
- [ ] (추후) `GameState.GameOver` 결과 화면
- [ ] (추후) `GameType.Battle` 모드의 InGame 로직 별도 계획 문서
