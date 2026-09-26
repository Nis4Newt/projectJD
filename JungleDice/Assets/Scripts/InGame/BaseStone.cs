using TMPro;
using UnityEngine;

namespace JungleDice.InGame
{
    public class BaseStone : MonoBehaviour
    {
        [SerializeField] private TextMeshPro _hpText;
        [SerializeField] private int _maxHp = 30;
        [SerializeField] private DamageEffect _damageEffectPrefab;

        public int CurrentHp { get; private set; }
        public int MaxHp => _maxHp;

        private void Awake()
        {
            CurrentHp = _maxHp;
            _hpText.text = CurrentHp.ToString();
        }

        public void TakeDamage(int amount)
        {
            DamageEffect.Spawn(_damageEffectPrefab, transform, amount);
            CurrentHp = Mathf.Max(0, CurrentHp - amount);
            _hpText.text = CurrentHp.ToString();
        }

        public void Heal(int amount)
        {
            CurrentHp = Mathf.Min(_maxHp, CurrentHp + amount);
            _hpText.text = CurrentHp.ToString();
        }
    }
}
