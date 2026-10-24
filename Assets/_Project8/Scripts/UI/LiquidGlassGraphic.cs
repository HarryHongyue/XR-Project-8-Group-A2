using UnityEngine;
using UnityEngine.UI;

namespace Project8.UI
{
    /// <summary>
    /// Minimal Graphic that draws a single quad and feeds the rect's PIXEL size
    /// to the Project8/UI/LiquidGlass shader through TEXCOORD1, so the shader's
    /// rounded-rect SDF keeps a constant corner radius for any widget size -
    /// one material role serves every panel, button and card. Requires the
    /// canvas to have TexCoord1 in additionalShaderChannels (the builder sets
    /// this on every cockpit canvas).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class LiquidGlassGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 size = r.size;
            Vector4 dims = new Vector4(size.x, size.y, 0f, 0f);

            // BL, TL, TR, BR - same winding as the built-in UI quad.
            Vector3[] p =
            {
                new Vector3(r.xMin, r.yMin),
                new Vector3(r.xMin, r.yMax),
                new Vector3(r.xMax, r.yMax),
                new Vector3(r.xMax, r.yMin),
            };
            Vector2[] u =
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 0f),
            };

            var v = UIVertex.simpleVert;
            v.color = color;
            for (int i = 0; i < 4; i++)
            {
                v.position = p[i];
                v.uv0 = u[i];
                v.uv1 = dims;
                vh.AddVert(v);
            }
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }
}
