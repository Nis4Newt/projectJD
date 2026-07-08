---
description: "git 변경사항을 확인하고 커밋 컨벤션에 맞게 자동 커밋. 사용: /git-commit"
agent: "agent"
tools: ["run_in_terminal"]
---

[커밋 컨벤션](../instructions/commit-convention.instructions.md)을 반드시 참고해 아래 절차를 수행해.

1. `git status`와 `git diff --staged` (스테이징 없으면 `git diff`)로 변경사항 파악
2. 변경 내용을 분석해 컨벤션에 맞는 커밋 메시지 결정
3. 아직 스테이징되지 않은 파일이 있으면 `git add`로 추가
4. `git commit -m "<subject>" -m "<description>"` 형식으로 커밋 실행 (Description에 변경 내용 상세 작성)
5. 커밋 결과 출력
