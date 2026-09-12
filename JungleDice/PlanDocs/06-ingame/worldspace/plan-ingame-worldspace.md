# InGame 필드 World Space 전환 개요

> 상위 문서: 없음 (기존 [InGame 로직 개요](../plan-ingame.md) 완료 이후 별도로 결정된 구조 개편 — `06-ingame/worldspace/`는 이 문서가 파생시키는 하위 문서들의 폴더)
> 관련 문서: [InGame 로직 개요](../plan-ingame.md), [핸드/필드 배치 계획](../plan-ingame-handfield.md) (`FieldSlot`/`Friend`/`FriendCard` 최초 설계 — `FieldSlot`은 이번 전환의 개조 대상, `Friend`는 유지된 채 `WorldFriend`가 신설됨), [공격 판정 계획](../plan-ingame-attack.md) (`Friend` 하이라이트·연출 — `WorldFriend` 기준으로 재확인 필요), [친구카드 합체 계획](../plan-ingame-merge.md), [필드 슬롯 치트 에디터 계획](../plan-ingame-cheat.md) (`FieldSlot` 참조), [Friend 컴포넌트 구현 계획](../../05-prefab/plan-prefab.md)
> 범위: 필드 카드 슬롯(`FieldSlot`)을 uGUI(`RectTransform`/`Image`) 기반에서 world space(`Transform`/`SpriteRenderer`) 기반으로 전환하고, 핸드→필드 드래그 앤 드롭의 드롭 판정이 world space 대상을 인식하도록 바꾼다. 필드에 배치되는 친구카드는 기존 UI 버전(`Friend`)을 그대로 유지한 채 world space 버전(`WorldFriend`)을 신설한다 — 두 프리팹은 기능(카드 데이터 표시)은 같고 사용처(UI/world space)만 다르다. 본체 체력 오브젝트(`BaseStone`)도 world space로 전환한다 — `Friend`와 달리 필드 밖 다른 사용처가 없어 별도 클래스 신설 없이 그대로 전환한다. 핸드 쪽(`HandSlot`/`FriendCard`, 카드를 쥐고 있는 UI)은 이번 범위에서 유지 — 드래그는 여전히 uGUI에서 시작하고, 도착점(필드)만 world space로 바뀐다.

---

## 배경

현재 필드는 `Canvas`(Screen Space - Overlay) 하위 `Image`/`RectTransform`로 구현돼 있다(`FieldSlot`, `Friend` 프리팹 모두). `FieldSlot`은 `SpriteRenderer` 기반 world space 오브젝트로 바꾸지만, `Friend`(필드용 친구카드)는 기존 UI 버전을 없애지 않는다 — `Friend`가 다른 UI 사용처(추후 확장 여지)에도 쓰일 수 있어, world space 전용 `WorldFriend`를 별도 프리팹/컴포넌트로 신설하고 기존 `Friend`는 그대로 둔다. 카메라는 이미 Orthographic(size 5)이라 world space 2D 좌표계 자체는 별도 카메라 작업 없이 바로 쓸 수 있다.

이번 문서는 개요만 담는다 — 실제 설계 결정과 구현 코드는 하위 문서 4개에서 각각 다룬다.

---

## 설계 목표

- `FieldSlot`은 world space `SpriteRenderer` 기반으로 전환하되, `Index`/`IsOccupied` 등 기존 공개 인터페이스는 최대한 유지 — [공격](../plan-ingame-attack.md)/[합체](../plan-ingame-merge.md)/[치트](../plan-ingame-cheat.md) 문서가 이미 이 인터페이스에 의존하고 있어 재작성 범위를 좁힌다
- `Friend`(UI)는 그대로 두고 `WorldFriend`(world space)를 신설 — 두 프리팹은 카드 데이터 표시(`SetKey`, 공격력/체력, 하이라이트)라는 같은 기능을 UI/world space 각자의 렌더링 방식으로 구현한다. `InGameSceneManager`가 필드에 배치할 때는 `WorldFriend`만 `Instantiate`
- 배치 승인 책임(`InGameSceneManager.TryPlaceFriendCard`가 한 곳에서 판정)은 그대로 유지 — 필드에 실제로 생성하는 프리팹만 `Friend`에서 `WorldFriend`로 교체
- 핸드는 건드리지 않는다 — 드래그 시작(`OnBeginDrag`/`OnDrag`)은 여전히 uGUI 이벤트, 드롭 판정 대상만 world space로 바뀌므로 "화면 좌표 → world 좌표 변환"이 새로 필요한 지점은 드롭 순간뿐

---

## 흐름도 (전환 순서)

```
[1] FieldSlot world space 전환
      RectTransform/Image → Transform/SpriteRenderer, 자식 없는 빈 슬롯 상태로 먼저 동작 확인
        │
        ▼
[2] WorldFriend(필드용 친구카드, world space 신설) 작성
      기존 Friend(UI)는 유지, SpriteRenderer 기반 WorldFriend를 별도로 추가
      텍스트(TMP UGUI) 대체 방식 결정
      (world space 슬롯이 먼저 있어야 그 자식으로 채워 넣는 이 단계를 검증 가능)
        │
        ▼
[3] 핸드 → 필드 드롭 브릿지
      Main Camera에 Physics2DRaycaster를 추가하면 기존 OnDrop 파이프라인이 그대로 동작 — 코드 변경 없음
```

세 단계는 이 순서로 진행한다 — 슬롯 골격이 없으면 카드 프리팹을 끼워 넣어 검증할 수 없고, 카드가 없으면 드롭 브릿지를 끝까지 테스트할 수 없다.

---

## 하위 문서

| # | 문서 | 내용 |
|---|------|------|
| 1 | [plan-ingame-worldspace-fieldslot.md](plan-ingame-worldspace-fieldslot.md) | `FieldSlot`이 붙는 씬 오브젝트를 `Transform`/`SpriteRenderer`/`Collider2D`로 전환, Main Camera에 `Physics2DRaycaster` 추가 |
| 2 | [plan-ingame-worldspace-worldfriend.md](plan-ingame-worldspace-worldfriend.md) | `WorldFriend.cs` 신설(`Friend.cs` 포팅) + `InGameSceneManager.cs`의 필드 인스턴스 타입 치환, `WorldFriend.prefab` 생성 |
| 3 | [plan-ingame-worldspace-carddrop.md](plan-ingame-worldspace-carddrop.md) | 핸드→필드 드롭이 `Physics2DRaycaster` 경로로 코드 변경 없이 동작함을 확인 |
| 4 | [plan-ingame-worldspace-basestone.md](plan-ingame-worldspace-basestone.md) | `BaseStone.cs`를 world space(`SpriteRenderer`/`TextMeshPro`)로 전환 — `Friend`와 달리 다른 사용처가 없어 별도 클래스 없이 직접 수정 |

네 문서는 각각 자기완결적으로 작성하고, 독립적으로 구현·커밋한다.

---

## 작업 순서

1. [plan-ingame-worldspace-fieldslot.md](plan-ingame-worldspace-fieldslot.md) — `FieldSlot` 전환
2. [plan-ingame-worldspace-worldfriend.md](plan-ingame-worldspace-worldfriend.md) — `WorldFriend` 신설(`Friend`는 유지)
3. [plan-ingame-worldspace-carddrop.md](plan-ingame-worldspace-carddrop.md) — 핸드→필드 드롭 브릿지 확인
4. [plan-ingame-worldspace-basestone.md](plan-ingame-worldspace-basestone.md) — `BaseStone` 전환 (1~3단계와 독립적으로 진행 가능)

---

## 이번 범위에서 제외

- 핸드(`HandSlot`/`FriendCard`) 자체의 world space 전환 — 카드를 쥐는 UI는 그대로 유지, "핸드에서 world space로" 드롭되는 대상(필드)만 바뀐다
- 기존 `Friend`(UI) 프리팹/컴포넌트 자체의 수정·제거 — 이번 전환은 `WorldFriend`를 신설할 뿐, `Friend`는 건드리지 않는다. `Friend`의 다른 사용처를 넓히는 것도 범위 밖
- 카메라 워크(줌/이동), 필드 화면비 반응형 재배치(현재 `mybase`는 Canvas 기준 반응형 — world space 전환 후 별도 검토 필요, [핸드 패널 반응형 계획](../plan-ingame-handpanel-responsive.md)과 유사한 후속 문서로)
- 공격/합체/치트 문서들의 연출 로직 자체 재작성 — 인터페이스가 유지되는 한 하위 문서에서 필요한 최소 수정만 다룸

---

## 구현 후 체크리스트

- [x] [plan-ingame-worldspace-fieldslot.md](plan-ingame-worldspace-fieldslot.md) 구현 — `FieldSlot` world space 전환, `Physics2DRaycaster`/`Collider2D` 설치
- [x] [plan-ingame-worldspace-worldfriend.md](plan-ingame-worldspace-worldfriend.md) 구현 — `WorldFriend.cs`/`WorldFriend.prefab`, `InGameSceneManager.cs` 타입 치환
- [x] [plan-ingame-worldspace-carddrop.md](plan-ingame-worldspace-carddrop.md) 확인 — 핸드→필드 드롭 정상 동작
- [x] [plan-ingame-worldspace-basestone.md](plan-ingame-worldspace-basestone.md) 구현 — `BaseStone` world space 전환
- [x] [InGame 로직 개요](../plan-ingame.md) 체크리스트에 이 문서 링크 추가
- [ ] (추후) 필드 화면비 반응형 재배치 계획 문서
