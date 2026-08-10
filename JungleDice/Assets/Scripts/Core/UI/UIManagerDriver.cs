using UnityEngine;

namespace JungleDice.Core.UI
{
    public class UIManagerDriver : MonoBehaviour
    {
        [SerializeField] private Transform[] _layerRoots; // UILayer 순서와 동일: HUD, Panel, Popup, Toast, SystemModal

        private void Awake() => UIManager.Initialize(_layerRoots);
        private void Update() => UIManager.HandleBackButton();
    }
}
