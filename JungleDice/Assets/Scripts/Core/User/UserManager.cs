namespace JungleDice.Core.User
{
    public static class UserManager
    {
        private static UserData _current;

        public static UserData Current => _current ??= CreateDefault();

        public static void Load()
        {
            // SaveSystem/서버 연동 전까지는 기본값으로 초기화.
            // 이후 로컬 세이브 또는 서버 응답으로 채우도록 이 메서드 내부만 교체하면 됨.
            _current = CreateDefault();
        }

        private static UserData CreateDefault() => new UserData();
    }
}
