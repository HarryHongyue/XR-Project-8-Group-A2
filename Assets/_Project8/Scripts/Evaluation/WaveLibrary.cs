using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Project8.Core;
using Project8.Story;
using Project8.UI;

namespace Project8.Evaluation
{
    /// <summary>
    /// Saved waves for the Wave Lab: exactly FOUR independent optional snapshots
    /// of the full SimulationParameters. Card click only selects - it never
    /// saves, loads, replays or touches the clock. Save writes into the selected
    /// slot; an occupied slot requires an explicit Confirm Overwrite (cancelled
    /// by any parameter change, selection change or Cancel). Load applies the
    /// stored snapshot and resets the wave preview paused at the current
    /// observation point - it never moves the ship or restarts the mission.
    ///
    /// Persistence: JSON v2 with four explicit slots (nulls preserved as
    /// occupied=false entries). In the editor the file lives INSIDE the
    /// workspace at .tools/UserData/saved_waves_v2.json; standalone builds use
    /// Application.persistentDataPath. A legacy single-file save list is
    /// migrated read-only - the original file is never modified.
    /// </summary>
    public class WaveLibrary : MonoBehaviour
    {
        public const int SlotCount = 4;

        [Header("References")]
        public SimulationParameters parameters;
        public SimulationController simulation;
        public StoryDirector story;
        public GravitationalWaveModel model;

        [Header("Widgets (assigned by the scene builder)")]
        public Button[] slotButtons;
        public Text[] slotTexts;
        public Text hintText;
        public Button saveButton;
        public Button loadButton;
        public Button confirmButton;
        public Button cancelButton;
        public Text saveButtonText;
        public Text loadButtonText;

        [System.Serializable]
        public class SavedWave
        {
            public string name;
            public float amplitude;
            public float frequencyHz;
            public float exaggeration;
            public float phaseRadians;
            public WaveMode waveMode;
            public PropagationAxis propagationAxis;
            public float packetDurationSeconds;
            public float inspiralDurationSeconds;
            public float ringdownSeconds;
            public float postMergerResidual;
            public float visualizationSpeedMps;

            public SavedWave Clone()
            {
                return (SavedWave)MemberwiseClone();
            }
        }

        // JSON shapes kept small and explicit so v2 preserves empty slots.
        [System.Serializable]
        class SlotEntry { public bool occupied; public SavedWave wave; }
        [System.Serializable]
        class SaveFileV2 { public int version; public List<SlotEntry> slots; }
        [System.Serializable]
        class LegacyFile { public List<SavedWave> waves; }

        readonly SavedWave[] _slots = new SavedWave[SlotCount];
        public int SelectedSlot { get; private set; }
        /// <summary>An occupied slot is armed for overwrite; Confirm performs it.</summary>
        public bool PendingOverwrite { get; private set; }
        /// <summary>Regression/testing: skip all file IO (in-memory semantics only).</summary>
        public bool disablePersistence;
        /// <summary>Optional injectable persistence writer for tests: return
        /// false to simulate a failed write without touching the filesystem.</summary>
        public System.Func<bool> saveOverride;
        /// <summary>Sticky persistence error - shown in the hint line until the
        /// next successful action so a failed write can never look like success.</summary>
        string _persistenceError;

        static readonly Color CardIdle = new Color(0.16f, 0.22f, 0.32f, 0.50f);
        static readonly Color CardChosen = new Color(0.30f, 0.52f, 0.72f, 0.70f);
        static readonly Color CardArmed = new Color(0.62f, 0.36f, 0.24f, 0.70f);

        void Start()
        {
            Load();
            if (parameters != null) parameters.Changed += OnParametersChanged;
            RefreshSlots();
        }

        void OnDestroy()
        {
            if (parameters != null) parameters.Changed -= OnParametersChanged;
        }

        /// <summary>Any parameter edit makes an armed overwrite stale - cancel
        /// it. Public so editor regression can drive the same code path.</summary>
        public void OnParametersChanged()
        {
            if (PendingOverwrite) PendingOverwrite = false;
            RefreshSlots();
        }

        // ---------------------------------------------------------- selection
        /// <summary>Click a card: selection ONLY. No save, load, replay or time
        /// change happens here - and an armed overwrite is cancelled.</summary>
        public void SelectSlot(int index)
        {
            if (index < 0 || index >= SlotCount) return;
            SelectedSlot = index;
            PendingOverwrite = false;
            RefreshSlots();
        }

        public bool HasSlot(int index) =>
            index >= 0 && index < SlotCount && _slots[index] != null;

        /// <summary>Detached copy of a stored snapshot (tests/inspection).</summary>
        public SavedWave GetSnapshot(int index) =>
            HasSlot(index) ? _slots[index].Clone() : null;

        // ------------------------------------------------------------- save
        /// <summary>Save the CURRENT parameters into the selected slot. On an
        /// empty slot this writes immediately; on an occupied slot it arms the
        /// inline Confirm Overwrite step instead of touching data.</summary>
        public void SaveSelected()
        {
            if (!HasSlot(SelectedSlot))
            {
                WriteSlot(SelectedSlot);
                Save();
                RefreshSlots();
                return;
            }
            PendingOverwrite = true;
            RefreshSlots();
        }

        /// <summary>Second explicit step: writes the current parameters over the
        /// armed occupied slot. A no-op unless armed and still current.</summary>
        public void ConfirmOverwrite()
        {
            if (!PendingOverwrite) return;
            PendingOverwrite = false;
            WriteSlot(SelectedSlot);
            Save();
            RefreshSlots();
        }

        public void CancelOverwrite()
        {
            PendingOverwrite = false;
            RefreshSlots();
        }

        /// <summary>Detached snapshot of the FULL parameter set.</summary>
        static SavedWave Snapshot(SimulationParameters p)
        {
            var w = new SavedWave();
            if (p == null) return w;
            w.amplitude = p.strainAmplitude;
            w.frequencyHz = p.frequencyHz;
            w.exaggeration = p.displacementExaggeration;
            w.phaseRadians = p.phaseRadians;
            w.waveMode = p.waveMode;
            w.propagationAxis = p.propagationAxis;
            w.packetDurationSeconds = p.packetDurationSeconds;
            w.inspiralDurationSeconds = p.inspiralDurationSeconds;
            w.ringdownSeconds = p.ringdownSeconds;
            w.postMergerResidual = p.postMergerResidual;
            w.visualizationSpeedMps = p.visualizationSpeedMps;
            return w;
        }

        void WriteSlot(int index)
        {
            if (parameters == null || index < 0 || index >= SlotCount) return;
            var w = Snapshot(parameters);
            w.name = "Wave " + (index + 1);
            _slots[index] = w;
        }

        /// <summary>Legacy saves only carried four fields - fill the rest from
        /// the CURRENT parameters (or fresh defaults) so a migrated wave still
        /// has working inspiral/ringdown/packet/speed values, never zeros.</summary>
        SavedWave FromLegacy(SavedWave legacy)
        {
            var w = Snapshot(parameters != null ? parameters
                : ScriptableObject.CreateInstance<SimulationParameters>());
            w.name = legacy.name;
            w.amplitude = legacy.amplitude;
            w.frequencyHz = legacy.frequencyHz;
            w.exaggeration = legacy.exaggeration;
            return w;
        }

        // ------------------------------------------------------------- load
        /// <summary>Apply the selected snapshot: parameters restored, the wave
        /// preview is reset PAUSED at the current observation point. The ship
        /// does not move and the mission does not restart.</summary>
        public void LoadSelected()
        {
            if (!HasSlot(SelectedSlot) || parameters == null) return;
            // Mid-transit a load would freeze the journey - disabled there.
            if (story != null && !story.CanPreview) return;
            PendingOverwrite = false;
            var w = _slots[SelectedSlot];
            parameters.strainAmplitude = w.amplitude;
            parameters.frequencyHz = w.frequencyHz;
            parameters.displacementExaggeration = w.exaggeration;
            parameters.phaseRadians = w.phaseRadians;
            parameters.waveMode = w.waveMode;
            parameters.propagationAxis = w.propagationAxis;
            parameters.packetDurationSeconds = w.packetDurationSeconds;
            parameters.inspiralDurationSeconds = w.inspiralDurationSeconds;
            parameters.ringdownSeconds = w.ringdownSeconds;
            parameters.postMergerResidual = w.postMergerResidual;
            parameters.visualizationSpeedMps = w.visualizationSpeedMps;
            // Radial emission delay tracks the inspiral duration in Main.
            if (model != null) model.emissionDelay = parameters.inspiralDurationSeconds;
            parameters.NotifyChanged();
            if (story != null) story.ResetCurrentWave();
            else if (simulation != null) simulation.RestartAt(0f, false); // paused fallback
            RefreshSlots();
        }

        /// <summary>Transport Replay delegates to the story's preview replay -
        /// it always uses the CURRENT sliders, never a selected snapshot.</summary>
        public void ReplayCurrent()
        {
            if (story != null)
            {
                if (story.CanPreview) story.ReplayCurrentWave();
            }
            else if (simulation != null) { simulation.RestartAt(0f, true); }
        }

        // ----------------------------------------------------------- widgets
        public void RefreshSlots()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                bool occupied = _slots[i] != null;
                if (slotTexts != null && i < slotTexts.Length && slotTexts[i] != null)
                {
                    if (ExperienceSettings.IsChinese)
                        LocalizedText.UseChineseFont(slotTexts[i]);
                    var w = _slots[i];
                    slotTexts[i].text = occupied
                        ? $"Wave {i + 1}\nh0={w.amplitude:0.00}  f={w.frequencyHz:0.00}  x{w.exaggeration:0.0}"
                        : (ExperienceSettings.IsChinese ? $"波形 {i + 1}\n（空）"
                           : ExperienceSettings.IsDutch ? $"Golf {i + 1}\n(leeg)"
                           : $"Wave {i + 1}\n(empty)");
                }
                if (slotButtons != null && i < slotButtons.Length &&
                    slotButtons[i] != null && slotButtons[i].targetGraphic != null)
                    slotButtons[i].targetGraphic.color =
                        PendingOverwrite && i == SelectedSlot ? CardArmed
                        : i == SelectedSlot ? CardChosen : CardIdle;
            }

            bool selOccupied = HasSlot(SelectedSlot);
            // Save/Load and Confirm/Cancel share the same two slots on the
            // panel - exactly one pair is ever visible.
            if (saveButton != null) saveButton.gameObject.SetActive(!PendingOverwrite);
            if (loadButton != null) loadButton.gameObject.SetActive(!PendingOverwrite);
            if (loadButton != null) loadButton.interactable = selOccupied;
            if (confirmButton != null)
                confirmButton.gameObject.SetActive(PendingOverwrite);
            if (cancelButton != null)
                cancelButton.gameObject.SetActive(PendingOverwrite);
            int n = SelectedSlot + 1;
            if (saveButtonText != null)
            {
                if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(saveButtonText);
                saveButtonText.text = ExperienceSettings.IsChinese
                    ? $"保存到槽位 {n}"
                    : ExperienceSettings.IsDutch ? $"Opslaan in {n}"
                    : $"Save to {n}";
            }
            if (loadButtonText != null)
            {
                if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(loadButtonText);
                loadButtonText.text = ExperienceSettings.IsChinese
                    ? $"载入槽位 {n}"
                    : ExperienceSettings.IsDutch ? $"Laden {n}"
                    : $"Load {n}";
            }
            if (hintText != null)
            {
                if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(hintText);
                hintText.text = _persistenceError != null
                    ? (ExperienceSettings.IsChinese ? $"保存失败：{_persistenceError}"
                        : ExperienceSettings.IsDutch ? $"opslaan mislukt: {_persistenceError}"
                        : $"save failed: {_persistenceError}")
                    : PendingOverwrite
                    ? (ExperienceSettings.IsChinese
                        ? $"槽位 {n} 已有波形——「确认覆盖」才会写入"
                        : ExperienceSettings.IsDutch
                            ? $"Slot {n} is bezet - CONFIRM overschrijft"
                            : $"slot {n} is occupied - CONFIRM OVERWRITE writes")
                    : (ExperienceSettings.IsChinese
                        ? "点击卡片仅选择；空槽可直接保存，占用槽需确认覆盖"
                        : ExperienceSettings.IsDutch
                            ? "klik een kaart om te selecteren; leeg = opslaan, bezet = bevestigen"
                            : "click a card to select only; empty slot saves directly, occupied needs confirm");
            }
        }

        /// <summary>Per-frame gating that RefreshSlots cannot express: Load is
        /// only enabled for an occupied slot AND a stable observation phase.</summary>
        void Update()
        {
            if (loadButton != null && loadButton.gameObject.activeSelf)
                loadButton.interactable = HasSlot(SelectedSlot) &&
                    (story == null || story.CanPreview);
        }

        // ----------------------------------------------------- JSON (pure API)
        /// <summary>Serialize all four slots - occupied flags keep empty slots
        /// distinct. Pure string API so regression can test without IO.</summary>
        public string ExportJson()
        {
            var file = new SaveFileV2 { version = 2, slots = new List<SlotEntry>(SlotCount) };
            for (int i = 0; i < SlotCount; i++)
                file.slots.Add(new SlotEntry
                {
                    occupied = _slots[i] != null,
                    wave = _slots[i] ?? new SavedWave(),
                });
            return JsonUtility.ToJson(file, true);
        }

        /// <summary>Parse a v2 save blob into the slots. EVERY occupied entry
        /// is validated before a single slot changes - a malformed or
        /// out-of-range blob is rejected whole and the current slots survive.
        /// Returns false on bad input; pure API, no IO.</summary>
        public bool ImportJson(string json)
        {
            try
            {
                var file = JsonUtility.FromJson<SaveFileV2>(json);
                if (file == null || file.slots == null || file.version != 2)
                    return false;
                var parsed = new SavedWave[SlotCount];
                for (int i = 0; i < SlotCount; i++)
                {
                    if (i < file.slots.Count && file.slots[i] != null &&
                        file.slots[i].occupied)
                    {
                        if (!ValidWave(file.slots[i].wave)) return false;
                        parsed[i] = file.slots[i].wave;
                    }
                }
                for (int i = 0; i < SlotCount; i++) _slots[i] = parsed[i];
                PendingOverwrite = false;
                _persistenceError = null;
                RefreshSlots();
                return true;
            }
            catch (System.Exception) { return false; }
        }

        /// <summary>Parse a LEGACY-format blob ({"waves":[...]}) into the slots
        /// with current-parameter defaults behind the stored fields. Every
        /// migrated entry is validated BEFORE any slot changes - a corrupt
        /// legacy blob is rejected whole and the current slots survive.
        /// Pure - no IO, never touches the old file. For migration + tests.</summary>
        public bool ImportLegacyJson(string json)
        {
            try
            {
                var legacy = JsonUtility.FromJson<LegacyFile>(json);
                if (legacy == null || legacy.waves == null) return false;
                var parsed = new SavedWave[SlotCount];
                for (int i = 0; i < SlotCount; i++)
                {
                    if (i < legacy.waves.Count && legacy.waves[i] != null)
                    {
                        var w = FromLegacy(legacy.waves[i]);
                        if (!ValidWave(w)) return false;
                        parsed[i] = w;
                    }
                }
                for (int i = 0; i < SlotCount; i++) _slots[i] = parsed[i];
                PendingOverwrite = false;
                _persistenceError = null;
                RefreshSlots();
                return true;
            }
            catch (System.Exception) { return false; }
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>Range-check an occupied snapshot - valid saved values are
        /// never silently clamped; anything outside the legal envelope rejects
        /// the whole blob.</summary>
        static bool ValidWave(SavedWave w)
        {
            if (w == null) return false;
            if (!Finite(w.amplitude) || !Finite(w.frequencyHz) ||
                !Finite(w.exaggeration) || !Finite(w.phaseRadians) ||
                !Finite(w.packetDurationSeconds) ||
                !Finite(w.inspiralDurationSeconds) || !Finite(w.ringdownSeconds) ||
                !Finite(w.postMergerResidual) || !Finite(w.visualizationSpeedMps))
                return false;
            if (w.amplitude < 0f || w.amplitude > 1f) return false;
            if (w.frequencyHz < .05f || w.frequencyHz > 4f) return false;
            if (w.exaggeration < 0f || w.exaggeration > 4f) return false;
            if (w.packetDurationSeconds <= 0f || w.inspiralDurationSeconds <= 0f ||
                w.ringdownSeconds <= 0f || w.visualizationSpeedMps <= 0f) return false;
            if (w.postMergerResidual < 0f || w.postMergerResidual > .5f) return false;
            if (!System.Enum.IsDefined(typeof(WaveMode), w.waveMode)) return false;
            if (!System.Enum.IsDefined(typeof(PropagationAxis), w.propagationAxis))
                return false;
            return true;
        }

        // ------------------------------------------------------------ file IO
        static string SaveDir
        {
            get
            {
#if UNITY_EDITOR
                // Workspace-local editor persistence (.tools/UserData).
                return System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "../.tools/UserData"));
#else
                return Application.persistentDataPath;
#endif
            }
        }
        static string FilePath => SaveDir + "/saved_waves_v2.json";
        static string LegacyPath => SaveDir + "/saved_waves.json";

        /// <summary>Write the slots to disk. A failure is stored in
        /// _persistenceError (sticky until the next successful action) so the
        /// hint line always shows a failed save as an error - never success.</summary>
        void Save()
        {
            _persistenceError = null;
            if (saveOverride != null)
            {
                if (!saveOverride()) _persistenceError = "write rejected";
                return;
            }
            if (disablePersistence) return;
            try
            {
                System.IO.Directory.CreateDirectory(SaveDir);
                System.IO.File.WriteAllText(FilePath, ExportJson());
            }
            catch (System.Exception e)
            {
                _persistenceError = e.Message;
                Debug.LogWarning($"[Project8] Wave save failed: {e.Message}");
            }
        }

        void Load()
        {
            if (disablePersistence) return;
            try
            {
                if (System.IO.File.Exists(FilePath))
                {
                    ImportJson(System.IO.File.ReadAllText(FilePath));
                    return;
                }
                // One-way migration: read the legacy list, detach up to four
                // entries into independent slots, never touch the old file.
                if (System.IO.File.Exists(LegacyPath) &&
                    ImportLegacyJson(System.IO.File.ReadAllText(LegacyPath)))
                    Save(); // write the migrated v2 file once
            }
            catch (System.Exception e)
            {
                _persistenceError = e.Message;
                Debug.LogWarning($"[Project8] Wave load failed: {e.Message}");
            }
        }
    }
}
