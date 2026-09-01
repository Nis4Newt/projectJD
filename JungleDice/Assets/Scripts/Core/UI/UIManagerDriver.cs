using JungleDice.Core.Event;
using UnityEngine;

namespace JungleDice.Core.UI
{
    public class UIManagerDriver : MonoBehaviour
    {
        private void Awake() => EventBus.Subscribe<SceneLoadRequested>(_ => UIManager.ClearStack());
        private void Update() => UIManager.HandleBackButton();
    }
}
