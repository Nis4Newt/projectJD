using TMPro;
using UnityEngine;

namespace JungleDice.InGame
{
    public class BaseStone : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _hpText;
        [SerializeField] private int _maxHp = 30;

        public int CurrentHp { get; private set; }

        private void Awake()
        {
            CurrentHp = _maxHp;
            _hpText.text = CurrentHp.ToString();
        }

        public void TakeDamage(int amount)
        {
            CurrentHp = Mathf.Max(0, CurrentHp - amount);
            _hpText.text = CurrentHp.ToString();
        }
    }
}
