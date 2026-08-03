using System.Collections.Generic;
using UnityEngine;

namespace JungleDice.InGame
{
    public static class DeckBuilder
    {
        private const int CopiesPerFriend = 10;

        public static List<int> Build(IReadOnlyList<int> friendKeys)
        {
            var deck = new List<int>(friendKeys.Count * CopiesPerFriend);
            foreach (var key in friendKeys)
            {
                for (int i = 0; i < CopiesPerFriend; i++)
                    deck.Add(key);
            }
            Shuffle(deck);
            return deck;
        }

        private static void Shuffle(List<int> deck)
        {
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
        }
    }
}
