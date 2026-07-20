using System;
using JungleDice.Core.Table;
using UnityEngine;

namespace JungleDice.Data.Table
{
    public enum CardCondition
    {
        None,
        Merge,
        Except,
    }

    public enum CardTarget
    {
        Same,
        All,
    }

    [Serializable]
    public class CardTableData : TableDataBase<int>
    {
        public int key;
        public string cardname;
        public int att;
        public int hp;
        public CardCondition cond;
        public CardTarget target;
        public string explain;

        public override int Key => key;
    }

    public class CardTable : TableBase<CardTable, CardTableData, int>
    {
        // 없는 key면 LogError 후 null 반환 — 예외로 죽지 않도록 TryGet 경유
        public CardTableData Get(int key)
        {
            if (TryGet(key, out var data))
            {
                return data;
            }
            Debug.LogError($"[Table] {nameof(CardTable)} key 없음: {key}");
            return null;
        }

        public string GetCardName(int key)
        {
            if (TryGet(key, out var data))
            {
                return data.cardname;
            }
            Debug.LogError($"[Table] {nameof(CardTable)} key 없음: {key}");
            return null;
        }

        public int GetAtt(int key)
        {
            if (TryGet(key, out var data))
            {
                return data.att;
            }
            Debug.LogError($"[Table] {nameof(CardTable)} key 없음: {key}");
            return 0;
        }

        public int GetHp(int key)
        {
            if (TryGet(key, out var data))
            {
                return data.hp;
            }
            Debug.LogError($"[Table] {nameof(CardTable)} key 없음: {key}");
            return 0;
        }

        public CardCondition GetCond(int key)
        {
            if (TryGet(key, out var data))
            {
                return data.cond;
            }
            Debug.LogError($"[Table] {nameof(CardTable)} key 없음: {key}");
            return CardCondition.None;
        }

        public CardTarget GetTarget(int key)
        {
            if (TryGet(key, out var data))
            {
                return data.target;
            }
            Debug.LogError($"[Table] {nameof(CardTable)} key 없음: {key}");
            return CardTarget.Same;
        }

        public string GetExplain(int key)
        {
            if (TryGet(key, out var data))
            {
                return data.explain;
            }
            Debug.LogError($"[Table] {nameof(CardTable)} key 없음: {key}");
            return null;
        }
    }
}
