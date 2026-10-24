using UnityEngine;
using Project8.Core;
using Project8.Visualization;

namespace Project8.EditorTools
{
    /// <summary>
    /// Builds the Einstein Telescope observatory display: three corner stations on
    /// an equilateral triangle (the published 2020 design baseline - sides drawn
    /// 12 m wide standing in for the real ~10 km). Each station carries a pair of
    /// interferometer lanes - LF and HF like the real xylophone layout - sharing
    /// the same qualitative strain model (the real noise split, Fabry-Perot
    /// cavities, recycling mirrors and suspensions are NOT simulated; the honest
    /// list lives on the MISSION and HELP drawer).
    /// </summary>
    public static partial class SceneBuilder
    {
        static EinsteinTelescopeDisplay BuildEinsteinTelescope(Mats mats,
            GravitationalWaveModel model, SimulationController sim,
            Vector3 center, Vector3 viewer)
        {
            var root = new GameObject("EinsteinTelescope (triangular observatory)");
            root.transform.position = center;
            root.transform.rotation = Quaternion.identity; // horizontal XZ - never tilted

            var et = root.AddComponent<EinsteinTelescopeDisplay>();
            et.model = model;
            et.simulation = sim;
            et.detectors = new LaserInterferometer[6];
            et.detailRoots = new Transform[3];

            const float side = 12f; // displayed side; real ET concept: ~10 km
            var v = et.vertices;

            // Faint triangle outline - the underground-vacuum-system baseline.
            MakeBeam(root.transform, "Baseline_AB", v[0], v[1], mats.NetLine, 0.05f);
            MakeBeam(root.transform, "Baseline_BC", v[1], v[2], mats.NetLine, 0.05f);
            MakeBeam(root.transform, "Baseline_CA", v[2], v[0], mats.NetLine, 0.05f);

            for (int s = 0; s < 3; s++)
            {
                Vector3 toNext = (v[(s + 1) % 3] - v[s]).normalized;
                Vector3 toPrev = (v[(s + 2) % 3] - v[s]).normalized;

                var station = new GameObject($"Station_D{s + 1}");
                station.transform.SetParent(root.transform, false);
                station.transform.localPosition = v[s];

                // Corner housing - standing in for the underground corner
                // cavern of the official cutaway renders.
                MakeBox(station.transform, "CornerStation", new Vector3(0f, -0.15f, 0f),
                    new Vector3(0.9f, 0.3f, 0.9f), mats.Hull);

                // All small optics live under Detail so the inspector can
                // collapse them while the triangle lanes stay drawn.
                var detail = new GameObject("Detail");
                detail.transform.SetParent(station.transform, false);
                detail.transform.localPosition = Vector3.zero;
                et.detailRoots[s] = detail.transform;

                // Two nested instrument lanes per station: LF then HF. Each is
                // shifted a little INWARD along the angle bisector (plus a small
                // vertical lift) so the nested xylophone lanes read as separate
                // paths instead of smearing onto the same baseline. Both the
                // lane root and its detail optics share the exact same offset.
                Vector3 inward = (toNext + toPrev).normalized;
                for (int lane = 0; lane < 2; lane++)
                {
                    string tag = lane == 0 ? "LF" : "HF";
                    Vector3 laneOffset =
                        inward * (lane == 0 ? 0.12f : 0.30f) +
                        Vector3.up * (lane == 0 ? 0.12f : -0.12f);

                    var laneGO = new GameObject($"Lane_{tag}");
                    laneGO.transform.SetParent(station.transform, false);
                    laneGO.transform.localPosition = laneOffset;

                    var laneDetail = new GameObject($"{tag}_Optics");
                    laneDetail.transform.SetParent(detail.transform, false);
                    laneDetail.transform.localPosition = laneOffset;

                    var li = laneGO.AddComponent<LaserInterferometer>();
                    li.model = model;
                    li.simulation = sim;
                    li.armAxisA = toNext;
                    li.armAxisB = toPrev;
                    li.armLength = side;
                    li.markerSpeed = 2.4f;
                    et.detectors[s * 2 + lane] = li;

                    BuildLaneOptics(mats, li, laneGO.transform, laneDetail.transform,
                        toNext, toPrev, side, s, lane);
                }
            }

            et.SelectedDetector = 0;
            et.DetailVisible = true;
            return et;
        }

        /// <summary>One station lane's optics: the two 60-degree arm beam lanes
        /// live on the lane object (always drawn), the small hardware - splitter
        /// plate normal to (axA - axB) so +axA reflects into +axB, near + end
        /// cavity mirrors, laser feeding along +axA, photodetector on the -axB
        /// dark port, light markers, input/output beams, history trace -
        /// lives under the station's Detail root.</summary>
        static void BuildLaneOptics(Mats mats, LaserInterferometer li,
            Transform lane, Transform optics, Vector3 axA, Vector3 axB,
            float side, int station, int laneIndex)
        {
            bool lf = laneIndex == 0;
            var beamMat = lf ? mats.Laser : mats.ReturnBeam;
            var backMat = lf ? mats.ReturnBeam : mats.Laser;

            // Arm lanes (kept outside Detail): outbound + return lines with the
            // usual readability offset, recomputed by the component each frame.
            li.outboundA = MakeBeam(lane, "Beam_OutA", axA * 0.02f, axA * side, beamMat, 0.05f);
            li.inboundA = MakeBeam(lane, "Beam_InA", axA * side, axA * 0.02f, backMat, 0.05f);
            li.outboundB = MakeBeam(lane, "Beam_OutB", axB * 0.02f, axB * side, beamMat, 0.05f);
            li.inboundB = MakeBeam(lane, "Beam_InB", axB * side, axB * 0.02f, backMat, 0.05f);

            // Beam splitter: the incoming beam travels +axA, arm A transmits
            // straight through and arm B is the REFLECTION - the plate normal
            // must be (axA - axB) so that Reflect(axA, normal) == axB.
            Vector3 splitNormal = (axA - axB).normalized;
            MakeBox(optics, "BeamSplitter", Vector3.zero,
                new Vector3(0.06f, 0.5f, 0.5f), mats.Glass,
                Quaternion.FromToRotation(Vector3.right, splitNormal));

            // Near + end cavity mirrors (conceptual cavity - no bouncing light
            // or finesse is simulated).
            MakeBox(optics, "NearMirror_A", axA * 0.4f,
                new Vector3(0.08f, 0.4f, 0.4f), mats.Hull,
                Quaternion.FromToRotation(Vector3.right, axA));
            MakeBox(optics, "NearMirror_B", axB * 0.4f,
                new Vector3(0.08f, 0.4f, 0.4f), mats.Hull,
                Quaternion.FromToRotation(Vector3.right, axB));
            var mA = MakeBox(optics, "EndMirror_A", axA * side,
                new Vector3(0.10f, 0.5f, 0.5f), mats.Hull,
                Quaternion.FromToRotation(Vector3.right, axA));
            var mB = MakeBox(optics, "EndMirror_B", axB * side,
                new Vector3(0.10f, 0.5f, 0.5f), mats.Hull,
                Quaternion.FromToRotation(Vector3.right, axB));
            li.mirrorA = mA.transform;
            li.mirrorB = mB.transform;

            // Laser sits behind the splitter OPPOSITE arm A: the beam travels
            // +axA into the plate (transmits into arm A, reflects into arm B).
            // The antisymmetric (dark) port exits along -axB.
            MakeBox(optics, "LaserSource", -axA * 1.5f,
                new Vector3(0.7f, 0.3f, 0.3f), mats.Console,
                Quaternion.FromToRotation(Vector3.right, axA));
            var pd = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pd.name = "Photodetector";
            pd.transform.SetParent(optics, false);
            pd.transform.localPosition = -axB * 1.4f;
            pd.transform.localScale = Vector3.one * 0.35f;
            pd.GetComponent<Renderer>().sharedMaterial = mats.EngineGlow;
            KillCollider(pd);
            li.photodetector = pd.GetComponent<Renderer>();

            li.inputBeam = MakeBeam(optics, "Beam_Input", -axA * 1.4f, Vector3.zero,
                beamMat, 0.045f);
            li.outputBeam = MakeBeam(optics, "Beam_Output", Vector3.zero, -axB * 1.3f,
                backMat, 0.045f);
            li.signalTrace = MakeBeam(optics, "SignalTrace",
                new Vector3(-2.2f, -0.9f, 0f), new Vector3(2.2f, -0.9f, 0f),
                backMat, 0.02f);

            li.markerA = MakeLightMarker(optics, "Marker_A", mats);
            li.markerB = MakeLightMarker(optics, "Marker_B", mats);
            // No world-space readout here: channel phase/signal go to the ET
            // INSPECTOR console text instead of floating prose.
        }
    }
}
