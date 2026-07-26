using JungleDice.Core.Event;
using JungleDice.Core.User;
using TMPro;
using UnityEngine;

namespace JungleDice.MainMenu
{
    public class MainMenuHudView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _nicknameText;
        [SerializeField] private TextMeshProUGUI _rankText;
        [SerializeField] private TextMeshProUGUI _scoreText;
        [SerializeField] private TextMeshProUGUI _shellText;
        [SerializeField] private TextMeshProUGUI _ticketText;

        private readonly CompositeDisposable _subs = new();

        private void Awake()
        {
            BindUserData();
            _subs.Add(EventBus.Subscribe<UserDataChanged>(_ => BindUserData()));
        }

        private void BindUserData()
        {
            var data = UserManager.Current;

            _nicknameText.text = data.Name;
            _rankText.text = data.Rank + "랭크";
            _scoreText.text = data.Score + "점";
            _shellText.text = data.Shell.ToString();
            _ticketText.text = data.Ticket.ToString();
        }

        private void OnDestroy()
        {
            _subs.Dispose();
        }
    }
}
