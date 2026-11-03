using UnityEngine;
using UnityEngine.UI;

namespace Project8.UI
{
    /// <summary>
    /// Creates Rick's tiny face icon at runtime. A Sprite.Create() texture made
    /// by an editor-only scene builder is not a persistent Unity asset, so the
    /// saved scene had an Image with a null sprite after reopening/rebuilding.
    /// Generating it in Awake makes the avatar visible in editor play, desktop
    /// builds and Quest without any external image dependency.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class RickFaceIcon : MonoBehaviour
    {
        Texture2D _texture;
        Sprite _sprite;
        bool _ownsTexture;
        bool _ownsSprite;

        void Awake() => Build();

        void Build()
        {
            var image = GetComponent<Image>();
            if (image == null) return;

            var importedSprite = Resources.Load<Sprite>("RickFace");
            if (importedSprite != null)
            {
                _sprite = importedSprite;
                image.sprite = importedSprite;
                image.color = Color.white;
                image.preserveAspect = true;
                image.raycastTarget = false;
                return;
            }

            _texture = Resources.Load<Texture2D>("RickFace");
            int size;
            if (_texture != null)
            {
                size = Mathf.Min(_texture.width, _texture.height);
            }
            else
            {
                size = 96;
                _texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "RickFace_Runtime",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _ownsTexture = true;
                DrawFallbackFace(size);
                _texture.Apply(false, false);
            }

            _sprite = Sprite.Create(_texture,
                new Rect(0f, 0f, _texture.width, _texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            _sprite.name = "RickFace_Runtime";
            _ownsSprite = true;
            image.sprite = _sprite;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        void DrawFallbackFace(int size)
        {
            Color clear = new Color(0f, 0f, 0f, 0f);
            Color skin = new Color(0.92f, 0.77f, 0.65f, 1f);
            Color hair = new Color(0.27f, 0.72f, 0.91f, 1f);
            Color dark = new Color(0.12f, 0.10f, 0.12f, 1f);
            float scale = size / 96f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = x / scale, py = y / scale;
                float nx = (px - 48f) / 36f;
                float ny = (py - 43f) / 43f;
                bool head = nx * nx + ny * ny < 1f;
                Color c = head ? skin : clear;

                // Uneven spikes across the crown, with a broad forehead fringe.
                int spike = Mathf.Abs(((int)px - 8) % 16);
                int crown = 76 + (spike < 8 ? spike : 16 - spike);
                if (head && py > 57 || py >= crown && py < 88 && px > 7 && px < 89)
                    c = hair;

                // Eyebrow and large expressive eyes.
                if (py >= 49 && py <= 54 && px >= 24 && px <= 72)
                    c = dark;
                DrawEllipse(ref c, px, py, 32f, 43f, 8f, 7f, Color.white);
                DrawEllipse(ref c, px, py, 63f, 43f, 8f, 7f, Color.white);
                DrawEllipse(ref c, px, py, 33f, 43f, 3f, 4f, dark);
                DrawEllipse(ref c, px, py, 64f, 43f, 3f, 4f, dark);

                // Nose, worried mouth and the signature blue drool.
                if (py >= 32 && py <= 38 && px >= 46 && px <= 50)
                    c = new Color(0.75f, 0.48f, 0.39f, 1f);
                if (py >= 22 && py <= 25 && px >= 38 && px <= 57)
                    c = dark;
                if (px >= 48 && px <= 51 && py >= 12 && py <= 21)
                    c = new Color(0.38f, 0.84f, 1f, 1f);

                _texture.SetPixel(x, y, c);
            }
        }

        static void DrawEllipse(ref Color pixel, float x, float y,
            float cx, float cy, float rx, float ry, Color color)
        {
            float dx = (x - cx) / rx;
            float dy = (y - cy) / ry;
            if (dx * dx + dy * dy <= 1f) pixel = color;
        }

        void OnDestroy()
        {
            if (_ownsSprite && _sprite != null) Destroy(_sprite);
            if (_ownsTexture && _texture != null) Destroy(_texture);
        }
    }
}
