using System;
using System.Collections.Generic;

namespace JungleDice.MainMenu
{
    // 친구탭 슬롯(FriendSlot[])과 모험탭 덱 미리보기(Friend[])가 공유하는 "Friends → SetKey" 갱신 로직
    internal static class FriendDeckDisplay
    {
        public static void Apply<T>(T[] targets, IReadOnlyList<int> friends, Action<T, int> setKey)
        {
            for (int i = 0; i < targets.Length; i++)
                setKey(targets[i], friends[i]);
        }
    }
}
