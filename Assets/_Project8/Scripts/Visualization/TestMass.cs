using UnityEngine;

namespace Project8.Visualization
{
    /// <summary>
    /// A single freely-floating test mass ("test particle"). Its rest position is captured
    /// at build time; each frame the field moves it by the transverse GW displacement.
    /// Colour encodes signed strain so stretch vs squeeze phases are visually distinct.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class TestMass : MonoBehaviour
    {
        public Vector3 RestLocalPosition { get; private set; }
        public float CurrentStrain { get; private set; }

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        Renderer _renderer;
        MaterialPropertyBlock _block;
        Color _baseColor;
        bool _highlighted;
        Vector3 _baseScale;

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
            _baseScale = transform.localScale;
            _renderer.GetPropertyBlock(_block);
            _baseColor = _block.GetColor(ColorId);
            if (_baseColor == default) _baseColor = Color.white;
        }

        public void CaptureRestPosition() => RestLocalPosition = transform.localPosition;

        /// <summary>Apply strain displacement (in field-local space) and tint by sign.</summary>
        public void ApplyStrain(Vector3 localDisplacement, float strain,
                                Color stretchColor, Color squeezeColor, bool colorize)
        {
            transform.localPosition = RestLocalPosition + localDisplacement;
            CurrentStrain = strain;
            if (!colorize || _renderer == null) return;

            Color c = strain >= 0f
                ? Color.Lerp(_baseColor, stretchColor, Mathf.Clamp01(strain * 2f))
                : Color.Lerp(_baseColor, squeezeColor, Mathf.Clamp01(-strain * 2f));
            _block.SetColor(ColorId, c);
            _block.SetColor(EmissionId, _highlighted ? c * 1.5f : Color.black);
            _renderer.SetPropertyBlock(_block);
        }

        /// <summary>Used by evaluation tasks to draw attention to a candidate mass.</summary>
        public void SetHighlighted(bool value)
        {
            _highlighted = value;
            transform.localScale = _baseScale * (value ? 1.5f : 1f);
            _block.SetColor(EmissionId, value ? Color.yellow * 0.8f : Color.black);
            _renderer.SetPropertyBlock(_block);
        }

        /// <summary>Desktop/editor fallback selection path (XR ray select is added by XRI).</summary>
        void OnMouseDown()
        {
            var task = FindAnyObjectByType<Evaluation.PredictionTask>();
            if (task != null) task.TrySelect(this);
        }
    }
}
