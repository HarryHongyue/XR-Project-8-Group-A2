using UnityEngine;
using Project8.Visualization;

namespace Project8.Visualization
{
    /// <summary>
    /// Draws straight links between orthogonal neighbours of a grid-layout TestMassField,
    /// turning the masses into a visible "net" that stretches and squeezes as the
    /// gravitational wave passes through it.
    /// </summary>
    public class LatticeNet : MonoBehaviour
    {
        public TestMassField field;
        public Material lineMaterial;
        [Range(0.002f, 0.05f)] public float lineWidth = 0.008f;
        public Color lineColor = new Color(0.35f, 0.85f, 1f, 0.55f);

        LineRenderer[] _links;
        int[] _a;
        int[] _b;
        bool _built;

        void LateUpdate()
        {
            if (!_built)
            {
                if (field == null || field.Masses == null || field.Masses.Count == 0) return;
                BuildLinks();
                _built = true;
            }

            var masses = field.Masses;
            for (int i = 0; i < _links.Length; i++)
            {
                var lr = _links[i];
                lr.SetPosition(0, masses[_a[i]].transform.position);
                lr.SetPosition(1, masses[_b[i]].transform.position);
            }
        }

        void BuildLinks()
        {
            int n = field.gridSize;
            int count = 2 * n * (n - 1);
            _links = new LineRenderer[count];
            _a = new int[count];
            _b = new int[count];

            int k = 0;
            // Rebuild() spawns masses row-major: index = ix * n + iy
            for (int ix = 0; ix < n; ix++)
            {
                for (int iy = 0; iy < n; iy++)
                {
                    if (ix + 1 < n) k = MakeLink(k, ix * n + iy, (ix + 1) * n + iy);
                    if (iy + 1 < n) k = MakeLink(k, ix * n + iy, ix * n + iy + 1);
                }
            }
        }

        int MakeLink(int k, int ia, int ib)
        {
            var go = new GameObject("NetLink_" + k);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.startWidth = lr.endWidth = lineWidth;
            lr.startColor = lr.endColor = lineColor;
            lr.material = lineMaterial != null ? lineMaterial : new Material(Shader.Find("Sprites/Default"));
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _links[k] = lr;
            _a[k] = ia;
            _b[k] = ib;
            return k + 1;
        }
    }
}
