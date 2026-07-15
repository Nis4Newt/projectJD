using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using JungleDice.Core.Event;

namespace JungleDice.Login
{
    public class LoginTapToContinueUI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _tapCanvasGroup; // 표시/숨김 + 깜빡임 겸용
        [SerializeField] private Button _tapButton;
        [SerializeField] private float _blinkInterval = 0.5f;
        [SerializeField] private float _dimAlpha = 0.3f;

        private readonly CompositeDisposable _subs = new();
        private bool _hasTapped;
        private Coroutine _blinkRoutine;

        private void Awake()
        {
            SetVisible(false);
            _tapButton.onClick.AddListener(OnTapButtonClicked);
            _subs.Add(EventBus.Subscribe<GoogleLoginSucceeded>(OnGoogleLoginSucceeded));
        }

        private void OnGoogleLoginSucceeded(GoogleLoginSucceeded e)
        {
            SetVisible(true);
            _blinkRoutine = StartCoroutine(BlinkRoutine());
        }

        private void SetVisible(bool visible)
        {
            _tapCanvasGroup.alpha = visible ? 1f : 0f;
            _tapCanvasGroup.interactable = visible;
            _tapCanvasGroup.blocksRaycasts = visible;
        }

        private IEnumerator BlinkRoutine()
        {
            while (true)
            {
                _tapCanvasGroup.alpha = _dimAlpha;
                yield return new WaitForSeconds(_blinkInterval);
                _tapCanvasGroup.alpha = 1f;
                yield return new WaitForSeconds(_blinkInterval);
            }
        }

        private void OnTapButtonClicked()
        {
            if (_hasTapped) return; // 중복 탭 방지
            _hasTapped = true;

            if (_blinkRoutine != null)
            {
                StopCoroutine(_blinkRoutine);
                _blinkRoutine = null;
            }
            _tapCanvasGroup.alpha = 1f;

            _tapButton.interactable = false;
            EventBus.Publish(new LoginSceneReady());
        }

        private void OnDestroy()
        {
            _subs.Dispose();
        }
    }
}
