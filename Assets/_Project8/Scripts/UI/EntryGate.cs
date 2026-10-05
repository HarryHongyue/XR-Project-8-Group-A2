using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project8.UI
{
    /// <summary>
    /// Guarantees the Welcome (language/settings) scene is the first thing the user sees:
    /// if a scene that assumes a chosen language starts without one, it redirects to
    /// "Welcome". Skipped in batch mode so automated smoke tests stay in place.
    /// </summary>
    public class EntryGate : MonoBehaviour
    {
        void Awake()
        {
            if (!Application.isBatchMode && !ExperienceSettings.HasChosen)
                SceneManager.LoadScene("Welcome");
        }
    }
}
