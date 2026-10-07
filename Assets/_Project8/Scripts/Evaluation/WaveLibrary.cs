using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Project8.Core;
using Project8.UI;

namespace Project8.Evaluation
{
    /// <summary>
    /// "Saved waves" for the Wave Lab: the user shapes a wave with the sliders, saves it,
    /// and can replay saved waves on the test-mass net / Earth as often as they like.
    /// Persisted as JSON under Application.persistentDataPath.
    /// </summary>
    public class WaveLibrary : MonoBehaviour
    {
        public SimulationParameters parameters;
        public SimulationController simulation;
        public Text[] slotTexts;   // up to 4 saved-wave slot labels
        public Text saveHintText;  // shows the armed "pick a slot" instruction
        public Text saveButtonText;

        bool _armSave;

        [System.Serializable]
        public class SavedWave
        {
            public string name;
            public float amplitude;
            public float frequencyHz;
            public float exaggeration;
        }

        [System.Serializable]
        class SaveFile { public List<SavedWave> waves = new List<SavedWave>(); }

        readonly List<SavedWave> _waves = new List<SavedWave>();
        string FilePath => Application.persistentDataPath + "/saved_waves.json";

        void Start()
        {
            Load();
            RefreshSlots();
        }

        public void SaveCurrent()
        {
            _waves.Add(new SavedWave
            {
                name = "Wave " + (_waves.Count + 1),
                amplitude = parameters.strainAmplitude,
                frequencyHz = parameters.frequencyHz,
                exaggeration = parameters.displacementExaggeration,
            });
            Save();
            RefreshSlots();
        }

        /// <summary>Arm the save mode: the next slot click writes the current wave
        /// into THAT slot (overwriting any preset). Click again to disarm.</summary>
        public void ArmSave()
        {
            _armSave = !_armSave;
            RefreshSlots();
        }

        /// <summary>Click a slot: armed = save/overwrite that slot; filled = load it;
        /// empty = save the current wave into it.</summary>
        public void LoadSlot(int i)
        {
            if (_armSave || i >= _waves.Count)
            {
                SaveToSlot(i);
                _armSave = false;
            }
            else
            {
                var w = _waves[i];
                parameters.strainAmplitude = w.amplitude;
                parameters.frequencyHz = w.frequencyHz;
                parameters.displacementExaggeration = w.exaggeration;
                parameters.NotifyChanged();
            }
            RefreshSlots();
        }

        void SaveToSlot(int i)
        {
            var w = new SavedWave
            {
                name = "Wave " + (i + 1),
                amplitude = parameters.strainAmplitude,
                frequencyHz = parameters.frequencyHz,
                exaggeration = parameters.displacementExaggeration,
            };
            while (_waves.Count <= i) _waves.Add(w);
            _waves[i] = w;
            Save();
        }

        /// <summary>Replay the current wave from the start.</summary>
        public void ReplayCurrent()
        {
            simulation.ResetSimulation();
            simulation.Play();
        }

        void RefreshSlots()
        {
            if (slotTexts != null)
            {
                for (int i = 0; i < slotTexts.Length; i++)
                {
                    if (slotTexts[i] == null) continue;
                    if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(slotTexts[i]);
                    slotTexts[i].text = i < _waves.Count
                        ? ExperienceSettings.IsChinese
                            ? $"波形 {i + 1}  h0={_waves[i].amplitude:0.00}  f={_waves[i].frequencyHz:0.00}"
                            : ExperienceSettings.IsDutch
                                ? $"Golf {i + 1}  h0={_waves[i].amplitude:0.00}  f={_waves[i].frequencyHz:0.00}"
                                : $"{_waves[i].name}  h0={_waves[i].amplitude:0.00}  f={_waves[i].frequencyHz:0.00}"
                        : ExperienceSettings.IsChinese
                            ? $"波形 {i + 1}（空槽）"
                            : ExperienceSettings.IsDutch
                                ? $"Golf {i + 1}  (leeg)"
                                : $"Wave {i + 1}  (empty)";
                }
            }
            if (saveHintText != null)
            {
                if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(saveHintText);
                saveHintText.text = _armSave
                    ? (ExperienceSettings.IsChinese ? "点击一个槽位：把当前波形保存进去（可覆盖）"
                       : ExperienceSettings.IsDutch ? "klik een slot: huidige golf opslaan (overschrijft)"
                       : "now click a slot: saves the current wave into it (overwrites)")
                    : (ExperienceSettings.IsChinese ? "点击槽位读取；空槽=保存当前；点「存入槽位」可覆盖任意槽"
                       : ExperienceSettings.IsDutch ? "klik een slot om te laden; leeg = opslaan; 'OPSlaAN' overschrijft"
                       : "click a slot to load; empty slot = save; 'SAVE TO SLOT' overwrites any slot");
            }
            if (saveButtonText != null)
            {
                if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(saveButtonText);
                saveButtonText.text = _armSave
                    ? (ExperienceSettings.IsChinese ? "取消" : ExperienceSettings.IsDutch ? "Annuleer" : "Cancel")
                    : (ExperienceSettings.IsChinese ? "存入槽位…" : ExperienceSettings.IsDutch ? "Opslaan in slot..." : "Save to slot...");
            }
        }

        void Save()
        {
            try
            {
                var file = new SaveFile { waves = _waves };
                System.IO.File.WriteAllText(FilePath, JsonUtility.ToJson(file, true));
            }
            catch (System.Exception e) { Debug.LogWarning($"[Project8] Wave save failed: {e.Message}"); }
        }

        void Load()
        {
            try
            {
                if (!System.IO.File.Exists(FilePath)) return;
                var file = JsonUtility.FromJson<SaveFile>(System.IO.File.ReadAllText(FilePath));
                if (file != null && file.waves != null) _waves.AddRange(file.waves);
            }
            catch (System.Exception e) { Debug.LogWarning($"[Project8] Wave load failed: {e.Message}"); }
        }
    }
}
