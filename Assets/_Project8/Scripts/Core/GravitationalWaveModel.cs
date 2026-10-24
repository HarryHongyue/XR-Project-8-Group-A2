using UnityEngine;

namespace Project8.Core
{
    /// <summary>
    /// Pedagogical gravitational-wave model.
    ///
    /// Strain state: h(t) = h0 * sin(2*pi*f*t + phi), optionally windowed by a Gaussian
    /// packet (Burst mode) or replaced by a chirp/ringdown (Chirp mode). The wave
    /// travels at a deliberately reduced VISUALIZATION speed; the phase at any
    /// position is evaluated at the retarded simulation time
    /// tau = t - emissionDelay - distance/v.
    ///
    /// Two propagation geometries are supported:
    ///   - planar (default): distance = s = (p - origin) . axis; the s = 0 plane
    ///     passes the origin at the emission instant.
    ///   - radial (Main scene): distance = |p - origin|; concentric wavefronts
    ///     leave the binary at emissionDelay (the merger instant) and nothing
    ///     exists anywhere before it - including positions "behind" the source.
    ///
    /// Test-mass displacement follows the proper-detector-frame '+' polarization
    /// picture: dx = +0.5*h*x0 and dy = -0.5*h*y0, i.e. alternating stretch and
    /// squeeze in the plane transverse to propagation. No field equations are
    /// solved here; this is the teaching model from the project Starter Pack.
    /// </summary>
    public class GravitationalWaveModel : MonoBehaviour
    {
        [Tooltip("Shared parameter asset.")]
        public SimulationParameters parameters;

        [Tooltip("Reference point of the wavefront; for radial propagation the source point.")]
        public Transform waveOrigin;

        [Tooltip("Planar default (fixed axis) vs concentric radial emission from the origin.")]
        public bool radialPropagation = false;

        [Tooltip("Seconds of simulation time before the wave is emitted at the origin. " +
                 "For the Main scene this equals the inspiral duration, so the first " +
                 "disturbance leaves the binary at the merger instant.")]
        [Min(0f)] public float emissionDelay = 0f;

        public Vector3 PropagationDirection => parameters != null
            ? parameters.PropagationDirection
            : Vector3.forward;

        public Vector3 Origin => waveOrigin != null ? waveOrigin.position : transform.position;

        /// <summary>Local propagation direction at a world position: the radial
        /// source->position direction in radial mode, the fixed axis otherwise.</summary>
        public Vector3 PropagationDirectionAt(Vector3 worldPosition)
        {
            if (!radialPropagation) return PropagationDirection;
            Vector3 d = worldPosition - Origin;
            float m = d.magnitude;
            return m > 1e-4f ? d / m : PropagationDirection;
        }

        /// <summary>Distance the wavefront must travel to reach a world position:
        /// radial |p - origin|, or projected axis distance s in planar mode.</summary>
        public float PropagationDistance(Vector3 worldPosition)
        {
            if (radialPropagation) return Vector3.Distance(worldPosition, Origin);
            return Vector3.Dot(worldPosition - Origin, PropagationDirection);
        }

        /// <summary>Retarded (emission) time at a position: tau = t - delay - distance/v.</summary>
        public float RetardedTime(Vector3 worldPosition, float simTime)
        {
            float v = parameters != null
                ? Mathf.Max(parameters.visualizationSpeedMps, 1e-4f)
                : 1f;
            return simTime - emissionDelay - PropagationDistance(worldPosition) / v;
        }

        /// <summary>
        /// Orthonormal axes spanning the plane transverse to propagation.
        /// u1 is the "+" stretch axis, u2 the orthogonal squeeze axis.
        /// </summary>
        public void GetTransverseAxes(out Vector3 u1, out Vector3 u2)
        {
            BuildAxes(PropagationDirection, out u1, out u2);
        }

        /// <summary>Position-aware overload: transverse to the local (possibly
        /// radial) propagation direction at worldPosition.</summary>
        public void GetTransverseAxes(Vector3 worldPosition, out Vector3 u1, out Vector3 u2)
        {
            BuildAxes(PropagationDirectionAt(worldPosition), out u1, out u2);
        }

        static void BuildAxes(Vector3 d, out Vector3 u1, out Vector3 u2)
        {
            u1 = Mathf.Abs(Vector3.Dot(d, Vector3.up)) > 0.99f
                ? Vector3.right
                : Vector3.Cross(Vector3.up, d).normalized;
            u2 = Vector3.Cross(d, u1).normalized;
        }

        /// <summary>Dimensionless strain h at a world position and simulation time.</summary>
        public float StrainAt(Vector3 worldPosition, float simTime)
        {
            if (parameters == null) return 0f;

            float tau = RetardedTime(worldPosition, simTime);
            if (tau <= 0f) return 0f; // wavefront has not reached this position yet

            float h;
            if (parameters.waveMode == WaveMode.Chirp)
            {
                h = radialPropagation ? RingdownStrain(tau) : ChirpStrain(tau);
            }
            else
            {
                h = parameters.strainAmplitude *
                    Mathf.Sin(2f * Mathf.PI * parameters.frequencyHz * tau + parameters.phaseRadians);
            }

            if (parameters.waveMode == WaveMode.Burst)
            {
                // Gaussian envelope centred mid-packet; sigma = duration/6 so the visible
                // packet occupies roughly its nominal duration.
                float sigma = parameters.packetDurationSeconds / 6f;
                float centre = parameters.packetDurationSeconds * 0.5f;
                float dTau = tau - centre;
                h *= Mathf.Exp(-(dTau * dTau) / (2f * sigma * sigma));
            }
            return h;
        }

        /// <summary>
        /// Simplified binary-inspiral waveform: frequency sweeps upward from 0.25*f1 to f1
        /// over the inspiral time while amplitude grows, then an exponential ringdown.
        /// A qualitative "GW150914-shaped" chirp for teaching - not a numerical-relativity output.
        /// </summary>
        float ChirpStrain(float tau)
        {
            var p = parameters;
            float td = Mathf.Max(p.inspiralDurationSeconds, 1e-3f);
            float f1 = p.frequencyHz;
            float f0 = f1 * 0.25f;

            if (tau <= td)
            {
                float x = tau / td;
                // phase is the integral of instantaneous frequency: phi = 2*pi*Int f dt
                float phase = 2f * Mathf.PI * (f0 * tau + (f1 - f0) * tau * tau / (2f * td))
                              + p.phaseRadians;
                float amp = p.strainAmplitude * (0.15f + 0.85f * Mathf.Sqrt(Mathf.Max(x, 0f)));
                return amp * Mathf.Sin(phase);
            }
            else
            {
                float tr = tau - td;
                float phaseAtMerger = 2f * Mathf.PI * (f0 * td + (f1 - f0) * td * 0.5f)
                                      + p.phaseRadians;
                // Stylized residual floor: real ringdowns decay to zero, but a small
                // sustained oscillation keeps the post-merger wave visible for teaching.
                float amp = p.strainAmplitude *
                    Mathf.Max(Mathf.Exp(-tr / p.ringdownSeconds), p.postMergerResidual);
                return amp * Mathf.Sin(phaseAtMerger + 2f * Mathf.PI * f1 * tr);
            }
        }

        /// <summary>
        /// Merger-emitted ringdown for radial propagation: h = A e^(-tau/tr) sin(2*pi*f*tau + phi),
        /// exactly zero once tau >= 8 * ringdownSeconds. The real inspiral also radiates;
        /// that earlier radiation is intentionally excluded here so the emitted wave is
        /// born at the merger flash (t = emissionDelay) - a teaching simplification.
        /// </summary>
        float RingdownStrain(float tau)
        {
            var p = parameters;
            if (tau >= 8f * p.ringdownSeconds) return 0f;
            return p.strainAmplitude * Mathf.Exp(-tau / p.ringdownSeconds) *
                Mathf.Sin(2f * Mathf.PI * p.frequencyHz * tau + p.phaseRadians);
        }

        /// <summary>
        /// Transverse ('+'-polarization) displacement of a test mass whose rest position is
        /// at worldPosition, scaled by the visualization exaggeration factor.
        /// PLANAR fallback: the basis is the fixed propagation axis and the offset is
        /// measured from the wave origin - the original teaching behaviour. Under radial
        /// propagation callers MUST use the three-argument overload: a mass's own radial
        /// direction is parallel to its offset from the source, so a basis taken at the
        /// mass position itself would project the offset to exactly zero.
        /// </summary>
        public Vector3 DisplacementAt(Vector3 restWorldPosition, float simTime)
        {
            float h = StrainAt(restWorldPosition, simTime);
            if (Mathf.Approximately(h, 0f)) return Vector3.zero;

            GetTransverseAxes(out Vector3 u1, out Vector3 u2);
            Vector3 rel = restWorldPosition - Origin;
            float x = Vector3.Dot(rel, u1);
            float y = Vector3.Dot(rel, u2);

            float k = 0.5f * h * parameters.displacementExaggeration;
            return k * (u1 * x - u2 * y);
        }

        /// <summary>
        /// Reference-centred variant used by radial propagation and test-mass fields:
        /// the transverse '+' basis is evaluated at referenceWorldPosition (e.g. the
        /// field centre) and the offset is measured from that same reference - so
        /// masses sitting off-axis relative to the field centre move, just as a ring
        /// of test masses does around the propagation axis.
        /// </summary>
        public Vector3 DisplacementAt(Vector3 restWorldPosition, float simTime,
                                      Vector3 referenceWorldPosition)
        {
            float h = StrainAt(restWorldPosition, simTime);
            if (Mathf.Approximately(h, 0f)) return Vector3.zero;

            GetTransverseAxes(referenceWorldPosition, out Vector3 u1, out Vector3 u2);
            Vector3 rel = restWorldPosition - referenceWorldPosition;
            float x = Vector3.Dot(rel, u1);
            float y = Vector3.Dot(rel, u2);

            float k = 0.5f * h * parameters.displacementExaggeration;
            return k * (u1 * x - u2 * y);
        }

        /// <summary>Distance covered by the wavefront since emission, never negative.</summary>
        public float WavefrontDistance(float simTime)
        {
            float v = parameters != null ? Mathf.Max(parameters.visualizationSpeedMps, 1e-4f) : 1f;
            return Mathf.Max(0f, (simTime - emissionDelay) * v);
        }
    }
}
