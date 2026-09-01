using JungleDice.Core.UI;

namespace JungleDice.MainMenu
{
    public class StorePanel : UIPanel
    {
        private void Awake()
        {
            gameObject.SetActive(false); // 프리팹 기본 상태 = 닫힘(OptionPanel/FriendPanel과 동일한 방어적 관례)
            UIManager.Register(this); // 씬에 이미 배치된 이 인스턴스를 캐시에 등록 — Show<StorePanel>()가 재사용
        }
    }
}
