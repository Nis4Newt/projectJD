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
