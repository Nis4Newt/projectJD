using System.Collections;
using DG.Tweening;
using JungleDice.Core;
using JungleDice.Core.Sprites;
using JungleDice.Core.User;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.InGame
{
    public class ResultPanel : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private GameObject _winFrame;
        [SerializeField] private GameObject _loseFrame;
        [SerializeField] private RectTransform _myTurnImage;
        [SerializeField] private Button _backButton;

        [SerializeField] private float _myTurnPunchScale = 1.1f;
        [SerializeField] private float _myTurnPunchDuration = 0.2f;
        [SerializeField] private float _myTurnHoldDuration = 0.5f;
        [SerializeField] private float _myTurnHideDuration = 0.2f;
        [SerializeField] private float _autoExitDelay = 20f;

        private Sequence _myTurnSequence;
        private Coroutine _autoExitRoutine;

        private void Awake()
        {
            _icon.sprite = SpriteManager.GetIcon(UserManager.Current.Icon);
            _icon.gameObject.SetActive(false);
            _myTurnImage.localScale = Vector3.zero;
            _myTurnImage.gameObject.SetActive(false);

            _winFrame.SetActive(false);
            _loseFrame.SetActive(false);
            _backButton.gameObject.SetActive(false);
            _backButton.onClick.AddListener(OnBackButtonClicked);
        }

        // 유저 PlayFriend 진입마다 호출 — 이미 재생 중이면 끊고 처음부터 다시 재생
        public void PlayMyTurnAlert()
        {
            _myTurnSequence?.Kill();
            _myTurnImage.gameObject.SetActive(true);
            _myTurnSequence = DOTween.Sequence()
                .Append(_myTurnImage.DOScale(_myTurnPunchScale, _myTurnPunchDuration))
                .Append(_myTurnImage.DOScale(1f, _myTurnPunchDuration))
                .AppendInterval(_myTurnHoldDuration)
                .Append(_myTurnImage.DOScale(0f, _myTurnHideDuration))
                .OnComplete(() => _myTurnImage.gameObject.SetActive(false));
        }

        // GameOver 전이 시 한 번만 호출
        public void ShowResult(bool userWon)
        {
            _icon.gameObject.SetActive(true);
            (userWon ? _winFrame : _loseFrame).SetActive(true);
            _backButton.gameObject.SetActive(true);
            _autoExitRoutine = StartCoroutine(AutoExitAfterDelay());
        }

        private IEnumerator AutoExitAfterDelay()
        {
            yield return new WaitForSeconds(_autoExitDelay);
            GoToMainMenu();
        }

        private void OnBackButtonClicked()
        {
            if (_autoExitRoutine != null) StopCoroutine(_autoExitRoutine);
            GoToMainMenu();
        }

        private void GoToMainMenu() => GameManager.Instance.ChangeState(GameState.MainMenu);

        private void OnDestroy() => _myTurnSequence?.Kill();
    }
}
