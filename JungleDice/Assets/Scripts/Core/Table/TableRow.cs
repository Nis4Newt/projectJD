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
