using UnityEngine;

namespace JungleDice.InGame
{
    public class HandSlot : MonoBehaviour
    {
        [SerializeField] private int _index; // hand 내 순서(왼쪽→오른쪽), 0~3

        public int Index => _index;
        public bool IsOccupied => transform.childCount > 0;
    }
}
