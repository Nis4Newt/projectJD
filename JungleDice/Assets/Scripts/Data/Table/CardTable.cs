using System;
using System.Collections.Generic;
using JungleDice.Core.Table;
using UnityEngine;

namespace JungleDice.Data.Table
{
    public enum CardCondition
    {
        None,
        Merge,
        Except,
        Die,
    }

    public enum CardTarget
    {
        Same,
        All, // 이 카드를 낼 때(합칠 때) 무엇에든 합쳐질 수 있음 — 능동, 예: 1004 블루베리
        Any, // 이 카드가 필드에 있을 때 무엇이든 받아줄 수 있음 — 수동(베이스 역할), 예: 1019 하이에나
    }

    // "누구에게" — 발동 효과의 대상 범위
    public enum CardAbilityScope
    {
        None,
        Self,
        AllyRandom,
        AllyAll,
        EnemyRandom,
        EnemyAll,
        AllyBase,
        EnemyBase,
    }

    [Serializable]
    public class CardTableData : TableDataBase<int>
    {
        public int key;
        public string animal;
        public string cardname;
        public int sheets;
        public int att;
        public int hp;
        public CardCondition cond;
        public CardTarget target;
        public CardAbilityScope scope;
        public string explain;

        // effect 문자열을 파싱한 결과 — ParseRow가 채움 (원본 문자열은 보관하지 않음)
        public List<CardEffectClause> EffectClauses;

        public override int Key => key;
    }

    public class CardTable : TableBase<CardTable, CardTableData, int>
    {
        protected override CardTableData ParseRow(TableRow row)
        {
            var key = row.Get<int>("key");
            return new CardTableData
            {
                key = key,
                animal = row.Get<string>("animal"),
                cardname = row.Get<string>("cardname"),
                sheets = row.Get<int>("sheets"),
                att = row.Get<int>("att"),
                hp = row.Get<int>("hp"),
                cond = row.Get<CardCondition>("cond"),
                target = row.Get<CardTarget>("target"),
                scope = row.Get<CardAbilityScope>("scope"),
                explain = row.Get<string>("explain"),
                EffectClauses = CardEffectParser.Parse(row.Get<string>("effect"), key),
            };
        }

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
