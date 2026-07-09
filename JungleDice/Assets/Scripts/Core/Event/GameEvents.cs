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
}
