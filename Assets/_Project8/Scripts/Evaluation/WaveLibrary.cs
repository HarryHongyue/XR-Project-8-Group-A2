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

        /// <summary>Load saved wave slot i; if the slot is empty, save the current wave into it.</summary>
        public void LoadSlot(int i)
        {
            if (i < _waves.Count)
            {
                var w = _waves[i];
                parameters.strainAmplitude = w.amplitude;
                parameters.frequencyHz = w.frequencyHz;
                parameters.displacementExaggeration = w.exaggeration;
            }
            else SaveCurrent();
            RefreshSlots();
        }

        /// <summary>Replay the current wave from the start.</summary>
        public void ReplayCurrent()
        {
            simulation.ResetSimulation();
            simulation.Play();
        }

        void RefreshSlots()
        {
            if (slotTexts == null) return;
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
                        ? $"波形 {i + 1}（空槽：点击保存）"
                        : ExperienceSettings.IsDutch
                            ? $"Golf {i + 1}  (leeg - klik om op te slaan)"
                            : $"Wave {i + 1}  (empty - click to save current)";
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
