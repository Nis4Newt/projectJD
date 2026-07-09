using System;
using System.Collections.Generic;
using UnityEngine;

namespace JungleDice.Core.Event
{
    /// <summary>
    /// 타입 기반 전역 이벤트 브로커.
    /// 시스템 간 직접 참조 없이 메시지를 발행/구독한다.
    /// Unity 메인 스레드 전용.
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, List<Delegate>> _listeners = new();

        /// <summary>
        /// 이벤트 T를 구독한다.
        /// 반환된 토큰을 Dispose하면 구독이 해제된다.
        /// 토큰은 반드시 저장 후 OnDestroy 등에서 해제할 것.
        /// </summary>
        public static IDisposable Subscribe<T>(Action<T> listener)
        {
            if (listener == null)
                throw new ArgumentNullException(nameof(listener));

            var type = typeof(T);
            if (!_listeners.TryGetValue(type, out var list))
            {
                list = new List<Delegate>();
                _listeners[type] = list;
            }

            list.Add(listener);

            return new Subscription<T>(listener);
        }

        /// <summary>
        /// 이벤트 T를 발행한다.
        /// 구독자가 없으면 아무 일도 일어나지 않는다.
        /// 리스너 내부에서 예외가 발생해도 나머지 리스너는 계속 호출된다.
        /// </summary>
        public static void Publish<T>(T evt)
        {
            var type = typeof(T);
            if (!_listeners.TryGetValue(type, out var list) || list.Count == 0)
                return;

            // 발행 중 구독/해제가 일어나도 안전하도록 스냅샷 순회
            var snapshot = list.ToArray();
            foreach (var del in snapshot)
            {
                try
                {
                    ((Action<T>)del).Invoke(evt);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[EventBus] {typeof(T).Name} 리스너에서 예외 발생: {e}");
                }
            }
        }

        /// <summary>
        /// 특정 타입의 구독을 모두 해제한다.
        /// </summary>
        public static void Clear<T>()
        {
            _listeners.Remove(typeof(T));
        }

        /// <summary>
        /// 모든 구독을 해제한다.
        /// DontDestroyOnLoad 시스템의 구독도 제거되므로 신중히 호출할 것.
        /// </summary>
        public static void Clear()
        {
            _listeners.Clear();
        }

        // ─── 내부 구현 ───────────────────────────────────────────────────────

        private static void Remove<T>(Action<T> listener)
        {
            var type = typeof(T);
            if (!_listeners.TryGetValue(type, out var list))
                return;

            list.Remove(listener);

            if (list.Count == 0)
                _listeners.Remove(type);
        }

        // ─── Subscription 토큰 ───────────────────────────────────────────────

        private sealed class Subscription<T> : IDisposable
        {
            private readonly Action<T> _listener;
            private bool _disposed;

            internal Subscription(Action<T> listener)
            {
                _listener = listener;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Remove(_listener);
            }
        }
    }
}
