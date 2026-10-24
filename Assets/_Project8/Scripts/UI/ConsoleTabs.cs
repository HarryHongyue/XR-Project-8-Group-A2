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

        void Awake() { ShowWave(); }

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
        }
    }
}
