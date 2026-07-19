using System;
using System.Collections.Generic;
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
        [SerializeField] private List<int> _friends = new();

        public string Name => _name;
        public int Shell => _shell;
        public int Ticket => _ticket;
        public int Score => _score;
        public int Rank => _rank;
        public IReadOnlyList<int> Friends => _friends;

        public void SetName(string name) => _name = name;

        public void AddShell(int amount) => _shell = Mathf.Max(0, _shell + amount);

        public bool TrySpendShell(int amount)
        {
            if (amount <= 0 || _shell < amount) return false;
            _shell -= amount;
            return true;
        }

        public void AddTicket(int amount) => _ticket = Mathf.Max(0, _ticket + amount);

        public bool TrySpendTicket(int amount)
        {
            if (amount <= 0 || _ticket < amount) return false;
            _ticket -= amount;
            return true;
        }

        public void SetScore(int score) => _score = score;

        public void SetRank(int rank) => _rank = rank;

        public void SetFriends(IEnumerable<int> cardIds)
        {
            _friends.Clear();
            _friends.AddRange(cardIds);
        }
    }
}
