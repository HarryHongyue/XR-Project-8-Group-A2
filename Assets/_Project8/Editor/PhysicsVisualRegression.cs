using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Project8.Core;
using Project8.Evaluation;
using Project8.Interaction;
using Project8.Story;
using Project8.UI;
using Project8.Visualization;

namespace Project8.EditorTools
{
    /// <summary>
    /// Deterministic regression checks for the unified physics/visualization fixes.
    ///
    /// Scene safety: a throwaway scene is created ADDITIVELY and made active, all
    /// temporary objects are spawned there, then the previous active scene is
    /// restored and the temp scene closed. Scenes the user had open are never
    /// unloaded; Main.unity is only ever opened additively for inspection and is
    /// closed again only if this runner opened it.
    ///
    /// A failed check throws InvalidOperationException so menu, runner and batch
    /// callers see a real failure. Menu: "Project 8/Physics Visual Regression".
    /// </summary>
    public static class PhysicsVisualRegression
    {
        const string Root = "Assets/_Project8";
        static int _failures;

        [MenuItem("Project 8/Physics Visual Regression")]
        public static void Run()
        {
            _failures = 0;
            var original = SceneManager.GetActiveScene();
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(temp);
            try
            {
                CheckRadialModel();
                CheckDisplacement();
                CheckSheet();
                CheckInterferometer();
                CheckEtArms();
                CheckSubtitlePagination();
                CheckWaveLibrary();
                CheckPlaybackSemantics();
                CheckGeneratedAssets();
            }
            finally
            {
                SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(temp, true);
            }
            if (_failures > 0)
                throw new System.InvalidOperationException(
                    $"[Project8] Physics visual regression: {_failures} check(s) FAILED");
            Debug.Log("[Project8] Physics visual regression: ALL CHECKS PASSED");
        }

        static void Check(bool cond, string name)
        {
            if (cond) Debug.Log($"[Project8]   PASS  {name}");
            else { _failures++; Debug.LogError($"[Project8]   FAIL  {name}"); }
        }

        // ---------------------------------------------------------------- model
        static GravitationalWaveModel MakeRadialModel(Vector3 origin,
            out SimulationParameters p)
        {
            p = ScriptableObject.CreateInstance<SimulationParameters>();
            p.strainAmplitude = 0.5f;
            p.frequencyHz = 0.55f;
            p.phaseRadians = 0f;
            p.propagationAxis = PropagationAxis.PositiveX;
            p.waveMode = WaveMode.Chirp;
            p.inspiralDurationSeconds = 8f;
            p.ringdownSeconds = 4f;
            p.postMergerResidual = 0.25f;
            p.visualizationSpeedMps = 3.2f;
            p.displacementExaggeration = 1.6f;
            var go = new GameObject("Model");
            var originGO = new GameObject("Origin");
            originGO.transform.position = origin;
            var m = go.AddComponent<GravitationalWaveModel>();
            m.parameters = p;
            m.waveOrigin = originGO.transform;
            m.radialPropagation = true;
            m.emissionDelay = p.inspiralDurationSeconds;
            return m;
        }

        static void CheckRadialModel()
        {
            Vector3 origin = new Vector3(-16f, -3.8f, 22f);
            var m = MakeRadialModel(origin, out _);
            CheckRadialModelBody(m);
            Object.DestroyImmediate(m.parameters);
        }

        static void CheckRadialModelBody(GravitationalWaveModel m)
        {
            Vector3 origin = m.Origin;
            float v = 3.2f, delay = 8f;

            // Nothing anywhere before emission - including "behind" the source
            // (the old planar model wrongly showed waves on the negative side).
            Check(m.StrainAt(origin + new Vector3(0f, 0f, 5f), 0f) == 0f, "t=0: zero strain");
            Check(m.StrainAt(origin + new Vector3(10f, 0f, 0f), delay - 0.5f) == 0f,
                "before merger: zero strain ahead");
            Check(m.StrainAt(origin - new Vector3(10f, 0f, 0f), delay + 1f) == 0f,
                "position behind source: zero until wavefront arrives");
            Check(m.StrainAt(origin, delay) == 0f, "emission instant: zero (tau<=0 gate)");

            // Concentric: equal radius -> equal strain in every quadrant.
            float tHit = delay + 20f / v + 1f; // tau = 1 s after arrival at r=20
            float h1 = m.StrainAt(origin + new Vector3(20f, 0f, 0f), tHit);
            float h2 = m.StrainAt(origin + new Vector3(0f, 20f, 0f), tHit);
            float h3 = m.StrainAt(origin + new Vector3(0f, 0f, 20f), tHit);
            float h4 = m.StrainAt(origin - new Vector3(20f, 0f, 0f), tHit);
            Check(Mathf.Approximately(h1, h2) && Mathf.Approximately(h1, h3) &&
                  Mathf.Approximately(h1, h4) && h1 != 0f,
                "equal radius -> equal strain in all quadrants");

            // Causal delay: nothing until t = delay + r/v; finite packet: zero
            // again once tau >= 8 * ringdown.
            Check(m.StrainAt(origin + new Vector3(10f, 0f, 0f), delay + 10f / v - 0.01f) == 0f,
                "causal delay: front has not arrived");
            Check(m.StrainAt(origin + new Vector3(10f, 0f, 0f), delay + 10f / v + 0.5f) != 0f,
                "causal delay: nonzero inside packet");
            Check(m.StrainAt(origin + new Vector3(10f, 0f, 0f), delay + 10f / v + 33f) == 0f,
                "finite ringdown: zero after 8 * ringdown");

            // WavefrontDistance never leads the emission.
            Check(m.WavefrontDistance(delay * 0.5f) == 0f, "wavefront distance zero pre-emission");
            Check(Mathf.Approximately(m.WavefrontDistance(delay + 1f), v),
                "wavefront distance = (t - delay) * v");

            // Pause/reset determinism: pure function of (position, time).
            Check(m.StrainAt(origin + new Vector3(7f, 2f, -3f), 15.3f) ==
                  m.StrainAt(origin + new Vector3(7f, 2f, -3f), 15.3f),
                "strain deterministic in (position, time)");
        }

        // ----------------------------------------------------------- displacement
        static void CheckDisplacement()
        {
            Vector3 origin = new Vector3(-16f, -3.8f, 22f);
            var m = MakeRadialModel(origin, out _);

            // Test-mass field reference: masses arranged +/-u1 around the field
            // centre must move OPPOSITELY and purely transverse under the radial
            // wave - a basis taken at each mass's own position would degenerate
            // to zero because its offset is parallel to its own radial direction.
            Vector3 refPoint = new Vector3(-5f, 1.8f, 15f);
            m.GetTransverseAxes(refPoint, out Vector3 u1, out _);
            Vector3 dir = m.PropagationDirectionAt(refPoint);
            const float r = 1.2f;
            Vector3 pPlus = refPoint + u1 * r;
            Vector3 pMinus = refPoint - u1 * r;
            // Arrival + quarter period: h is near its ringdown peak.
            float tHit = 8f + m.PropagationDistance(refPoint) / 3.2f + 0.45f;

            Vector3 dPlus = m.DisplacementAt(pPlus, tHit, refPoint);
            Vector3 dMinus = m.DisplacementAt(pMinus, tHit, refPoint);
            Check(dPlus.magnitude > 1e-4f, "radial mode: off-axis mass actually moves");
            Check(Vector3.Distance(dPlus, -dMinus) < 1e-3f,
                "paired +/-u1 masses move oppositely");
            Check(Mathf.Abs(Vector3.Dot(dPlus, dir)) < dPlus.magnitude * 0.02f,
                "displacement is transverse to the local propagation direction");
            Check(m.DisplacementAt(pPlus, 0f, refPoint) == Vector3.zero,
                "no displacement before wavefront arrival");
            Object.DestroyImmediate(m.parameters);
        }

        // ---------------------------------------------------------------- sheet
        static void CheckSheet()
        {
            Vector3 origin = new Vector3(-16f, -3.8f, 22f);
            var m = MakeRadialModel(origin, out _);

            // Non-identity transform: translated AND rotated.
            var sheetGO = new GameObject("Sheet");
            sheetGO.transform.position = new Vector3(10f, 2f, -7f);
            sheetGO.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            var sheet = sheetGO.AddComponent<SpacetimeSheet>();
            sheet.model = m;
            sheet.size = new Vector2(20f, 20f);
            sheet.baseHeight = 1.5f;
            sheet.flexibility = 1f;

            var srcGO = new GameObject("WellSource");
            Vector3 srcWorld = sheetGO.transform.TransformPoint(new Vector3(3f, 0f, -2f));
            srcGO.transform.position = srcWorld;
            // A second well COINCIDENT with the first: wells must merge as a
            // maximum envelope, never sum to a doubled dent.
            var src3GO = new GameObject("CoincidentWell");
            src3GO.transform.position = srcWorld;
            var src2GO = new GameObject("InactiveWell");
            src2GO.transform.position = sheetGO.transform.TransformPoint(new Vector3(-4f, 0f, 4f));
            src2GO.SetActive(false);
            sheet.wells = new[]
            {
                new SpacetimeSheet.GravityWell { source = srcGO.transform, depth = 7f, radius = 3f },
                new SpacetimeSheet.GravityWell { source = src2GO.transform, depth = 7f, radius = 3f },
                new SpacetimeSheet.GravityWell { source = src3GO.transform, depth = 7f, radius = 3f },
            };

            // Isolated well: the dent minimum must sit under the live source's
            // LOCAL position - correct for a translated/rotated sheet.
            float t = 0f; // pre-emission: dents only
            float minY = float.MaxValue; Vector3 minLocal = Vector3.zero;
            for (float x = -9f; x <= 9f; x += 0.5f)
            for (float z = -9f; z <= 9f; z += 0.5f)
            {
                float y = sheet.EvaluateLocalSurface(new Vector3(x, 1.5f, z), t);
                if (y < minY) { minY = y; minLocal = new Vector3(x, y, z); }
            }
            Vector3 srcLocal = sheetGO.transform.InverseTransformPoint(srcWorld);
            Check(Mathf.Abs(minLocal.x - srcLocal.x) < 0.6f &&
                  Mathf.Abs(minLocal.z - srcLocal.z) < 0.6f,
                "isolated well minimum centred on live source (local)");
            Check(Mathf.Approximately(minY, 1.5f - 7f),
                "coincident wells: max envelope 7 m, not summed 14 m");
            Check(Mathf.Approximately(
                    sheet.EvaluateLocalSurface(new Vector3(-9f, 1.5f, 9f), t), 1.5f),
                "inactive well ignored");

            // Pre-emission: dents only, identical at any t < delay, no wave term.
            float ya = sheet.EvaluateLocalSurface(new Vector3(6f, 1.5f, 6f), 2f, out float ha);
            float yb = sheet.EvaluateLocalSurface(new Vector3(6f, 1.5f, 6f), 7.9f, out _);
            Check(ha == 0f && Mathf.Approximately(ya, yb),
                "pre-merger: dents only, no wave term, stable over time");
            var quiet = sheet.EvaluateLocalColor(new Vector3(6f, 1.5f, 6f), 2f);
            var lit = new Color(1f, 0.72f, 0.30f); // stretch tint
            Check(Mathf.Abs(quiet.r - lit.r) > 0.05f,
                "pre-merger: no wave highlight colour");

            // Dents follow the live well positions (source moved -> dent moved).
            srcGO.transform.position = sheetGO.transform.TransformPoint(new Vector3(5f, 0f, 5f));
            float moved = sheet.EvaluateLocalSurface(new Vector3(5f, 1.5f, 5f), t);
            Check(moved < -4f, "wells follow live source positions");

            // Wave term: h * exaggeration * 2 applied as height at emission+.
            // Choose t so the front sits 1 s past this vertex's in-plane distance.
            Vector3 wpt = sheetGO.transform.TransformPoint(new Vector3(6f, 1.5f, 6f));
            wpt.y = origin.y;
            float tHit = 8f + Vector3.Distance(wpt, origin) / 3.2f + 1f;
            sheet.EvaluateLocalSurface(new Vector3(6f, 1.5f, 6f), tHit, out float hh);
            Check(hh != 0f, "post-merger: wave strain reaches sheet points");
            Object.DestroyImmediate(m.parameters);
        }

        // ---------------------------------------------------------- interferometer
        static void CheckInterferometer()
        {
            Vector3 origin = new Vector3(-16f, -3.8f, 22f);
            var m = MakeRadialModel(origin, out _);
            Vector3 ifoPos = new Vector3(80f, 6f, 22f);

            var ifoGO = new GameObject("IF0");
            ifoGO.transform.position = ifoPos;
            ifoGO.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var ifo = ifoGO.AddComponent<LaserInterferometer>();
            ifo.model = m;
            ifo.armLength = 5f;

            var mirrorA = new GameObject("MirrorA").transform;
            mirrorA.SetParent(ifoGO.transform, false);
            var mirrorB = new GameObject("MirrorB").transform;
            mirrorB.SetParent(ifoGO.transform, false);
            ifo.mirrorA = mirrorA;
            ifo.mirrorB = mirrorB;
            ifo.outboundA = MakeLine(ifoGO.transform);
            ifo.inboundA = MakeLine(ifoGO.transform);
            ifo.outboundB = MakeLine(ifoGO.transform);
            ifo.inboundB = MakeLine(ifoGO.transform);
            ifo.signalTrace = MakeLine(ifoGO.transform);

            // Both arm axes are perpendicular to the outward radial direction.
            Vector3 dirAtIfo = m.PropagationDirectionAt(ifoPos);
            Vector3 axisA = ifoGO.transform.TransformDirection(Vector3.right).normalized;
            Vector3 axisB = ifoGO.transform.TransformDirection(Vector3.up).normalized;
            Check(Mathf.Abs(Vector3.Dot(axisA, dirAtIfo)) < 0.2f &&
                  Mathf.Abs(Vector3.Dot(axisB, dirAtIfo)) < 0.2f,
                "detector arms lie transverse to the radial propagation direction");

            // Aligned polarization: one arm in u1, the other in u2 -> opposite fractions.
            float tHit = 8f + Vector3.Distance(ifoPos, origin) / 3.2f + 0.7f;
            float fa = ifo.EvaluateArmFraction(axisA, ifoPos, tHit);
            float fb = ifo.EvaluateArmFraction(axisB, ifoPos, tHit);
            Check(fa != 0f && Mathf.Sign(fa) == -Mathf.Sign(fb),
                "aligned polarization: arm fractions have opposite signs");

            // Equal arms (no strain yet) -> dark port stays dark.
            ifo.Refresh(0f);
            Check(Mathf.Approximately(ifo.CurrentPhase, 0f) &&
                  Mathf.Approximately(ifo.DarkPortIntensity, 0f),
                "equal arms -> dark port zero");
            Check(LaserInterferometer.ComputeDarkPortIntensity(0.5f) > 0f,
                "nonzero phase -> nonzero dark-port light");

            // Mirrors and beam endpoints share the strained arm endpoints.
            ifo.Refresh(tHit);
            Check(Mathf.Approximately(mirrorA.localPosition.x, ifo.outboundA.GetPosition(1).x),
                "mirror A coincides with outbound beam end during strain");
            Check(Mathf.Abs(mirrorA.localPosition.x - ifo.armLength) > 0.01f,
                "mirror A actually displaced under strain");

            // Signal-trace determinism: analytic history, not accumulated frames.
            // Rewind to t=0 flattens it; re-jumping to tHit reproduces it exactly.
            var first = new Vector3[ifo.signalTrace.positionCount];
            ifo.signalTrace.GetPositions(first);
            ifo.Refresh(0f);
            bool flat = true;
            for (int i = 0; i < ifo.signalTrace.positionCount; i++)
                if (Mathf.Abs(ifo.signalTrace.GetPosition(i).y - (-2.1f)) > 1e-4f) flat = false;
            Check(flat, "signal trace flat after rewind to t=0");
            ifo.Refresh(tHit);
            var second = new Vector3[ifo.signalTrace.positionCount];
            ifo.signalTrace.GetPositions(second);
            bool identical = first.Length == second.Length;
            if (identical)
                for (int i = 0; i < first.Length; i++)
                    if (Vector3.Distance(first[i], second[i]) > 1e-4f) { identical = false; break; }
            Check(identical, "signal trace deterministic across rewind/replay");
            Object.DestroyImmediate(m.parameters);
        }

        static LineRenderer MakeLine(Transform parent)
        {
            var go = new GameObject("beam");
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            return lr;
        }

        // ------------------------------------------- ET: arbitrary 60-degree arms
        /// <summary>ET arms meet at 60 degrees in a horizontal plane and each arm
        /// is projected INDEPENDENTLY through deltaL/L = .5 h ex (d1^2 - d2^2) -
        /// nothing may force the two responses into opposite signs.</summary>
        static void CheckEtArms()
        {
            Vector3 origin = new Vector3(-16f, -3.8f, 22f);
            var m = MakeRadialModel(origin, out _);
            Vector3 etPos = new Vector3(82f, 1.5f, 26f);

            var go = new GameObject("ETStation");
            go.transform.position = etPos; // horizontal, unrotated
            var li = go.AddComponent<LaserInterferometer>();
            li.model = m;
            li.simulation = null;
            li.armAxisA = new Vector3(0.8660254f, 0f, 0.5f);   // 60 deg apart
            li.armAxisB = new Vector3(0.8660254f, 0f, -0.5f);
            li.armLength = 12f;

            Check(Mathf.Approximately(
                    Vector3.Dot(li.armAxisA.normalized, li.armAxisB.normalized), 0.5f),
                "station arms are 60 degrees apart in the horizontal plane");
            Check(Mathf.Abs(li.armAxisA.y) < 1e-6f && Mathf.Abs(li.armAxisB.y) < 1e-6f,
                "station arm axes are horizontal");

            // Independent projection: recompute the formula for BOTH arbitrary
            // axes - a hardcoded opposite-response shortcut would fail this.
            float tHit = 8f + Vector3.Distance(etPos, origin) / 3.2f + 0.8f;
            Vector3 wA = go.transform.TransformDirection(li.armAxisA.normalized).normalized;
            Vector3 wB = go.transform.TransformDirection(li.armAxisB.normalized).normalized;
            float fA = li.EvaluateArmFraction(wA, etPos, tHit);
            float fB = li.EvaluateArmFraction(wB, etPos, tHit);
            m.GetTransverseAxes(etPos, out Vector3 u1, out Vector3 u2);
            float h = m.StrainAt(etPos, tHit);
            float ex = m.parameters.displacementExaggeration;
            float expA = Mathf.Clamp(0.5f * h * ex *
                (Vector3.Dot(wA, u1) * Vector3.Dot(wA, u1) -
                 Vector3.Dot(wA, u2) * Vector3.Dot(wA, u2)),
                -li.maxArmFraction, li.maxArmFraction);
            float expB = Mathf.Clamp(0.5f * h * ex *
                (Vector3.Dot(wB, u1) * Vector3.Dot(wB, u1) -
                 Vector3.Dot(wB, u2) * Vector3.Dot(wB, u2)),
                -li.maxArmFraction, li.maxArmFraction);
            Check(Mathf.Approximately(fA, expA) && Mathf.Approximately(fB, expB),
                "each 60-degree arm uses the independent projection formula");
            Check(!(Mathf.Approximately(fA, 0f) && Mathf.Approximately(fB, 0f)),
                "ET arms actually respond to the wave");

            // Beam-splitter law used by the generated optics: the plate normal
            // is (axA - axB) so the +axA input beam reflects into +axB.
            Vector3 n = (li.armAxisA.normalized - li.armAxisB.normalized).normalized;
            Vector3 refl = Vector3.Reflect(li.armAxisA.normalized, n);
            Check(Vector3.Dot(refl, li.armAxisB.normalized) > 0.999f,
                "splitter normal (axA-axB) reflects arm-A input into arm-B");

            // Endpoints track the moving arms along THEIR axes.
            var mirrorA = new GameObject("mA").transform;
            mirrorA.SetParent(go.transform, false);
            var mirrorB = new GameObject("mB").transform;
            mirrorB.SetParent(go.transform, false);
            li.mirrorA = mirrorA; li.mirrorB = mirrorB;
            li.outboundA = MakeLine(go.transform);
            li.inboundA = MakeLine(go.transform);
            li.outboundB = MakeLine(go.transform);
            li.inboundB = MakeLine(go.transform);
            li.Refresh(tHit);
            float lenA = li.armLength * (1f + li.ArmFractionA);
            Check(Vector3.Distance(mirrorA.localPosition,
                    li.armAxisA.normalized * lenA) < 1e-4f,
                "mirror A rides its own 60-degree arm axis");
            Check(Mathf.Abs(Vector3.Distance(li.outboundA.GetPosition(1),
                    mirrorA.localPosition) - li.beamOffset) < 0.02f,
                "beam endpoint follows the displaced end mirror");
            Object.DestroyImmediate(m.parameters);
        }

        // -------------------------------------------- subtitle pagination --
        /// <summary>Measured pagination: every page must fit MaxLinesPerPage
        /// rendered lines inside the real Text rect width - Latin wrap, CJK
        /// measurement, explicit newlines and unbroken overlong tokens alike,
        /// with no character ever dropped.</summary>
        static void CheckSubtitlePagination()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Check(font != null, "builtin font available for measured pagination");
            if (font == null) return;
            var gen = new TextGenerator();
            var settings = new TextGenerationSettings
            {
                font = font, fontSize = 28, lineSpacing = 1f, richText = false,
                generationExtents = new Vector2(976f, 2000f),
                horizontalOverflow = HorizontalWrapMode.Wrap,
                verticalOverflow = VerticalWrapMode.Overflow,
                generateOutOfBounds = true, textAnchor = TextAnchor.UpperLeft,
            };
            int LineCount(string page)
            {
                gen.Populate(page, settings);
                return gen.lineCount;
            }
            void AllPagesFit(List<string> pages, string name)
            {
                Check(pages.Count > 0, name + ": produced pages");
                bool fit = true;
                foreach (var pg in pages) if (LineCount(pg) > 2) fit = false;
                Check(fit, name + ": every page <= 2 rendered lines");
            }
            void NoCharsDropped(List<string> pages, string source, string name)
            {
                string joined = string.Concat(pages).Replace(" ", "").Replace("\n", "");
                string src = source.Replace(" ", "").Replace("\n", "").Replace("\r", "");
                Check(joined == src, name + ": no characters truncated");
            }

            string latin = "The Einstein Telescope is a triangular underground " +
                "observatory with three corner stations and sixty degree arms, " +
                "each station hosting a nested low and high frequency pair.";
            var lp = RickSubtitles.PaginateForDisplay(latin, 976f, font, 28);
            AllPagesFit(lp, "latin paragraph");
            NoCharsDropped(lp, latin, "latin paragraph");

            string cjk = "爱因斯坦望远镜是一个三角形的地下观测站，由三个角站组成，" +
                "每个测站有一对六十度夹角的干涉臂以及嵌套的低频和高频探测器。";
            var cp = RickSubtitles.PaginateForDisplay(cjk, 976f, font, 28);
            AllPagesFit(cp, "cjk paragraph");
            NoCharsDropped(cp, cjk, "cjk paragraph");

            string hard = "line one\nline two is somewhat longer than the first " +
                "one and should paginate\nline three";
            var hp = RickSubtitles.PaginateForDisplay(hard, 976f, font, 28);
            AllPagesFit(hp, "explicit newlines");
            NoCharsDropped(hp, hard, "explicit newlines");

            string token = new string('x', 300);
            var tp = RickSubtitles.PaginateForDisplay(token, 976f, font, 28);
            AllPagesFit(tp, "unbroken 300-char token");
            NoCharsDropped(tp, token, "unbroken 300-char token");

            // Queue semantics on a live component (no play mode needed).
            var go = new GameObject("Subs");
            var rt2 = go.AddComponent<RectTransform>();
            rt2.sizeDelta = new Vector2(1000f, 100f);
            var txt = new GameObject("T").AddComponent<Text>();
            txt.transform.SetParent(rt2, false);
            txt.GetComponent<RectTransform>().sizeDelta = new Vector2(1000f, 100f);
            txt.font = font;
            txt.fontSize = 28;
            var subs = go.AddComponent<RickSubtitles>();
            subs.text = txt;
            subs.ShowNarration(latin);
            string first = txt.text;
            Check(!string.IsNullOrEmpty(first), "narration shows its first page");
            subs.AdvancePageForTest();
            Check(txt.text != first, "advance moves to the next page");
            // Narration during an answer updates the story queue WITHOUT
            // interrupting the answer being read.
            subs.ShowAnswer("short answer");
            Check(subs.IsShowingAnswer && txt.text.Contains("answer"),
                "answer interrupts the story queue");
            subs.ShowNarration("new story page");
            Check(subs.IsShowingAnswer,
                "new narration does not cut off a spoken answer");
            subs.AdvancePageForTest(); // finish the answer
            Check(!subs.IsShowingAnswer && txt.text.Contains("new story page"),
                "latest story page resumes after the answer");
            subs.AdvancePageForTest(); // finish the resumed story page too
            Check(!subs.IsShowingAnswer && txt.text == "",
                "story queue drains fully once the resumed page ends");
        }

        // ------------------------------------------------ four-slot wave library
        static void CheckWaveLibrary()
        {
            var p = ScriptableObject.CreateInstance<SimulationParameters>();
            p.strainAmplitude = 0.5f;
            p.frequencyHz = 0.55f;
            p.displacementExaggeration = 1.6f;
            var libGO = new GameObject("Lib");
            var lib = libGO.AddComponent<WaveLibrary>();
            lib.parameters = p;
            lib.disablePersistence = true;

            // Selection alone NEVER writes, loads or touches parameters.
            lib.SelectSlot(2);
            Check(lib.SelectedSlot == 2, "select slot 3 registers selection");
            Check(!lib.HasSlot(0) && !lib.HasSlot(1) && !lib.HasSlot(2) && !lib.HasSlot(3),
                "selecting a card writes nothing");
            float ampBefore = p.strainAmplitude;
            lib.SelectSlot(0);
            Check(Mathf.Approximately(p.strainAmplitude, ampBefore),
                "selecting a slot never changes parameters");

            // Save to an empty slot writes ONLY that slot.
            lib.SelectSlot(2);
            lib.SaveSelected();
            Check(lib.HasSlot(2) && !lib.HasSlot(0) && !lib.HasSlot(1) && !lib.HasSlot(3),
                "saving slot 3 leaves slots 0-1-2 empty");
            Check(Mathf.Approximately(lib.GetSnapshot(2).amplitude, 0.5f),
                "snapshot stores the current parameters");

            // Saving a different slot must not mutate an existing snapshot.
            p.strainAmplitude = 0.9f;
            lib.SelectSlot(0);
            lib.SaveSelected();
            Check(lib.HasSlot(0) &&
                  Mathf.Approximately(lib.GetSnapshot(2).amplitude, 0.5f),
                "saving slot 0 leaves slot 3 untouched");

            // Occupied save arms confirm; only CONFIRM writes.
            p.strainAmplitude = 0.33f;
            lib.SaveSelected();
            Check(lib.PendingOverwrite &&
                  Mathf.Approximately(lib.GetSnapshot(0).amplitude, 0.9f),
                "occupied save requires explicit confirmation");
            lib.CancelOverwrite();
            Check(!lib.PendingOverwrite &&
                  Mathf.Approximately(lib.GetSnapshot(0).amplitude, 0.9f),
                "cancelled overwrite leaves the snapshot unchanged");
            lib.SaveSelected();
            lib.ConfirmOverwrite();
            Check(Mathf.Approximately(lib.GetSnapshot(0).amplitude, 0.33f),
                "confirm overwrite writes the current parameters");

            // A parameter change cancels a pending overwrite.
            lib.SaveSelected();
            lib.OnParametersChanged();
            Check(!lib.PendingOverwrite, "parameter change cancels pending overwrite");
            // A selection change also cancels it.
            lib.SaveSelected();
            lib.SelectSlot(1);
            Check(!lib.PendingOverwrite, "selection change cancels pending overwrite");

            // Invalid indices are inert.
            int sel = lib.SelectedSlot;
            lib.SelectSlot(-1); lib.SelectSlot(9);
            Check(lib.SelectedSlot == sel, "invalid slot index has no side effects");
            Check(!lib.ImportJson("{ not json") && !lib.ImportJson(null),
                "malformed JSON fails safely");
            Check(lib.GetSnapshot(9) == null && lib.GetSnapshot(-2) == null,
                "invalid snapshot index returns null");

            // Pure JSON round-trip preserves which slots are occupied.
            string json = lib.ExportJson();
            var lib2GO = new GameObject("Lib2");
            var lib2 = lib2GO.AddComponent<WaveLibrary>();
            lib2.parameters = p;
            lib2.disablePersistence = true;
            Check(lib2.ImportJson(json), "v2 JSON imports");
            Check(lib2.HasSlot(0) && lib2.HasSlot(2) && !lib2.HasSlot(1) && !lib2.HasSlot(3),
                "JSON preserves distinct occupied slot indices");
            Check(lib2.GetSnapshot(0) != lib.GetSnapshot(0),
                "imported snapshots are detached copies");
            Check(lib2.GetSnapshot(2) != lib.GetSnapshot(2), "slot copies independent");

            // Load restores the stored values exactly.
            p.strainAmplitude = 0.77f;
            p.frequencyHz = 3f;
            lib2.SelectSlot(2);
            lib2.LoadSelected();
            Check(Mathf.Approximately(p.strainAmplitude, 0.5f) &&
                  Mathf.Approximately(p.frequencyHz, 0.55f),
                "load restores the exact stored values");

            // Legacy migration: the old three-field format keeps its values but
            // inherits the CURRENT defaults for everything it never stored, so
            // a migrated wave never replays with zero durations/speed.
            var lib3GO = new GameObject("Lib3");
            var lib3 = lib3GO.AddComponent<WaveLibrary>();
            lib3.parameters = p;
            lib3.disablePersistence = true;
            Check(lib3.ImportLegacyJson(
                    "{\"waves\":[{\"name\":\"L\",\"amplitude\":0.6," +
                    "\"frequencyHz\":1.5,\"exaggeration\":2.0}]}"),
                "legacy JSON imports through the pure API");
            Check(lib3.HasSlot(0) &&
                  Mathf.Approximately(lib3.GetSnapshot(0).amplitude, 0.6f) &&
                  lib3.GetSnapshot(0).inspiralDurationSeconds > 0f &&
                  lib3.GetSnapshot(0).ringdownSeconds > 0f &&
                  lib3.GetSnapshot(0).visualizationSpeedMps > 0f &&
                  lib3.GetSnapshot(0).packetDurationSeconds > 0f,
                "legacy wave inherits nonzero durations and speed");

            // Malformed/out-of-range v2 preserves the populated slots wholesale.
            string good = lib.ExportJson();
            Check(lib.ImportJson(good), "valid v2 re-imports");
            string bad = "{\"version\":2,\"slots\":[{\"occupied\":true,\"wave\":" +
                "{\"amplitude\":9.9,\"frequencyHz\":1.0,\"exaggeration\":1.0," +
                "\"phaseRadians\":0.0,\"waveMode\":0,\"propagationAxis\":0," +
                "\"packetDurationSeconds\":4.0,\"inspiralDurationSeconds\":8.0," +
                "\"ringdownSeconds\":3.0,\"postMergerResidual\":0.1," +
                "\"visualizationSpeedMps\":3.2}}]}";
            Check(!lib.ImportJson(bad), "out-of-range v2 blob is rejected whole");
            Check(lib.HasSlot(0) && Mathf.Approximately(lib.GetSnapshot(0).amplitude, 0.33f),
                "rejected blob leaves populated slots intact");

            // A failed save must surface as an error hint, never success.
            var hintGO = new GameObject("Hint");
            var hint = hintGO.AddComponent<Text>();
            hint.font = font2;
            lib.hintText = hint;
            lib.saveOverride = () => false;
            lib.SelectSlot(1);
            lib.SaveSelected();               // empty slot: writes memory + fails disk
            Check(lib.HasSlot(1) && !string.IsNullOrEmpty(hint.text) &&
                  hint.text.Contains("write rejected"),
                "failed save is reported as an error hint");
            lib.saveOverride = () => true;
            lib.SelectSlot(3);
            lib.SaveSelected();
            Check(!hint.text.Contains("write rejected"),
                "successful save clears the error hint");
            lib.hintText = null;
            lib.saveOverride = null;
            Object.DestroyImmediate(p);
        }

        static Font font2 => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // ------------------------------------------------ playback semantics
        static void CheckPlaybackSemantics()
        {
            Vector3 origin = new Vector3(-16f, -3.8f, 22f);
            var m = MakeRadialModel(origin, out var p);
            var simGO = new GameObject("Sim");
            var sim = simGO.AddComponent<SimulationController>();
            sim.parameters = p;

            var ship = new GameObject("Ship").transform;
            ship.position = new Vector3(-16f, 1.5f, 4f);
            ship.rotation = Quaternion.Euler(0f, 35f, 0f);
            var earth = new GameObject("Earth").transform;
            earth.position = new Vector3(52f, 6.8f, 22f);
            var binary = new GameObject("Binary").transform;
            binary.position = origin;
            var et = new GameObject("ET").transform;
            et.position = new Vector3(82f, 1.5f, 26f);

            var dirGO = new GameObject("Story");
            var story = dirGO.AddComponent<StoryDirector>();
            story.simulation = sim;
            story.model = m;
            story.ship = ship;
            story.earth = earth;
            story.binary = binary;
            story.ligo = et;
            story.CaptureInitialPose();

            // Preview replay at the parked Waiting phase (binary focus): same
            // ship pose, wave clock at the arrival-relative start, choreography
            // suspended.
            ship.position = new Vector3(3f, 2f, -7f);
            Vector3 parked = ship.position;
            story.ReplayCurrentWave();
            float expectedStart = Mathf.Max(0f,
                m.emissionDelay + m.PropagationDistance(binary.position) /
                p.visualizationSpeedMps - 1f);
            Check(ship.position == parked && story.InPreview && sim.IsPlaying,
                "preview replay runs the wave without moving the ship");
            Check(Mathf.Abs(sim.SimulationTime - expectedStart) < 0.01f,
                "preview starts one second before arrival at the focus");

            // Reset: same point, paused.
            story.ResetCurrentWave();
            Check(!sim.IsPlaying && story.InPreview &&
                  Mathf.Abs(sim.SimulationTime - expectedStart) < 0.01f,
                "preview reset is paused at the same start time");

            // START after a preview does not inherit the preview clock: the
            // journey begins at t=0 from the stored parked pose.
            ship.position = new Vector3(9f, 4f, -2f);
            story.StartMission();
            Check(sim.IsPlaying && sim.SimulationTime < 0.01f && !story.InPreview &&
                  Vector3.Distance(ship.position, new Vector3(-16f, 1.5f, 4f)) < 1e-4f,
                "start mission clears preview state and begins at t=0 parked");
            sim.Pause();

            // Mid-transit: replay/reset are hard no-ops so a preview can never
            // freeze the journey (the buttons gate on the same CanPreview).
            var phaseField = typeof(StoryDirector).GetField("_phase",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            var phaseType = typeof(StoryDirector).GetNestedType("Phase",
                System.Reflection.BindingFlags.NonPublic);
            if (phaseField == null || phaseType == null)
            {
                Check(false, "story phase is reachable for transit-state tests");
            }
            else
            {
                object riding = System.Enum.Parse(phaseType, "Riding");
                phaseField.SetValue(story, riding);
                Check(!story.CanPreview, "mid-transit CanPreview is false");
                float tBefore = sim.SimulationTime;
                Vector3 posBefore = ship.position;
                story.ReplayCurrentWave();
                story.ResetCurrentWave();
                Check(!story.InPreview &&
                      Mathf.Approximately(sim.SimulationTime, tBefore) &&
                      ship.position == posBefore,
                    "replay/reset are no-ops during transit phases");
                phaseField.SetValue(story, System.Enum.Parse(phaseType, "Waiting"));
            }

            // Restart mission: parked pose, t=0, paused, preview cleared.
            ship.position = new Vector3(50f, 9f, 9f);
            story.RestartMission();
            Check(Vector3.Distance(ship.position, new Vector3(-16f, 1.5f, 4f)) < 1e-4f &&
                  Quaternion.Angle(ship.rotation, Quaternion.Euler(0f, 35f, 0f)) < 1e-3f,
                "restart mission restores the stored ship pose");
            Check(!sim.IsPlaying && sim.SimulationTime == 0f && !story.InPreview,
                "restart mission is paused at t=0 with preview cleared");

            // TogglePlayback at the parked start begins the mission.
            story.TogglePlayback();
            Check(sim.IsPlaying, "toggle at Waiting starts the mission");
            sim.Pause();
            Check(!sim.IsPlaying, "pause holds the clock");

            // Black-hole seek sync: pair/remnant swap instantly, no fake merger.
            var binGO = new GameObject("BHB");
            var bhb = binGO.AddComponent<BlackHoleBinary>();
            var hA = new GameObject("A").transform; hA.SetParent(binGO.transform, false);
            var hB = new GameObject("B").transform; hB.SetParent(binGO.transform, false);
            var rem = new GameObject("Rem").transform; rem.SetParent(binGO.transform, false);
            bhb.blackHoleA = hA; bhb.blackHoleB = hB; bhb.mergedHole = rem;
            bhb.simulation = sim;
            bhb.inspiralDuration = p.inspiralDurationSeconds;
            sim.RestartAt(p.inspiralDurationSeconds + 2f, false);
            bhb.SyncToSimulationTime();
            Check(!hA.gameObject.activeSelf && !hB.gameObject.activeSelf &&
                  rem.gameObject.activeSelf && bhb.HasMerged,
                "seek past merger: remnant shown immediately");
            sim.RestartAt(2f, false);
            bhb.SyncToSimulationTime();
            Check(hA.gameObject.activeSelf && hB.gameObject.activeSelf &&
                  !rem.gameObject.activeSelf && !bhb.HasMerged,
                "seek before merger: pair restored without a second merger");
            Object.DestroyImmediate(p);
        }

        // ------------------------------------------- generated assets + open scene
        static void CheckGeneratedAssets()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/M_Earth.mat");
            Check(mat != null, "M_Earth material exists");
            if (mat != null)
            {
                Check(mat.mainTexture != null, "Earth material keeps the Blue Marble texture");
                Check(mat.mainTextureScale == Vector2.one && mat.mainTextureOffset == Vector2.zero,
                    "full-globe UV tiling 1 / offset 0");
                Check(mat.shader != null && mat.shader.name.Contains("EarthSurface"),
                    "matte EarthSurface shader assigned");
            }

            var imp = AssetImporter.GetAtPath($"{Root}/Textures/Earth/earth-blue-marble.jpg")
                as TextureImporter;
            Check(imp != null && imp.wrapModeU == TextureWrapMode.Repeat &&
                  imp.wrapModeV == TextureWrapMode.Clamp,
                "equirectangular wrap: U repeat / V clamp");

            // The generated scene must contain the new physics rig and no occluding
            // shell. Only open Main.unity when it is not loaded already - never
            // close a scene the user had open.
            var main = SceneManager.GetSceneByPath($"{Root}/Scenes/Main.unity");
            bool openedHere = false;
            if (!main.isLoaded)
            {
                if (!System.IO.File.Exists($"{Root}/Scenes/Main.unity"))
                {
                    Debug.LogWarning("[Project8]   SKIP  Main.unity not generated yet");
                    return;
                }
                main = EditorSceneManager.OpenScene($"{Root}/Scenes/Main.unity",
                    OpenSceneMode.Additive);
                openedHere = true;
            }
            try
            {
                bool shell = false;
                GameObject earthGO = null;
                BlackHoleBinary binary = null;
                GravitationalWaveModel model = null;
                SpacetimeSheet sheet = null;
                LaserInterferometer ifo = null;
                EinsteinTelescopeDisplay et = null;
                RickSubtitles subs = null;
                PlaybackDock dock = null;
                CockpitControls cockpit = null;
                int textMeshCount = 0;
                foreach (var root in main.GetRootGameObjects())
                {
                    if (root.name == "AtmosphereGlow" ||
                        (root.name == "Earth" &&
                         root.transform.Find("AtmosphereGlow") != null))
                        shell = true;
                    if (root.name == "Earth") earthGO = root;
                    if (binary == null) binary = root.GetComponent<BlackHoleBinary>();
                    if (model == null) model = root.GetComponent<GravitationalWaveModel>();
                    if (sheet == null) sheet = root.GetComponent<SpacetimeSheet>();
                    if (ifo == null) ifo = root.GetComponentInChildren<LaserInterferometer>(true);
                    if (et == null) et = root.GetComponentInChildren<EinsteinTelescopeDisplay>(true);
                    if (subs == null) subs = root.GetComponentInChildren<RickSubtitles>(true);
                    if (dock == null) dock = root.GetComponentInChildren<PlaybackDock>(true);
                    if (cockpit == null) cockpit = root.GetComponent<CockpitControls>();
                    textMeshCount += root.GetComponentsInChildren<TextMesh>(true).Length;
                }
                Check(!shell, "generated Main scene has no occluding AtmosphereGlow");
                Check(textMeshCount == 0,
                    "no floating TextMesh prose anywhere (sheet / binary / ET)");

                if (model == null)
                {
                    Check(false, "generated Main scene has a GravitationalWaveModel");
                }
                else
                {
                    Check(model.radialPropagation,
                        "generated model: radialPropagation enabled");
                    Check(model.parameters != null && model.emissionDelay > 0f &&
                          Mathf.Approximately(model.emissionDelay,
                              model.parameters.inspiralDurationSeconds),
                        "generated model: emissionDelay == inspiral duration");
                    Check(model.StrainAt(earthGO != null ? earthGO.transform.position
                                : new Vector3(52f, 6.8f, 22f), 0f) == 0f,
                        "generated model: zero strain at Earth at t=0");
                }

                if (sheet == null || binary == null)
                {
                    Check(false, "generated Main scene has sheet + binary wells");
                }
                else
                {
                    Check(binary.mergedHole != null &&
                          !binary.mergedHole.gameObject.activeSelf,
                        "merged remnant well source exists and starts inactive");
                    // Live-well alignment: the dent minimum must sit under the
                    // binary's live hole transform in sheet-local space.
                    if (binary.blackHoleA != null)
                    {
                        Vector3 holeLocal = sheet.transform
                            .InverseTransformPoint(binary.blackHoleA.position);
                        float yUnder = sheet.EvaluateLocalSurface(
                            new Vector3(holeLocal.x, sheet.baseHeight, holeLocal.z), 0f);
                        Check(yUnder < -4f,
                            "live well dent centred under the live black-hole transform");
                    }
                }

                if (ifo == null)
                {
                    Check(false, "generated Main scene has a LaserInterferometer");
                }
                else
                {
                    Check(ifo.mirrorA != null && ifo.mirrorB != null &&
                          ifo.outputBeam != null && ifo.signalTrace != null,
                        "generated detector: mirrors, output beam and trace wired");
                }

                // ---- Einstein Telescope display -------------------------------
                if (et == null || et.detectors == null || et.detectors.Length != 6)
                {
                    Check(false, "generated scene has an ET display with six lanes");
                }
                else
                {
                    int prevSel = et.SelectedDetector;
                    bool prevDetail = et.DetailVisible;
                    Check(Quaternion.Angle(et.transform.rotation,
                              Quaternion.identity) < 0.01f,
                        "ET triangle lies horizontal (no tilt)");
                    var v = et.vertices;
                    Check(v != null && v.Length == 3 &&
                          Mathf.Approximately(v[0].y, v[1].y) &&
                          Mathf.Approximately(v[1].y, v[2].y),
                        "all three ET vertices share one height");
                    Check(Mathf.Approximately(Vector3.Distance(v[0], v[1]), 12f) &&
                          Mathf.Approximately(Vector3.Distance(v[1], v[2]), 12f) &&
                          Mathf.Approximately(Vector3.Distance(v[2], v[0]), 12f),
                        "ET triangle sides are the displayed 12 m");
                    for (int s = 0; s < 3; s++)
                    {
                        var lane = et.detectors[s * 2];
                        Check(lane != null &&
                              Mathf.Approximately(Vector3.Dot(
                                  lane.armAxisA.normalized,
                                  lane.armAxisB.normalized), 0.5f),
                            $"station D{s + 1} arms meet at 60 degrees");
                    }
                    Check(et.detectors[0].gameObject != et.detectors[1].gameObject,
                        "LF and HF are separate lane objects, not one shared object");
                    // Nested lanes are horizontally staggered inward along the
                    // bisector - they must not sit on identical collinear paths.
                    var lf0 = et.detectors[0].transform.localPosition;
                    var hf0 = et.detectors[1].transform.localPosition;
                    Vector3 flat = lf0 - hf0; flat.y = 0f;
                    Check(flat.magnitude > 0.05f,
                        "LF/HF lanes carry distinct inward horizontal offsets");
                    // Selection detail does not overlay every station.
                    et.SelectDetector(1);
                    bool d1On = et.detailRoots != null && et.detailRoots.Length > 1 &&
                                et.detailRoots[1] != null &&
                                et.detailRoots[1].gameObject.activeSelf;
                    bool d0Off = et.detailRoots != null && et.detailRoots.Length > 0 &&
                                 et.detailRoots[0] != null &&
                                 !et.detailRoots[0].gameObject.activeSelf;
                    Check(d1On && d0Off,
                        "selected station shows detail; others stay collapsed");
                    Check(et.statusText != null && et.statusText.text.Contains("2"),
                        "station indicator text reflects the selection");
                    et.SetDetailVisible(false);
                    Check(et.detailRoots[1] == null ||
                          !et.detailRoots[1].gameObject.activeSelf,
                        "detail toggle collapses the selected optics too");
                    Check(et.stationButtons != null && et.stationButtons.Length == 3,
                        "ET station selector buttons wired to the display");
                    // Restore the serialized selection state, not a hardcoded one.
                    et.SelectDetector(prevSel);
                    et.SetDetailVisible(prevDetail);
                }

                // ---- subtitle strip + persistent dock --------------------------
                if (subs == null)
                {
                    Check(false, "generated scene has the shared subtitle presenter");
                }
                else
                {
                    var c = subs.GetComponent<Canvas>();
                    Check(c != null && c.gameObject.activeSelf,
                        "subtitle canvas exists and stays active");
                    Check(subs.GetComponent<GraphicRaycaster>() == null &&
                          subs.GetComponentInChildren<Button>(true) == null &&
                          subs.GetComponentInChildren<Image>(true) == null,
                        "subtitles: no raycaster, buttons or background image");
                    Check(subs.text != null && !subs.text.raycastTarget &&
                          Mathf.Approximately(subs.text.fontSize, 28),
                        "subtitle text is non-interactive, 28 pt");
                }

                if (dock == null || cockpit == null)
                {
                    Check(false, "generated scene has the persistent playback dock");
                }
                else
                {
                    Check(dock.simulation != null && dock.story != null,
                        "playback dock wired to simulation + story");
                    bool insideDrawer =
                        (cockpit.controlPanel != null &&
                         dock.transform.IsChildOf(cockpit.controlPanel.transform)) ||
                        (cockpit.secondaryPanel != null &&
                         dock.transform.IsChildOf(cockpit.secondaryPanel.transform));
                    Check(!insideDrawer,
                        "playback dock lives outside every drawer");
                    Check(cockpit.secondaryPanel != null &&
                          cockpit.secondaryPanel.name.Contains("MissionPanel") &&
                          cockpit.controlPanel != null &&
                          cockpit.controlPanel.name.Contains("ControlPanel") &&
                          !cockpit.secondaryPanel.activeSelf,
                        "Mission & Help + Controls are separate closed drawers");

                    // PanelHitArea: blank glass must still catch pointer rays.
                    int hitAreas = 0;
                    foreach (var panel in new[]
                             { cockpit.controlPanel, cockpit.secondaryPanel,
                               cockpit.compactPanel })
                        if (panel != null &&
                            panel.GetComponentInChildren<Image>(true) != null)
                            foreach (var img in panel
                                         .GetComponentsInChildren<Image>(true))
                                if (img.name == "PanelHitArea" && img.raycastTarget)
                                    hitAreas++;
                    Check(hitAreas >= 3,
                        "each drawer face carries a PanelHitArea ray blocker");

                    // Progressive disclosure: the console tabs live on the
                    // control drawer with exactly one page visible.
                    var tabs = cockpit.controlPanel != null
                        ? cockpit.controlPanel.GetComponent<ConsoleTabs>() : null;
                    Check(tabs != null && tabs.wavePage != null &&
                          tabs.etPage != null &&
                          tabs.wavePage.activeSelf != tabs.etPage.activeSelf,
                        "console tabs: exactly one page visible");

                    // Rect-overlap assertions on the transport row and the two
                    // slot-action positions (Save/Load vs Confirm/Cancel share
                    // positions intentionally - checked among actives only).
                    // The drawer starts closed; open it for the measurement.
                    if (cockpit.controlPanel != null)
                    {
                        bool drawerWasActive = cockpit.controlPanel.activeSelf;
                        if (!drawerWasActive) cockpit.controlPanel.SetActive(true);
                        var rects = cockpit.controlPanel
                            .GetComponentsInChildren<RectTransform>(true);
                        RectTransform Find(string n)
                        {
                            foreach (var r in rects) if (r.name == n) return r;
                            return null;
                        }
                        bool NoOverlap(params string[] names)
                        {
                            var rs = new System.Collections.Generic.List<Rect>();
                            foreach (string n in names)
                            {
                                var t = Find(n);
                                if (t == null || !t.gameObject.activeInHierarchy)
                                    continue;
                                var c = new Vector3[4];
                                t.GetWorldCorners(c);
                                var r = Rect.MinMaxRect(
                                    Mathf.Min(c[0].x, c[2].x),
                                    Mathf.Min(c[0].y, c[2].y),
                                    Mathf.Max(c[0].x, c[2].x),
                                    Mathf.Max(c[0].y, c[2].y));
                                foreach (var o in rs)
                                    if (r.xMin < o.xMax - 0.0001f &&
                                        r.xMax > o.xMin + 0.0001f &&
                                        r.yMin < o.yMax - 0.0001f &&
                                        r.yMax > o.yMin + 0.0001f)
                                        return false;
                                rs.Add(r);
                            }
                            return true;
                        }
                        Check(NoOverlap("Btn_Replay", "Btn_Reset", "Btn_SpeedQ",
                                        "Btn_SpeedHalf", "Btn_Speed1"),
                            "transport row buttons do not overlap");
                        Check(NoOverlap("Btn_Save", "Btn_Load",
                                        "Btn_Ride", "Btn_Predict",
                                        "Btn_TabWave", "Btn_TabET"),
                            "slot actions / mission tools / tabs do not overlap");
                        Check(NoOverlap("Card_Wave0", "Card_Wave1",
                                        "Card_Wave2", "Card_Wave3"),
                            "the four slot cards do not overlap");
                        if (!drawerWasActive) cockpit.controlPanel.SetActive(false);
                    }
                }
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(main, true);
            }
        }
    }
}
