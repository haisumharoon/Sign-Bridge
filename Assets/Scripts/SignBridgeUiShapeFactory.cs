using System.Collections.Generic;
using UnityEngine;

public static class SignBridgeUiShapeFactory
{
    private static readonly Dictionary<string, Sprite> RoundedSpriteCache = new Dictionary<string, Sprite>();

    public static Sprite GetRoundedRectSprite(int width, int height, int radius)
    {
        string key = width + "x" + height + "r" + radius;
        if (RoundedSpriteCache.TryGetValue(key, out Sprite cached) && cached != null)
        {
            return cached;
        }

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "RoundedRect_" + key,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color fill = Color.white;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool inside = IsInsideRoundedRect(x + 0.5f, y + 0.5f, width, height, radius);
                texture.SetPixel(x, y, new Color(fill.r, fill.g, fill.b, inside ? 1f : 0f));
            }
        }

        texture.Apply(false, false);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
        RoundedSpriteCache[key] = sprite;
        return sprite;
    }

    private static bool IsInsideRoundedRect(float x, float y, int width, int height, int radius)
    {
        float left = radius;
        float right = width - radius;
        float bottom = radius;
        float top = height - radius;

        if ((x >= left && x <= right) || (y >= bottom && y <= top))
        {
            return true;
        }

        float cx = x < left ? radius : right;
        float cy = y < bottom ? radius : top;
        float dx = x - cx;
        float dy = y - cy;
        return dx * dx + dy * dy <= radius * radius;
    }
}