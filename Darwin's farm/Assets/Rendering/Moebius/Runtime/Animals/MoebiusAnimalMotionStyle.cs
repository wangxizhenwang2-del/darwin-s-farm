using UnityEngine;

namespace Darwin.Rendering
{
    public sealed class MoebiusAnimalMotionStyle : ScriptableObject
    {
        public Material material;
        [Range(.4f, 2.5f)] public float trailLifetime = 1.35f;
        [Range(.005f, .06f)] public float lineWidth = .018f;
        [Range(0, 1)] public float intensity = .85f;
        [Range(0, 1)] public float wingtipStrength = .45f;
        [Range(0, 1)] public float gustStrength = .55f;
        [Range(0, .2f)] public float curvature = .045f;
        public Color pale = new Color(.9f, .98f, .94f, 1);
        public Color ink = new Color(.24f, .45f, .47f, 1);
        [Header("Swimming wake and detached ripples")]
        [Range(.5f, 2.5f)] public float wakeLifetime = 1.8f;
        [Range(.3f, 1.2f)] public float rippleInterval = .55f;
        [Range(.5f, 2.8f)] public float rippleLifetime = 2.4f;
        [Range(.1f, 1)] public float rippleExpansion = .4f;
    }
}
