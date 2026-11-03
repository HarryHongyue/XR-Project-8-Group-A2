using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Project8.UI
{
    /// <summary>
    /// The persistent mission log: a chat-style history panel that records every
    /// narration line Rick speaks, every AI-tutor answer and every question the
    /// user asks. It is a small world-space canvas bolted to the cockpit's upper
    /// right (roughly a third of the canopy width), so it stays put for the WHOLE
    /// experience - the space flight AND the underground laboratory - because it
    /// travels with the ship.
    ///
    /// Interaction: the ScrollRect accepts pointer drags (mouse, XR ray or a
    /// finger poke through the XR UI input module); a thin scrollbar on the right
    /// edge shows the position. New entries auto-scroll only while the view is
    /// already at the bottom, so reading old messages is never yanked away.
    ///
    /// Everything is built at runtime by Attach() - no scene wiring needed.
    /// </summary>
    public class MissionLogUI : MonoBehaviour, IBeginDragHandler, IScrollHandler
    {
        public static MissionLogUI Instance { get; private set; }

        ScrollRect _scroll;
        RectTransform _content;
        Text _titleText;
        Font _font;
        bool _cjk;
        bool _autoFollow = true;
        readonly List<GameObject> _entries = new List<GameObject>();
        const int MaxEntries = 120;

        static readonly Color BgColor = new Color(0.03f, 0.05f, 0.09f, 0.82f);
        static readonly Color TitleColor = new Color(0.55f, 0.85f, 1f, 0.95f);
        static readonly Color RickColor = new Color(0.45f, 0.85f, 1f);
        static readonly Color YouColor = new Color(1f, 0.8f, 0.45f);
        static readonly Color SysColor = new Color(0.65f, 0.9f, 0.65f);
        static readonly Color BodyColor = new Color(0.92f, 0.95f, 1f, 0.95f);

        /// <summary>Create the whole panel under `host` (the ship) at a fixed
        /// upper-right cockpit position. Called once by the lab bootstrap.</summary>
        public static MissionLogUI Attach(Transform host, Transform avatarAnchor,
            Camera eventCamera = null)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("MissionLog", typeof(RectTransform));
            bool anchoredToAvatar = avatarAnchor != null;
            go.transform.SetParent(anchoredToAvatar ? avatarAnchor : host, false);
            var rt = (RectTransform)go.transform;
            if (anchoredToAvatar)
            {
                // Avatar is an 80 px chip on the 0.0011-scale world canvas parked
                // far right of the control panel. Stack the history above Rick,
                // right edges aligned, and keep its left edge clear of the
                // panel's right edge (~0.68 m ship-local) at all times.
                rt.localPosition = new Vector3(-170f, 350f, -1f);
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            }
            else
            {
                // Safe fallback for scenes without a Rick avatar.
                rt.localPosition = new Vector3(1.1f, 1.3f, 1.15f);
                rt.localRotation = Quaternion.Euler(6f, -30f, 0f);
                rt.localScale = Vector3.one * 0.00105f;
            }
            rt.sizeDelta = new Vector2(460f, 520f);

            var ui = go.AddComponent<MissionLogUI>();
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (eventCamera != null) canvas.worldCamera = eventCamera;
            // XRUIInputModule serves every pointer source (rays, pokes, mouse) -
            // the same raycaster the other cockpit canvases carry.
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            ui.Build(rt);
            Instance = ui;
            return ui;
        }

        void OnEnable() { if (Instance == null) Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------ builders
        void Build(RectTransform root)
        {
            _cjk = ExperienceSettings.IsChinese;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var bg = AddImage(root, "Bg", BgColor);
            Stretch(bg.rectTransform);
            bg.sprite = RuntimeUiSprites.RoundedWhite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.45f, 0.75f, 1f, 0.16f); // glass frame tint

            var inner = AddImage(root, "InnerBg", BgColor);
            Stretch(inner.rectTransform);
            inner.sprite = RuntimeUiSprites.RoundedWhite;
            inner.type = Image.Type.Sliced;
            inner.rectTransform.offsetMin = new Vector2(3f, 3f);
            inner.rectTransform.offsetMax = new Vector2(-3f, -3f);

            _titleText = AddText(root, "Title", "", 15, FontStyle.Bold,
                TextAnchor.MiddleLeft, TitleColor);
            var trt = _titleText.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(12f, -34f);
            trt.offsetMax = new Vector2(-12f, -4f);
            _titleText.text = _cjk ? "记录  MISSION LOG"
                : ExperienceSettings.IsDutch ? "LOGBOEK" : "MISSION LOG";

            var viewport = NewRect(root, "Viewport");
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(6f, 8f);
            viewport.offsetMax = new Vector2(-18f, -38f);
            viewport.gameObject.AddComponent<RectMask2D>();

            _scroll = gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;
            _scroll.inertia = true;

            _content = NewRect(viewport, "Content");
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = new Vector2(0f, 0f);
            _content.offsetMax = new Vector2(0f, 0f);
            var vlg = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;

            // Thin scrollbar on the right edge: visible position cue and a
            // draggable handle for mouse/ray.
            var sbGO = NewRect(root, "Scrollbar");
            sbGO.anchorMin = new Vector2(1f, 0f);
            sbGO.anchorMax = new Vector2(1f, 1f);
            sbGO.pivot = new Vector2(1f, 0.5f);
            sbGO.offsetMin = new Vector2(-14f, 8f);
            sbGO.offsetMax = new Vector2(-4f, -38f);
            var sbBg = sbGO.gameObject.AddComponent<Image>();
            sbBg.sprite = RuntimeUiSprites.RoundedWhite;
            sbBg.type = Image.Type.Sliced;
            sbBg.color = new Color(1f, 1f, 1f, 0.08f);
            var sb = sbGO.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            var handle = NewRect(sbGO, "Handle");
            Stretch(handle);
            var hImg = handle.gameObject.AddComponent<Image>();
            hImg.sprite = RuntimeUiSprites.RoundedWhite;
            hImg.type = Image.Type.Sliced;
            hImg.color = new Color(0.55f, 0.85f, 1f, 0.55f);
            sb.handleRect = handle;
            sb.targetGraphic = hImg;
            _scroll.verticalScrollbar = sb;
            _scroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHide;
        }

        static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        Image AddImage(Transform parent, string name, Color c)
        {
            var rt = NewRect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            return img;
        }

        Text AddText(Transform parent, string name, string s, int size,
                     FontStyle style, TextAnchor anchor, Color color)
        {
            var rt = NewRect(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            if (_cjk) LocalizedText.UseChineseFont(t);
            t.text = s;
            return t;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------- logging
        /// <summary>Speaker-keyed log entry: "Rick", "You" or "Lab" (system).
        /// No-op until the panel exists, so early calls never crash.</summary>
        public static void Add(string speaker, string message)
        {
            if (Instance == null || string.IsNullOrWhiteSpace(message)) return;
            Instance.AddEntry(speaker, message);
        }

        public void OnBeginDrag(PointerEventData eventData) => _autoFollow = false;

        public void OnScroll(PointerEventData eventData) => _autoFollow = false;

        void AddEntry(string speaker, string message)
        {
            bool cjk = ExperienceSettings.IsChinese;
            if (cjk != _cjk) { _cjk = cjk; } // language switched mid-session
            // Narration strings already carry a "Rick:"/"Rick：" prefix - strip
            // it so the speaker column does not show it twice.
            if (speaker == "Rick" &&
                (message.StartsWith("Rick:") || message.StartsWith("Rick：")))
                message = message.Substring(5).TrimStart();
            string who = speaker == "You"
                ? (cjk ? "你" : ExperienceSettings.IsDutch ? "Jij" : "You")
                : speaker == "Lab"
                    ? (cjk ? "实验室" : ExperienceSettings.IsDutch ? "Lab" : "Lab")
                    : "Rick";
            Color c = speaker == "You" ? YouColor
                : speaker == "Lab" ? SysColor : RickColor;
            string whoHex = ColorUtility.ToHtmlStringRGB(c);
            string bodyHex = ColorUtility.ToHtmlStringRGB(BodyColor);

            var entry = NewRect(_content, "Entry");
            var t = entry.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = 14;
            t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            if (cjk) LocalizedText.UseChineseFont(t);
            t.text = $"<color=#{whoHex}><b>{who}</b></color>  " +
                     $"<color=#{bodyHex}>{message}</color>";
            var fit = entry.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _entries.Add(entry.gameObject);

            // Cap the history so a long session cannot grow the layout forever.
            while (_entries.Count > MaxEntries)
            {
                var oldest = _entries[0];
                _entries.RemoveAt(0);
                if (oldest != null) Destroy(oldest);
            }

            // Auto-scroll only when already at (or near) the bottom.
            Canvas.ForceUpdateCanvases();
            if (_scroll != null &&
                (_autoFollow || _scroll.verticalNormalizedPosition <= 0.06f))
            {
                _autoFollow = true;
                _scroll.verticalNormalizedPosition = 0f;
            }
        }
    }
}
