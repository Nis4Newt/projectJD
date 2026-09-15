# 핸드 → 필드 드롭 브릿지 확인 계획

> 상위 문서: [InGame 필드 World Space 전환 개요](plan-ingame-worldspace.md) (3단계, [FieldSlot 전환](plan-ingame-worldspace-fieldslot.md)·[WorldFriend 신설](plan-ingame-worldspace-worldfriend.md) 완료 후)
> 의존 관계: `JungleDice.InGame.FriendCardBattleControl`, `JungleDice.InGame.FieldSlot`, `JungleDice.InGame.InGameSceneManager`
> 범위: 핸드(UI) → 필드(world space) 드래그 앤 드롭이 실제로 동작하는지 확인. 코드 변경은 없다 — 왜 필요 없는지와 검증 지점만 다룬다.

---

## 배경

[개요 문서](plan-ingame-worldspace.md)는 처음에 "스크린 좌표 → world 좌표 변환이 필요할 것"으로 예상했다. 하지만 [FieldSlot 전환 계획](plan-ingame-worldspace-fieldslot.md)에서 Main Camera에 `Physics2DRaycaster`를 추가하는 것만으로, uGUI `EventSystem`의 표준 드래그 앤 드롭 파이프라인(`OnBeginDrag`→`OnDrag`→`OnDrop`→`OnEndDrag`)이 world space `Collider2D` 대상에도 그대로 동작한다는 게 확인됐다 — `EventSystem`은 씬에 등록된 모든 `BaseRaycaster`의 히트 결과를 합쳐 처리하며, `GraphicRaycaster`와 `Physics2DRaycaster`를 구분하지 않는다.

따라서 `FriendCardBattleControl.cs`와 `FieldSlot.cs`는 코드 변경이 필요 없다. 이 문서는 "왜 필요 없는지"와 "그래도 검증해야 할 지점"만 남긴다.

---

## 핵심 확인 사항 (설계 결정 아님 — 기존 경로가 그대로 맞물리는지 확인)

- `FieldSlot.OnDrop(eventData)`의 `eventData.pointerDrag.GetComponent<FriendCardBattleControl>()` — 드래그 중인 오브젝트는 여전히 UI GameObject이므로 world space 전환과 무관하게 그대로 동작
- `InGameSceneManager.TryPlaceFriendCard(slot, card)` — `slot.transform`이 world space Transform이어도 `WorldFriend` 생성([WorldFriend 신설 계획](plan-ingame-worldspace-worldfriend.md)의 `SpawnWorldFriend`)과 `FieldSlot.PlaceFriend`([FieldSlot 전환 계획](plan-ingame-worldspace-fieldslot.md))는 동일하게 동작
- `FriendCardBattleControl.OnEndDrag`의 `_wasPlaced` 플래그 판정도 좌표계와 무관 — 변경 없음
- `InGameSceneManager.ShowMergePreview`/`HideMergePreview`는 `FieldSlot.PlacedFriend`를 읽을 뿐이라 별도 확인 불필요

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `Physics2DRaycaster`가 카메라에 없음(설치 누락) | `OnDrop`이 호출되지 않아 카드가 항상 원래 핸드 슬롯으로 복귀 — [FieldSlot 전환 계획](plan-ingame-worldspace-fieldslot.md) 체크리스트로 사전 확인 |
| 다른 UI 패널이 화면상 필드 위를 덮음 | UI와 world space는 서로 다른 좌표계라 레이캐스트 우선순위 자체엔 문제가 없지만, 사용자가 물리적으로 그 위치를 드롭할 수 없으므로 레이아웃 확인 |
| `Physics2DRaycaster` 경로가 기대대로 동작하지 않음(Input System UI Input Module 호환성 등) | 이 문서 범위 밖 — 발생 시 `FriendCardBattleControl.OnEndDrag`에서 `Physics2D.OverlapPoint`로 슬롯을 직접 찾는 수동 경로를 별도 후속 문서로 추가 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 핸드 카드를 드래그해 빈 필드 슬롯(world space) 위에서 놓기 | `OnDrop` 호출, `WorldFriend` 인스턴스가 슬롯 자식으로 생성, 카드 UI 파괴 |
| 2 | 점유된 필드 슬롯 위에서 병합 가능한 카드 드롭 | `MergeCardIntoSlot` 경로 정상 진입 — `ShowMergePreview`로 미리 하이라이트된 슬롯과 일치 |
| 3 | 필드 밖(배경) 또는 드롭 판정이 없는 위치에 드롭 | 카드가 원래 핸드 슬롯으로 복귀(`OnEndDrag`의 `AttachToSlot`) |

---

## 구현 시 주의사항

- 이 문서는 "코드를 추가하는" 문서가 아니라 "왜 코드가 필요 없는지 확인하는" 문서다 — [FieldSlot 전환 계획](plan-ingame-worldspace-fieldslot.md)의 `Physics2DRaycaster` 설치가 선행되지 않으면 이 문서의 전제가 깨진다.
- 실제 테스트에서 기대대로 동작하지 않으면 그때 수동 `Physics2D.OverlapPoint` 경로를 검토한다(예상되는 표준 경로이므로 선제적으로 코드를 추가하지 않는다 — YAGNI).

---

## 구현 후 체크리스트

- [x] Main Camera에 `Physics2DRaycaster`가 설치돼 있는지 확인
- [x] 테스트 시나리오 검증 — 핸드→필드 드롭이 코드 변경 없이 정상 동작
