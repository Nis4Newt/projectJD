using JungleDice.Core.Sprites;
using JungleDice.Data.Table;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.InGame
{
    public class FriendCard : MonoBehaviour
    {
        [SerializeField] private Image _cardImage;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _descText;
        [SerializeField] private TextMeshProUGUI _attText;
        [SerializeField] private TextMeshProUGUI _hpText;

        public int Key { get; private set; }

        public void SetKey(int key)
        {
            Key = key;

            var data = CardTable.Instance?.Get(key);
            if (data == null) return; // CardTable.Get이 이미 LogError를 남김

            _cardImage.sprite = SpriteManager.GetCard(key.ToString());
            _nameText.text = data.cardname;
            _descText.text = data.explain;
            _attText.text = data.att.ToString();
            _hpText.text = data.hp.ToString();
        }
    }
}
