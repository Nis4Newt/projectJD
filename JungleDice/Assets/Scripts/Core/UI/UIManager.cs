using System;
using System.Collections.Generic;
using UnityEngine;

namespace JungleDice.Core.UI
{
    public static class UIManager
    {
        private static readonly Dictionary<Type, MonoBehaviour> _instances = new();
        private static readonly Stack<UIPanel> _popupStack = new();
        private static Transform[] _layerRoots;

        public static T Load<T>(Transform parent, Action<T> onCreated = null) where T : MonoBehaviour
        {
            if (_instances.TryGetValue(typeof(T), out var cached) && cached != null)
                return (T)cached;

            var prefab = Resources.Load<T>($"UI/{typeof(T).Name}");
            var instance = UnityEngine.Object.Instantiate(prefab, parent);
            _instances[typeof(T)] = instance;
            onCreated?.Invoke(instance);
            return instance;
        }

        public static void Initialize(Transform[] layerRoots) => _layerRoots = layerRoots;

        public static T Show<T>(Action<T> onCreated = null) where T : UIPanel
        {
            var panel = Load<T>(_layerRoots[(int)UILayer.Popup], onCreated);
            panel.transform.SetParent(_layerRoots[(int)panel.Layer], false);
            panel.Open();
            _popupStack.Push(panel);
            return panel;
        }

        public static void HideTop()
        {
            if (_popupStack.Count == 0) return;
            _popupStack.Pop().Close();
        }

        public static void HandleBackButton()
        {
            if (_popupStack.Count > 0 && Input.GetKeyDown(KeyCode.Escape))
                HideTop();
        }
    }
}
