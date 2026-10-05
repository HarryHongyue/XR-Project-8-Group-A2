using UnityEngine;

namespace Project8.Core
{
    /// <summary>
    /// Pedagogical gravitational-wave model.
    ///
    /// Strain state: h(t) = h0 * sin(2*pi*f*t + phi), optionally windowed by a Gaussian
    /// packet (Burst mode). The wave travels along a chosen axis at a deliberately reduced
    /// VISUALIZATION speed; the phase at any position is evaluated at the retarded
    /// simulation time tau = t - s/v, where s is the distance along the propagation axis.
    ///
    /// Test-mass displacement follows the proper-detector-frame '+' polarization picture:
    /// for a wave along +z, dx = +0.5*h*x0 and dy = -0.5*h*y0, i.e. alternating stretch
    /// and squeeze in the transverse plane. No field equations are solved here; this is
    /// the teaching model from the project Starter Pack.
    /// </summary>
    public class GravitationalWaveModel : MonoBehaviour
    {
        [Tooltip("Shared parameter asset.")]
        public SimulationParameters parameters;

        [Tooltip("Reference point of the s = 0 plane; the wavefront passes this point at t = 0.")]
        public Transform waveOrigin;

        public Vector3 PropagationDirection => parameters != null
            ? parameters.PropagationDirection
            : Vector3.forward;

        public Vector3 Origin => waveOrigin != null ? waveOrigin.position : transform.position;

        /// <summary>
        /// Orthonormal axes spanning the plane transverse to propagation.
        /// u1 is the "+" stretch axis, u2 the orthogonal squeeze axis.
        /// </summary>
        public void GetTransverseAxes(out Vector3 u1, out Vector3 u2)
        {
            Vector3 d = PropagationDirection;
            u1 = Mathf.Abs(Vector3.Dot(d, Vector3.up)) > 0.99f
                ? Vector3.right
                : Vector3.Cross(Vector3.up, d).normalized;
            u2 = Vector3.Cross(d, u1).normalized;
        }

        /// <summary>Dimensionless strain h at a world position and simulation time.</summary>
        public float StrainAt(Vector3 worldPosition, float simTime)
        {
            if (parameters == null) return 0f;

            float s = Vector3.Dot(worldPosition - Origin, PropagationDirection);
            float v = Mathf.Max(parameters.visualizationSpeedMps, 1e-4f);
            float tau = simTime - s / v;
            if (tau < 0f) return 0f; // wavefront has not reached this position yet

            float h;
            if (parameters.waveMode == WaveMode.Chirp)
            {
                h = ChirpStrain(tau);
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
                float amp = p.strainAmplitude * Mathf.Exp(-tr / p.ringdownSeconds);
                return amp * Mathf.Sin(phaseAtMerger + 2f * Mathf.PI * f1 * tr);
            }
        }

        /// <summary>
        /// Transverse ('+'-polarization) displacement of a test mass whose rest position is
        /// at worldPosition, scaled by the visualization exaggeration factor.
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

        /// <summary>Distance along the propagation axis covered by the wavefront at time t.</summary>
        public float WavefrontDistance(float simTime)
        {
            float v = parameters != null ? Mathf.Max(parameters.visualizationSpeedMps, 1e-4f) : 1f;
            return v * simTime;
        }
    }
}
