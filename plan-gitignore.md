# Unity .gitignore 추가 계획

## 현재 상태
- 워크스페이스 루트: `d:\02_Repository\projectJD`
- Unity 프로젝트 경로: `JungleDice/`
- `.gitignore` 파일 없음

---

## 작업 목록

- [ ] 1. 루트에 `.gitignore` 파일 생성 (Unity 공식 권장 패턴 기반)
- [ ] 2. 무시할 디렉터리 확인 및 포함 여부 결정
- [ ] 3. 현재 추적 중인 불필요한 파일 정리 (git rm --cached)
- [ ] 4. `.gitignore` 적용 확인

---

## .gitignore 포함 항목

### Unity 자동 생성 디렉터리
| 경로 | 이유 |
|------|------|
| `JungleDice/Library/` | Unity 캐시, 로컬 설정 |
| `JungleDice/Temp/` | 빌드 임시 파일 |
| `JungleDice/Logs/` | 에디터 로그 |
| `JungleDice/obj/` | 빌드 중간 산출물 |
| `JungleDice/Build/` | 빌드 결과물 |
| `JungleDice/Builds/` | 빌드 결과물 (복수형) |
| `JungleDice/UserSettings/` | 개인 에디터 설정 |

### Unity 자동 생성 파일
| 경로 | 이유 |
|------|------|
| `JungleDice/*.suo` | Visual Studio 솔루션 옵션 |
| `JungleDice/*.tmp` | 임시 파일 |
| `JungleDice/ExportedObj/` | 익스포트 캐시 |
| `JungleDice/.vsconfig` | VS 로컬 설정 |

### IDE / OS 파일
| 경로 | 이유 |
|------|------|
| `.vs/` | Visual Studio 설정 |
| `*.csproj` | 자동 생성 프로젝트 파일 |
| `*.sln` | 자동 생성 솔루션 파일 (`.slnx`는 추적 유지) |
| `.DS_Store` | macOS 메타 파일 |
| `Thumbs.db` | Windows 썸네일 캐시 |

---

## 주의 사항

- `JungleDice/Assets/` — **반드시 추적** (게임 소스)
- `JungleDice/Packages/` — **반드시 추적** (패키지 의존성)
- `JungleDice/ProjectSettings/` — **반드시 추적** (프로젝트 설정)
- `JungleDice/JungleDice.slnx` — 추적 여부 팀과 협의 (자동 생성 여부 확인)

---

## 실행 단계

```bash
# 1. 루트에 .gitignore 생성 (완료)

# 2. 변경사항 커밋
git add .gitignore
git commit -m "chore: add Unity .gitignore"
```

> 기존 커밋된 파일이 없으므로 `git rm --cached` 불필요.

---

## 최종 .gitignore 내용 (초안)

```gitignore
# Unity 생성 디렉터리
JungleDice/[Ll]ibrary/
JungleDice/[Tt]emp/
JungleDice/[Oo]bj/
JungleDice/[Bb]uild/
JungleDice/[Bb]uilds/
JungleDice/[Ll]ogs/
JungleDice/[Uu]ser[Ss]ettings/
JungleDice/ExportedObj/
JungleDice/MemoryCaptures/

# Unity 자동 생성 파일
JungleDice/.vsconfig
JungleDice/sysinfo.txt

# Visual Studio / Rider IDE
.vs/
*.csproj
*.unityproj
*.sln
!JungleDice/JungleDice.slnx

# OS
.DS_Store
Thumbs.db
Desktop.ini
```
