using UnityEngine;
using Project8.Core;

namespace Project8.Visualization
{
    /// <summary>
    /// A single flexible "bedsheet" of spacetime - one translucent membrane instead
    /// of the old multi-layer lattice. Masses press smooth dents into it (black
    /// holes deep and narrow, Earth shallow and wide), and the gravitational wave
    /// ripples visibly across the cloth as it propagates.
    ///
    /// One mesh, ~10k verts, CPU-displaced once per frame - far cheaper than the
    /// 48k-node lattice plus particle system it replaces, and reads as a soft
    /// fabric surface rather than a wireframe grid.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SpacetimeSheet : MonoBehaviour
    {
        /// <summary>A mass that presses the sheet downward.</summary>
        [System.Serializable]
        public struct GravityWell
        {
            public Transform source;
            [Tooltip("How deep the dent is, in metres.")]
            public float depth;
            [Tooltip("Gaussian width of the dent - larger spreads it wider.")]
            public float radius;
        }

        public GravitationalWaveModel model;
        public SimulationController simulation;
        public GravityWell[] wells;
        public Material sheetMaterial;

        [Header("Sheet geometry")]
        public Vector2 size = new Vector2(160f, 78f);
        [Range(16, 240)] public int countX = 150;
        [Range(16, 120)] public int countZ = 74;
        [Tooltip("In metres below the simulation plane; the ship flies above the sheet.")]
        public float baseHeight = -1.4f;
        [Tooltip("How soft the fabric is: larger values make dips wider and shallower-edged.")]
        public float flexibility = 1f;

        Mesh _mesh;
        Vector3[] _rest;
        Vector3[] _verts;
        Color[] _colors;

        static readonly Color ClothTint = new Color(0.30f, 0.65f, 0.85f, 0.28f);
        static readonly Color StretchTint = new Color(1f, 0.72f, 0.30f, 0.85f);
        static readonly Color SqueezeTint = new Color(0.35f, 0.80f, 1f, 0.85f);

        void Start()
        {
            int nx = countX + 1, nz = countZ + 1;
            _rest = new Vector3[nx * nz];
            _verts = new Vector3[nx * nz];
            _colors = new Color[nx * nz];

            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
                _rest[z * nx + x] = transform.position + new Vector3(
                    (x / (float)countX - 0.5f) * size.x,
                    baseHeight,
                    (z / (float)countZ - 0.5f) * size.y);

            int[] tris = new int[countX * countZ * 6];
            int t = 0;
            for (int z = 0; z < countZ; z++)
            for (int x = 0; x < countX; x++)
            {
                int i = z * nx + x;
                tris[t++] = i; tris[t++] = i + nx; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + nx; tris[t++] = i + nx + 1;
            }

            _mesh = new Mesh { name = "SpacetimeSheet",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.colors = _colors;
            _mesh.triangles = tris;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            GetComponent<MeshRenderer>().sharedMaterial = sheetMaterial;
        }

        void LateUpdate()
        {
            if (_mesh == null || model == null || simulation == null) return;
            float t = simulation.SimulationTime;
            float front = model.WavefrontDistance(t);
            Vector3 dir = model.PropagationDirection;
            int nx = countX + 1;
            float halfX = size.x * 0.5f, halfZ = size.y * 0.5f;
            Vector3 centre = transform.position;

            for (int i = 0; i < _rest.Length; i++)
            {
                Vector3 rest = _rest[i];
                Vector3 p = rest;

                // Cloth dents: gaussian wells pull down and slightly inward.
                if (wells != null)
                    foreach (var well in wells)
                    {
                        var src = well.source;
                        if (src == null || !src.gameObject.activeInHierarchy) continue;
                        Vector2 d = new Vector2(src.position.x - rest.x, src.position.z - rest.z);
                        float s = well.radius * flexibility;
                        float g = well.depth * Mathf.Exp(-d.sqrMagnitude / (2f * s * s));
                        if (g < 0.001f) continue;
                        p.y -= g;
                        p.x += d.x * g * 0.06f;
                        p.z += d.y * g * 0.06f;
                    }

                // The wave displaces the fabric itself (transverse ripple).
                p += model.DisplacementAt(rest, t);

                // Colour: quiet cloth tinted pale blue; near the wavefront the
                // crest paints orange (stretch) / blue (squeeze); dents catch a
                // subtle extra glow so the pressed areas read as depth.
                float distanceFromFront = front - Vector3.Dot(rest - model.Origin, dir);
                float pulse = Mathf.Exp(-0.5f * distanceFromFront * distanceFromFront / 4f);
                float h = model.StrainAt(rest, t);
                Color tint = h >= 0f ? StretchTint : SqueezeTint;
                float dipGlow = Mathf.Clamp01((rest.y - p.y) * 0.14f);
                Color c = Color.Lerp(ClothTint, tint, Mathf.Clamp01(pulse + dipGlow * 0.4f));

                // Edge fade: the sheet dissolves into the dark instead of a hard border.
                float edge = Mathf.Clamp01((halfX - Mathf.Abs(rest.x - centre.x)) / 6f) *
                             Mathf.Clamp01((halfZ - Mathf.Abs(rest.z - centre.z)) / 6f);
                c.a *= edge;

                _verts[i] = p;
                _colors[i] = c;
            }
            _mesh.vertices = _verts;
            _mesh.colors = _colors;
            _mesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
