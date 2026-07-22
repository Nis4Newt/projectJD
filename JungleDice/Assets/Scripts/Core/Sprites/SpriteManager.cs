using UnityEngine;

namespace JungleDice.Core.Sprites
{
    public enum SpriteCategory
    {
        Card,
    }

    public static class SpriteManager
    {
        public static Sprite GetCard(string name) => Load(SpriteCategory.Card, name);

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
