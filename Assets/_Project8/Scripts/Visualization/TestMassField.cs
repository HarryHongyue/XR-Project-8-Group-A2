using System.Collections.Generic;
using UnityEngine;
using Project8.Core;

namespace Project8.Visualization
{
    /// <summary>
    /// Generates and animates a field of test masses - either a ring perpendicular to the
    /// propagation direction (the classic LIGO-style visualization) or a flat grid.
    /// Every frame, each mass is displaced by the model's transverse strain.
    /// </summary>
    public class TestMassField : MonoBehaviour
    {
        public enum FieldLayout { Ring, Grid, Volume }

        [Header("References")]
        public GravitationalWaveModel model;
        public SimulationController simulation;
        public TestMass testMassPrefab;

        [Header("Layout")]
        public FieldLayout layout = FieldLayout.Ring;
        [Tooltip("Number of masses on the ring.")]
        [Range(6, 64)] public int ringCount = 24;
        [Tooltip("Ring radius in metres.")]
        [Min(0.2f)] public float ringRadius = 1.2f;
        [Tooltip("Grid side length (gridSize x gridSize masses).")]
        [Range(3, 15)] public int gridSize = 7;
        [Min(0.1f)] public float gridSpacing = 0.4f;
        [Range(2, 10)] public int depthCount = 5;
        [Min(0.2f)] public float depthSpacing = 2f;

        [Header("Strain colouring")]
        public bool colorizeByStrain = true;
        public Color stretchColor = new Color(1f, 0.55f, 0.1f);
        public Color squeezeColor = new Color(0.15f, 0.65f, 1f);

        readonly List<TestMass> _masses = new List<TestMass>();
        PropagationAxis _axis;
        public IReadOnlyList<TestMass> Masses => _masses;

        void Start()
        {
            if (_masses.Count == 0) Rebuild();
            if (model != null && model.parameters != null)
                model.parameters.Changed += OnParametersChanged;
        }

        void OnDestroy()
        {
            if (model != null && model.parameters != null)
                model.parameters.Changed -= OnParametersChanged;
        }

        void OnParametersChanged()
        {
            // Propagation axis may have changed -> rest positions must be re-laid out
            // in the new transverse plane.
            if (model.parameters.propagationAxis != _axis) Rebuild();
        }

        /// <summary>(Re)spawns all test masses in the current transverse plane.</summary>
        public void Rebuild()
        {
            foreach (var mass in _masses)
            {
                if (mass == null) continue;
                if (Application.isPlaying) Destroy(mass.gameObject);
                else DestroyImmediate(mass.gameObject);
            }
            _masses.Clear();
            if (testMassPrefab == null || model == null) return;
            _axis = model.parameters.propagationAxis;

            model.GetTransverseAxes(out Vector3 u1, out Vector3 u2);
            // transverse axes are world-space; convert into field-local directions
            Vector3 l1 = transform.InverseTransformDirection(u1);
            Vector3 l2 = transform.InverseTransformDirection(u2);

            if (layout == FieldLayout.Ring)
            {
                for (int i = 0; i < ringCount; i++)
                {
                    float a = i * Mathf.PI * 2f / ringCount;
                    Vector3 local = (l1 * Mathf.Cos(a) + l2 * Mathf.Sin(a)) * ringRadius;
                    SpawnMass(local);
                }
            }
            else
            {
                float half = (gridSize - 1) * 0.5f * gridSpacing;
                int layers = layout == FieldLayout.Volume ? depthCount : 1;
                Vector3 depth = transform.InverseTransformDirection(model.PropagationDirection);
                for (int iz = 0; iz < layers; iz++)
                for (int ix = 0; ix < gridSize; ix++)
                for (int iy = 0; iy < gridSize; iy++)
                    SpawnMass(l1 * (ix * gridSpacing - half) + l2 * (iy * gridSpacing - half) +
                        depth * ((iz - (layers - 1) * 0.5f) * depthSpacing));
            }
        }

        void SpawnMass(Vector3 localPosition)
        {
            TestMass mass = Instantiate(testMassPrefab, transform);
            mass.transform.localPosition = localPosition;
            mass.transform.localRotation = Quaternion.identity;
            mass.CaptureRestPosition();
            _masses.Add(mass);
        }

        void Update()
        {
            if (model == null || simulation == null) return;
            float t = simulation.SimulationTime;
            foreach (var mass in _masses)
            {
                Vector3 worldRest = transform.TransformPoint(mass.RestLocalPosition);
                float h = model.StrainAt(worldRest, t);
                Vector3 worldDisp = model.DisplacementAt(worldRest, t);
                Vector3 localDisp = transform.InverseTransformDirection(worldDisp);
                mass.ApplyStrain(localDisp, h, stretchColor, squeezeColor, colorizeByStrain);
            }
        }
    }
}
