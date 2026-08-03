# InGame 로직(Solo 모드) 구현 계획

> 상위 문서: 없음 (신규 최상위 시스템, `03-table`/`04-userdata`/`05-prefab`과 동일한 위계)
> 관련 문서: [메인메뉴 게임 시작 계획](../02-scenemanager/mainmenuscene/plan-mainmenuscene-gamestart.md) (`GameSession.CurrentGameType`을 InGame 씬에서 실제로 소비하는 첫 지점이 이 문서), [씬별 매니저 구현 계획](../02-scenemanager/plan-scenemanager.md) (`InGameSceneManager` 스켈레톤에 실제 로직을 채움), [UserData 구현 계획](../04-userdata/plan-userdata.md) (`_friends`를 덱 구성의 출처로 사용, 이번 문서에서 `icon`/`nextStage` 필드 추가), [테이블 리더 시스템 구현 계획](../03-table/plan-table.md) (`StageTable.GetFriends`로 컴퓨터 덱 구성)
> 범위: `GameType.Solo`(1인모드)로 InGame 씬에 진입했을 때의 초기 덱 구성과 턴 진행 상태 머신. 실제 카드 합체/피격 등 전투 판정, `GameType.Battle`(대전) 모드의 InGame 로직은 범위 밖.

---

## 배경

이 시스템은 두 단계로 나뉜다 — 하나의 문서에 다 담기엔 "덱을 어떻게 만드는가"와 "턴을 어떻게 진행하는가"가 서로 다른 관심사이고, 실제로 이번 구현 단계에서는 두 부분이 아직 서로 연결되지도 않기 때문이다(덱은 생성 후 로그로만 확인, 턴은 실제 카드 소비 없이 스텁 로그만 남김).

1. [덱 구성 계획](plan-ingame-decksetup.md) — `UserData` 필드 확장(`icon`, `nextStage`) + 유저/컴퓨터 30매 덱 생성·셔플·로그 출력
2. [턴 진행 계획](plan-ingame-turnsystem.md) — 유저/컴퓨터가 번갈아 진행하는 3단계 턴 상태 머신(친구카드 플레이 → 공격 주사위 → 타겟 주사위) + 액션 버튼 텍스트 전환

두 문서는 각각 독립적으로 구현·커밋 가능한 단위다. 실제로 덱과 턴을 연결하는 소비 로직(카드 뽑기, 합체 판정 등)은 두 문서 모두에서 범위 밖으로 명시하고, 이후 별도 문서에서 다룬다.

---

## 작업 순서

1. [plan-ingame-decksetup.md](plan-ingame-decksetup.md) — `UserData`에 `icon`/`nextStage` 추가, `InGameSceneManager`에서 유저/컴퓨터 덱 생성 및 로그 출력
2. [plan-ingame-turnsystem.md](plan-ingame-turnsystem.md) — `InGameSceneManager`에 턴 상태 머신 추가, 액션 버튼 연결

---

## 흐름도

```
InGame 씬 진입 (GameSession.CurrentGameType == Solo)
        │
        ▼
[덱 구성]  UserData.Friends           → 유저 30매 셔플
           StageTable.GetFriends(UserData.NextStage) → 컴퓨터 30매 셔플
           (Debug.Log로 확인, 실제 소비는 아직 없음)
        │
        ▼
[턴 시작]  유저 선공, TurnPhase.PlayFriend
        │
        ▼
   ┌───────────────────────────────┐
   │ PlayFriend → RollAttacker →   │  유저: 버튼 클릭으로 진행
   │ RollTarget → (2초 대기)       │  컴퓨터: 매 단계 2초 대기로 진행
   └───────────────────────────────┘
        │
        ▼
   턴 교대 (User ↔ Computer), PlayFriend로 리셋 후 반복
```

---

## 이번 범위에서 제외

- 실제 카드 소비(덱에서 뽑기)/합체 판정(`CardCondition`)/피격(HP 감소) — 턴 상태 머신은 이번엔 스텁 로그만 남김
- 승패 판정, `GameState.GameOver` 전이 — 턴은 종료 조건 없이 반복됨
- `GameType.Battle`(대전) 모드의 InGame 로직 — 이 문서는 Solo 전용

---

## 구현 후 체크리스트

- [ ] [plan-ingame-decksetup.md](plan-ingame-decksetup.md) 구현
- [ ] [plan-ingame-turnsystem.md](plan-ingame-turnsystem.md) 구현
- [ ] (추후) 덱 소비/합체/피격/승패 판정을 다루는 후속 계획 문서 작성
- [ ] (추후) `GameType.Battle` 모드의 InGame 로직 별도 계획 문서
