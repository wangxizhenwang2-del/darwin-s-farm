using System.Collections.Generic;
using UnityEngine;

namespace Darwin.Rendering
{
    /// <summary>Shares one world-space color field across explicitly selected renderers, without editing materials.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Moebius Spatial Color Gradient")]
    public sealed class MoebiusSpatialColorGradient : MonoBehaviour
    {
        public Transform pointA;
        public Transform pointB;
        public Color colorA = new Color(.95f,.52f,.38f,1);
        public Color colorB = new Color(.64f,.63f,.88f,1);
        [Range(0,1)] public float strength = 1;
        [Tooltip("Fraction of the distance between A and B occupied by the transition, centered between them.")]
        [Range(.01f,1)] public float transitionWidth = 1;
        public Renderer[] targets = new Renderer[0];

        sealed class Binding
        {
            public Renderer renderer;
            public int index;
            public MaterialPropertyBlock original;
            public readonly MaterialPropertyBlock working = new MaterialPropertyBlock();
        }
        readonly List<Binding> bindings = new List<Binding>();
        bool targetsDirty = true;
        static readonly int Enabled = Shader.PropertyToID("_EnableSpatialGradient");
        static readonly int Start = Shader.PropertyToID("_SpatialGradientPointA");
        static readonly int End = Shader.PropertyToID("_SpatialGradientPointB");
        static readonly int ColorA = Shader.PropertyToID("_SpatialGradientColorA");
        static readonly int ColorB = Shader.PropertyToID("_SpatialGradientColorB");
        static readonly int Strength = Shader.PropertyToID("_SpatialGradientStrength");
        static readonly int Width = Shader.PropertyToID("_SpatialGradientWidth");

        void OnEnable() { targetsDirty = true; }
        void OnValidate() { targetsDirty = true; }
        void LateUpdate() { ApplyNow(); }
        void OnDisable() { Restore(); }

        // Call this after replacing targets from gameplay code. Inspector changes automatically rebind.
        public void RefreshTargets() { targetsDirty = true; ApplyNow(); }
        public void ApplyNow()
        {
            if(!isActiveAndEnabled) return;
            if(targetsDirty)
            {
                Restore(); targetsDirty = false;
                var seen = new HashSet<Renderer>();
                foreach(var target in targets ?? new Renderer[0])
                {
                    if(!target || !seen.Add(target)) continue;
                    var materials = target.sharedMaterials;
                    for(int i=0;i<materials.Length;i++)
                    {
                        if(!materials[i] || !materials[i].HasProperty(Enabled)) continue;
                        var binding = new Binding { renderer=target, index=i, original=new MaterialPropertyBlock() };
                        target.GetPropertyBlock(binding.original,i);
                        // A per-slot block masks a renderer-wide block; preserve its existing values when no slot override exists.
                        if(binding.original.isEmpty) target.GetPropertyBlock(binding.working);
                        else target.GetPropertyBlock(binding.working,i);
                        bindings.Add(binding);
                    }
                }
            }
            bool valid = pointA && pointB && (pointB.position-pointA.position).sqrMagnitude > .000001f;
            var a = QualitySettings.activeColorSpace == ColorSpace.Linear ? colorA.linear : colorA;
            var b = QualitySettings.activeColorSpace == ColorSpace.Linear ? colorB.linear : colorB;
            foreach(var binding in bindings)
            {
                if(!binding.renderer) continue;
                var block = binding.working;
                block.SetFloat(Enabled,valid ? 1 : 0);
                block.SetFloat(Strength,Mathf.Clamp01(strength));
                block.SetFloat(Width,Mathf.Clamp(transitionWidth,.01f,1));
                block.SetVector(ColorA,a); block.SetVector(ColorB,b);
                if(valid)
                {
                    block.SetVector(Start,pointA.position); block.SetVector(End,pointB.position);
                }
                binding.renderer.SetPropertyBlock(block,binding.index);
            }
        }
        void Restore()
        {
            foreach(var binding in bindings)
                if(binding.renderer) binding.renderer.SetPropertyBlock(binding.original.isEmpty ? null : binding.original,binding.index);
            bindings.Clear(); targetsDirty = true;
        }
        void OnDrawGizmosSelected()
        {
            if(!pointA || !pointB) return;
            Gizmos.color=colorA; Gizmos.DrawWireSphere(pointA.position,.12f);
            Gizmos.color=colorB; Gizmos.DrawWireSphere(pointB.position,.12f);
            Gizmos.color=Color.white; Gizmos.DrawLine(pointA.position,pointB.position);
        }
    }
}
