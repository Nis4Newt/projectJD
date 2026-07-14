using UnityEngine;

namespace JungleDice.Core
{
    public abstract class SceneSingleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        public static T Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this as T)
            {
                Debug.LogWarning($"[SceneSingleton] 씬 내 중복 {typeof(T).Name} 감지, 파괴: {gameObject.name}");
                Destroy(gameObject);
                return;
            }

            Instance = this as T;
            OnAwake();
        }

        protected virtual void OnAwake() { }

        protected virtual void OnDestroy()
        {
            if (Instance == this as T)
                Instance = null;
        }
    }
}
