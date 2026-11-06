using UnityEngine;
using Project8.UI;

namespace Project8.Visualization
{
    /// <summary>
    /// Interstellar-style accretion visuals, applied at runtime so no scene
    /// regen is needed: hides the old flat additive disc and replaces it with
    ///   - a textured, doppler-lit main disc (bright approaching side, like the
    ///     Gargantua renders from Interstellar),
    ///   - two LENSED arcs - the far side of the same disc seen bent over and
    ///     under the hole by gravitational lensing,
    ///   - a hot photon ring on the silhouette edge.
    /// Geometry and material are generated procedurally; the BlackHoleBinary
    /// orbit/merger logic is untouched and keeps driving transforms. The arcs
    /// are a teaching approximation: this is NOT a Kerr metric or a ray tracer.
    /// Interstellar's DNGR renderer integrates light-ray bundles through curved
    /// spacetime; this low-cost Quest visual uses shaped meshes and view-dependent
    /// Doppler shading instead.
    /// </summary>
    public static class AccretionDress
    {
        const int Seg = 96, Bands = 10;

        /// <summary>Dress one black-hole transform (sphere child + old
        /// AccretionDisc/PhotonRing children as built by SceneBuilder).</summary>
        public static void Apply(Transform hole)
        {
            if (hole == null) return;
            var mat = MakeMaterial();
            // Hide the simple originals - keep the Light (warm rim glow).
            foreach (var r in hole.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name == "AccretionDisc") r.enabled = false;
            foreach (var lr in hole.GetComponentsInChildren<LineRenderer>(true))
                if (lr.name == "PhotonRing") lr.enabled = false;

            Add(hole, "MainDisc", BuildDiscMesh(), mat);
            var up = Add(hole, "LensArcTop", BuildArcMesh(+1f), mat);
            var dn = Add(hole, "LensArcBottom", BuildArcMesh(-1f), mat);
            up.localRotation = Quaternion.Euler(14f, 0f, 9f);
            dn.localRotation = Quaternion.Euler(14f, 0f, 9f);

            var ring = Add(hole, "PhotonRingHot", null, mat);
            var line = ring.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 96;
            line.widthMultiplier = 0.02f;
            line.sharedMaterial = mat;
            for (int i = 0; i < line.positionCount; i++)
            {
                float a = i * 2f * Mathf.PI / line.positionCount;
                line.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.56f);
            }
            ring.localRotation = Quaternion.Euler(78f, 0f, 0f);
        }

        static Transform Add(Transform hole, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(hole.transform, false);
            go.transform.localRotation = Quaternion.Euler(14f, 0f, 9f);
            if (mesh != null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            return go.transform;
        }

        /// <summary>Main disc: flat ring, vertex colors carry heat (inner = white
        /// hot, outer = deep orange) and a subtle radial brightness falloff.</summary>
        static Mesh BuildDiscMesh()
        {
            var v = new Vector3[(Bands + 1) * (Seg + 1)];
            var uv = new Vector2[v.Length];
            var c = new Color[v.Length];
            var tr = new int[Bands * Seg * 6];
            for (int r = 0; r <= Bands; r++)
            for (int a = 0; a <= Seg; a++)
            {
                int i = r * (Seg + 1) + a;
                float t = r / (float)Bands;
                float ang = a * 2f * Mathf.PI / Seg;
                float rad = Mathf.Lerp(0.58f, 2.8f, Mathf.Pow(t, 1.25f));
                v[i] = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                uv[i] = new Vector2(a / (float)Seg, t);
                // white-hot inner rim cooling to amber then dull red outside
                Color hot = Color.Lerp(new Color(1f, 0.98f, 0.9f),
                    new Color(1f, 0.45f, 0.08f), Mathf.SmoothStep(0f, 1f, t));
                hot.a = Mathf.Lerp(0.95f, 0.35f, t);
                c[i] = hot;
                if (r == Bands || a == Seg) continue;
                int k = (r * Seg + a) * 6, n = i + Seg + 1;
                tr[k] = i; tr[k + 1] = n; tr[k + 2] = i + 1;
                tr[k + 3] = i + 1; tr[k + 4] = n; tr[k + 5] = n + 1;
            }
            var m = new Mesh { name = "Disc" };
            m.vertices = v; m.uv = uv; m.colors = c; m.triangles = tr;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A band of the far side's disc seen through the lens: an arc
        /// lifted over (sign=+1) or under (sign=-1) the hole - reads as the
        /// glowing halo above/below the silhouette.</summary>
        static Mesh BuildArcMesh(float sign)
        {
            const int seg = 72, bands = 5;
            var v = new Vector3[(bands + 1) * (seg + 1)];
            var uv = new Vector2[v.Length];
            var c = new Color[v.Length];
            var tr = new int[bands * seg * 6];
            for (int r = 0; r <= bands; r++)
            for (int a = 0; a <= seg; a++)
            {
                int i = r * (seg + 1) + a;
                float t = r / (float)bands;         // radial position inside band
                float s = a / (float)seg;           // 0..1 along the arc
                float ang = Mathf.Lerp(-0.82f * Mathf.PI, 0.82f * Mathf.PI, s);
                float rad = Mathf.Lerp(0.66f, 1.05f, t);
                // lift peaks mid-arc and tapers to zero at the arc ends, so the
                // band lands on the disc plane at its tips.
                float lift = sign * (0.62f + 0.5f * (rad - 0.66f)) *
                             Mathf.Sin(Mathf.PI * Mathf.SmoothStep(0f, 1f, s));
                v[i] = new Vector3(Mathf.Cos(ang) * rad, lift, Mathf.Sin(ang) * rad * 0.9f);
                uv[i] = new Vector2(s, t);
                Color hot = Color.Lerp(new Color(1f, 0.9f, 0.7f),
                    new Color(1f, 0.5f, 0.12f), t);
                hot.a = Mathf.Lerp(0.7f, 0.25f, t) * Mathf.Sin(Mathf.PI * s);
                c[i] = hot;
                if (r == bands || a == seg) continue;
                int k = (r * seg + a) * 6, n = i + seg + 1;
                tr[k] = i; tr[k + 1] = n; tr[k + 2] = i + 1;
                tr[k + 3] = i + 1; tr[k + 4] = n; tr[k + 5] = n + 1;
            }
            var m = new Mesh { name = "LensArc" };
            m.vertices = v; m.uv = uv; m.colors = c; m.triangles = tr;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>One shared unlit material: additive glow, doppler-boosted by
        /// the swirl shader (Project8/AccretionDisc).</summary>
        static Material MakeMaterial()
        {
            // Resources material keeps this runtime-only visual shader in Quest
            // builds; Shader.Find alone can be stripped by Unity's build pipeline.
            var included = Resources.Load<Material>("M_AccretionDisc");
            if (included != null) return included;

            var sh = Shader.Find("Project8/AccretionDisc");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_TintColor"))
                m.SetColor("_TintColor", new Color(1f, 0.75f, 0.4f));
            return m;
        }
    }
}
