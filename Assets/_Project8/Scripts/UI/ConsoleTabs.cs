using UnityEngine;
using UnityEngine.UI;

namespace Project8.UI
{
    /// <summary>
    /// Two progressive-disclosure pages inside the control console drawer:
    /// the wave laboratory (sliders / slots / transport) and the Einstein
    /// Telescope inspector. Only one page is active at a time.
    /// </summary>
    public class ConsoleTabs : MonoBehaviour
    {
        public GameObject wavePage;
        public GameObject etPage;
        public Button waveButton;
        public Button etButton;

        static readonly Color ActiveTint = new Color(0.15f, 0.36f, 0.52f, 0.96f);
        static readonly Color IdleTint = new Color(0.08f, 0.13f, 0.21f, 0.82f);

        Text _waveLabel, _etLabel;

        void Awake()
        {
            if (waveButton != null) _waveLabel = waveButton.GetComponentInChildren<Text>(true);
            if (etButton != null) _etLabel = etButton.GetComponentInChildren<Text>(true);
            ShowWave();
        }

        public void ShowWave() { Apply(true); }
        public void ShowET() { Apply(false); }

        void Apply(bool wave)
        {
            if (wavePage != null) wavePage.SetActive(wave);
            if (etPage != null) etPage.SetActive(!wave);
            if (waveButton != null && waveButton.targetGraphic != null)
                waveButton.targetGraphic.color = wave ? ActiveTint : IdleTint;
            if (etButton != null && etButton.targetGraphic != null)
                etButton.targetGraphic.color = wave ? IdleTint : ActiveTint;
            if (_waveLabel != null) _waveLabel.color = Color.white;
            if (_etLabel != null) _etLabel.color = Color.white;
        }
    }
}
