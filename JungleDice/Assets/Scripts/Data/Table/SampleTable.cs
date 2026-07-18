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
            UnityEngine.Debug.Log($"SampleTable loaded. {Rows.Count} rows.");
            _byName = new Dictionary<string, SampleTableData>();
            foreach (var row in Rows)
                _byName[row.name] = row;
        }

        public SampleTableData GetByName(string name) =>
            _byName.TryGetValue(name, out var data) ? data : null;
    }
}
