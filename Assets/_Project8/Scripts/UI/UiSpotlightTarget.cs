using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Project8.UI
{
    /// <summary>
    /// A pulsing accent-coloured ring drawn around a button (or any UI rect) so
    /// Rick's narration can point at the control it is talking about - "press
    /// START" literally lights up START. The ring is a border-only sliced image:
    /// it renders as a glowing frame, never blocks raycasts and adds no layout.
    ///
    /// Usage: add the component next to a Button and give it an id, then call
    ///   UiSpotlightTarget.Show("start");    // one at a time
    ///   UiSpotlightTarget.SetGroup("station-", true);  // several by prefix
    ///   UiSpotlightTarget.Clear();
    /// </summary>
    public class UiSpotlightTarget : MonoBehaviour
    {
        public string id;

        static readonly Dictionary<string, UiSpotlightTarget> _byId =
            new Dictionary<string, UiSpotlightTarget>();
        static readonly Color Glow = new Color(0.35f, 0.78f, 0.98f); // UI accent cyan

        Image _ring;
        bool _lit;

        void OnEnable()
        {
            if (!string.IsNullOrEmpty(id)) _byId[id] = this;
        }

        void OnDisable()
        {
            if (!string.IsNullOrEmpty(id) &&
                _byId.TryGetValue(id, out var t) && t == this)
                _byId.Remove(id);
            _lit = false;
        }

        /// <summary>Light exactly one target; everything else goes dark.</summary>
        public static void Show(string id)
        {
            Clear();
            if (string.IsNullOrEmpty(id)) return;
            if (TryFind(id, out var t)) t.SetLit(true);
        }

        /// <summary>Light/darken every registered id starting with prefix
        /// (e.g. "station-" lights D1/D2/D3 together for a pick-one prompt).</summary>
        public static void SetGroup(string idPrefix, bool on)
        {
            RefreshRegistry();
            foreach (var kv in _byId)
                if (kv.Key.StartsWith(idPrefix)) kv.Value.SetLit(on);
        }

        public static void Clear()
        {
            RefreshRegistry();
            foreach (var t in _byId.Values)
                if (t != null) t.SetLit(false);
        }

        static bool TryFind(string targetId, out UiSpotlightTarget target)
        {
            if (_byId.TryGetValue(targetId, out target) && target != null)
                return true;

            // Some scene builders assign `id` immediately after AddComponent;
            // OnEnable has already run by then. Repair the registry lazily so
            // those cues still work in both generated and older serialized scenes.
            RefreshRegistry();
            return _byId.TryGetValue(targetId, out target) && target != null;
        }

        static void RefreshRegistry()
        {
            var stale = new List<string>();
            foreach (var kv in _byId)
                if (kv.Value == null || !kv.Value.gameObject.activeInHierarchy)
                    stale.Add(kv.Key);
            foreach (var key in stale) _byId.Remove(key);

            var all = Resources.FindObjectsOfTypeAll<UiSpotlightTarget>();
            foreach (var t in all)
            {
                if (t == null || !t.gameObject.scene.IsValid() ||
                    !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.id))
                    continue;
                _byId[t.id] = t;
            }
        }

        void SetLit(bool on)
        {
            _lit = on;
            if (on) EnsureRing();
            if (_ring != null) _ring.enabled = on;
        }

        void EnsureRing()
        {
            if (_ring != null || !(transform is RectTransform host)) return;
            var go = new GameObject("SpotlightGlow", typeof(RectTransform), typeof(Image));
            go.layer = gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(host, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-4f, -4f);
            rt.offsetMax = new Vector2(4f, 4f);
            rt.SetAsLastSibling();
            _ring = go.GetComponent<Image>();
            _ring.sprite = RuntimeUiSprites.ThinOutline;
            _ring.type = Image.Type.Sliced;
            _ring.fillCenter = false;               // border only -> glowing outline
            _ring.color = Glow;
            _ring.raycastTarget = false;
        }

        void Update()
        {
            if (!_lit || _ring == null) return;
            float a = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
            _ring.color = new Color(Glow.r, Glow.g, Glow.b, a);
        }
    }
}
