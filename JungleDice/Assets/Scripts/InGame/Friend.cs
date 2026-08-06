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

        public void SetKey(int key)
        {
            Key = key;

            var data = CardTable.Instance?.Get(key);
            if (data == null) return; // CardTable.Get이 이미 LogError를 남김

            _cardImage.sprite = SpriteManager.GetCard(key.ToString());
            _attText.text = data.att.ToString();
            _hpText.text = data.hp.ToString();
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
    }
}
