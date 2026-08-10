using UnityEngine;

namespace JungleDice.Core.UI
{
    public abstract class UIPanel : MonoBehaviour
    {
        public virtual UILayer Layer => UILayer.Popup;

        public virtual void Open() => gameObject.SetActive(true);
        public virtual void Close() => gameObject.SetActive(false);
    }
}
