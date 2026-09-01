using UnityEditor;

namespace JungleDice.InGame.Editor
{
    internal static class InGameLogMenu
    {
        private const string MenuPath = "Tools/InGame/Toggle InGame Log";

        [MenuItem(MenuPath)]
        private static void Toggle() => InGameLog.Enabled = !InGameLog.Enabled;

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, InGameLog.Enabled);
            return true;
        }
    }
}
