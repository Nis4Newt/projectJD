using UnityEngine;

namespace JungleDice.Core.Sprites
{
    public enum SpriteCategory
    {
        // 카테고리 확정 시 여기에 값 추가 (예: Card) — 값 이름이 곧 Resources/Sprite/ 하위 폴더명이 된다
    }

    public static class SpriteManager
    {
        // 카테고리 폴더가 확정되는 대로 여기에 전용 조회 메서드를 추가한다.
        // 예: public static Sprite GetCard(string name) => Load(SpriteCategory.Card, name);

        private static Sprite Load(SpriteCategory category, string name)
        {
            var folder = $"Sprite/{category}";
            var sprite = Resources.Load<Sprite>($"{folder}/{name}");
            if (sprite == null)
                Debug.LogWarning($"[SpriteManager] Sprite not found: {folder}/{name}");
            return sprite;
        }
    }
}
