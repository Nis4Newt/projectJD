using JungleDice.Core;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.Core.Settings
{
    public enum OptionPanelMode
    {
        MainMenu,
        InGame,
    }

    public class OptionPanel : MonoBehaviour
    {
        [SerializeField] private Slider _bgmSlider;
        [SerializeField] private Slider _sfxSlider;
        [SerializeField] private Toggle _vibrationToggle;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
        [SerializeField] private Button _quitButton;
        [SerializeField] private Button _surrenderButton;

        private bool _pausesTimeScale;

        private void Awake()
        {
            gameObject.SetActive(false);
            OptionManager.BindVolumeSliders(_bgmSlider, _sfxSlider);
            OptionManager.BindVibrationToggle(_vibrationToggle);
            _closeButton.onClick.AddListener(OnCloseButtonClicked);
            _dimButton.onClick.AddListener(OnCloseButtonClicked);
            _quitButton.onClick.AddListener(OnQuitButtonClicked);
            _surrenderButton.onClick.AddListener(OnSurrenderButtonClicked);
        }

        // UIManager.Load<OptionPanel>()의 onCreated로 1회 호출 — 부가 버튼 표시와 일시정지 여부를 씬에 맞게 확정한다.
        public void Configure(OptionPanelMode mode)
        {
            _pausesTimeScale = mode == OptionPanelMode.InGame;
            _quitButton.gameObject.SetActive(mode == OptionPanelMode.MainMenu);
            _surrenderButton.gameObject.SetActive(mode == OptionPanelMode.InGame);
        }

        public void Show()
        {
            OptionManager.SyncVolumeSliders(_bgmSlider, _sfxSlider);
            OptionManager.SyncVibrationToggle(_vibrationToggle);
            gameObject.SetActive(true);
            if (_pausesTimeScale) Time.timeScale = 0f;
        }

        private void OnCloseButtonClicked()
        {
            OptionManager.CommitVolumeSliders(_bgmSlider, _sfxSlider);
            gameObject.SetActive(false);
            if (_pausesTimeScale) Time.timeScale = 1f;
        }

        private void OnQuitButtonClicked()
        {
            OptionManager.CommitVolumeSliders(_bgmSlider, _sfxSlider);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnSurrenderButtonClicked()
        {
            OptionManager.CommitVolumeSliders(_bgmSlider, _sfxSlider);
            if (_pausesTimeScale) Time.timeScale = 1f;
            GameManager.Instance.ChangeState(GameState.MainMenu);
        }
    }
}
