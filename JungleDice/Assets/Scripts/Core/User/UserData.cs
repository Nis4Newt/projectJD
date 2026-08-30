using System;
using System.Collections.Generic;
using System.Linq;
using JungleDice.Core.Event;
using UnityEngine;

namespace JungleDice.Core.User
{
    [Serializable]
    public class UserData
    {
        [SerializeField] private string _name = "";
        [SerializeField] private int _shell;
        [SerializeField] private int _ticket;
        [SerializeField] private int _score;
        [SerializeField] private int _rank;
        [SerializeField] private List<int[]> _decks = new()
        {
            new[] { 1001, 1012, 1003 },
            new[] { 1004, 1005, 1006 },
            new[] { 1007, 1008, 1009 },
        };
        [SerializeField] private int _currentDeckIndex;
        [SerializeField] private string _icon = "";
        [SerializeField] private int _nextStage = 1;

        public string Name => _name;
        public int Shell => _shell;
        public int Ticket => _ticket;
        public int Score => _score;
        public int Rank => _rank;
        public IReadOnlyList<int> Friends => _decks[_currentDeckIndex];
        public int CurrentDeckIndex => _currentDeckIndex;
        public int DeckCount => _decks.Count;
        public string Icon => _icon;
        public int NextStage => _nextStage;

        public void SetName(string name)
        {
            _name = name;
            EventBus.Publish(new UserDataChanged());
        }

        public void AddShell(int amount)
        {
            _shell = Mathf.Max(0, _shell + amount);
            EventBus.Publish(new UserDataChanged());
        }

        public bool TrySpendShell(int amount)
        {
            if (amount <= 0 || _shell < amount) return false;
            _shell -= amount;
            EventBus.Publish(new UserDataChanged());
            return true;
        }

        public void AddTicket(int amount)
        {
            _ticket = Mathf.Max(0, _ticket + amount);
            EventBus.Publish(new UserDataChanged());
        }

        public bool TrySpendTicket(int amount)
        {
            if (amount <= 0 || _ticket < amount) return false;
            _ticket -= amount;
            EventBus.Publish(new UserDataChanged());
            return true;
        }

        public void SetScore(int score)
        {
            _score = score;
            EventBus.Publish(new UserDataChanged());
        }

        public void SetRank(int rank)
        {
            _rank = rank;
            EventBus.Publish(new UserDataChanged());
        }

        public void SetFriends(IEnumerable<int> cardIds)
        {
            _decks[_currentDeckIndex] = cardIds.ToArray();
            EventBus.Publish(new UserDataChanged());
        }

        public void SelectDeck(int index)
        {
            if (index < 0 || index >= _decks.Count || index == _currentDeckIndex) return;
            _currentDeckIndex = index;
            EventBus.Publish(new UserDataChanged());
        }

        public void SetIcon(string icon)
        {
            _icon = icon;
            EventBus.Publish(new UserDataChanged());
        }

        public void SetNextStage(int stage)
        {
            _nextStage = stage;
            EventBus.Publish(new UserDataChanged());
        }
    }
}
