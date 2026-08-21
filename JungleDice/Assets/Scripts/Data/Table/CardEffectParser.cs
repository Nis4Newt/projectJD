using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace JungleDice.Data.Table
{
    // effect 수식 문자열의 대상 스탯 — Att/Hp 두 축만 존재
    public enum CardStat
    {
        Att,
        Hp,
    }

    // effect 수식 한 조각의 종류
    public enum CardEffectClauseKind
    {
        Stat,       // Att/Hp + 사칙연산 + 값 — 영구적인 스탯 증감(MaxHp도 함께 변함)
        Damage,     // dmg+n — 전투와 같은 피해(방어막 소모, MaxHp 불변)
        Heal,       // heal+n — 고정량 회복(MaxHp까지만, MaxHp 자체는 불변)
        HealToMax,  // heal+max — 최대치까지 회복
        Keyword,    // Shield/MultiplierMerge처럼 값이 없는 상태 변경
        Spawn,      // spawn+key,att=n,hp=n — 부활/포자감염처럼 카드를 새로 생성
    }

    // effect 수식 한 조각. Kind로 어떤 필드가 유효한지 정해진다(정적 팩터리로만 생성)
    // Unity 직렬화 대상이라 readonly struct가 아닌 일반 struct(+ [Serializable]) — readonly 필드는 Unity가 직렬화하지 않음
    [Serializable]
    public struct CardEffectClause
    {
        public CardEffectClauseKind Kind;
        public CardStat Stat;   // Kind == Stat일 때만 유효
        public char Op;         // Kind == Stat일 때만 유효: '+' '-' '*' '/'
        public int Value;       // Kind == Stat/Damage/Heal일 때만 유효
        public string Keyword;  // Kind == Keyword일 때만 유효: "Shield"/"MultiplierMerge"
        public int SpawnKey;    // Kind == Spawn일 때만 유효
        public int SpawnAtt;    // Kind == Spawn일 때만 유효
        public int SpawnHp;     // Kind == Spawn일 때만 유효

        private CardEffectClause(CardEffectClauseKind kind, CardStat stat, char op, int value, string keyword, int spawnKey, int spawnAtt, int spawnHp)
        {
            Kind = kind;
            Stat = stat;
            Op = op;
            Value = value;
            Keyword = keyword;
            SpawnKey = spawnKey;
            SpawnAtt = spawnAtt;
            SpawnHp = spawnHp;
        }

        public static CardEffectClause StatOp(CardStat stat, char op, int value) => new(CardEffectClauseKind.Stat, stat, op, value, null, 0, 0, 0);
        public static CardEffectClause Damage(int value) => new(CardEffectClauseKind.Damage, default, default, value, null, 0, 0, 0);
        public static CardEffectClause Heal(int value) => new(CardEffectClauseKind.Heal, default, default, value, null, 0, 0, 0);
        public static CardEffectClause HealToMax() => new(CardEffectClauseKind.HealToMax, default, default, 0, null, 0, 0, 0);
        public static CardEffectClause KeywordOf(string keyword) => new(CardEffectClauseKind.Keyword, default, default, 0, keyword, 0, 0, 0);
        public static CardEffectClause Spawn(int key, int att, int hp) => new(CardEffectClauseKind.Spawn, default, default, 0, null, key, att, hp);
    }

    // "무엇을" 수식 문자열을 해석하는 전용 파서 — 테이블의 다른 컬럼과 달리 TableValueParser의 범용 파싱으로 표현할 수 없어 별도로 둔다
    public static class CardEffectParser
    {
        private static readonly Regex StatOpPattern = new(@"^(Att|Hp)([+\-*/])(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DamagePattern = new(@"^dmg\+(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HealPattern = new(@"^heal\+(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HealMaxPattern = new(@"^heal\+max$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // spawn 조각은 콤마를 자기 내부 구분자로 쓰므로(부활/포자감염 전용) 전체 문자열 단위로 따로 검사한다
        private static readonly Regex SpawnPattern = new(@"^spawn\+(\d+),att=(\d+),hp=(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // 알려진 키워드만 통과시키고 대소문자를 정규화 — CSV에 "shield"처럼 casing이 달라도 항상 같은 문자열로 취급
        private static readonly Dictionary<string, string> KnownKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Shield"] = "Shield",
            ["MultiplierMerge"] = "MultiplierMerge",
        };

        public static List<CardEffectClause> Parse(string raw, int key)
        {
            var result = new List<CardEffectClause>();
            if (string.IsNullOrWhiteSpace(raw)) return result;

            var trimmedRaw = raw.Trim();
            if (trimmedRaw.Equals("None", StringComparison.OrdinalIgnoreCase)) return result; // "능력 없음"을 CSV에 명시적으로 적어둔 것

            var spawnMatch = SpawnPattern.Match(trimmedRaw);
            if (spawnMatch.Success)
            {
                result.Add(CardEffectClause.Spawn(
                    int.Parse(spawnMatch.Groups[1].Value),
                    int.Parse(spawnMatch.Groups[2].Value),
                    int.Parse(spawnMatch.Groups[3].Value)));
                return result;
            }

            foreach (var token in trimmedRaw.Split(','))
            {
                var trimmed = token.Trim();
                if (trimmed.Length == 0) continue; // 콤마 뒤 빈 조각(트레일링 콤마 등)은 조용히 건너뜀

                var statMatch = StatOpPattern.Match(trimmed);
                var dmgMatch = DamagePattern.Match(trimmed);
                var healMaxMatch = HealMaxPattern.Match(trimmed);
                var healMatch = HealPattern.Match(trimmed);

                if (statMatch.Success)
                {
                    var stat = statMatch.Groups[1].Value.Equals("Att", StringComparison.OrdinalIgnoreCase) ? CardStat.Att : CardStat.Hp;
                    char op = statMatch.Groups[2].Value[0];
                    int value = int.Parse(statMatch.Groups[3].Value);
                    result.Add(CardEffectClause.StatOp(stat, op, value));
                }
                else if (dmgMatch.Success)
                {
                    result.Add(CardEffectClause.Damage(int.Parse(dmgMatch.Groups[1].Value)));
                }
                else if (healMaxMatch.Success)
                {
                    result.Add(CardEffectClause.HealToMax());
                }
                else if (healMatch.Success)
                {
                    result.Add(CardEffectClause.Heal(int.Parse(healMatch.Groups[1].Value)));
                }
                else if (KnownKeywords.TryGetValue(trimmed, out var canonical))
                {
                    result.Add(CardEffectClause.KeywordOf(canonical));
                }
                else
                {
                    Debug.LogError($"[Table] CardTableData.effect 알 수 없는 조각(key={key}): '{trimmed}'");
                }
            }
            return result;
        }
    }
}
