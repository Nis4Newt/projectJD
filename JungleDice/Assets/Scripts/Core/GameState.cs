namespace JungleDice.Core
{
    public enum GameState
    {
        None,       // 초기화 전 (기본값)
        Logo,       // 앱 최초 실행 — 코어 시스템 초기화
        Login,      // 로그인 화면
        MainMenu,   // 메인 메뉴
        InGame,     // 게임 플레이 중
        Pause,      // 일시정지 (InGame에서만 진입 가능)
        GameOver,   // 게임 종료 결과 화면
    }
}
