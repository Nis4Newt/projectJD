---
applyTo: "**"
---

# 커밋 메시지 컨벤션

## 형식

```
JD#<issue-number> <type>(<scope>): <subject>
```

## Issue Number

- branch 이름에 이슈 번호가 포함되어 있는 경우, 커밋 메시지에도 이슈 번호를 포함
- 이슈 번호 없으면 0번으로 표기

## Type

| type | 용도 |
|------|------|
| `feat` | 새 기능 추가 |
| `fix` | 버그 수정 |
| `chore` | 빌드, 설정, 패키지 등 기능 무관 작업 |
| `docs` | 문서 수정 |
| `refactor` | 리팩토링 (기능 변화 없음) |
| `test` | 테스트 추가·수정 |
| `style` | 코드 스타일·포맷 (로직 변화 없음) |
| `perf` | 성능 개선 |

## Scope (선택)

- Unity 프로젝트의 경우 변경된 시스템이나 폴더명 사용
- 예: `gameplay`, `ui`, `audio`, `build`, `assets`

## Subject 규칙

- 한국어 또는 영어 모두 허용
- 동사로 시작 (추가, 수정, 제거 / add, fix, remove)
- 마침표 없음
- 50자 이내

## 예시

```
JD#123 feat(gameplay): 주사위 굴리기 애니메이션 추가
JD#124 fix(ui): 점수판 갱신 누락 수정
JD#125 chore: Unity .gitignore 추가
JD#126 refactor(audio): AudioManager 싱글턴 패턴으로 변경
```

# Description

## 형식

변경된 내용에 대해 자세히 작성