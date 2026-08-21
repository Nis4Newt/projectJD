using System.Collections.Generic;
using System.Linq;
using JungleDice.Data.Table;
using UnityEngine;

namespace JungleDice.InGame
{
    // 필드 슬롯 한 칸의 스냅샷 — Friend를 직접 들고 있지 않고 값만 복사해 판단 로직이 Unity에 의존하지 않게 한다
    public readonly struct FriendSnapshot
    {
        public readonly int SlotIndex;
        public readonly int Key;
        public readonly int Att;
        public readonly int CurrentHp;
        public readonly int MaxHp;

        public FriendSnapshot(int slotIndex, int key, int att, int currentHp, int maxHp)
        {
            SlotIndex = slotIndex;
            Key = key;
            Att = att;
            CurrentHp = currentHp;
            MaxHp = maxHp;
        }
    }

    // 컴퓨터가 이번 PlayFriend 단계에 낼 카드/슬롯을 고르기 위해 필요한 보드 상태 전체.
    // PvE_AI_카드_우선순위_설계.md(PlanDocs/99-요청문서/)의 "AI 턴 동작 방식"에서 만드는 스냅샷을 값 객체로 옮긴 것
    // — 상대 손패는 장수(PlayerHandCount)만 담고 내용은 담지 않는다.
    public readonly struct ComputerObservation
    {
        public readonly int AiHp;
        public readonly int AiMaxHp;
        public readonly List<FriendSnapshot> AiField;
        public readonly List<int> AiEmptySlotIndices;
        public readonly List<int> AiHand;
        public readonly int AiDeckRemainingCount;

        public readonly int PlayerHp;
        public readonly List<FriendSnapshot> PlayerField;
        public readonly int PlayerHandCount;
        public readonly int PlayerDeckRemainingCount;

        public readonly int InitialComputerDeckSize;

        public int AiEmptySlotCount => AiEmptySlotIndices.Count;

        public ComputerObservation(int aiHp, int aiMaxHp, List<FriendSnapshot> aiField, List<int> aiEmptySlotIndices,
            List<int> aiHand, int aiDeckRemainingCount, int playerHp, List<FriendSnapshot> playerField,
            int playerHandCount, int playerDeckRemainingCount, int initialComputerDeckSize)
        {
            AiHp = aiHp;
            AiMaxHp = aiMaxHp;
            AiField = aiField;
            AiEmptySlotIndices = aiEmptySlotIndices;
            AiHand = aiHand;
            AiDeckRemainingCount = aiDeckRemainingCount;
            PlayerHp = playerHp;
            PlayerField = playerField;
            PlayerHandCount = playerHandCount;
            PlayerDeckRemainingCount = playerDeckRemainingCount;
            InitialComputerDeckSize = initialComputerDeckSize;
        }
    }

    // 컴퓨터가 결정한 행동 — 실행은 InGameSceneManager가 기존 배치/병합 경로(TryPlaceFriendCard와 같은 뿌리)로 수행한다
    public readonly struct ComputerAction
    {
        public readonly int Key;
        public readonly int SlotIndex;
        public readonly bool IsMerge;

        public ComputerAction(int key, int slotIndex, bool isMerge)
        {
            Key = key;
            SlotIndex = slotIndex;
            IsMerge = isMerge;
        }
    }

    // 컴퓨터의 PlayFriend 판단 로직 — CardTable 데이터와 ComputerObservation 스냅샷만으로 동작하는 순수 계산.
    // Friend/FieldSlot/BaseStone 등 Unity 컴포넌트에 직접 의존하지 않는다.
    // PvE_AI_카드_우선순위_설계.md(PlanDocs/99-요청문서/)의 "우선순위 데이터 테이블"을 그대로 구현한다 —
    // 예전 PvE_AI_알고리즘_설계.md 기반 6카테고리/3그룹 방식은 보류되어 더 이상 쓰지 않는다.
    public static class ComputerAI
    {
        private const int FieldSlotsPerSide = 3;

        private const float ConditionAHpRatio = 0.5f;
        private const int ConditionAMinEmptySlots = 2;
        private const int DirectAttackDangerHpThreshold = 10;
        private const double DirectAttackThreatThreshold = 0.5; // 미결 사항 — 플레이테스트로 조정
        private const int EnemyBaseDmgReferenceKey = 1002; // 현재 유일한 EnemyBaseDmg 카드(개구리) — 같은 카테고리 카드가 늘면 초기하분포 추정도 합산하도록 확장 필요

        // ===== 진입점 =====

        // UrgencyState는 턴 시작 시 한 번만 확인한다 — InGameSceneManager가 이 값을 들고 있다가
        // 한 턴에 여러 장을 낼 때마다(DecideNextAction 반복 호출) 매번 같은 상태를 넘겨준다.
        public static (UrgencyState Ai, UrgencyState Player) DetermineUrgencyStates(ComputerObservation obs) =>
            (GetUrgencyState(isAi: true, obs), GetUrgencyState(isAi: false, obs));

        // obs는 카드를 낼 때마다 달라지는 필드/손패 스냅샷을 그대로 받는다 — UrgencyState만 고정값으로 재사용.
        // 문서의 "순위 매트릭스"는 ActionPriorityTable(원본: Assets/Tables/Source/ActionPriorityTable.csv)에서 조회한다.
        public static ComputerAction? DecideNextAction(ComputerObservation obs, UrgencyState aiState, UrgencyState playerState)
        {
            var overrideAction = TryGlobalOverride(obs, aiState, playerState);
            if (overrideAction.HasValue) return overrideAction;

            foreach (var category in ActionPriorityTable.Instance.GetPriority(aiState, playerState))
            {
                if (!IsEligible(category, aiState, playerState, obs)) continue;
                var action = TryExecuteCategory(category, obs);
                if (action.HasValue) return action;
            }
            return null;
        }

        // ===== 위급 상태 판정 =====

        private static UrgencyState GetUrgencyState(bool isAi, ComputerObservation obs)
        {
            int hp = isAi ? obs.AiHp : obs.PlayerHp;
            int emptySlotCount = isAi ? obs.AiEmptySlotCount : FieldSlotsPerSide - obs.PlayerField.Count;

            if (IsFieldExposure(hp, emptySlotCount, obs)) return UrgencyState.FieldExposure;
            if (IsDirectAttackThreat(isAi, hp, obs)) return UrgencyState.DirectAttackThreat;
            return UrgencyState.None;
        }

        // 조건 A: 필드 전체(내 + 상대) 친구 중 최댓값 Att ≥ 체력의 절반, 그리고 내 필드 빈 슬롯 ≥ 2
        private static bool IsFieldExposure(int hp, int emptySlotCount, ComputerObservation obs)
        {
            if (emptySlotCount < ConditionAMinEmptySlots) return false;
            int maxAtt = obs.AiField.Concat(obs.PlayerField).Select(f => f.Att).DefaultIfEmpty(0).Max();
            return maxAtt >= hp * ConditionAHpRatio;
        }

        // 조건 B: 체력 ≤ 10, 그리고 상대가 직접공격 카드를 가지고 있을 가능성이 높음
        private static bool IsDirectAttackThreat(bool isAi, int hp, ComputerObservation obs)
        {
            if (hp > DirectAttackDangerHpThreshold) return false;
            return isAi ? IsAiFacingDirectAttackThreat(obs) : IsPlayerFacingDirectAttackThreat(obs);
        }

        // AI 관점: 플레이어 손패 내용은 모르므로 초기하분포로 추정(정보 제한 유지)
        private static bool IsAiFacingDirectAttackThreat(ComputerObservation obs)
        {
            if (obs.PlayerField.Any(f => Classify(f.Key) == AbilityPriorityCategory.EnemyBaseDmg)) return true;
            return EstimatePlayerHasAtLeast(EnemyBaseDmgReferenceKey, wantCount: 2, obs) >= DirectAttackThreatThreshold;
        }

        // 플레이어 관점: 상대(AI)의 패는 AI 자신의 데이터라 확률 추정 없이 그대로 확인 가능
        private static bool IsPlayerFacingDirectAttackThreat(ComputerObservation obs)
        {
            if (obs.AiField.Any(f => Classify(f.Key) == AbilityPriorityCategory.EnemyBaseDmg)) return true;
            return obs.AiHand.Count(key => Classify(key) == AbilityPriorityCategory.EnemyBaseDmg) >= 2;
        }

        // ===== 매트릭스보다 우선하는 전역 규칙: 확정 킬 / 위협 해소 보너스 =====

        private static ComputerAction? TryGlobalOverride(ComputerObservation obs, UrgencyState aiState, UrgencyState playerState)
        {
            var lethalHit = FindLethalAdventurerHit(obs);
            if (lethalHit.HasValue) return lethalHit;

            if (aiState != UrgencyState.FieldExposure && playerState != UrgencyState.FieldExposure) return null;

            var threat = FindThreatCard(obs);
            if (!threat.HasValue || !IsOnPlayerField(threat.Value, obs)) return null;

            return FindGuaranteedThreatRemoval(obs, threat.Value);
        }

        // EnemyBaseDmg 카드로 상대 모험가를 확정으로 죽일 수 있으면 순위와 무관하게 최우선
        private static ComputerAction? FindLethalAdventurerHit(ComputerObservation obs)
        {
            foreach (int handKey in obs.AiHand)
            {
                var data = CardTable.Instance.Get(handKey);
                if (data == null || Classify(data) != AbilityPriorityCategory.EnemyBaseDmg) continue;
                if (TotalDamage(data) < obs.PlayerHp) continue;

                var slot = FindMergeSlot(obs.AiField, handKey);
                if (slot.HasValue) return new ComputerAction(handKey, slot.Value.SlotIndex, isMerge: true);
            }
            return null;
        }

        // EnemyFieldDmg 카드로 조건 A를 유발한 위협 카드(상대 소유일 때만)를 확정으로 제거할 수 있으면 최우선
        private static ComputerAction? FindGuaranteedThreatRemoval(ComputerObservation obs, FriendSnapshot threat)
        {
            if (CardTable.Instance.GetCond(threat.Key) == CardCondition.Except) return null; // 면역 카드는 애초에 EnemyFieldDmg 대상이 아님

            foreach (int handKey in obs.AiHand)
            {
                var data = CardTable.Instance.Get(handKey);
                if (data == null || Classify(data) != AbilityPriorityCategory.EnemyFieldDmg) continue;
                if (!GuaranteesHit(data, obs)) continue;
                if (TotalDamage(data) < threat.CurrentHp) continue;

                var slot = FindMergeSlot(obs.AiField, handKey);
                if (slot.HasValue) return new ComputerAction(handKey, slot.Value.SlotIndex, isMerge: true);
            }
            return null;
        }

        // EnemyFieldDmg 카드가 위협 카드에 확정으로 명중하는가 — EnemyAll(전체 적용)은 항상 포함되지만,
        // EnemyRandom(무작위 단일 대상)은 상대 필드에 타겟 가능한(면역 제외) 후보가 이 카드 하나뿐일 때만 확정된다.
        private static bool GuaranteesHit(CardTableData data, ComputerObservation obs) =>
            data.scope switch
            {
                CardAbilityScope.EnemyAll => true,
                CardAbilityScope.EnemyRandom => obs.PlayerField.Count(f => CardTable.Instance.GetCond(f.Key) != CardCondition.Except) == 1,
                _ => false,
            };

        private static int TotalDamage(CardTableData data) =>
            data.EffectClauses.Where(c => c.Kind == CardEffectClauseKind.Damage).Sum(c => c.Value);

        // 조건 A가 가리키는 위협 카드 — 필드 전체(내 + 상대)에서 공격력 최댓값을 가진 친구.
        // 동점일 때는 상대 카드를 먼저 넣어(OrderByDescending은 안정 정렬) 상대 소유 쪽을 우선 선택한다 —
        // 내 카드를 먼저 넣으면 동점 시 항상 "위협이 내 카드"로 판정돼 위협 해소 대응이 통째로 스킵된다.
        private static FriendSnapshot? FindThreatCard(ComputerObservation obs)
        {
            var all = obs.PlayerField.Concat(obs.AiField).ToList();
            return all.Count == 0 ? null : all.OrderByDescending(f => f.Att).First();
        }

        private static bool IsOnPlayerField(FriendSnapshot friend, ComputerObservation obs) =>
            obs.PlayerField.Any(f => f.SlotIndex == friend.SlotIndex);

        // ===== 조건부 셀: EnemyWeaken / EnemyFieldDmg 발동 조건 =====

        private static bool IsEligible(AbilityPriorityCategory category, UrgencyState ai, UrgencyState player, ComputerObservation obs)
        {
            if (category != AbilityPriorityCategory.EnemyWeaken && category != AbilityPriorityCategory.EnemyFieldDmg)
                return true;

            // 위협 해소 목적 — AI 자신이 조건 A일 때만, 위협 카드가 상대 소유일 때만 발동
            if (ai == UrgencyState.FieldExposure)
            {
                var threat = FindThreatCard(obs);
                return threat.HasValue && IsOnPlayerField(threat.Value, obs);
            }

            // AI만 조건 B 상황의 EnemyFieldDmg는 압박 목적이지만 "플레이어 필드에 직접공격 카드가 이미 있을 때만" 예외 조건이 붙는다
            if (category == AbilityPriorityCategory.EnemyFieldDmg && ai == UrgencyState.DirectAttackThreat && player == UrgencyState.None)
                return obs.PlayerField.Any(f => Classify(f.Key) == AbilityPriorityCategory.EnemyBaseDmg);

            // 나머지는 압박 목적 — 조건 없음
            return true;
        }

        // ===== 초기하분포 =====

        // key 카드가 상대 진영의 "아직 확인되지 않은 카드 풀"(덱 잔여 + 손패, 손패 내용은 모름)에서 wantCount장 이상 있을 확률.
        private static double EstimatePlayerHasAtLeast(int key, int wantCount, ComputerObservation obs)
        {
            var data = CardTable.Instance.Get(key);
            if (data == null) return 0.0;

            int observedOnField = obs.PlayerField.Count(f => f.Key == key);
            int unseenKeyCount = Mathf.Max(0, data.sheets - observedOnField);
            int unseenPopulation = obs.PlayerDeckRemainingCount + obs.PlayerHandCount;
            int sampleSize = obs.PlayerHandCount;

            double pZero = HypergeometricPmf(unseenPopulation, unseenKeyCount, sampleSize, 0);
            double pOne = wantCount >= 2 ? HypergeometricPmf(unseenPopulation, unseenKeyCount, sampleSize, 1) : 0.0;
            return 1.0 - pZero - pOne;
        }

        private static double HypergeometricPmf(int population, int successStates, int sampleSize, int successCount)
        {
            if (successCount < 0 || successCount > successStates || successCount > sampleSize) return 0.0;
            if (sampleSize - successCount > population - successStates) return 0.0;
            return Combination(successStates, successCount)
                 * Combination(population - successStates, sampleSize - successCount)
                 / Combination(population, sampleSize);
        }

        // 큰 값 오버플로를 피하려고 (n-i)/(i+1) 비율을 누적 곱하는 방식으로 계산
        private static double Combination(int n, int k)
        {
            if (k < 0 || k > n) return 0.0;
            k = Mathf.Min(k, n - k);
            double result = 1.0;
            for (int i = 0; i < k; i++)
                result *= (double)(n - i) / (i + 1);
            return result;
        }

        // ===== 카드 분류 =====

        // PvE_AI_카드_우선순위_설계.md의 "카드별 값" 표와 동일한 결과를 cond/target/scope/effect 조합으로 도출한다
        // — 카드 20종 전부 이 규칙만으로 문서의 표와 정확히 일치한다(별도 key 분기 없음).
        public static AbilityPriorityCategory Classify(CardTableData data)
        {
            if (data.cond == CardCondition.Except) return AbilityPriorityCategory.Fill; // 독수리

            if (data.cond is CardCondition.None or CardCondition.Die)
                // target=All(능동으로 무엇에든 합쳐짐, 블루베리)만 특수 효과 취급 — target=Any(베이스 역할, 하이에나)/Same은 그냥 카드
                return data.target == CardTarget.All ? AbilityPriorityCategory.Special : AbilityPriorityCategory.Fill;

            // cond == Merge
            if (data.EffectClauses.Any(c => c.Kind == CardEffectClauseKind.Spawn)) return AbilityPriorityCategory.Special; // 버섯(포자감염)
            if (data.EffectClauses.Any(c => c.Kind == CardEffectClauseKind.Keyword && c.Keyword == "MultiplierMerge")) return AbilityPriorityCategory.SoloGrowth; // 까마귀
            if (data.EffectClauses.Any(c => c.Kind == CardEffectClauseKind.Keyword && c.Keyword == "Shield")) return AbilityPriorityCategory.Shield; // 거북이, 달팽이

            if (data.EffectClauses.Any(c => c.Kind == CardEffectClauseKind.Damage))
                return data.scope == CardAbilityScope.EnemyBase ? AbilityPriorityCategory.EnemyBaseDmg : AbilityPriorityCategory.EnemyFieldDmg; // 개구리 / 박쥐,고릴라

            if (data.EffectClauses.Any(c => c.Kind is CardEffectClauseKind.Heal or CardEffectClauseKind.HealToMax))
                return data.scope == CardAbilityScope.AllyBase ? AbilityPriorityCategory.AdvGrowth : AbilityPriorityCategory.FieldHeal; // 고래 / 오리

            if (data.EffectClauses.Any(c => c.Kind == CardEffectClauseKind.Stat && c.Op is '-' or '/')) return AbilityPriorityCategory.EnemyWeaken; // 거미
            if (data.EffectClauses.Any(c => c.Kind == CardEffectClauseKind.Stat && c.Op is '+' or '*')) return AbilityPriorityCategory.AllyGrowth; // 코끼리,바오밥나무,염소,해파리

            return AbilityPriorityCategory.Fill; // 현재 20종 카드에서는 도달하지 않음(신규 카드 추가 시 대비한 기본값)
        }

        private static AbilityPriorityCategory Classify(int key)
        {
            var data = CardTable.Instance.Get(key);
            return data == null ? AbilityPriorityCategory.Fill : Classify(data);
        }

        // 병합 가능 판정 — InGameSceneManager.CanMerge가 이 메서드에 위임한다(판정 로직은 하나만 존재해야 함)
        public static bool CanMerge(int existingKey, int mergeKey)
        {
            var existingData = CardTable.Instance.Get(existingKey);
            var data = CardTable.Instance.Get(mergeKey);

            bool sameKind = existingKey == mergeKey;
            bool existingAcceptsAnything = existingData.target == CardTarget.Any; // 필드 카드가 베이스 역할(하이에나류)
            bool mergeJoinsAnything = data.target == CardTarget.All; // 낸 카드가 무엇에든 합쳐지는 역할(블루베리류)
            return sameKind || existingAcceptsAnything || mergeJoinsAnything;
        }

        // ===== 카테고리 실행 =====

        private static ComputerAction? TryExecuteCategory(AbilityPriorityCategory category, ComputerObservation obs)
        {
            if (category == AbilityPriorityCategory.Fill) return TryFill(obs);

            foreach (int handKey in obs.AiHand)
            {
                var data = CardTable.Instance.Get(handKey);
                if (data == null || Classify(data) != category) continue;

                var slot = FindMergeSlot(obs.AiField, handKey);
                if (slot.HasValue) return new ComputerAction(handKey, slot.Value.SlotIndex, isMerge: true);
            }
            return null;
        }

        // 필드 채우기: 조커형/필러형(Fill로 분류된 카드)을 스탯 합이 낮은 순으로 우선(강한 카드를 낭비하지 않기 위함), 없으면 손패 맨 앞 카드
        private static ComputerAction? TryFill(ComputerObservation obs)
        {
            if (obs.AiEmptySlotCount == 0 || obs.AiHand.Count == 0) return null;

            var fillerCandidates = obs.AiHand
                .Where(key => Classify(key) == AbilityPriorityCategory.Fill)
                .OrderBy(key => { var d = CardTable.Instance.Get(key); return d.att + d.hp; })
                .ToList();

            int chosenKey = fillerCandidates.Count > 0 ? fillerCandidates[0] : obs.AiHand[0];
            return new ComputerAction(chosenKey, obs.AiEmptySlotIndices[0], isMerge: false);
        }

        // handKey를 병합할 수 있는 내 필드 슬롯을 찾는다 — 완전히 같은 카드가 있는 슬롯을 우선하고
        // ("필드에 같은게 있으면 바로 제출"), 없으면 와일드카드(Any/All) 조합만 허용한다.
        private static FriendSnapshot? FindMergeSlot(List<FriendSnapshot> aiField, int handKey)
        {
            FriendSnapshot? wildcard = null;
            foreach (var slot in aiField)
            {
                if (!CanMerge(slot.Key, handKey)) continue;
                if (slot.Key == handKey) return slot;
                wildcard ??= slot;
            }
            return wildcard;
        }
    }
}
