namespace JungleDice.Core
{
    public static class GameSession
    {
        public static GameType CurrentGameType { get; private set; }

        public static void SetGameType(GameType type)
        {
            CurrentGameType = type;
        }
    }
}
