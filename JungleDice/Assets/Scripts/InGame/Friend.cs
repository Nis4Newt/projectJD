using DG.Tweening;
using JungleDice.Core.Sprites;
using JungleDice.Data.Table;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.InGame
{
    public class Friend : MonoBehaviour
    {
        [SerializeField] private Image _cardImage;
        [SerializeField] private TextMeshProUGUI _attText;
        [SerializeField] private TextMeshProUGUI _hpText;
        [SerializeField] private Image _highlightImage; // 카드 전체를 덮는 하이라이트 오버레이, 기본 비활성화

        public int Key { get; private set; }
        public int Att { get; private set; }
        public int CurrentHp { get; private set; }
        public int MaxHp { get; private set; }
        public bool HasShield { get; private set; }
        public bool HasRevived { get; private set; }
        public bool IsDead => CurrentHp <= 0;

        // 사망 시 새 카드를 낳는 예약(부활/포자감염 공용) — key/att/hp는 spawn+key,att=n,hp=n 조각에서 옴
        public bool HasSpawnMark { get; private set; }
        public int SpawnMarkKey { get; private set; }
        public int SpawnMarkAtt { get; private set; }
        public int SpawnMarkHp { get; private set; }

        public void SetKey(int key)
        {
            Key = key;

            var data = CardTable.Instance?.Get(key);
            if (data == null) return; // CardTable.Get이 이미 LogError를 남김

            Att = data.att;
            CurrentHp = data.hp;
            MaxHp = data.hp;

            _cardImage.sprite = SpriteManager.GetCard(key.ToString());
            _attText.text = Att.ToString();
            _attText.color = Color.white;
            _hpText.text = CurrentHp.ToString();
            _hpText.color = Color.white;
        }

        public void TakeDamage(int amount)
        {
            if (HasShield)
            {
                HasShield = false;
                return; // 이번 피해 전부 무효, 텍스트/색 변화 없음
            }

            int previousHp = CurrentHp;
            CurrentHp = Mathf.Max(0, CurrentHp - amount);
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        public void MergeWith(int addAtt, int addHp)
        {
            int previousAtt = Att;
            int previousHp = CurrentHp;

            Att += addAtt;
            CurrentHp += addHp;
            MaxHp += addHp;

            _attText.text = Att.ToString();
            _attText.color = GetStatColor(Att, previousAtt);
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        public void DoubleAtt()
        {
            int previousAtt = Att;
            Att *= 2;
            _attText.text = Att.ToString();
            _attText.color = GetStatColor(Att, previousAtt);
        }

        public void AddAtt(int amount)
        {
            int previous = Att;
            Att = Mathf.Max(0, Att + amount);
            _attText.text = Att.ToString();
            _attText.color = GetStatColor(Att, previous);
        }

        public void MultiplyAtt(int factor)
        {
            int previous = Att;
            Att *= factor;
            _attText.text = Att.ToString();
            _attText.color = GetStatColor(Att, previous);
        }

        public void DivideAtt(int divisor)
        {
            int previous = Att;
            Att = Mathf.Max(0, Att / divisor);
            _attText.text = Att.ToString();
            _attText.color = GetStatColor(Att, previous);
        }

        // 스탯 증감(성장/저하) — MaxHp도 같이 바뀜. 전투 피해(TakeDamage)·회복(Heal)과 달리 방어막과 무관하고 최대치 자체가 변한다
        public void AddHp(int amount)
        {
            int previousHp = CurrentHp;
            CurrentHp = Mathf.Max(0, CurrentHp + amount);
            MaxHp = Mathf.Max(1, MaxHp + amount);
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        // 고정량 회복 — MaxHp를 넘지 않고, MaxHp 자체는 바꾸지 않는다(성장인 AddHp와 구분)
        public void Heal(int amount)
        {
            int previousHp = CurrentHp;
            CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        public void MultiplyHp(int factor)
        {
            int previousHp = CurrentHp;
            CurrentHp *= factor;
            MaxHp *= factor;
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        public void DivideHp(int divisor)
        {
            int previousHp = CurrentHp;
            CurrentHp = Mathf.Max(0, CurrentHp / divisor);
            MaxHp = Mathf.Max(1, MaxHp / divisor);
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        public void HealToMax()
        {
            int previousHp = CurrentHp;
            CurrentHp = MaxHp;
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        public void AddShield() => HasShield = true; // 이미 있어도 그대로 유지(스택 없음)

        public void ApplySpawnMark(int key, int att, int hp)
        {
            HasSpawnMark = true;
            SpawnMarkKey = key;
            SpawnMarkAtt = att;
            SpawnMarkHp = hp;
        }

        // 사망 시 1회 부활(CardCondition.Die) — att/hp는 자신의 effect(spawn+key,att=n,hp=n)에서 읽어온 값
        public bool TryRevive(int att, int hp)
        {
            if (HasRevived) return false;
            HasRevived = true;
            OverrideStats(att, hp);
            return true;
        }

        // SetKey로 채워진 기본 스탯을 명시적인 값으로 덮어쓴다 — 부활/포자감염처럼 카드 기본값이 아닌 수치로 등장할 때 사용
        public void OverrideStats(int att, int hp)
        {
            Att = att;
            CurrentHp = hp;
            MaxHp = hp;
            _attText.text = Att.ToString();
            _attText.color = Color.white;
            _hpText.text = CurrentHp.ToString();
            _hpText.color = Color.white;
        }

        // 직전 값 대비로 판정 — 오르면 초록, 떨어지면 빨강, 변화 없으면 흰색(최초값 같은 고정 기준값과 비교하지 않음)
        private static Color GetStatColor(int current, int previous)
        {
            if (current == previous) return Color.white;
            return current > previous ? Color.green : Color.red;
        }

        public void SetHighlight(bool on, Color color)
        {
            _highlightImage.color = color;
            _highlightImage.gameObject.SetActive(on);
        }

        // vibrato를 1로 둬 "커졌다 바로 돌아오는" 단일 펀치로 — 기본값(10)은 여러 번 진동해 목적에 맞지 않음
        public void PunchScale(float strength, float duration)
        {
            transform.DOKill();
            transform.DOPunchScale(Vector3.one * strength, duration, vibrato: 1, elasticity: 0.3f);
        }

        public void MoveTo(Vector3 worldPosition, float duration, Ease ease)
        {
            transform.DOKill();
            transform.DOMove(worldPosition, duration).SetEase(ease);
        }

        public void SetParent(Transform parent) => transform.SetParent(parent, worldPositionStays: true);
    }
}
