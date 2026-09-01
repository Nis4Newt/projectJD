using UnityEngine;

namespace JungleDice.Core.UI
{
    public abstract class UIPanel : MonoBehaviour
    {
        public virtual void Open() => gameObject.SetActive(true);
        public virtual void Close() => gameObject.SetActive(false);
    }
}
