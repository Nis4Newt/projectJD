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

- 텍스트 파싱, 원본 key 조회, 런타임 싱글턴 로드는 전부 공용 베이스가 담당 — 이 부분은 테이블마다 반복 작성하지 않음
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
    where TData : TableDataBase<TKey>, new()
```

- `Singleton<T>`가 `T`를 자기 자신으로 받아 콘크리트 타입별 독립된 `Instance`를 갖듯, `TableBase`도 `TSelf`로 동일 효과를 얻음
- Unity는 오픈 제네릭 `ScriptableObject`를 직접 `CreateAsset`/직렬화할 수 없으므로, 테이블마다 닫힌 제네릭으로 상속하는 콘크리트 클래스가 **반드시** 필요 — 이것이 요청사항의 "변환 클래스는 테이블이 추가될 때 수동 생성해야 함"과 정확히 맞아떨어지는 이유 (선택이 아니라 Unity 제약)
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

- 구체 데이터 클래스는 실제 컬럼을 **`public` 필드**로 선언 (Unity 직렬화 대상 + 리플렉션 매칭 대상은 필드만 가능, 프로퍼티는 제외)
- `Key`는 그 필드 중 하나를 반환하도록 오버라이드 — "검색을 위한 key를 지정할 수 있도록"이라는 요청사항을 상속으로 강제
- `Key`는 프로퍼티(계산값)이므로 Unity가 직렬화하지 않음 — 중복 데이터 없음

### 조회 API: `protected` 빌딩 블록 + 테이블별 공개 메서드

처음에는 "key 인덱서 하나로 통일"(`xxxTable.Instance[key].필드`)해서 모든 테이블이 같은 조회 형태를 쓰게 하려 했으나, 테이블마다 실제로 필요한 조회 형태가 다르다는 점(단순 key 조회, 등급별 그룹, 이름별 검색 등)을 반영해 방향을 바꿨다. 베이스는 "원본 데이터에 안전하게 접근하는 수단"만 `protected`로 제공하고, 그 위에 어떤 공개 API를 얹을지는 각 테이블 클래스가 결정한다.

```csharp
protected IReadOnlyList<TData> Rows => _rows;
protected TData this[TKey key] => Map[key];
protected bool TryGet(TKey key, out TData data) => Map.TryGetValue(key, out data);
```

- `public`이 아니라 `protected` — 외부 코드가 `Rows`나 인덱서를 직접 들고 각자 다른 방식으로 쿼리하기 시작하면 조회 로직이 프로젝트 전역에 흩어짐. 반드시 테이블 클래스 자신의 메서드를 거치도록 강제
- `Map`은 여전히 `Dictionary<TKey, TData>`를 지연 생성(lazy) 후 캐시 — 최초 접근 시 1회만 `_rows`를 순회해 O(1) 조회로 전환 (변경 없음)
- 단순 key 조회만 필요한 테이블은 한 줄만 추가하면 됨: `public TData Get(TKey key) => this[key];` — "빈 클래스" 원칙은 폐기하지만 보일러플레이트는 여전히 최소

### 가공 데이터 훅: `OnLoaded()`

원본 행 그대로가 아니라 가공된 형태(등급별 그룹 `Dictionary`, 이름별 인덱스 등)로 조회하고 싶은 테이블을 위한 훅. `Dictionary` 등은 Unity가 직렬화하지 못하므로 에셋에 미리 구워둘 수 없다 — 대신 **런타임에 `Instance`를 처음 로드하는 시점**에 한 번 계산한다 (베이스가 이미 쓰고 있는 `Map => _map ??= BuildMap()` 지연 생성과 같은 사고방식을 서브클래스에도 열어주는 것).

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

- `OnLoaded()`는 `Resources.Load`가 성공한 직후 딱 한 번 호출됨 — 테이블 클래스는 이 안에서 자신의 private 필드(예: `Dictionary<int, List<PowerTableData>> _byGrade`)를 `Rows`를 순회해 채움
- 에디터의 `.csv → .asset` 변환(`PopulateFromText`) 시점에는 호출되지 않음 — 그 시점엔 아직 `Resources.Load`를 거치지 않았고, 어차피 `Dictionary`는 직렬화되지 않으므로 계산해봐야 저장되지 않음
- 기본 구현은 빈 메서드 — 가공 데이터가 필요 없는 테이블은 오버라이드하지 않으면 그만

### 파싱: 리플렉션 기반 컬럼→필드 매칭

```csharp
public override void PopulateFromText(string[] headers, List<string[]> rows)
{
    var fields = typeof(TData).GetFields(BindingFlags.Public | BindingFlags.Instance);
    var newRows = new List<TData>(rows.Count);
    var seenKeys = new HashSet<TKey>();

    foreach (var cols in rows)
    {
        var data = new TData();
        for (int i = 0; i < headers.Length && i < cols.Length; i++)
        {
            var field = Array.Find(fields, f => f.Name.Equals(headers[i], StringComparison.OrdinalIgnoreCase));
            if (field == null) continue; // 데이터 클래스에 없는 컬럼은 무시

            if (!TableValueParser.TryParse(field.FieldType, cols[i], out var value))
            {
                Debug.LogError($"[Table] {typeof(TData).Name}.{field.Name} 파싱 실패: '{cols[i]}'");
                continue;
            }
            field.SetValue(data, value);
        }

        if (!seenKeys.Add(data.Key))
            Debug.LogError($"[Table] {typeof(TSelf).Name} 중복 key 발견: {data.Key}");

        newRows.Add(data);
    }

    _rows = newRows;
    _map = null; // 다음 접근 시 재생성
}
```

- 헤더에는 있지만 데이터 클래스에 필드가 없으면 조용히 무시 (컬럼 일부만 옮겨도 되도록)
- 파싱 실패/중복 key는 임포트를 막지 않고 `LogError`만 남김 — 기획자가 원본 CSV를 고치는 동안에도 나머지 테이블 작업은 계속 가능

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

- `string`, `int`, `float`, `double`, `bool`, `enum` 지원 — 요청에 명시되지 않은 배열/리스트 컬럼은 범위 밖 (필요해지면 별도 확장)

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

- 다중 선택은 지원하지 않음(의도적 범위 제한): Project 창에서 테이블 asset을 2개 이상 동시 선택하면 Unity가 Inspector 대신 "Multi-object editing not supported."를 표시함 — `[CanEditMultipleObjects]`로 없앨 수 있지만, 애초에 서로 다른 콘크리트 타입(예: `SampleTable`과 다른 테이블)을 함께 선택하면 그 속성이 있어도 Unity가 타입 불일치로 여전히 다중 편집을 거부하므로 실효성이 낮음. 재로드는 asset 하나씩 선택해서 사용하는 걸 기본 워크플로로 삼음(YAGNI)

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
- `GenerateAllTables`의 `TryGenerateTable`도 동일한 `TryReadLines`를 호출하도록 리팩터링 — CSV 파싱 로직이 두 곳에 중복되지 않음

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
└── PopulateFromText(...)                         ← TableAssetBase의 abstract 멤버를 override, 에디터 전용 호출

TableValueParser                                  (internal, Core 공용)
└── TryParse(Type, string, out object) : bool

PowerTable : TableBase<PowerTable, PowerTableData, int>   ← 테이블별 변환 클래스 (수동)
├── Get(int key) : PowerTableData                  ← 예: 단순 key 조회 (protected this[key] 감싸기)
├── GetByGrade(int grade) : IReadOnlyList<...>      ← 예: OnLoaded()에서 채운 보조 인덱스로 조회
└── OnLoaded() override                            ← 필요할 때만, 보조 인덱스 계산
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
│       └── SampleTable.csv              ← 신규, 파이프라인 검증용 예시 (id|name|value, 3행)
├── Resources/
│   └── Tables/                          ← 신규 폴더, 변환기가 최초 실행 시 자동 생성
├── Scripts/
│   ├── Core/
│   │   └── Table/                       ← 신규
│   │       ├── TableDataBase.cs
│   │       ├── ITableAsset.cs
│   │       ├── TableAssetBase.cs        ← 신규 (CustomEditor 바인딩용 비-제네릭 마커)
│   │       ├── TableBase.cs
│   │       └── TableValueParser.cs
│   ├── Data/
│   │   └── Table/                       ← 신규, 테이블 추가 시 이 폴더에 파일 추가
│   │       └── SampleTable.cs           ← SampleTableData + SampleTable 한 파일에 (GameEvents.cs 관례처럼 관련 타입 묶음)
│   └── Editor/
│       └── Table/                       ← 신규
│           ├── TableGenerator.cs
│           └── TableAssetEditor.cs      ← 신규 (개별 asset Inspector 재로드 버튼)
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
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace JungleDice.Core.Table
{
    public abstract class TableBase<TSelf, TData, TKey> : TableAssetBase
        where TSelf : TableBase<TSelf, TData, TKey>
        where TData : TableDataBase<TKey>, new()
    {
        [SerializeField] private List<TData> _rows = new();

        private Dictionary<TKey, TData> _map;

        protected IReadOnlyList<TData> Rows => _rows;

        protected TData this[TKey key] => Map[key];

        protected bool TryGet(TKey key, out TData data) => Map.TryGetValue(key, out data);

        protected virtual void OnLoaded() { }

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
            var fields = typeof(TData).GetFields(BindingFlags.Public | BindingFlags.Instance);
            var newRows = new List<TData>(rows.Count);
            var seenKeys = new HashSet<TKey>();

            foreach (var cols in rows)
            {
                var data = new TData();
                for (int i = 0; i < headers.Length && i < cols.Length; i++)
                {
                    var field = Array.Find(fields, f => f.Name.Equals(headers[i], StringComparison.OrdinalIgnoreCase));
                    if (field == null) continue;

                    if (!TableValueParser.TryParse(field.FieldType, cols[i], out var value))
                    {
                        Debug.LogError($"[Table] {typeof(TData).Name}.{field.Name} 파싱 실패: '{cols[i]}'");
                        continue;
                    }
                    field.SetValue(data, value);
                }

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
| 헤더에는 있는데 데이터 클래스에 필드가 없음 | 조용히 무시 (컬럼 일부만 옮겨도 되도록 허용) |
| 데이터 클래스에는 있는데 헤더에 없는 필드 | 기본값(0/null) 유지 |
| 셀 값 파싱 실패 (예: `int` 필드에 "abc") | `Debug.LogError` 후 해당 필드만 스킵, 나머지 필드/행은 계속 처리 |
| 중복 key | `Debug.LogError`로 안내, `Dictionary` 구성 시 마지막 값으로 덮어씀 (크래시 없음) |
| `Resources/Tables/xxx.asset`이 없는데 런타임에서 `xxxTable.Instance` 접근 | `Instance`가 `null` 반환 + `Debug.LogError`. 호출부에서 `null` 체크 필요 (예외로 죽이지 않음 — 데이터 부재는 기획 이슈지 크래시 사유 아님) |
| 변환기를 재실행해 기존 `.asset`을 다시 생성 | 기존 에셋을 `LoadAssetAtPath`로 재사용 후 `PopulateFromText`로 덮어씀 — 다른 곳에서 이 에셋을 참조 중이어도 참조가 깨지지 않음 |
| 테이블 파일명이 규칙과 다름 (`xxxTable.csv`가 아님) | 요청사항에 명시된 명명 규칙(`xxxTable`)을 강제하지 않음 — 파일명과 클래스명만 일치하면 동작. 규칙 이탈은 코드 리뷰로 방지 |
| Inspector "Reload" 버튼을 눌렀는데 대응 `.csv`가 없음 (asset만 있고 원본 CSV가 삭제/이동됨) | `Debug.LogError`로 경로 안내, asset은 기존 값 그대로 유지 (덮어쓰지 않음) |
| 다른 `ScriptableObject`(테이블이 아닌 것)를 선택했을 때 이 버튼이 보이는지 | 보이지 않음 — `CustomEditor`가 `TableAssetBase`(및 그 서브클래스)에만 바인딩되므로 무관한 asset에는 영향 없음 |
| Project 창에서 테이블 asset을 2개 이상 동시 선택 | 의도적으로 미지원. Unity가 "Multi-object editing not supported."를 표시 — 재로드는 asset을 하나씩 선택해서 사용 |
| 외부 코드가 `table.Rows`나 `table[key]`를 직접 호출 시도 | 컴파일 에러 (`protected` 멤버) — 설계대로 동작. 테이블 클래스가 노출한 public 메서드(`Get`, `GetByGrade` 등)를 거쳐야 함 |
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

---

## 구현 시 주의사항

- **데이터 클래스는 `public` 필드로 선언**: 프로퍼티는 리플렉션 매칭 대상에서 제외되고 Unity도 직렬화하지 않음. `Key`만 예외적으로 프로퍼티(계산값).
- **`Rows`/인덱서/`TryGet`은 `protected`, `public`으로 되돌리지 않는다**: 외부 코드가 이 셋을 직접 쓰기 시작하면 조회 로직이 컨슈머 여러 곳에 흩어짐 — 반드시 테이블 클래스 자신의 메서드(`Get`, `GetByGrade` 등)를 추가해서 노출할 것.
- **가공 데이터(보조 인덱스 등)는 필드 이니셜라이저나 생성자가 아니라 `OnLoaded()`에서 채운다**: 필드 이니셜라이저/생성자 시점에는 `Resources.Load`로 역직렬화된 `_rows`가 아직 채워지기 전이거나(생성자는 asset 생성 시에도 호출됨) 시점이 불명확함. `OnLoaded()`는 `Instance`가 실제로 로드된 직후에만 정확히 한 번 호출되도록 보장됨.
- **커스텀 파싱(컬럼→필드 타입 변환)이 필요해지면 `TableValueParser`를 확장**: 이건 `OnLoaded()`로 하지 않음 — `OnLoaded()`는 이미 파싱된 `Rows`를 가공하는 단계고, 파싱 자체는 여전히 `PopulateFromText`/`TableValueParser`의 책임.
- **`Resources/Tables/` 하위 파일명은 절대 수동으로 바꾸지 않는다**: `Instance` 로드가 `typeof(TSelf).Name` 문자열에 의존하므로 파일명이 클래스명과 어긋나면 로드가 조용히 실패(`null` + LogError)함.
- **`PopulateFromText`는 `TableAssetBase`의 `public abstract` 멤버, `TableBase<TSelf,TData,TKey>`가 `override`**: 명시적 인터페이스 구현(`void ITableAsset.X`)은 상속 체인 중간의 서브클래스에서 쓸 수 없어(C# 제약) 이 형태를 택함. 결과적으로 `PopulateFromText`는 런타임 게임 코드에서도 호출 가능한 `public` 메서드가 됨 — 에디터 전용 경로라는 보장은 명시적 인터페이스 구현만큼 강하지 않으므로, 실수로 런타임 코드에서 호출하지 않도록 주의(리뷰로 방지). 필요해지면 `[Conditional]`이나 별도 어셈블리 분리로 강화 가능(지금은 YAGNI).
- **배열/리스트 컬럼은 이번 범위 밖**: 필요해지면 `TableValueParser`에 구분자(예: `,`) 기반 배열 파싱을 추가하는 방향으로 확장 (지금은 YAGNI).
- **`[CustomEditor]`는 반드시 `TableAssetBase`(비-제네릭)를 대상으로**: `TableBase<,,>`처럼 오픈 제네릭 타입은 Unity `CustomEditor` 속성의 대상이 될 수 없음. 새 마커 클래스를 만들지 않고 우회하려 하지 말 것.
- **`TableGenerator.TryReadLines`는 유일한 CSV 파싱 경로**: "전체 생성"과 "개별 재로드"가 각자 파싱 로직을 따로 구현하지 않도록, 새 진입점을 추가할 때도 반드시 이 헬퍼를 재사용.

---

## 구현 후 체크리스트

- [x] `Assets/Tables/Source/`, `Assets/Resources/Tables/` 폴더 생성 (`Resources/Tables/`는 `TableGenerator` 최초 실행 시 자동 생성)
- [x] `TableDataBase.cs`, `ITableAsset.cs`, `TableBase.cs`, `TableValueParser.cs` 작성 (`Assets/Scripts/Core/Table/`)
- [x] `TableAssetBase.cs` 작성 (`Assets/Scripts/Core/Table/`), `TableBase.cs`가 `TableAssetBase` 상속하도록 수정
- [x] `SampleTable.csv` 예시 데이터 작성 (`Assets/Tables/Source/`)
- [x] `TableGenerator.cs` 작성 (`Assets/Scripts/Editor/Table/`)
- [x] `TableGenerator.cs`에 `ReloadTable(TableAssetBase)` + `TryReadLines` 리팩터링 추가
- [x] `TableAssetEditor.cs` 작성 (`Assets/Scripts/Editor/Table/`) — 개별 asset Inspector "Reload" 버튼
- [x] `TableBase.cs`: `Rows`/인덱서/`TryGet`을 `protected`로 변경, `protected virtual void OnLoaded()` 훅 추가, `Instance` getter에서 로드 직후 `OnLoaded()` 호출
- [x] `SampleTable.cs` 재작성: `Get(int id)`, `GetByName(string name)` + `OnLoaded()` 오버라이드로 `_byName` 보조 인덱스 구성 (`Assets/Scripts/Data/Table/`)
- [ ] 컴파일 에러 없는지 확인 — Unity 배치 모드가 아니라 이미 열려있는 에디터에서 직접 확인 (동시에 같은 프로젝트를 배치 모드로 열 수 없어 자동 검증 불가)
- [ ] 에디터에서 `Tools > Table > Generate All Tables` 메뉴 실행, `SampleTable.asset` 생성 및 3행 확인
- [ ] `SampleTable.asset` 선택 → Inspector 상단 "Reload" 버튼 동작 확인
- [ ] 테스트 시나리오 10개 중 가능한 범위 수동 검증 (특히 #3, #4, #10: `Get(2).name`, `GetByName("Second")`, `GetByName("없는이름")`)
- [x] `Assets/Plans/03-table/plan-table.md`로 이 문서 저장 (프로젝트 관례)
