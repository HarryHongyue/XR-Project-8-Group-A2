using UnityEngine;
using UnityEngine.UI;

namespace Project8.UI
{
    /// <summary>Unity 6 no longer guarantees the old built-in UI/Skin/UISprite
    /// resource. This tiny runtime 9-slice gives panels, scrollbars and spotlight
    /// rings a reliable white sprite on every target, including Quest.</summary>
    public static class RuntimeUiSprites
    {
        static Sprite _roundedWhite;
        static Sprite _thinOutline;

        public static Sprite RoundedWhite => _roundedWhite != null
            ? _roundedWhite : _roundedWhite = CreateRounded(7f, 10f, "RoundedWhite_Runtime");

        public static Sprite ThinOutline => _thinOutline != null
            ? _thinOutline : _thinOutline = CreateRounded(5f, 3.5f, "ThinOutline_Runtime");

        static Sprite CreateRounded(float radius, float slice, string spriteName)
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = spriteName + "_Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(radius + 0.5f - distance);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            tex.Apply(false, false);
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect,
                new Vector4(slice, slice, slice, slice));
            sprite.name = spriteName;
            return sprite;
        }
    }

    /// <summary>
    /// Repairs cockpit UI in already-saved scenes at runtime: remove the offset
    /// duplicate glass-shadow plate and apply one readable button palette to
    /// every button, including inactive drawer pages.
    /// </summary>
    public static class RuntimeCockpitTheme
    {
        public static void Apply(Transform root)
        {
            if (root == null) return;

            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.name == "GlassShadow")
                    graphic.enabled = false;
            }

            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                var style = button.GetComponent<ReadableButtonStyle>();
                if (style == null) style = button.gameObject.AddComponent<ReadableButtonStyle>();
                style.Apply();
            }
        }
    }

    /// <summary>
    /// Keeps button labels white while the glass face shifts subtly between
    /// dark normal, slightly brighter hover, and a clear cyan press state.
    /// Unlike the old white-flash palette, text and face never become the same
    /// value and stay legible on Quest's high-glare display.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ReadableButtonStyle : MonoBehaviour
    {
        static readonly Color Normal = Color.white;
        static readonly Color Hover = new Color(0.86f, 0.95f, 1f, 1f);
        static readonly Color Pressed = new Color(0.68f, 0.88f, 1f, 1f);
        static readonly Color Disabled = new Color(0.62f, 0.68f, 0.76f, 0.72f);

        public void Apply()
        {
            var button = GetComponent<Button>();
            if (button == null) return;
            var colors = button.colors;
            colors.normalColor = Normal;
            colors.highlightedColor = Hover;
            colors.pressedColor = Pressed;
            colors.selectedColor = Hover;
            colors.disabledColor = Disabled;
            colors.colorMultiplier = 1f;
            button.colors = colors;

            foreach (var label in GetComponentsInChildren<Text>(true))
            {
                label.color = Color.white;
                label.raycastTarget = false;
            }

            var hover = GetComponent<ButtonLabelHover>();
            if (hover != null)
            {
                hover.labelNormal = Color.white;
                hover.labelHot = Color.white;
            }
        }
    }
}
