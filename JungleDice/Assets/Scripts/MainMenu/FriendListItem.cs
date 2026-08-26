using System;
using JungleDice.InGame;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.MainMenu
{
    public class FriendListItem : MonoBehaviour
    {
        [SerializeField] private FriendCard _friendCard;
        [SerializeField] private Button _button;

        public int Key => _friendCard.Key;
        public event Action<FriendListItem> Clicked;

        private void Awake() => _button.onClick.AddListener(() => Clicked?.Invoke(this));

        public void SetKey(int key) => _friendCard.SetKey(key);
    }
}
