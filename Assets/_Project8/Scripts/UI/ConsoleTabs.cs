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

        static readonly Color ActiveTint = Color.white;
        static readonly Color IdleTint = new Color(.72f, .78f, .9f, .6f);
        // White face + white label was invisible - the active tab flips its
        // label to deep blue, matching ButtonLabelHover's hover inversion.
        static readonly Color LabelHot = new Color(0.02f, 0.25f, 0.45f);

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
            if (_waveLabel != null) _waveLabel.color = wave ? LabelHot : Color.white;
            if (_etLabel != null) _etLabel.color = wave ? Color.white : LabelHot;
        }
    }
}
