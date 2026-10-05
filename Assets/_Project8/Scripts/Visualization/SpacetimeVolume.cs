using UnityEngine;
using Project8.Core;

namespace Project8.Visualization
{
    /// <summary>
    /// World-fixed volumetric lattice of test-mass nodes. The grid does NOT move with
    /// the ship - it fills the space around the ship, the binary and the Earth, so the
    /// gravitational wave visibly ripples through the whole scene.
    ///
    /// Links are drawn inside each horizontal (XZ) layer; vertical (Y) links are off by
    /// default because horizontal sheets make the transverse stretch/squeeze easiest to
    /// read. Nodes still displace along both transverse axes of the wave model and are
    /// pulled into a stylized "gravity well" near each black hole.
    /// </summary>
    public class SpacetimeVolume : MonoBehaviour
    {
        public GravitationalWaveModel model;
        public SimulationController simulation;
        public Transform ship;
        public Transform[] blackHoles;
        public Material lineMaterial;
        public Material pointMaterial;
        [Range(5, 31)] public int countX = 21;
        [Range(3, 15)] public int countY = 7;
        [Range(5, 31)] public int countZ = 21;
        public float spacing = 3.2f;
        public Vector3 centre = new Vector3(0f, 0.5f, 0f);
        public float wellDepth = 2.2f;
        public float pointSize = 0.14f;
        [Tooltip("Draw vertical (Y) links between horizontal layers. Off by default: " +
                 "horizontal sheets read more clearly.")]
        public bool connectLayers;

        Mesh _mesh;
        Vector3[] _positions;
        Color[] _colors;
        ParticleSystem.Particle[] _particles;
        ParticleSystem _points;

        void Start()
        {
            int count = countX * countY * countZ;
            _positions = new Vector3[count];
            _colors = new Color[count];
            _particles = new ParticleSystem.Particle[count];

            int linkCount = (countX - 1) * countY * countZ +
                            countX * countY * (countZ - 1) +
                            (connectLayers ? countX * (countY - 1) * countZ : 0);
            int[] links = new int[2 * linkCount];
            int link = 0;
            for (int z = 0; z < countZ; z++)
            for (int y = 0; y < countY; y++)
            for (int x = 0; x < countX; x++)
            {
                int i = Index(x, y, z);
                if (x + 1 < countX) { links[link++] = i; links[link++] = Index(x + 1, y, z); }
                if (z + 1 < countZ) { links[link++] = i; links[link++] = Index(x, y, z + 1); }
                if (connectLayers && y + 1 < countY)
                { links[link++] = i; links[link++] = Index(x, y + 1, z); }
                _particles[i].startLifetime = 100000f;
                _particles[i].remainingLifetime = 100000f;
            }

            _mesh = new Mesh { name = "WorldSpacetimeLattice" };
            _mesh.MarkDynamic();
            _mesh.vertices = _positions;
            _mesh.SetIndices(links, MeshTopology.Lines, 0);
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = lineMaterial;

            var points = new GameObject("FineTestMasses");
            points.transform.SetParent(transform, false);
            _points = points.AddComponent<ParticleSystem>();
            var main = _points.main;
            main.loop = true;
            main.startLifetime = 100000f;
            main.startSpeed = 0f;
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = _points.emission;
            emission.enabled = false;
            _points.GetComponent<ParticleSystemRenderer>().sharedMaterial = pointMaterial;
            _points.Play();
        }

        int Index(int x, int y, int z) => (z * countY + y) * countX + x;

        void LateUpdate()
        {
            if (_mesh == null || model == null || simulation == null) return;
            float t = simulation.SimulationTime;
            float front = model.WavefrontDistance(t);
            Vector3 direction = model.PropagationDirection;
            Vector3 shipPos = ship != null ? ship.position : new Vector3(0f, -999f, 0f);
            for (int z = 0; z < countZ; z++)
            for (int y = 0; y < countY; y++)
            for (int x = 0; x < countX; x++)
            {
                int i = Index(x, y, z);
                Vector3 rest = centre + new Vector3(
                    (x - (countX - 1) * 0.5f) * spacing,
                    (y - (countY - 1) * 0.5f) * spacing,
                    (z - (countZ - 1) * 0.5f) * spacing);
                Vector3 point = rest + model.DisplacementAt(rest, t);
                if (blackHoles != null)
                {
                    foreach (var hole in blackHoles)
                    {
                        if (hole == null || !hole.gameObject.activeInHierarchy) continue;
                        Vector3 delta = hole.position - rest;
                        float strength = wellDepth / (1f + delta.sqrMagnitude / 20f);
                        point += Vector3.down * strength +
                            new Vector3(delta.x, 0f, delta.z).normalized * (strength * 0.3f);
                    }
                }
                _positions[i] = point;
                float fade = Mathf.Clamp01((Vector3.Distance(rest, shipPos) - 2.7f) / 3f);
                float distanceFromFront = front - Vector3.Dot(rest - model.Origin, direction);
                float pulse = Mathf.Exp(-0.5f * distanceFromFront * distanceFromFront / 3.5f);
                float h = model.StrainAt(rest, t);
                Color tint = h >= 0f ? new Color(1f, 0.76f, 0.32f) : new Color(0.3f, 0.78f, 1f);
                Color line = Color.Lerp(new Color(0.18f, 0.5f, 0.72f), tint, pulse);
                line.a = (0.16f + pulse * 0.8f) * fade;
                _colors[i] = line;
                _particles[i].position = point;
                _particles[i].startSize = pointSize * (0.8f + pulse * 1.8f) * fade;
                _particles[i].startColor = new Color(tint.r, tint.g, tint.b,
                    (0.55f + pulse * 0.45f) * fade);
                _particles[i].remainingLifetime = 100000f;
            }
            _mesh.vertices = _positions;
            _mesh.colors = _colors;
            _mesh.RecalculateBounds();
            _points.SetParticles(_particles, _particles.Length);
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
