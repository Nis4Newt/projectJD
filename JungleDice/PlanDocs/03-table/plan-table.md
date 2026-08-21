# 테이블 리더 시스템 구현 계획

> 상위 문서: 없음 (신규 최상위 카테고리 — 데이터 계층 인프라)
> Phase: 신규 시스템   
> 의존 관계: 없음 (독립적인 데이터 계층. 런타임 접근 패턴은 향후 다른 시스템이 `xxxTable.Instance.공개메서드(...)`로 참조하게 됨)   
> 범위: `.csv` 테이블 파일 → `ScriptableObject` 변환 파이프라인, 런타임 조회 API. Addressables 기반 로드나 다국어/바이너리 포맷 지원은 제외 (YAGNI)   

---

## 배경 / 문제 인식

기획 데이터를 코드에 하드코딩하면 값 변경마다 재컴파일이 필요하고, 기획자가 직접 수정하기 어렵다. `.csv` + `|` 구분 포맷으로 데이터를 관리하고, 에디터에서 `ScriptableObject`로 미리 구워두면:

- 런타임에는 파싱 비용 없이 `Resources.Load` 한 번으로 로드
- Unity Inspector에서 값 확인 가능
- 기획자는 텍스트 편집기만으로 데이터 수정 가능

문제는 테이블마다 "텍스트 파싱 → 필드 채우기 → 원본 데이터 보관" 로직을 반복 작성하면 테이블 수만큼 보일러플레이트가 늘어난다는 것. 기존 `Singleton<T>`(`Assets/Scripts/Core/Singleton.cs`)이 CRTP로 이 문제를 해결한 전례가 있으므로 동일 패턴을 재사용한다.

---

## 설계 목표

- 원본 key 조회, 중복 key 검증, 런타임 싱글턴 로드는 전부 공용 베이스가 담당 — 이 부분은 테이블마다 반복 작성하지 않음
- CSV 텍스트 → `TData` 필드 변환(파싱)은 테이블 클래스가 `ParseRow` 훅으로 직접 담당 — 컬럼 하나가 필드 하나에 대응할 필요가 없어, 여러 컬럼을 합쳐 배열 하나로 만들거나 컬럼 일부만 골라 쓰는 것도 자유롭게 표현 가능 (대신 파싱할 컬럼명은 테이블 클래스 코드에 하드코딩됨)
- 반면 **공개 조회 API는 테이블마다 자유롭게 설계**: 어떤 테이블은 단순 key 조회면 충분하고, 어떤 테이블은 등급별/이름별 등 보조 인덱스나 가공된 데이터가 필요할 수 있음 — 베이스가 하나의 API 형태를 강제하지 않음
- 원본 데이터(`Rows`/key 인덱서)는 베이스에서 `protected`로 감춰 각 테이블 클래스만 접근 가능 — 외부 코드는 반드시 그 테이블 클래스가 노출한 메서드를 거치도록 강제해 조회 로직이 여러 곳에 흩어지는 것을 방지
- 로드 시점에 계산이 필요한 가공 데이터(보조 인덱스 등)를 위한 훅 제공 — 각 테이블 클래스가 자신만의 필드를 선언하고 채워 넣을 수 있음
- 파일명 == 클래스명 == 에셋명 규칙으로 테이블 간 매핑을 리플렉션만으로 해결 (별도 등록 테이블/설정 파일 불필요)
- 기존 관례(CRTP 싱글턴, `Core/` 하위 공용 베이스, Editor 폴더 분리) 재사용, 새 메커니즘 발명 최소화

---

## 핵심 설계 결정

### 파일명 = 클래스명 = 에셋명 규칙으로 등록 테이블 제거

```
Assets/Tables/Source/PowerTable.csv  →  class PowerTable (C#)  →  Assets/Resources/Tables/PowerTable.asset
```

- 에디터 변환기는 `.csv` 파일명과 정확히 일치하는 이름의 타입을 전체 어셈블리에서 검색 (`AppDomain.CurrentDomain.GetAssemblies()` → `GetTypes()` → 이름/인터페이스 매칭)
- 런타임 `Instance`는 `Resources.Load<TSelf>($"Tables/{typeof(TSelf).Name}")`로 동일 규칙을 재사용
- 규칙 위반(파일은 있는데 클래스가 없음) 시 변환기가 `Debug.LogError`로 안내하고 해당 파일만 스킵 — 전체 변환은 중단하지 않음

### CRTP 기반 제네릭 베이스: `Singleton<T>`와 동일한 사고방식

```csharp
public abstract class TableAssetBase : ScriptableObject, ITableAsset
{
    // 실제 구현은 TableBase<TSelf,TData,TKey>가 담당 (TData/TKey 제네릭 정보가 필요)
    public abstract void PopulateFromText(string[] headers, List<string[]> rows);
}

public abstract class TableBase<TSelf, TData, TKey> : TableAssetBase
    where TSelf : TableBase<TSelf, TData, TKey>
    where TData : TableDataBase<TKey>
```

- `Singleton<T>`가 `T`를 자기 자신으로 받아 콘크리트 타입별 독립된 `Instance`를 갖듯, `TableBase`도 `TSelf`로 동일 효과를 얻음
- Unity는 오픈 제네릭 `ScriptableObject`를 직접 `CreateAsset`/직렬화할 수 없으므로, 테이블마다 닫힌 제네릭으로 상속하는 콘크리트 클래스가 **반드시** 필요 (선택이 아니라 Unity 제약)
- `ScriptableObject`와 `TableBase<TSelf,TData,TKey>` 사이에 비-제네릭 중간 계층 `TableAssetBase`를 끼워 넣는 이유 두 가지:
  1. `PopulateFromText`를 여기서 `public abstract`으로 선언해 `ITableAsset`을 암묵적으로 구현 — C#은 명시적 인터페이스 구현(`void ITableAsset.X`)을 상속 체인 중간의 서브클래스에서 쓸 수 없으므로, 인터페이스를 직접 선언한 타입이 abstract 멤버로 계약을 노출해야 `TableBase<TSelf,TData,TKey>`가 `public override`로 채울 수 있음
  2. Unity `[CustomEditor]`는 오픈 제네릭 타입(`TableBase<,,>`)을 대상으로 지정할 수 없음 — 비-제네릭인 `TableAssetBase`가 뒤에서 다룰 "개별 재로드 버튼" 기능의 바인딩 대상이 됨

### 행 데이터: `TableDataBase<TKey>` 상속 + `Key` 추상 프로퍼티

```csharp
[Serializable]
public abstract class TableDataBase<TKey>
{
    public abstract TKey Key { get; }
}
```

- 구체 데이터 클래스는 실제 컬럼을 **`public` 필드**로 선언 — Unity가 직렬화하는 대상이 필드뿐이라 유지되는 제약
- `Key`는 그 필드 중 하나를 반환하도록 오버라이드 — "검색을 위한 key를 지정할 수 있도록"이라는 요청사항을 상속으로 강제
- `Key`는 프로퍼티(계산값)이므로 Unity가 직렬화하지 않음 — 중복 데이터 없음

### 조회 API: `protected` 빌딩 블록 + 테이블별 공개 메서드

테이블마다 실제로 필요한 조회 형태가 다르므로(단순 key 조회, 등급별 그룹, 이름별 검색 등), 베이스는 "원본 데이터에 안전하게 접근하는 수단"만 `protected`로 제공하고, 그 위에 어떤 공개 API를 얹을지는 각 테이블 클래스가 결정한다.

```csharp
protected IReadOnlyList<TData> Rows => _rows;
protected TData this[TKey key] => Map[key];
protected bool TryGet(TKey key, out TData data) => Map.TryGetValue(key, out data);
```

- `public`이 아니라 `protected` — 외부 코드가 `Rows`나 인덱서를 직접 들고 각자 다른 방식으로 쿼리하기 시작하면 조회 로직이 프로젝트 전역에 흩어짐. 반드시 테이블 클래스 자신의 메서드를 거치도록 강제
- `Map`은 `Dictionary<TKey, TData>`를 지연 생성(lazy) 후 캐시 — 최초 접근 시 1회만 `_rows`를 순회해 O(1) 조회로 전환
- 단순 key 조회만 필요한 테이블은 한 줄만 추가하면 됨: `public TData Get(TKey key) => this[key];`

### 존재하지 않는 key 처리 컨벤션 (`CardTable`에서 확립)

`this[key]` 인덱서는 내부 `Dictionary`의 인덱싱을 그대로 노출하므로, 없는 key를 넘기면 `KeyNotFoundException`이 던져진다. 이는 시스템 전반의 원칙("데이터 부재는 기획 이슈지 크래시 사유가 아님" — 엣지 케이스 표 참고)과 어긋난다. 따라서 테이블 클래스가 **외부에 공개하는 조회 메서드는 인덱서가 아니라 `TryGet`을 거쳐야 한다**.

`CardTable`(`Assets/Scripts/Data/Table/CardTable.cs`)에서 이 패턴을 실사용 예시로 확립:

```csharp
public CardTableData Get(int key)
{
    if (TryGet(key, out var data))
    {
        return data;
    }
    Debug.LogError($"[Table] {nameof(CardTable)} key 없음: {key}");
    return null;
}
```

- 컬럼별 getter(`GetCardName`, `GetAtt`, `GetHp`, `GetCond`, `GetTarget`, `GetExplain`)도 `Get(key)?.필드`로 위임하지 않고 각자 `TryGet`을 직접 호출 — 각 getter가 자신의 default 값과 로그 메시지를 독립적으로 갖도록 명시적으로 분리
- 없는 key일 때 반환값은 필드 타입별 default: `string` → `null`, `int` → `0`, `enum` → 의미상 "없음"에 가까운 값(`CardCondition.None`, `CardTarget.Same`)
- 예외를 던지는 대신 `LogError` + default를 반환 — 크래시 없이 진행되면서도 원인은 콘솔에 남김 (파싱 실패/중복 key 등 다른 케이스와 동일한 원칙 — 엣지 케이스 표 참고)
- `if (TryGet(...))`처럼 한 줄로 끝나는 조건문도 항상 `{}`로 감쌈 (프로젝트 스타일)

### 가공 데이터 훅: `OnLoaded()`

원본 행 그대로가 아니라 가공된 형태(등급별 그룹 `Dictionary`, 이름별 인덱스 등)로 조회하고 싶은 테이블을 위한 훅. `Dictionary` 등은 Unity가 직렬화하지 못하므로 에셋에 미리 구워둘 수 없다 — 대신 **런타임에 `Instance`를 처음 로드하는 시점**에 한 번 계산한다.

```csharp
protected virtual void OnLoaded() { }

public static TSelf Instance
{
    get
    {
        if (_instance == null)
        {
            _instance = Resources.Load<TSelf>($"Tables/{typeof(TSelf).Name}");
            if (_instance == null)
                Debug.LogError($"[Table] {typeof(TSelf).Name} 로드 실패: ...");
            else
                _instance.OnLoaded();
        }
        return _instance;
    }
}
```

- `OnLoaded()`는 `Resources.Load`가 성공한 직후 딱 한 번 호출됨 — 테이블 클래스는 이 안에서 자신의 private 필드(예: `Dictionary<string, SampleTableData> _byName`)를 `Rows`를 순회해 채움
- 에디터의 `.csv → .asset` 변환(`PopulateFromText`) 시점에는 호출되지 않음 — 어차피 `Dictionary`는 직렬화되지 않으므로 계산해봐야 저장되지 않음
- 기본 구현은 빈 메서드 — 가공 데이터가 필요 없는 테이블은 오버라이드하지 않으면 그만
- `ParseRow`(아래)와의 역할 구분: `ParseRow`는 파싱 시점에 CSV 컬럼을 `TData` 필드로 채우고 그 결과가 `.asset`에 그대로 구워진다. `OnLoaded()`는 `Dictionary` 등 애초에 직렬화가 불가능한 자료구조에만 쓴다 — 컬럼을 합쳐 직렬화 가능한 필드 하나로 만드는 것(`StageTable`의 `friends` 배열, `CardTable`의 `EffectClauses`, `ActionPriorityTable`의 `priority`)은 `ParseRow`의 몫이다.

### 파싱: 테이블별 `ParseRow` 훅 + `TableRow` 컬럼 접근자

행 하나를 `TData`로 변환하는 책임은 테이블 클래스가 구현하는 `ParseRow` 훅에 있다. `PopulateFromText`는 행 순회, 중복 key 검증, `_rows`/`_map` 갱신 등 공용 로직만 담당하고, 각 행을 `TData`로 바꾸는 부분만 `ParseRow`에 위임한다.

```csharp
protected abstract TData ParseRow(TableRow row);

public override void PopulateFromText(string[] headers, List<string[]> rows)
{
    var newRows = new List<TData>(rows.Count);
    var seenKeys = new HashSet<TKey>();

    foreach (var cols in rows)
    {
        var data = ParseRow(new TableRow(headers, cols));

        if (!seenKeys.Add(data.Key))
            Debug.LogError($"[Table] {typeof(TSelf).Name} 중복 key 발견: {data.Key}");

        newRows.Add(data);
    }

    _rows = newRows;
    _map = null; // 다음 접근 시 재생성
}
```

- 행 순회, 중복 key 검증, `_rows`/`_map` 갱신은 베이스 책임 — 테이블마다 반복 작성하지 않음
- `ParseRow`는 `protected abstract` — 모든 테이블 클래스가 반드시 구현. 컬럼 재구성이 필요 없는 단순 테이블도 `row.Get<T>("컬럼명")`을 필드 수만큼 나열하는 한 줄짜리 구현이면 충분
- 제네릭 제약은 `TData : TableDataBase<TKey>`(`new()` 없음) — 베이스가 `TData`를 직접 생성하지 않으므로, 생성 방식(object initializer/생성자 등)은 각 `ParseRow` 구현이 자유롭게 정함
- 트레이드오프: 파싱할 컬럼명이 테이블 클래스 코드에 하드코딩됨 — CSV 헤더명이 바뀌면 `ParseRow`도 함께 고칠 것. 어긋나면 컴파일 에러가 아니라 런타임 `LogError`(컬럼 없음)로만 드러남

### `TableRow`: 컬럼명 기반 값 접근자

`ParseRow`가 헤더/컬럼 배열의 인덱스를 직접 다루지 않도록 감싸는 얇은 헬퍼 — 헤더 이름으로 컬럼 값을 찾고 타입 변환까지 처리한다.

```csharp
public readonly struct TableRow
{
    private readonly string[] _headers;
    private readonly string[] _cols;

    public TableRow(string[] headers, string[] cols)
    {
        _headers = headers;
        _cols = cols;
    }

    public T Get<T>(string column)
    {
        var index = Array.FindIndex(_headers, h => h.Equals(column, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index >= _cols.Length)
        {
            Debug.LogError($"[Table] 컬럼 없음: '{column}'");
            return default;
        }

        if (!TableValueParser.TryParse(typeof(T), _cols[index], out var value))
        {
            Debug.LogError($"[Table] 컬럼 '{column}' 파싱 실패: '{_cols[index]}'");
            return default;
        }

        return (T)value;
    }
}
```

- 내부적으로 `TableValueParser.TryParse(Type, string, out object)`를 사용해 타입 변환
- 컬럼 이름 매칭은 `StringComparison.OrdinalIgnoreCase` (대소문자 구분 없음)
- 컬럼이 헤더에 없거나 타입 변환에 실패해도 예외를 던지지 않고 `default(T)` 반환 + `LogError` — 파싱 실패가 임포트 전체를 막지 않음
- `ParseRow`가 호출하지 않는 컬럼은 자동으로 무시됨 — 컬럼 일부만 옮겨도 되도록 허용

### `TableValueParser`: 지원 타입 최소 집합

```csharp
internal static class TableValueParser
{
    public static bool TryParse(Type type, string raw, out object value)
    {
        raw = raw.Trim();
        try
        {
            if (type == typeof(string)) { value = raw; return true; }
            if (type.IsEnum) { value = Enum.Parse(type, raw, true); return true; }
            if (type == typeof(bool)) { value = raw is "1" or "true" or "True" or "TRUE"; return true; }
            value = Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }
}
```

- `string`, `int`, `float`, `double`, `bool`, `enum` 지원
- 여러 **컬럼**을 필드 하나로 합치는 것(`StageTable`의 `friends` 배열 등)은 `ParseRow`에서 `TableRow.Get<T>`를 여러 번 호출해 조합 — 이 파서를 확장할 필요 없음
- 한 **셀** 안에 구분자로 여러 값이 들어있는 경우(예: `ActionPriorityTable`의 `priority` 컬럼)는 이 파서의 범위 밖 — 해당 테이블의 `ParseRow` 옆에 전용 `private static` 헬퍼로 처리 (`ActionPriorityTable.ParsePriority` 참고)

### 개별 asset Inspector에서 재로드 버튼 — `CustomEditor`

`Tools/Table/Generate All Tables`는 모든 테이블을 한 번에 갱신하지만, 특정 테이블 하나만 원본 CSV에서 다시 불러오고 싶을 때(예: 그 테이블만 방금 수정함)도 매번 전체 변환을 돌려야 하는 건 불편하다. 생성된 `.asset`을 Project 창에서 선택했을 때 Inspector에 그 테이블 전용 재로드 버튼이 뜨도록 한다 — 위에서 만든 비-제네릭 `TableAssetBase`를 그대로 `[CustomEditor]` 대상으로 쓴다.

- `[CustomEditor(typeof(TableAssetBase), true)]`로 모든 테이블 asset(`SampleTable` 등 닫힌 제네릭 서브클래스 포함)에 자동 적용됨 (`editorForChildClasses: true`)
- `[CustomEditor(typeof(ScriptableObject), true)]`처럼 더 넓게 걸지 않는 이유: 프로젝트의 다른 `ScriptableObject`(테이블이 아닌 것)에까지 이 인스펙터가 적용되는 걸 막기 위함

```csharp
[CustomEditor(typeof(TableAssetBase), true)]
internal class TableAssetEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Reload"))
            TableGenerator.ReloadTable((TableAssetBase)target);

        EditorGUILayout.Space();
        DrawDefaultInspector();
    }
}
```

- 다중 선택은 지원하지 않음(의도적 범위 제한): Project 창에서 테이블 asset을 2개 이상 동시 선택하면 Unity가 Inspector 대신 "Multi-object editing not supported."를 표시함 — 재로드는 asset 하나씩 선택해서 사용하는 걸 기본 워크플로로 삼음(YAGNI)

`TableGenerator`는 "전체 변환"과 "개별 재로드"가 CSV 읽기 로직(`.csv` → 헤더/행 분리)을 공유하도록 `TryReadLines` 헬퍼로 추출한다:

```csharp
public static bool ReloadTable(TableAssetBase asset)
{
    var tableName = asset.GetType().Name;
    var path = $"{SourceDir}/{tableName}.csv";

    if (!File.Exists(path))
    {
        Debug.LogError($"[TableGenerator] '{tableName}' 원본 CSV를 찾을 수 없음: {path}");
        return false;
    }

    if (!TryReadLines(path, out var headers, out var rows))
        return false;

    asset.PopulateFromText(headers, rows);
    EditorUtility.SetDirty(asset);
    AssetDatabase.SaveAssets();
    Debug.Log($"[TableGenerator] '{tableName}' 다시 로드 완료 ({rows.Count}행)");
    return true;
}
```

- 대상 asset이 이미 알고 있는 자신의 타입명(`asset.GetType().Name`)으로 원본 `.csv` 경로를 바로 찾음 — `FindTableType`(이름→타입 리플렉션 검색)은 asset이 아직 없는 "전체 생성" 경로에서만 필요
- `GenerateAllTables`의 `TryGenerateTable`도 동일한 `TryReadLines`를 호출 — CSV 파싱 로직이 두 곳에 중복되지 않음

---

## 클래스 구조

```
TableDataBase<TKey>                              (abstract, Core 공용)
└── Key : TKey (abstract property)

ITableAsset                                       (Core 공용, 에디터↔런타임 경계)
└── PopulateFromText(string[] headers, List<string[]> rows)

TableAssetBase : ScriptableObject, ITableAsset    (abstract, Core 공용, 비-제네릭 중간 계층)
└── PopulateFromText(...)                         ← public abstract 선언만 (구현은 TableBase가 override) — ITableAsset 암묵적 구현 + CustomEditor 바인딩 대상 역할

TableBase<TSelf, TData, TKey> : TableAssetBase    (abstract, Core 공용)
├── Instance : TSelf (static, Resources.Load 캐싱, 로드 직후 OnLoaded() 호출)
├── Rows : IReadOnlyList<TData>                    ← protected, 서브클래스 전용
├── this[TKey key] : TData                         ← protected, 서브클래스 전용
├── TryGet(TKey key, out TData) : bool              ← protected, 서브클래스 전용
├── OnLoaded()                                      ← protected virtual, 가공 데이터 계산 훅 (기본 구현 없음)
├── ParseRow(TableRow row) : TData                  ← protected abstract, 컬럼→필드 변환은 테이블별 구현
└── PopulateFromText(...)                         ← TableAssetBase의 abstract 멤버를 override, 행마다 ParseRow 호출 + 중복 key 검증, 에디터 전용 호출

TableRow                                          (readonly struct, Core 공용)
└── Get<T>(string column) : T                      ← 헤더 이름으로 컬럼 값을 찾아 TableValueParser로 변환

TableValueParser                                  (internal, Core 공용)
└── TryParse(Type, string, out object) : bool

PowerTable : TableBase<PowerTable, PowerTableData, int>   ← 테이블별 변환 클래스 (수동)
├── ParseRow(TableRow row) : PowerTableData         ← 필수, 컬럼 값을 row.Get<T>(...)로 읽어 PowerTableData 구성
├── Get(int key) : PowerTableData                  ← 예: 단순 key 조회 (protected this[key] 감싸기)
├── GetByGrade(int grade) : IReadOnlyList<...>      ← 예: OnLoaded()에서 채운 보조 인덱스로 조회
└── OnLoaded() override                            ← 필요할 때만, 직렬화 불가능한 보조 인덱스 계산
PowerTableData : TableDataBase<int>                        ← 테이블별 행 데이터 (수동, 필드 선언)

TableGenerator                                    (Editor 전용, static)
├── GenerateAllTables()                           ← [MenuItem], 전체 .csv → 신규/기존 asset 일괄 갱신
├── ReloadTable(TableAssetBase asset)              ← 개별 asset 하나만 원본 CSV에서 재로드
└── TryReadLines(...)                              ← 위 두 경로가 공유하는 CSV 파싱 헬퍼

TableAssetEditor : Editor                         (Editor 전용)
└── [CustomEditor(typeof(TableAssetBase), true)] — 상단 "Reload" 버튼 + 기본 Inspector
```

---

## 파일 구성

```
Assets/
├── Tables/
│   └── Source/
│       ├── SampleTable.csv              ← 파이프라인 검증용 예시 (id|name|value)
│       ├── CardTable.csv                ← key|animal|cardname|sheets|att|hp|cond|target|effect|scope|explain
│       ├── StageTable.csv               ← key|icon|hp|friend1|friend2|friend3
│       └── ActionPriorityTable.csv      ← ai|player|priority
├── Resources/
│   └── Tables/                          ← TableGenerator 최초 실행 시 자동 생성
├── Scripts/
│   ├── Core/
│   │   └── Table/
│   │       ├── TableDataBase.cs
│   │       ├── ITableAsset.cs
│   │       ├── TableAssetBase.cs        ← CustomEditor 바인딩용 비-제네릭 마커
│   │       ├── TableBase.cs
│   │       ├── TableRow.cs              ← ParseRow가 컬럼 값을 이름으로 조회하는 헬퍼
│   │       └── TableValueParser.cs
│   ├── Data/
│   │   └── Table/                       ← 테이블 추가 시 이 폴더에 파일 추가
│   │       ├── SampleTable.cs           ← SampleTableData + SampleTable 한 파일에 (GameEvents.cs 관례처럼 관련 타입 묶음)
│   │       ├── CardTable.cs             ← CardTableData + CardTable, "없는 key" 컨벤션(TryGet 기반) 확립
│   │       ├── CardEffectParser.cs      ← CardTable의 effect 컬럼 전용 DSL 파서 (CardEffectClause 등)
│   │       ├── StageTable.cs
│   │       └── ActionPriorityTable.cs
│   └── Editor/
│       └── Table/
│           ├── TableGenerator.cs
│           └── TableAssetEditor.cs      ← 개별 asset Inspector 재로드 버튼
```

`Core/Table/`은 특정 하위 시스템에 속하지 않는 공용 베이스이므로 `Core/` 하위 배치 (`Singleton.cs`, `SceneSingleton.cs`와 동일 원칙). 테이블별 변환 클래스는 `Core` 밖 `Data/Table/`에 배치해 프레임워크와 실제 데이터 정의를 분리.

---

## 상세 구현 명세

### TableDataBase.cs

```csharp
using System;

namespace JungleDice.Core.Table
{
    [Serializable]
    public abstract class TableDataBase<TKey>
    {
        public abstract TKey Key { get; }
    }
}
```

### ITableAsset.cs

```csharp
using System.Collections.Generic;

namespace JungleDice.Core.Table
{
    public interface ITableAsset
    {
        void PopulateFromText(string[] headers, List<string[]> rows);
    }
}
```

### TableAssetBase.cs

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace JungleDice.Core.Table
{
    public abstract class TableAssetBase : ScriptableObject, ITableAsset
    {
        // 실제 구현은 TableBase<TSelf,TData,TKey>가 담당 (TData/TKey 제네릭 정보가 필요)
        public abstract void PopulateFromText(string[] headers, List<string[]> rows);
    }
}
```

### TableBase.cs

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace JungleDice.Core.Table
{
    public abstract class TableBase<TSelf, TData, TKey> : TableAssetBase
        where TSelf : TableBase<TSelf, TData, TKey>
        where TData : TableDataBase<TKey>
    {
        [SerializeField] private List<TData> _rows = new();

        private Dictionary<TKey, TData> _map;

        protected IReadOnlyList<TData> Rows => _rows;

        protected TData this[TKey key] => Map[key];

        protected bool TryGet(TKey key, out TData data) => Map.TryGetValue(key, out data);

        protected virtual void OnLoaded() { }

        protected abstract TData ParseRow(TableRow row);

        private Dictionary<TKey, TData> Map => _map ??= BuildMap();

        private Dictionary<TKey, TData> BuildMap()
        {
            var map = new Dictionary<TKey, TData>(_rows.Count);
            foreach (var row in _rows)
                map[row.Key] = row; // 중복 key는 마지막 값으로 덮어씀 (임포트 시점에 이미 LogError로 안내됨)
            return map;
        }

        public override void PopulateFromText(string[] headers, List<string[]> rows)
        {
            var newRows = new List<TData>(rows.Count);
            var seenKeys = new HashSet<TKey>();

            foreach (var cols in rows)
            {
                var data = ParseRow(new TableRow(headers, cols));

                if (!seenKeys.Add(data.Key))
                    Debug.LogError($"[Table] {typeof(TSelf).Name} 중복 key 발견: {data.Key}");

                newRows.Add(data);
            }

            _rows = newRows;
            _map = null;
        }

        private static TSelf _instance;

        public static TSelf Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<TSelf>($"Tables/{typeof(TSelf).Name}");
                    if (_instance == null)
                        Debug.LogError($"[Table] {typeof(TSelf).Name} 로드 실패: Assets/Resources/Tables/{typeof(TSelf).Name}.asset 없음");
                    else
                        _instance.OnLoaded();
                }
                return _instance;
            }
        }
    }
}
```

### TableRow.cs

```csharp
using System;
using UnityEngine;

namespace JungleDice.Core.Table
{
    public readonly struct TableRow
    {
        private readonly string[] _headers;
        private readonly string[] _cols;

        public TableRow(string[] headers, string[] cols)
        {
            _headers = headers;
            _cols = cols;
        }

        public T Get<T>(string column)
        {
            var index = Array.FindIndex(_headers, h => h.Equals(column, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index >= _cols.Length)
            {
                Debug.LogError($"[Table] 컬럼 없음: '{column}'");
                return default;
            }

            if (!TableValueParser.TryParse(typeof(T), _cols[index], out var value))
            {
                Debug.LogError($"[Table] 컬럼 '{column}' 파싱 실패: '{_cols[index]}'");
                return default;
            }

            return (T)value;
        }
    }
}
```

### TableValueParser.cs

```csharp
using System;
using System.Globalization;

namespace JungleDice.Core.Table
{
    internal static class TableValueParser
    {
        public static bool TryParse(Type type, string raw, out object value)
        {
            raw = raw.Trim();
            try
            {
                if (type == typeof(string)) { value = raw; return true; }
                if (type.IsEnum) { value = Enum.Parse(type, raw, true); return true; }
                if (type == typeof(bool)) { value = raw is "1" or "true" or "True" or "TRUE"; return true; }

                value = Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }
    }
}
```

### Data/Table/SampleTable.cs (예시 — 파이프라인 검증용)

```csharp
using System;
using System.Collections.Generic;
using JungleDice.Core.Table;

namespace JungleDice.Data.Table
{
    [Serializable]
    public class SampleTableData : TableDataBase<int>
    {
        public int id;
        public string name;
        public int value;

        public override int Key => id;
    }

    public class SampleTable : TableBase<SampleTable, SampleTableData, int>
    {
        protected override SampleTableData ParseRow(TableRow row) => new()
        {
            id = row.Get<int>("id"),
            name = row.Get<string>("name"),
            value = row.Get<int>("value"),
        };

        // 단순 key 조회 — protected 인덱서를 그대로 감싸기만 함
        public SampleTableData Get(int id) => this[id];

        // 가공 데이터 예시 — 이름으로 조회하는 보조 인덱스
        private Dictionary<string, SampleTableData> _byName;

        protected override void OnLoaded()
        {
            _byName = new Dictionary<string, SampleTableData>();
            foreach (var row in Rows)
                _byName[row.name] = row;
        }

        public SampleTableData GetByName(string name) =>
            _byName.TryGetValue(name, out var data) ? data : null;
    }
}
```

### Tables/Source/SampleTable.csv (예시)

```
id|name|value
1|First|100
2|Second|200
3|Third|300
```

### Editor/Table/TableGenerator.cs

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JungleDice.Core.Table;
using UnityEditor;
using UnityEngine;

namespace JungleDice.Core.Table.Editor
{
    internal static class TableGenerator
    {
        private const string SourceDir = "Assets/Tables/Source";
        private const string OutputDir = "Assets/Resources/Tables";

        [MenuItem("Tools/Table/Generate All Tables")]
        public static void GenerateAllTables()
        {
            Directory.CreateDirectory(SourceDir);
            Directory.CreateDirectory(OutputDir);

            var csvFiles = Directory.GetFiles(SourceDir, "*.csv", SearchOption.TopDirectoryOnly);
            int success = 0, failed = 0;

            foreach (var path in csvFiles)
            {
                var tableName = Path.GetFileNameWithoutExtension(path);
                if (TryGenerateTable(tableName, path))
                    success++;
                else
                    failed++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TableGenerator] 완료: 성공 {success}, 실패 {failed}");
        }

        /// 이미 존재하는 테이블 asset 하나만 자신의 원본 CSV에서 다시 읽어들인다.
        /// TableAssetEditor의 "Reload" 버튼에서 호출.
        public static bool ReloadTable(TableAssetBase asset)
        {
            var tableName = asset.GetType().Name;
            var path = $"{SourceDir}/{tableName}.csv";

            if (!File.Exists(path))
            {
                Debug.LogError($"[TableGenerator] '{tableName}' 원본 CSV를 찾을 수 없음: {path}");
                return false;
            }

            if (!TryReadLines(path, out var headers, out var rows))
                return false;

            asset.PopulateFromText(headers, rows);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TableGenerator] '{tableName}' 다시 로드 완료 ({rows.Count}행)");
            return true;
        }

        private static bool TryGenerateTable(string tableName, string path)
        {
            var type = FindTableType(tableName);
            if (type == null)
            {
                Debug.LogError($"[TableGenerator] '{tableName}'에 대응하는 변환 클래스를 찾을 수 없음 (ITableAsset 구현 + ScriptableObject 상속 + 클래스명 == 파일명 필요)");
                return false;
            }

            if (!TryReadLines(path, out var headers, out var rows))
                return false;

            var assetPath = $"{OutputDir}/{tableName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance(type);
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            ((ITableAsset)asset).PopulateFromText(headers, rows);
            EditorUtility.SetDirty(asset);
            return true;
        }

        private static bool TryReadLines(string path, out string[] headers, out List<string[]> rows)
        {
            headers = null;
            rows = null;

            var lines = File.ReadAllLines(path, System.Text.Encoding.UTF8)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            if (lines.Length == 0)
            {
                Debug.LogError($"[TableGenerator] '{Path.GetFileNameWithoutExtension(path)}' 파일이 비어있음");
                return false;
            }

            headers = lines[0].Split('|').Select(h => h.Trim()).ToArray();
            rows = lines.Skip(1)
                .Select(l => l.Split('|').Select(c => c.Trim()).ToArray())
                .ToList();
            return true;
        }

        private static Type FindTableType(string tableName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => SafeGetTypes(a))
                .FirstOrDefault(t =>
                    t.Name == tableName &&
                    typeof(ScriptableObject).IsAssignableFrom(t) &&
                    typeof(ITableAsset).IsAssignableFrom(t));
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }
    }
}
```

### Editor/Table/TableAssetEditor.cs

```csharp
using JungleDice.Core.Table;
using UnityEditor;
using UnityEngine;

namespace JungleDice.Core.Table.Editor
{
    [CustomEditor(typeof(TableAssetBase), true)]
    internal class TableAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Reload"))
                TableGenerator.ReloadTable((TableAssetBase)target);

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
```

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `.csv` 파일은 있는데 대응하는 C# 클래스가 없음 | `Debug.LogError`로 안내, 해당 파일만 스킵, 나머지 테이블은 계속 처리 |
| `ParseRow`가 호출하지 않는 컬럼(헤더에는 있지만 안 쓰기로 한 컬럼) | 자동으로 무시됨 — 애초에 `TableRow.Get`을 호출하지 않으므로 무시할 것도 없음 (컬럼 일부만 옮겨도 되도록 허용하는 취지 그대로) |
| `ParseRow`가 요청한 컬럼명이 헤더에 없음 (오타, CSV 컬럼 삭제 등) | `TableRow.Get<T>`가 `Debug.LogError` 후 `default(T)` 반환, 해당 필드만 기본값 유지, 나머지 필드/행은 계속 처리 |
| 셀 값 파싱 실패 (예: `int` 컬럼에 "abc") | `TableRow.Get<T>`가 `Debug.LogError` 후 `default(T)` 반환, 나머지 필드/행은 계속 처리 |
| 중복 key | `Debug.LogError`로 안내, `Dictionary` 구성 시 마지막 값으로 덮어씀 (크래시 없음) |
| `Resources/Tables/xxx.asset`이 없는데 런타임에서 `xxxTable.Instance` 접근 | `Instance`가 `null` 반환 + `Debug.LogError`. 호출부에서 `null` 체크 필요 (예외로 죽이지 않음 — 데이터 부재는 기획 이슈지 크래시 사유 아님) |
| 변환기를 재실행해 기존 `.asset`을 다시 생성 | 기존 에셋을 `LoadAssetAtPath`로 재사용 후 `PopulateFromText`로 덮어씀 — 다른 곳에서 이 에셋을 참조 중이어도 참조가 깨지지 않음 |
| 테이블 파일명이 규칙과 다름 (`xxxTable.csv`가 아님) | 요청사항에 명시된 명명 규칙(`xxxTable`)을 강제하지 않음 — 파일명과 클래스명만 일치하면 동작. 규칙 이탈은 코드 리뷰로 방지 |
| Inspector "Reload" 버튼을 눌렀는데 대응 `.csv`가 없음 (asset만 있고 원본 CSV가 삭제/이동됨) | `Debug.LogError`로 경로 안내, asset은 기존 값 그대로 유지 (덮어쓰지 않음) |
| 다른 `ScriptableObject`(테이블이 아닌 것)를 선택했을 때 이 버튼이 보이는지 | 보이지 않음 — `CustomEditor`가 `TableAssetBase`(및 그 서브클래스)에만 바인딩되므로 무관한 asset에는 영향 없음 |
| Project 창에서 테이블 asset을 2개 이상 동시 선택 | 의도적으로 미지원. Unity가 "Multi-object editing not supported."를 표시 — 재로드는 asset을 하나씩 선택해서 사용 |
| 외부 코드가 `table.Rows`나 `table[key]`를 직접 호출 시도 | 컴파일 에러 (`protected` 멤버) — 설계대로 동작. 테이블 클래스가 노출한 public 메서드(`Get`, `GetByGrade` 등)를 거쳐야 함 |
| 테이블 클래스의 public 조회 메서드(`Get`, `GetCardName` 등)에 존재하지 않는 key 전달 | `this[key]` 인덱서를 직접 쓰지 않고 `TryGet` 기반으로 구현: `Debug.LogError` 후 필드 타입별 default 반환 (예외로 죽이지 않음) — `CardTable` 참고 |
| `OnLoaded()` 오버라이드 안에서 예외 발생 | `Instance` 프로퍼티 호출부까지 그대로 전파됨 (별도 방어 없음) — 테이블 클래스 구현 버그이므로 숨기지 않고 명확히 실패시키는 쪽을 택함 |
| `OnLoaded()`가 아직 호출되기 전에 가공 데이터 필드(`_byGrade` 등)에 접근 | 발생하지 않음 — `Instance` getter가 `Resources.Load` 성공 직후 동기적으로 `OnLoaded()`를 호출한 다음 인스턴스를 반환하므로, `Instance`를 통해 얻은 참조는 항상 가공 데이터까지 채워진 상태 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `SampleTable.csv` + `SampleTable`/`SampleTableData` 존재 상태에서 `Tools/Table/Generate All Tables` 실행 | `Assets/Resources/Tables/SampleTable.asset` 생성, 3행 채워짐 |
| 2 | 같은 메뉴 재실행 | 기존 asset GUID 유지, 값만 갱신 (참조 안 깨짐) |
| 3 | 런타임에서 `SampleTable.Instance.Get(2).name` 호출 | `"Second"` 반환 |
| 4 | 런타임에서 `SampleTable.Instance.GetByName("Second")` 호출 | `id == 2`인 `SampleTableData` 반환 (OnLoaded()에서 채운 보조 인덱스 사용) |
| 5 | `.csv`에 대응 클래스가 없는 새 파일 추가 후 변환 실행 | 해당 파일만 `LogError`로 스킵, 나머지 테이블 정상 처리 |
| 6 | 데이터 행에 중복 key 존재 | `LogError` 출력, 마지막 행 값으로 유지, 임포트는 계속 진행 |
| 7 | `int` 필드에 숫자가 아닌 문자열 셀 | `LogError` 출력, 해당 필드만 기본값 유지, 나머지 필드는 정상 파싱 |
| 8 | `SampleTable.asset` 선택 → Inspector 상단 "Reload" 버튼 클릭 | `SampleTable.csv`만 다시 읽어 값 갱신, 다른 테이블 asset은 건드리지 않음 |
| 9 | 원본 `.csv`를 삭제한 상태에서 같은 버튼 클릭 | `LogError`로 경로 안내, asset 값은 그대로 유지 (크래시 없음) |
| 10 | 런타임에서 `SampleTable.Instance.GetByName("없는이름")` 호출 | `null` 반환, 예외 없음 |
| 11 | 런타임에서 `CardTable.Instance.GetAtt(없는key)` 호출 | `LogError` 출력, `0` 반환, 예외 없음 (`Get`/다른 컬럼 getter도 동일 패턴) |
| 12 | `ParseRow` 안에서 `row.Get<int>("없는컬럼")`처럼 헤더에 없는 컬럼명을 요청 | `LogError` 출력, `0`(해당 타입의 `default`) 반환, 나머지 필드 파싱 및 임포트는 계속 진행 |

---

## 구현 시 주의사항

- **데이터 클래스는 `public` 필드로 선언**: Unity가 직렬화하는 대상이 필드뿐이라 유지되는 제약. `Key`만 예외적으로 프로퍼티(계산값).
- **`Rows`/인덱서/`TryGet`은 `protected`, `public`으로 되돌리지 않는다**: 외부 코드가 이 셋을 직접 쓰기 시작하면 조회 로직이 컨슈머 여러 곳에 흩어짐 — 반드시 테이블 클래스 자신의 메서드(`Get`, `GetByGrade` 등)를 추가해서 노출할 것.
- **가공 데이터(보조 인덱스 등)는 필드 이니셜라이저나 생성자가 아니라 `OnLoaded()`에서 채운다**: `OnLoaded()`는 `Instance`가 실제로 로드된 직후에만 정확히 한 번 호출되도록 보장됨.
- **컬럼을 조합/재구성해 직렬화 가능한 필드를 만드는 로직은 `ParseRow`에서, 컬럼 값 자체의 타입 변환 규칙은 `TableValueParser`에서 처리**: 결과 타입(`CardEffectClause` 등)도 Unity가 직렬화할 수 있어야 하므로 `readonly struct`가 아니라 `[Serializable] struct`(가변 필드)로 정의할 것.
- **`ParseRow`는 `protected abstract`이며 모든 테이블 클래스가 구현해야 한다**: 파싱할 컬럼명은 코드에 하드코딩되므로, CSV 헤더명을 바꾸면 대응하는 `ParseRow`도 함께 고칠 것 — 어긋나면 컴파일 에러가 아니라 런타임 `LogError`(컬럼 없음)로만 드러남.
- **`TableRow.Get<T>`가 반환하는 `default(T)`는 참조 타입에서 `null`이 될 수 있음**: `row.Get<string>(...)`을 받아 직접 조합/파싱하는 테이블 전용 헬퍼(`ActionPriorityTable.ParsePriority` 등)는 이 `null`을 방어적으로 처리할 것 — 그러지 않으면 컬럼 하나가 어긋났을 때 `PopulateFromText` 전체가 예외로 죽는다.
- **`Resources/Tables/` 하위 파일명은 절대 수동으로 바꾸지 않는다**: `Instance` 로드가 `typeof(TSelf).Name` 문자열에 의존하므로 파일명이 클래스명과 어긋나면 로드가 조용히 실패(`null` + LogError)함.
- **`PopulateFromText`는 `TableAssetBase`의 `public abstract` 멤버, `TableBase<TSelf,TData,TKey>`가 `override`**: 명시적 인터페이스 구현(`void ITableAsset.X`)은 상속 체인 중간의 서브클래스에서 쓸 수 없어(C# 제약) 이 형태를 택함. 결과적으로 `PopulateFromText`는 런타임 게임 코드에서도 호출 가능한 `public` 메서드가 됨 — 실수로 런타임 코드에서 호출하지 않도록 주의(리뷰로 방지).
- **한 셀 안에 구분자로 여러 값이 들어있는 컬럼은 `TableValueParser` 범위 밖**: 필요하면 해당 테이블의 `ParseRow` 옆에 전용 헬퍼를 둔다 (`ActionPriorityTable.ParsePriority` 참고).
- **`[CustomEditor]`는 반드시 `TableAssetBase`(비-제네릭)를 대상으로**: `TableBase<,,>`처럼 오픈 제네릭 타입은 Unity `CustomEditor` 속성의 대상이 될 수 없음. 새 마커 클래스를 만들지 않고 우회하려 하지 말 것.
- **`TableGenerator.TryReadLines`는 유일한 CSV 파싱 경로**: "전체 생성"과 "개별 재로드"가 각자 파싱 로직을 따로 구현하지 않도록, 새 진입점을 추가할 때도 반드시 이 헬퍼를 재사용.

---

## 구현 후 체크리스트

- [x] `Assets/Tables/Source/`, `Assets/Resources/Tables/` 폴더 생성 (`Resources/Tables/`는 `TableGenerator` 최초 실행 시 자동 생성)
- [x] `Core/Table/`: `TableDataBase.cs`, `ITableAsset.cs`, `TableAssetBase.cs`, `TableBase.cs`, `TableRow.cs`, `TableValueParser.cs` 작성
- [x] `Editor/Table/`: `TableGenerator.cs`(`GenerateAllTables`/`ReloadTable`/`TryReadLines`), `TableAssetEditor.cs`(Inspector "Reload" 버튼) 작성
- [x] `Data/Table/`: `SampleTable.cs`, `CardTable.cs`, `CardEffectParser.cs`, `StageTable.cs`, `ActionPriorityTable.cs` 작성 — 각 `ParseRow`로 컬럼 값을 `TData` 필드에 매핑
- [ ] 컴파일 에러 없는지 확인 — Unity 에디터에서 직접 확인 (동시에 같은 프로젝트를 배치 모드로 열 수 없어 자동 검증 불가)
- [ ] 에디터에서 `Tools > Table > Generate All Tables` 실행 — `CardTable.asset`/`ActionPriorityTable.asset`이 아직 이전 스키마로 남아있어 재생성 필요
- [ ] `SampleTable.asset` 선택 → Inspector 상단 "Reload" 버튼 동작 확인
- [ ] 테스트 시나리오 표 항목 수동 검증 (특히 #3, #4, #10, #12)
