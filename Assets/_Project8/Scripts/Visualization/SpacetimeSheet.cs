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
    /// This is an EMBEDDING ANALOGY for teaching: a 2D surface sagging in a 3rd
    /// dimension stands in for curved 4D spacetime. It is not a material surface
    /// and not a solution of Einstein's equations - the label near the sheet says
    /// the same to the viewer.
    ///
    /// One mesh, ~10k verts, CPU-displaced once per frame. All mesh vertices are
    /// strictly LOCAL space: the rest grid is built around the transform origin
    /// and gravity-well world positions are converted to local coordinates each
    /// frame, so the sheet stays correct under any translated/rotated transform.
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

        struct CachedWell
        {
            public Vector2 centre; // sheet-local XZ
            public float depth;
            public float sigma;
        }

        public GravitationalWaveModel model;
        public SimulationController simulation;
        public GravityWell[] wells;
        public Material sheetMaterial;

        [Header("Sheet geometry")]
        public Vector2 size = new Vector2(160f, 78f);
        [Range(16, 240)] public int countX = 150;
        [Range(16, 120)] public int countZ = 74;
        [Tooltip("Sheet-local height of the flat rest surface.")]
        public float baseHeight = 1.5f;
        [Tooltip("How soft the fabric is: larger values make dips wider and shallower-edged.")]
        public float flexibility = 1f;

        Mesh _mesh;
        Vector3[] _rest;
        Vector3[] _verts;
        Color[] _colors;
        CachedWell[] _wellCache;

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
                _rest[z * nx + x] = new Vector3(
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

        /// <summary>Recompute the sheet-local centres of the live wells. Called once
        /// per frame (not per vertex) so orbiting holes and the post-merger active
        /// remnant leave moving dents; inactive sources are skipped entirely.</summary>
        void RefreshWellCache()
        {
            int n = wells != null ? wells.Length : 0;
            if (_wellCache == null || _wellCache.Length != n)
                _wellCache = new CachedWell[n];
            _wellCount = 0;
            for (int i = 0; i < n; i++)
            {
                var src = wells[i].source;
                if (src == null || !src.gameObject.activeInHierarchy) continue;
                Vector3 local = transform.InverseTransformPoint(src.position);
                _wellCache[_wellCount++] = new CachedWell
                {
                    centre = new Vector2(local.x, local.z),
                    depth = wells[i].depth,
                    sigma = Mathf.Max(wells[i].radius * flexibility, 1e-3f)
                };
            }
        }
        int _wellCount;

        /// <summary>Signed wave strain under a rest vertex: evaluated in world space
        /// on the source plane (the vertex's world position flattened to the wave
        /// origin's height) so radial propagation draws concentric ripples.</summary>
        float WaveStrainAt(Vector3 restLocal, float simTime)
        {
            if (model == null || model.parameters == null) return 0f;
            Vector3 worldRest = transform.TransformPoint(restLocal);
            worldRest.y = model.Origin.y;
            return model.StrainAt(worldRest, simTime);
        }

        float SurfaceYCore(Vector3 restLocal, float simTime, out float waveStrain)
        {
            float y = restLocal.y;
            // Stylized union: overlapping wells take the DEEPEST dent, not the sum.
            // Summation would double the dent to ~14 m when the holes merge and tear
            // the cloth away from the remnant sphere.
            float deepest = 0f;
            for (int i = 0; i < _wellCount; i++)
            {
                var w = _wellCache[i];
                float dx = w.centre.x - restLocal.x;
                float dz = w.centre.y - restLocal.z;
                float g = w.depth * Mathf.Exp(-(dx * dx + dz * dz) / (2f * w.sigma * w.sigma));
                if (g > deepest) deepest = g;
            }
            if (deepest >= 0.001f) y -= deepest;
            waveStrain = WaveStrainAt(restLocal, simTime);
            float ex = model != null && model.parameters != null
                ? model.parameters.displacementExaggeration : 1f;
            return y + waveStrain * ex * 2f;
        }

        /// <summary>Deterministic local-space surface height for a rest vertex:
        /// dents plus the signed wave ripple (height += h * exaggeration * 2).
        /// Public so editor regression checks can probe the surface directly.</summary>
        public float EvaluateLocalSurface(Vector3 restLocal, float simTime)
            => EvaluateLocalSurface(restLocal, simTime, out _);

        /// <summary>As above, also returning the signed strain that drove the ripple.</summary>
        public float EvaluateLocalSurface(Vector3 restLocal, float simTime, out float waveStrain)
        {
            RefreshWellCache();
            return SurfaceYCore(restLocal, simTime, out waveStrain);
        }

        /// <summary>Vertex colour for a rest vertex: orange/blue by strain sign,
        /// intensity by |h| - no colour at all while h is zero (before emission the
        /// sheet shows only its static dents and a faint depth glow).</summary>
        public Color EvaluateLocalColor(Vector3 restLocal, float simTime)
        {
            float y = EvaluateLocalSurface(restLocal, simTime, out float h);
            return EvaluateLocalColorCore(restLocal, y, h);
        }

        void LateUpdate()
        {
            if (_mesh == null || model == null || simulation == null) return;
            float t = simulation.SimulationTime;
            RefreshWellCache();

            for (int i = 0; i < _rest.Length; i++)
            {
                Vector3 rest = _rest[i];
                float h;
                float y = SurfaceYCore(rest, t, out h);
                // XZ is preserved: wells only press down, the wave only lifts/dips.
                _verts[i] = new Vector3(rest.x, y, rest.z);
                _colors[i] = EvaluateLocalColorCore(rest, y, h);
            }
            _mesh.vertices = _verts;
            _mesh.colors = _colors;
            _mesh.RecalculateBounds();
        }

        Color EvaluateLocalColorCore(Vector3 restLocal, float displacedY, float h)
        {
            Color tint = h >= 0f ? StretchTint : SqueezeTint;
            float dipGlow = Mathf.Clamp01((restLocal.y - displacedY) * 0.14f);
            float waveGlow = Mathf.Clamp01(Mathf.Abs(h) * 1.8f);
            Color c = Color.Lerp(ClothTint, tint, Mathf.Clamp01(waveGlow + dipGlow * 0.4f));

            float halfX = size.x * 0.5f, halfZ = size.y * 0.5f;
            float edge = Mathf.Clamp01((halfX - Mathf.Abs(restLocal.x)) / 6f) *
                         Mathf.Clamp01((halfZ - Mathf.Abs(restLocal.z)) / 6f);
            c.a *= edge;
            return c;
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
