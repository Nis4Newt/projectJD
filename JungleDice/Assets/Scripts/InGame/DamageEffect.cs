using DG.Tweening;
using TMPro;
using UnityEngine;

namespace JungleDice.InGame
{
    public class DamageEffect : MonoBehaviour
    {
        [SerializeField] private TextMeshPro _valueText;
        [SerializeField] private float _popDuration = 0.25f;
        [SerializeField] private float _holdDuration = 0.5f;
        [SerializeField] private float _localDepth = -1.5f; // WorldFriend는 카드 스프라이트(Image 자식)가 루트보다 z -1 더 앞이라, 그보다 앞에 그려지도록 고정

        public static void Spawn(DamageEffect prefab, Transform parent, int amount)
        {
            Instantiate(prefab, parent).Show(amount);
        }

        private void Show(int amount)
        {
            var localPosition = transform.localPosition;
            localPosition.z = _localDepth; // 프리팹에 저장된 z 값에 기대지 않고, 부모(피격 대상)보다 항상 앞에 그려지도록 명시적으로 고정
            transform.localPosition = localPosition;

            _valueText.text = $"-{amount}";
            var targetScale = transform.localScale; // 프리팹에 세팅된 원래 크기 — Vector3.one으로 고정하지 않는다
            transform.localScale = Vector3.zero;
            transform.DOScale(targetScale, _popDuration).SetEase(Ease.OutBack);
            Destroy(gameObject, _popDuration + _holdDuration);
        }
    }
}
