# 메인메뉴 탭/슬라이드 애니메이션 DOTween 전환 계획

> 상위 문서: [메인메뉴 슬라이드/탭 구현 계획](./plan-mainmenuscene-tabslide.md) (코루틴 기반 스냅 애니메이션을 DOTween으로 교체하며 파생)
> 의존 관계: `Assets/Plugins/Demigiant/DOTween`(이미 설치됨, `DOTweenSettings.asset` 확인 결과 UI 모듈 활성화 상태)
> 범위: `MainMenuTabSlideController`의 페이지 스냅 애니메이션(`SnapToPageRoutine` 코루틴)을 DOTween 트윈으로 교체. 탭/스와이프 입력 처리, 페이지 폭 계산, 에디터 전용 리사이즈 로직은 범위 밖(기존 그대로 유지).

---

## 배경 / 문제 인식

`plan-mainmenuscene-tabslide.md` 작성 시점엔 프로젝트에 DOTween이 없는 것으로 확인돼(`Packages/manifest.json` 기준) 코루틴 기반 `Vector2.Lerp`로 스냅 애니메이션을 구현했다. 이후 `Assets/Plugins/Demigiant/DOTween`이 실제로 설치돼 있는 것을 확인했다 — Asset Store/유니티 패키지 방식 설치라 `Packages/manifest.json` 기준 확인에서는 안 잡혔던 것으로 보인다. `DOTweenSettings.asset` 확인 결과 UI 모듈(`uiEnabled: 1`)이 활성화돼 있어 `RectTransform.DOAnchorPos` 등 UI 트윈 확장 메서드를 바로 쓸 수 있다.

수동 Lerp 코루틴은 이징(easing) 곡선을 직접 구현해야 하고, 진행 중인 애니메이션을 취소/재시작하는 보일러플레이트(`StopCoroutine` + null 체크)를 매번 반복해야 한다. DOTween을 쓰면 이징이 내장돼 있고 `Tween.Kill()`로 취소가 더 명확하다.

---

## 설계 목표

- 페이지 스냅 애니메이션을 DOTween 트윈으로 교체, 코루틴(`SnapToPageRoutine`, `IEnumerator`, `System.Collections`) 제거
- 기존 동작(진행 중 스냅을 새 입력이 오면 즉시 취소하고 새 목표로 재시작) 그대로 유지
- `ScrollRect` 자체 관성과의 충돌 방지(매 프레임 `velocity` 0으로 무력화)도 그대로 유지 — DOTween의 `OnUpdate` 콜백으로 이전
- 탭/스와이프 입력 처리, 페이지 폭 계산(`RecalculatePageWidths`), 에디터 전용 리사이즈(`OnRectTransformDimensionsChange`)는 변경하지 않음 — 이번 문서는 애니메이션 메커니즘 교체만 다룸
- 새 어셈블리 정의(.asmdef) 추가하지 않음 — 프로젝트에 `.asmdef`가 하나도 없어 전부 `Assembly-CSharp`로 컴파일되므로 `DG.Tweening` 네임스페이스는 별도 참조 설정 없이 바로 사용 가능(`DOTweenSettings.asset`의 `createASMDEF: 0`과 일치)

---

## 핵심 설계 결정

### 1. `Coroutine _snapRoutine` → `Tween _snapTween`

```csharp
using DG.Tweening;

private Tween _snapTween;
```

`StartCoroutine`/`StopCoroutine` 대신 `Tween.Kill()`로 취소한다. `Tween`은 `Tweener`(단일 트윈)의 베이스 타입이라 `DOAnchorPos`가 반환하는 타입을 그대로 담을 수 있다.

### 2. 스냅 로직: `SnapToPageRoutine` 코루틴 → `SnapToPage` 일반 메서드

```csharp
private void SnapToPage(int index)
{
    Vector2 target = CalculateTargetPosition(index);

    _snapTween?.Kill();
    _snapTween = _scrollRect.content
        .DOAnchorPos(target, _snapDuration)
        .SetEase(_snapEase)
        .OnUpdate(() => _scrollRect.velocity = Vector2.zero);
}
```

- `_snapTween?.Kill()`이 기존 `if (_snapRoutine != null) StopCoroutine(_snapRoutine)`을 대체 — null이어도(`?.`) 안전
- `OnUpdate` 콜백이 기존 코루틴 루프 안의 `_scrollRect.velocity = Vector2.zero`를 그대로 대체 — DOTween 트윈도 매 프레임(Update 단계) 갱신되므로, `ScrollRect`의 관성 보정(LateUpdate)보다 먼저 실행돼 동일하게 무력화된다
- `SetEase(_snapEase)`로 이징 커브 적용 — 기존 `Vector2.Lerp`는 사실상 `Ease.Linear`와 동일했으므로, 살짝 감속하는 `Ease.OutQuint`를 기본값으로 제안(인스펙터에서 취향껏 조정 가능)
- 코루틴이 스스로 `_snapRoutine = null`로 정리하던 것과 달리, `Tween`은 완료 시 필드를 자동으로 null 처리하지 않는다 — 다만 다음 `SnapToPage` 호출 시 `_snapTween?.Kill()`이 이미 재생 완료된 트윈에 대해서도 무해하게 동작하므로 별도 null 대입은 불필요

### 3. `SetPage`는 `SnapToPageRoutine` 대신 `SnapToPage` 호출

```csharp
public void SetPage(int index)
{
    index = Mathf.Clamp(index, 0, _pages.Length - 1);
    CurrentPage = index;

    UpdateTabHighlights(index);
    SnapToPage(index);
}
```

### 4. 에디터 전용 즉시 재배치는 애니메이션 대상이 아니므로 DOTween 미적용

`OnRectTransformDimensionsChange`(`#if UNITY_EDITOR`)의 재배치는 리사이즈 도중 사용자가 보던 페이지가 순간이동해야 하는 경우라 애초에 Lerp/트윈 없이 즉시 대입이었다. 이 부분은 그대로 유지하되, 진행 중이던 스냅을 취소하는 코드만 `StopCoroutine` → `_snapTween?.Kill()`로 바꾼다.

```csharp
#if UNITY_EDITOR
private void OnRectTransformDimensionsChange()
{
    if (!isActiveAndEnabled || !Application.isPlaying || _scrollRect == null) return;

    RecalculatePageWidths();
    _snapTween?.Kill();
    _scrollRect.content.anchoredPosition = CalculateTargetPosition(CurrentPage);
}
#endif
```

### 5. `OnDestroy`에서 트윈 정리

코루틴은 `MonoBehaviour`가 파괴되면 Unity가 자동으로 중단시키지만, DOTween 트윈은 대상(`content`)이 파괴돼도 `useSafeMode`(현재 프로젝트 설정 `1`)로 안전하게 처리되긴 한다. 다만 명시적으로 정리하는 편이 의도가 분명하다.

```csharp
private void OnDestroy()
{
    _snapTween?.Kill();
}
```

---

## 클래스 구조

```
MainMenuTabSlideController : MonoBehaviour, IEndDragHandler   (기존 파일 수정, MainMenu/)
├── (기존 필드/메서드 대부분 유지)
├── _snapEase : Ease = Ease.OutQuint     ← 신규 [SerializeField]
├── _snapTween : Tween                   ← 기존 _snapRoutine(Coroutine) 대체
├── SnapToPage(int index)                ← 기존 SnapToPageRoutine(IEnumerator) 대체, 코루틴 아님
├── OnDestroy()                          ← 신규, _snapTween 정리
└── (CalculateTargetPosition/CalculateNearestPageIndex/RecalculatePageWidths 등 변경 없음)
```

---

## 파일 구성

```
Assets/
└── Scripts/
    └── MainMenu/
        └── MainMenuTabSlideController.cs   ← 기존 파일 수정 (신규 파일 없음)
```

`Assets/Plugins/Demigiant/DOTween`은 이미 설치돼 있으므로 이번 문서에서 추가로 임포트할 파일 없음.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 스냅 진행 중 다른 탭 클릭/새 스와이프 발생 | `SnapToPage`가 `_snapTween?.Kill()`로 기존 트윈 즉시 종료 후 새 목표로 트윈 재시작 (기존 코루틴 취소와 동일한 동작) |
| 스냅 트윈 진행 중 오브젝트/씬 파괴 | DOTween `useSafeMode: 1` 설정으로 대상이 파괴돼도 예외 없이 트윈이 자동 정리됨. `OnDestroy`의 `_snapTween?.Kill()`은 명시적 정리를 위한 추가 안전장치 |
| 에디터 리사이즈(`OnRectTransformDimensionsChange`)가 스냅 트윈 도중 발생 | `_snapTween?.Kill()` 후 즉시 위치 대입(트윈 없이) — 기존 코루틴 버전과 동일한 흐름 |
| 이미 완료된 트윈에 다시 `Kill()` 호출 | DOTween이 무해하게 처리(예외 없음) — 별도 상태 체크 불필요 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | Tab_2 클릭 | `_snapTween`이 새로 생성되고 `_snapEase` 곡선으로 Page_2 위치까지 이동, 완료 후 `Content.anchoredPosition`이 정확히 목표값과 일치 |
| 2 | 스냅 애니메이션 도중 다른 탭 클릭 | 기존 `_snapTween`이 즉시 `Kill()`되고 새 목표로 트윈이 현재 위치에서 재시작(중간에 위치가 튀지 않음) |
| 3 | 스냅 애니메이션 도중 Game 뷰 리사이즈(에디터) | 트윈이 취소되고 새 뷰포트 폭 기준 위치로 즉시(트윈 없이) 재배치 |
| 4 | 스냅 도중 반대 방향으로 새 스와이프 시작 | `OnEndDrag` 시점에 `SnapToPage`가 다시 호출되며 기존 트윈 `Kill` 후 재시작 — 동작은 기존 코루틴 버전과 동일 |
| 5 | 오브젝트가 스냅 도중 파괴됨(씬 전환 등) | 예외 없이 정리됨(`OnDestroy`) |

---

## 구현 시 주의사항

- **`using DG.Tweening;` 추가, `using System.Collections;`는 코루틴이 없어지므로 제거**
- **`_snapTween?.Kill()`은 반드시 새 트윈을 시작하기 직전에 호출** — 안 하면 이전 트윈과 새 트윈이 동시에 같은 `content.anchoredPosition`을 두고 경합해 떨림(jitter)이 생긴다
- **`OnUpdate` 콜백에서 `velocity = 0` 무력화를 빼먹지 않는다** — 코루틴 버전에서 이미 검증된 요구사항(`ScrollRect` 자체 관성과의 충돌 방지)이 애니메이션 메커니즘이 바뀌어도 동일하게 필요하다
- **DOTween은 기본적으로 `Time.timeScale`의 영향을 받는다** — 기존 코루틴(`Time.deltaTime` 사용)과 동일한 특성이라 별도 처리 불필요
- **새 `.asmdef`를 추가하지 않는다** — 프로젝트 전역에 `.asmdef`가 없어 `DG.Tweening`은 참조 설정 없이 바로 컴파일된다

---

## 구현 후 체크리스트

- [x] `MainMenuTabSlideController.cs`: `using DG.Tweening;` 추가, `using System.Collections;` 제거
- [x] `_snapRoutine`(Coroutine) → `_snapTween`(Tween) 필드 교체
- [x] `SnapToPageRoutine`(IEnumerator 코루틴) 제거, `SnapToPage(int)` 일반 메서드로 교체
- [x] `SetPage`가 `StartCoroutine(SnapToPageRoutine(...))` 대신 `SnapToPage(index)` 호출하도록 수정
- [x] `OnRectTransformDimensionsChange`의 `StopCoroutine`/`_snapRoutine = null`을 `_snapTween?.Kill()`로 교체
- [x] `OnDestroy()` 추가: `_snapTween?.Kill()`
- [x] `_snapEase`(Ease, 기본 `Ease.OutQuint`) `[SerializeField]` 추가
- [ ] 테스트 시나리오 5개 검증(특히 #2: 스냅 도중 취소/재시작이 자연스러운지 육안 확인) — Play로 직접 확인 필요
