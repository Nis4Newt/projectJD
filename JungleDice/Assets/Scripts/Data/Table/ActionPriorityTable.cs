using System;
using System.Linq;
using JungleDice.Core.Table;
using UnityEngine;

namespace JungleDice.Data.Table
{
    // 카드 우선순위 카테고리 — PvE_AI_카드_우선순위_설계.md(PlanDocs/99-요청문서/)의 11개 카테고리 키.
    public enum AbilityPriorityCategory
    {
        Fill,
        Shield,
        AdvGrowth,
        AdvHeal,
        FieldHeal,
        AllyGrowth,
        SoloGrowth,
        Special,
        EnemyBaseDmg,
        EnemyWeaken,
        EnemyFieldDmg,
    }

    // AI/플레이어 각각의 위급 상태 — 같은 문서의 "위급 상황 판단 조건" 조건 A/B.
    public enum UrgencyState
    {
        None,
        FieldExposure,      // 조건 A — 필드에 큰 위협 + 내 빈 슬롯 2개 이상 (다이스 직격 위험)
        DirectAttackThreat, // 조건 B — 체력 10 이하 + 상대의 직접공격 능력 위협
    }

    // 순위 매트릭스 한 행 — (ai, player) 조합 하나에 대한 11개 카테고리 우선순위.
    [Serializable]
    public class ActionPriorityTableData : TableDataBase<string>
    {
        public UrgencyState ai;
        public UrgencyState player;
        public AbilityPriorityCategory[] priority; // 1순위부터 11순위까지 순서대로 — ParseRow가 CSV의 ','구분 문자열을 파싱해 채운다

        public override string Key => $"{ai}_{player}";
    }

    // PvE_AI_카드_우선순위_설계.md(PlanDocs/99-요청문서/)의 "순위 매트릭스"를 그대로 옮긴 테이블.
    // 원본: Assets/Tables/Source/ActionPriorityTable.csv
    public class ActionPriorityTable : TableBase<ActionPriorityTable, ActionPriorityTableData, string>
    {
        protected override ActionPriorityTableData ParseRow(TableRow row) => new()
        {
            ai = row.Get<UrgencyState>("ai"),
            player = row.Get<UrgencyState>("player"),
            priority = ParsePriority(row.Get<string>("priority")),
        };

        // "priority" 컬럼은 한 셀 안에 ','로 여러 값이 들어있는 형태라 TableValueParser/TableRow의 범용 파싱 범위 밖 — 직접 분리
        private static AbilityPriorityCategory[] ParsePriority(string raw)
        {
            // raw는 컬럼이 없거나 파싱 실패 시 null일 수 있음(TableRow.Get이 이미 LogError를 남김) — 임포트를 막지 않도록 빈 배열로 처리
            if (string.IsNullOrEmpty(raw)) return Array.Empty<AbilityPriorityCategory>();

            return raw.Split(',')
                .Select(token => (AbilityPriorityCategory)Enum.Parse(typeof(AbilityPriorityCategory), token.Trim(), true))
                .ToArray();
        }

        // (ai, player) 조합에 대한 1~11순위 카테고리 배열 — 문서의 순위 매트릭스 조회와 동일한 키
        public AbilityPriorityCategory[] GetPriority(UrgencyState ai, UrgencyState player)
        {
            var key = $"{ai}_{player}";
            if (TryGet(key, out var data))
                return data.priority;

            Debug.LogError($"[Table] {nameof(ActionPriorityTable)} 조합 없음: ai={ai}, player={player}");
            return Array.Empty<AbilityPriorityCategory>();
        }
    }
}
