using System;
using System.Collections.Generic;
using JungleDice.Core.Event;
using UnityEngine;

namespace JungleDice.Core.UI
{
    public static class UIManager
    {
        private static readonly Dictionary<Type, MonoBehaviour> _instances = new();
        private static readonly Stack<UIPanel> _popupStack = new();

        public static bool HasOpenPanel => _popupStack.Count > 0;

        // 씬에 이미 배치돼있는 인스턴스를 캐시에 등록한다 — 이후 Load<T>/Show<T>가 Resources.Load로 새로 만들지 않고 이 인스턴스를 재사용한다.
        public static void Register<T>(T instance) where T : MonoBehaviour => _instances[typeof(T)] = instance;

        public static T Load<T>(Transform parent, Action<T> onCreated = null) where T : MonoBehaviour
        {
            if (_instances.TryGetValue(typeof(T), out var cached))
            {
                if (cached != null) return (T)cached;
                _instances.Remove(typeof(T)); // 씬 전환 등으로 파괴된 참조 — 새로 채운다
            }

            var prefab = Resources.Load<T>($"UI/{typeof(T).Name}");
            var instance = UnityEngine.Object.Instantiate(prefab, parent);
            _instances[typeof(T)] = instance;
            onCreated?.Invoke(instance);
            return instance;
        }

        public static T Show<T>(Transform parent, Action<T> onCreated = null) where T : UIPanel
        {
            var panel = Load<T>(parent, onCreated);
            panel.Open();
            _popupStack.Push(panel);
            EventBus.Publish(new PopupStackChanged(HasOpenPanel));
            return panel;
        }

        public static void HideTop()
        {
            if (_popupStack.Count == 0) return;
            _popupStack.Pop().Close();
            EventBus.Publish(new PopupStackChanged(HasOpenPanel));
        }

        public static void HideAll()
        {
            if (_popupStack.Count == 0) return;

            while (_popupStack.Count > 0)
                _popupStack.Pop().Close();
            EventBus.Publish(new PopupStackChanged(HasOpenPanel));
        }

        public static void HandleBackButton()
        {
            if (_popupStack.Count > 0 && Input.GetKeyDown(KeyCode.Escape))
                HideTop();
        }

        // 씬 전환 시 이전 씬 소속 패널이 파괴되므로, Close() 호출 없이 스택만 비운다 — _instances 캐시는 건드리지 않는다(Load<T>가 파괴 여부를 그때그때 판단).
        public static void ClearStack() => _popupStack.Clear();
    }
}
