using JungleDice.Core;

namespace JungleDice.Core.Event
{
    // 각 시스템 구현 시 관련 이벤트를 여기에 추가한다.

    // 게임 상태
    public record GameStateChanged(GameState Previous, GameState Next);

    // 앱 생명주기
    public record AppPauseChanged(bool IsPaused);
    public record AppFocusChanged(bool HasFocus);
    public record AppQuitRequested();

    // 씬 시스템
    public record SceneLoadRequested(string SceneName);
    public record SceneLoadCompleted(string SceneName);

    // 씬 매니저
    public record LogoSceneReady();
    public record LoginSceneReady();

    // MainMenu 씬 — 게임 시작 요청 (게임 타입은 발행 전 GameSession에 이미 기록됨)
    public record MainMenuPlayRequested();

    // Login 씬 — task 진행률
    public record LoginProgressChanged(int Completed, int Total, string TaskName);

    // Login 씬 — Google 로그인 (plan-loginscene-googleauth.md에서 확정 예정, 시그니처만 선반영)
    public record GoogleLoginSucceeded();

    // User 시스템 — 유저 데이터 변경 알림 (plan-mainmenuscene-userdata-hud.md)
    public record UserDataChanged();
}
