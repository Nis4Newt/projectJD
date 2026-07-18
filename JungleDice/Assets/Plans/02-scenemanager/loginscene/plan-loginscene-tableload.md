# Login 씬 — 테이블 프리로드 구현 계획

> 상위 문서: [Login 씬 구현 계획 — 개요](plan-loginscene.md) (1번 하위 문서의 placeholder task를 실제 로직으로 교체), [테이블 리더 시스템 구현 계획](../../03-table/plan-table.md) (여기서 만든 `TableBase<TSelf,TData,TKey>.Instance`를 소비)
> Phase: Login 씬 Phase 1 후속 — [1] task 순차 실행([plan-loginscene-tasksequence.md](plan-loginscene-tasksequence.md), 이미 구현됨)이 끝난 상태를 전제
> 의존 관계: `plan-table.md`(테이블 프레임워크), `plan-loginscene-tasksequence.md`(`LoginTask[]` 순차 실행 인프라)
> 범위: `LoginSceneManager._tasks`의 placeholder 하나를 실제 "전체 테이블 프리로드" 로직으로 교체. 신규 테이블 추가 시 로그인 코드 수정이 필요 없도록 리플렉션 기반 전체 스캔 방식 채택. 테이블별 실패 시 재시도/에러 팝업은 범위 밖

---

## 배경

`plan-table.md`가 만든 `XxxTable.Instance`는 **최초 접근 시점**에 `Resources.Load`를 수행하는 지연 로딩(lazy) 구조다. 즉 아무 코드도 손대지 않으면, 게임 중 처음 `PowerTable.Instance.Get(...)` 등을 호출하는 그 순간에 로드 비용(디스크 I/O + 역직렬화 + `OnLoaded()` 가공)이 발생한다. 문제는 두 가지:

- 그 호출이 하필 전투 연출 중이거나 UI 프레임 중이면 예기치 않은 프레임 드랍(hitch)이 생긴다.
- 테이블 asset이 누락된 경우(`Resources/Tables/xxx.asset` 없음) `Debug.LogError`만 남기고 `null`을 반환하는데, 이 문제를 게임 진행 중 우연히 발견하게 되면 원인 추적이 늦어진다.

Login 씬은 이미 [1] `LoginTask[]` 순차 실행 인프라(`LoginSceneManager.TaskSequenceRoutine`)가 있고, 그 안에 "설정 로드" 등 3개 placeholder(`WaitForSeconds`)가 자리만 잡고 있다. 이 문서는 그중 하나를 "지금까지 만들어진 모든 테이블을 미리 `Instance`에 접근해 로드해두는" 실제 로직으로 교체한다 — 로딩 화면이라는, 프레임 드랍이 이미 용인되는 시점에 비용을 몰아넣고, asset 누락도 이 시점에 조기 발견한다.

---

## 설계 목표

- Login 씬 진입 시 모든 테이블을 한 번씩 `Instance`로 접근해 워밍업(warm-up) — 이후 게임플레이 코드가 처음 접근할 때는 이미 캐시된 상태
- **테이블이 늘어나도 로그인 쪽 코드를 수정할 필요가 없어야 함** — `plan-table.md`가 이미 채택한 "등록 리스트 없이 리플렉션으로 발견" 철학을 런타임 프리로드에도 동일하게 적용
- 테이블 개수가 늘어나도 한 프레임에 전부 몰아서 로드하지 않고, 여러 프레임에 걸쳐 분산 — task 인프라가 이미 코루틴 기반이므로 자연스럽게 가능
- 테이블 하나가 실패(asset 없음)해도 나머지 테이블 로드와 로그인 흐름 전체를 막지 않음 — `plan-table.md`가 이미 정한 "데이터 부재는 기획 이슈지 크래시 사유 아님" 원칙을 그대로 재사용
- 새 메커니즘 최소화: `TableGenerator.FindTableType`가 이미 쓰는 `AppDomain` 전체 스캔 방식을 런타임 코드에도 그대로 재사용(단, "이름 하나 찾기"가 아니라 "전체 목록 나열"이므로 쿼리 형태만 다름)

---

## 핵심 설계 결정

### 어떤 placeholder를 대체할 것인가: "설정 로드"

```csharp
private static readonly LoginTask[] _tasks =
{
    new("설정 로드", () => PlaceholderTask(0.3f)),      // ← 이 자리를 테이블 로드로 교체
    new("유저 데이터 로드", () => PlaceholderTask(0.5f)), // 그대로 유지 (SaveSystem 연동은 별도 문서)
    new("서버 시간 동기화", () => PlaceholderTask(0.3f)), // 그대로 유지 (서버 통신은 별도 문서)
};
```

테이블은 기획자가 관리하는 게임 설정 데이터이므로 세 placeholder 중 "설정 로드"가 의미상 가장 가깝다. "유저 데이터 로드"(세이브 시스템)와 "서버 시간 동기화"(서버 통신)는 이번 범위와 무관하므로 손대지 않는다.

### 런타임 전체 테이블 스캔: `TableLoader` (신규, `Core/Table/`)

`TableGenerator.FindTableType`(Editor 전용)은 "파일명 하나 → 대응 타입 하나"를 찾는 반면, 여기서는 "존재하는 모든 테이블 타입"이 필요하다. 같은 `AppDomain.CurrentDomain.GetAssemblies()` 스캔 방식을 재사용하되, 쿼리 조건을 이름 일치 대신 `TableAssetBase` 상속 여부로 바꾼다. `UnityEditor` API를 전혀 쓰지 않으므로 빌드에도 포함되는 런타임 코드이며, 위치는 Editor 폴더가 아니라 `Core/Table/`이다.

```csharp
namespace JungleDice.Core.Table
{
    public static class TableLoader
    {
        public static IEnumerator LoadAllRoutine()
        {
            foreach (var type in FindAllTableTypes())
            {
                // TSelf별로 닫힌 제네릭이 독립적인 static Instance를 가지므로
                // 리플렉션으로도 정확히 해당 타입의 프로퍼티를 찾아낼 수 있다.
                var instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                instanceProperty?.GetValue(null); // getter 내부에서 Resources.Load + OnLoaded() 수행 (실패해도 LogError만, 예외 없음)
                yield return null; // 테이블 하나당 1프레임 — 다수 테이블이 한 프레임에 몰리는 것을 방지
            }
        }

        private static IEnumerable<Type> FindAllTableTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeGetTypes)
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && typeof(TableAssetBase).IsAssignableFrom(t));
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }
    }
}
```

- `!IsAbstract && !IsGenericTypeDefinition` 조건으로 `TableAssetBase`, `TableBase<,,>` 같은 중간 추상/오픈 제네릭 타입은 제외하고 `SampleTable`처럼 실제 닫힌 제네릭 서브클래스만 남긴다.
- `Instance` 프로퍼티는 `TableBase<TSelf,TData,TKey>`가 `TSelf`마다 별도로 갖는 static 멤버이므로, 리플렉션으로 얻은 콘크리트 타입에서 `GetProperty("Instance", ...)`로 정확히 그 테이블의 프로퍼티를 찾는다.
- `instanceProperty.GetValue(null)`이 곧 `Resources.Load` + `OnLoaded()` 트리거 — 반환값은 버린다(워밍업이 목적이므로 값 자체는 필요 없음).

### `LoginSceneManager` 연동

```csharp
new("설정 로드", TableLoader.LoadAllRoutine),
```

기존 `LoginTask`의 `Func<IEnumerator> Run` 시그니처와 `TableLoader.LoadAllRoutine`(정적 메서드, `IEnumerator` 반환)이 그대로 맞으므로 델리게이트 변환에 별도 래핑이 필요 없다.

### 실패 처리: 테이블 프레임워크의 기존 원칙을 그대로 승계

`plan-table.md`가 이미 정한 두 가지 실패 처리 원칙을 이 문서에서 새로 정의하지 않고 그대로 물려받는다:

- **asset 누락**(`Resources/Tables/xxx.asset` 없음): `Instance` getter가 `Debug.LogError` 후 `null` 반환 — 예외를 던지지 않으므로 `TableLoader`가 별도로 try/catch할 필요 없음. 다음 테이블로 계속 진행.
- **`OnLoaded()` 내부 예외**: `plan-table.md`가 "테이블 클래스 구현 버그이므로 숨기지 않고 명확히 실패시키는 쪽을 택함"이라 명시했으므로, 여기서도 감싸지 않는다. 이 경우 `TaskSequenceRoutine` 코루틴이 예외로 중단되고 로그인 흐름 전체가 멈춘다 — 의도된 동작(치명적 코드 버그를 로그인 단계에서 바로 드러냄).

---

## 클래스 구조

```
TableLoader                                       (신규, Core/Table/, 런타임 static)
└── LoadAllRoutine() : IEnumerator                 ← [MenuItem 아님] LoginTask.Run에 바로 연결
    └── FindAllTableTypes() : IEnumerable<Type>    ← AppDomain 전체 스캔, TableAssetBase 서브클래스만

LoginSceneManager                                 (기존, Login/)
└── _tasks[0] = new("설정 로드", TableLoader.LoadAllRoutine)   ← PlaceholderTask(0.3f) 대체
```

---

## 파일 구성

```
Assets/Scripts/
├── Core/
│   └── Table/
│       └── TableLoader.cs        ← 신규, 런타임 전체 테이블 프리로드
└── Login/
    └── LoginSceneManager.cs      ← 변경, "설정 로드" task의 Run을 TableLoader.LoadAllRoutine으로 교체
```

---

## 엣지 케이스

| 상황 | 처리 방식 |
|------|-----------|
| 테이블이 아직 하나도 없음(`Resources/Tables/` 비어있음) | `FindAllTableTypes()`가 빈 시퀀스 반환 → `foreach`가 즉시 종료, task는 사실상 즉시 완료 처리 |
| 특정 테이블의 `.asset`이 없음(타입은 있는데 변환 안 됨) | 해당 `Instance` getter가 `LogError` 후 `null`, `TableLoader`는 다음 타입으로 계속 진행 — 로그인 흐름은 끊기지 않음 |
| `OnLoaded()` 내부에서 예외 발생 | 감싸지 않고 그대로 전파 → `TaskSequenceRoutine` 중단 (테이블 코드 버그를 로그인 단계에서 즉시 노출, `plan-table.md`와 동일 원칙) |
| 테이블 개수가 많아짐(예: 20개) | task 하나가 최대 20프레임에 걸쳐 완료됨 — `LoginProgressChanged`는 task 단위로만 발행되므로 세부 진행률 표시는 안 됨(범위 밖, 필요 시 별도 문서) |
| 새 테이블 클래스를 프로젝트에 추가 | `TableLoader`/`LoginSceneManager` 코드 수정 불필요 — 다음 스캔에서 자동 포함 |
| `type.GetProperty("Instance", ...)`가 `null`인 경우 | 이론상 발생하지 않음(`TableAssetBase` 서브클래스는 전부 `TableBase<TSelf,TData,TKey>`를 거쳐 `Instance`를 가짐) — 방어적으로 `?.GetValue(null)`로 null 조건부 접근만 유지 |
| Login 씬이 아닌 다른 씬(MainMenu 등)에서 테이블에 먼저 접근 | 문제 없음 — `Instance`는 여전히 지연 로딩 + 정적 캐시이므로 이 task가 실행되기 전에 접근해도 정상 동작(그 접근 시점에 로드됨). 이 task는 "미리 당겨오는" 최적화일 뿐 필수 전제조건이 아님 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `SampleTable.asset`만 존재하는 상태로 Login 씬 진입 | "설정 로드" task 동안 `SampleTable.Instance`가 워밍업되어 캐시됨(콘솔에 `LogError` 없음), 이후 정상적으로 `LoginProgressChanged(1, 3, "설정 로드")` 발행 |
| 2 | 테스트용으로 테이블 클래스를 하나 더 추가 후(예: `DummyTable`) 재진입 | 코드 수정 없이 두 테이블 모두 자동으로 로드됨, task 완료까지 최소 2프레임 소요 |
| 3 | `SampleTable.asset`을 삭제한 상태로 진입 | 콘솔에 `[Table] SampleTable 로드 실패` `LogError` 출력되지만 "설정 로드" task는 정상 완료되고 이후 task(유저 데이터 로드 등)와 `GoogleLoginSucceeded`까지 정상 도달 |
| 4 | 테이블이 하나도 없는 상태(신규 프로젝트 클론 직후, `Generate All Tables` 미실행) | `foreach`가 즉시 종료, task가 사실상 0프레임 대기로 완료 처리 |
| 5 | 테이블 로드 이후 게임플레이 코드에서 `SampleTable.Instance.Get(1)` 호출 | 이미 캐시된 인스턴스를 즉시 반환(추가 `Resources.Load` 없음) |

---

## 구현 시 주의사항

- **`TableLoader`는 `Core/Table/`(런타임)에 두고 `Editor/Table/`(에디터 전용)과 절대 혼동하지 않는다**: `TableGenerator`는 `.txt → .asset` 변환용 에디터 전용 코드이고, `TableLoader`는 이미 생성된 `.asset`을 런타임에 미리 읽어들이는 별개의 코드다.
- **`FindAllTableTypes`의 스캔 조건은 `TableAssetBase` 상속 여부만 본다**: 파일명 매칭이 아니므로 `TableGenerator.FindTableType`과 로직을 공유할 필요는 없다(목적이 다른 별도 헬퍼로 유지).
- **테이블별 실패를 흡수해 로그인 흐름을 막지 않는 것은 의도된 동작**: 여기서 try/catch를 추가해 "전체 실패 시 재시도" 같은 로직을 넣지 않는다(범위 밖, YAGNI) — asset 누락은 `LogError`로 충분히 드러난다.
- **테이블 개수가 늘어나 로딩 시간이 체감될 정도가 되면**: `yield return null`(1테이블/프레임)이 아니라 일정 개수씩 묶어 처리하거나, task별 세부 진행률 이벤트를 추가하는 것을 고려할 수 있음 — 지금은 필요성이 확인되지 않았으므로 범위 밖.

---

## 구현 후 체크리스트

- [x] `TableLoader.cs` 작성 (`Assets/Scripts/Core/Table/`)
- [x] `LoginSceneManager._tasks[0]`의 `Run`을 `TableLoader.LoadAllRoutine`으로 교체
- [ ] 테스트 시나리오 5개 검증 (특히 #3: asset 삭제 상태에서도 로그인 흐름이 끊기지 않는지) — Unity 에디터에서 플레이 모드로 직접 확인 필요
- [ ] `plan-loginscene.md`의 하위 문서 표에 이 문서를 반영할지 검토(선택 사항 — 이 문서는 [1]의 후속 보강이라 별도 번호를 붙이지 않음)
