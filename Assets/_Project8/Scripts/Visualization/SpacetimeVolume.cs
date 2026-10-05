using UnityEngine;
using Project8.Core;

namespace Project8.Visualization
{
    public class SpacetimeVolume : MonoBehaviour
    {
        public GravitationalWaveModel model;
        public SimulationController simulation;
        public Transform ship;
        public Transform[] blackHoles;
        public Material lineMaterial;
        public Material pointMaterial;
        [Range(5, 25)] public int countX = 17;
        [Range(5, 21)] public int countY = 11;
        [Range(5, 25)] public int countZ = 21;
        public float spacing = 1.45f;
        public float wellDepth = 2.2f;
        public float pointSize = 0.12f;
        public bool showDepthLinks;

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

            int[] links = new int[2 * ((countX - 1) * countY * countZ +
                countX * (countY - 1) * countZ +
                (showDepthLinks ? countX * countY * (countZ - 1) : 0))];
            int link = 0;
            for (int z = 0; z < countZ; z++)
            for (int y = 0; y < countY; y++)
            for (int x = 0; x < countX; x++)
            {
                int i = Index(x, y, z);
                if (x + 1 < countX) { links[link++] = i; links[link++] = Index(x + 1, y, z); }
                if (y + 1 < countY) { links[link++] = i; links[link++] = Index(x, y + 1, z); }
                if (showDepthLinks && z + 1 < countZ)
                { links[link++] = i; links[link++] = Index(x, y, z + 1); }
                _particles[i].startLifetime = 100000f;
                _particles[i].remainingLifetime = 100000f;
            }

            _mesh = new Mesh { name = "ContinuousSpacetimeLattice" };
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
            if (_mesh == null || ship == null || model == null || simulation == null) return;
            Vector3 centre = ship.position + ship.forward * 12f + Vector3.up * 1.8f;
            float t = simulation.SimulationTime;
            float front = model.WavefrontDistance(t);
            Vector3 direction = model.PropagationDirection;
            for (int z = 0; z < countZ; z++)
            for (int y = 0; y < countY; y++)
            for (int x = 0; x < countX; x++)
            {
                int i = Index(x, y, z);
                Vector3 rest = centre + ship.right * ((x - (countX - 1) * 0.5f) * spacing) +
                    ship.up * ((y - (countY - 1) * 0.5f) * spacing) +
                    ship.forward * ((z - (countZ - 1) * 0.5f) * spacing);
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
                float fade = Mathf.Clamp01((Vector3.Distance(rest, ship.position) - 2.7f) / 3f);
                float distanceFromFront = front - Vector3.Dot(rest - model.Origin, direction);
                float pulse = Mathf.Exp(-0.5f * distanceFromFront * distanceFromFront / 3.5f);
                float h = model.StrainAt(rest, t);
                Color tint = h >= 0f ? new Color(1f, 0.76f, 0.32f) : new Color(0.3f, 0.78f, 1f);
                Color line = Color.Lerp(new Color(0.18f, 0.5f, 0.72f), tint, pulse);
                line.a = (0.13f + pulse * 0.75f) * fade;
                _colors[i] = line;
                _particles[i].position = point;
                _particles[i].startSize = pointSize * (0.8f + pulse * 1.8f) * fade;
                _particles[i].startColor = new Color(tint.r, tint.g, tint.b,
                    (0.5f + pulse * 0.5f) * fade);
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
