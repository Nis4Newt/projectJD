---
name: jd-review
description: JungleDice 프로젝트의 변경 코드를 리뷰한다. 이 프로젝트에서 코드 리뷰나 변경점 점검을 요청하면 기본 code-review 대신 이 스킬을 사용.
---

## 분석 대상

현재 변경점으로 생성되거나 수정된 코드

## 진행 순서

1. 먼저 code-review 스킬을 실행해 정확성 버그를 찾는다.
2. 그 결과에 아래 프로젝트 기준을 추가로 적용해 하나의 리뷰로 합쳐 보고한다.

## 프로젝트 추가 기준

- 최적화 방안 제시
- 중복된 내용 제거
- 코루틴 기반인지 (UniTask 미도입)
- 시스템 간 통신에 EventBus를 사용해 결합도를 낮췄는지
- `Singleton<T>`/`SceneSingleton<T>` 템플릿 메서드 패턴을 따르는지
- 구독 해제를 `CompositeDisposable`로 관리하는지
