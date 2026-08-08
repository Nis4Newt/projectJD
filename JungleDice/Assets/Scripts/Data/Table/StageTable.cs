using System;
using JungleDice.Core.Table;
using UnityEngine;

namespace JungleDice.Data.Table
{
    [Serializable]
    public class StageTableData : TableDataBase<int>
    {
        public int key;
        public string icon;
        public int hp;
        public int friend1;
        public int friend2;
        public int friend3;

        [NonSerialized] public int[] friends;

        public override int Key => key;
    }

    public class StageTable : TableBase<StageTable, StageTableData, int>
    {
        // 없는 key면 LogError 후 null 반환 — 예외로 죽지 않도록 TryGet 경유
        public StageTableData Get(int key)
        {
            if (TryGet(key, out var data))
            {
                return data;
            }
            Debug.LogError($"[Table] {nameof(StageTable)} key 없음: {key}");
            return null;
        }

        public int[] GetFriends(int key)
        {
            if (TryGet(key, out var data))
            {
                return data.friends;
            }
            Debug.LogError($"[Table] {nameof(StageTable)} key 없음: {key}");
            return null;
        }

        public string GetIcon(int key)
        {
            if (TryGet(key, out var data))
            {
                return data.icon;
            }
            Debug.LogError($"[Table] {nameof(StageTable)} key 없음: {key}");
            return null;
        }

        public int GetHp(int key)
        {
            if (TryGet(key, out var data))
            {
                return data.hp;
            }
            Debug.LogError($"[Table] {nameof(StageTable)} key 없음: {key}");
            return 0;
        }

        protected override void OnLoaded()
        {
            foreach (var row in Rows)
                row.friends = new[] { row.friend1, row.friend2, row.friend3 };
        }
    }
}
