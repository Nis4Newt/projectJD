using System;
using JungleDice.InGame;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.MainMenu
{
    public class FriendPickPopup : MonoBehaviour
    {
        [SerializeField] private FriendCard _friendCard;
        [SerializeField] private Button _selectButton;
        [SerializeField] private Button _closeButton;

        private Action _onSelect;

        // 씬에 기본 비활성 상태로 배치된 오브젝트를 전제한다(에디터 설정) — 여기서 SetActive(false)를 호출하면
        // Awake가 최초 Show()의 SetActive(true) 도중 지연 실행되어 그 활성화를 즉시 되돌려버린다.
        private void Awake()
        {
            _selectButton.onClick.AddListener(OnSelectButtonClicked);
            _closeButton.onClick.AddListener(OnCloseButtonClicked);
        }

        public void Show(int key, Action onSelect)
        {
            _friendCard.SetKey(key);
            _onSelect = onSelect;
            gameObject.SetActive(true);
        }

        private void OnSelectButtonClicked()
        {
            gameObject.SetActive(false);
            _onSelect?.Invoke();
        }

        // 선택 없이 팝업만 닫는다 — onSelect를 호출하지 않으므로 Replace 상태로 전환되지 않는다
        private void OnCloseButtonClicked() => gameObject.SetActive(false);
    }
}
