using System;
using System.Collections.Generic;

namespace JungleDice.Core.Event
{
    /// <summary>
    /// 여러 IDisposable 구독 토큰을 하나로 묶어 일괄 해제하는 헬퍼.
    /// MonoBehaviour 멤버로 선언하고 OnDestroy에서 Dispose를 호출하면
    /// Add로 등록한 모든 구독이 한 번에 해제된다.
    /// </summary>
    /// <example>
    /// <code>
    /// private readonly CompositeDisposable _subs = new();
    ///
    /// void OnEnable()
    /// {
    ///     _subs.Add(EventBus.Subscribe&lt;PlayerGoldChanged&gt;(OnGoldChanged));
    ///     _subs.Add(EventBus.Subscribe&lt;GameStateChanged&gt;(OnStateChanged));
    /// }
    ///
    /// void OnDestroy() => _subs.Dispose();
    /// </code>
    /// </example>
    public sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _disposables = new();
        private bool _disposed;

        /// <summary>관리할 구독 토큰을 추가한다.</summary>
        public void Add(IDisposable disposable)
        {
            if (disposable == null)
                throw new ArgumentNullException(nameof(disposable));

            if (_disposed)
            {
                // 이미 해제된 상태라면 즉시 해제
                disposable.Dispose();
                return;
            }

            _disposables.Add(disposable);
        }

        /// <summary>등록된 모든 구독을 해제한다. 중복 호출은 무시된다.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var disposable in _disposables)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"[CompositeDisposable] Dispose 중 예외 발생: {e}");
                }
            }

            _disposables.Clear();
        }

        /// <summary>현재 등록된 구독 수.</summary>
        public int Count => _disposables.Count;
    }
}
