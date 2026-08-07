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
        public bool IsDead => CurrentHp <= 0;

        public void SetKey(int key)
        {
            Key = key;

            var data = CardTable.Instance?.Get(key);
            if (data == null) return; // CardTable.Get이 이미 LogError를 남김

            Att = data.att;
            CurrentHp = data.hp;

            _cardImage.sprite = SpriteManager.GetCard(key.ToString());
            _attText.text = Att.ToString();
            _attText.color = Color.white;
            _hpText.text = CurrentHp.ToString();
            _hpText.color = Color.white;
        }

        public void TakeDamage(int amount)
        {
            int previousHp = CurrentHp;
            CurrentHp = Mathf.Max(0, CurrentHp - amount);
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
