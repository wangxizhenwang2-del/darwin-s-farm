using UnityEngine;
using UnityEngine.Rendering;

namespace Darwin.Rendering
{
    /// <summary>Tracks the sheet's bottom line without changing shared water material assets.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer))]
    [DefaultExecutionOrder(1000)]
    [AddComponentMenu("Rendering/Moebius Waterfall Impact Binding")]
    public sealed class MoebiusWaterfallImpactBinding : MonoBehaviour
    {
        [Tooltip("Local XY waterfall sheet. Its material's Sheet Size defines the bottom edge.")]
        public MeshRenderer waterfallSheet;

        MeshRenderer receivingWater;
        Material lastMaterial;
        MaterialPropertyBlock original, working;
        Vector3 lastA, lastB;
        bool applied, refresh = true;
        static readonly int Size = Shader.PropertyToID("_SheetSize");
        static readonly int PointA = Shader.PropertyToID("_ImpactPointA");
        static readonly int PointB = Shader.PropertyToID("_ImpactPointB");

        void OnEnable()
        {
            refresh = true;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            SynchronizeNow();
        }
        void OnValidate() { refresh = true; }
        void LateUpdate() { SynchronizeNow(); }
        void BeforeCamera(ScriptableRenderContext context, Camera camera) { SynchronizeNow(); }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            Restore();
        }

        // Also usable after a gameplay script moves the sheet outside the normal Update cycle.
        public bool SynchronizeNow()
        {
            if (!isActiveAndEnabled) return false;
            if (!receivingWater) receivingWater = GetComponent<MeshRenderer>();
            var source = waterfallSheet ? waterfallSheet.sharedMaterial : null;
            var target = receivingWater ? receivingWater.sharedMaterial : null;
            if (!source || source.shader.name != "Darwin/Moebius Waterfall" ||
                !target || target.shader.name != "Darwin/Moebius Water")
            {
                Restore();
                return false;
            }

            var size = source.GetVector(Size);
            var a = waterfallSheet.transform.TransformPoint(new Vector3(-size.x * .5f, -size.y * .5f, 0));
            var b = waterfallSheet.transform.TransformPoint(new Vector3( size.x * .5f, -size.y * .5f, 0));
            if (applied && !refresh && lastMaterial == target && a.Equals(lastA) && b.Equals(lastB)) return true;

            if (working == null) working = new MaterialPropertyBlock();
            if (!applied)
            {
                if (original == null) original = new MaterialPropertyBlock();
                receivingWater.GetPropertyBlock(original, 0);
            }
            // Preserve existing properties; a per-slot block otherwise masks renderer-wide values.
            receivingWater.GetPropertyBlock(working, 0);
            if (working.isEmpty) receivingWater.GetPropertyBlock(working);
            working.SetVector(PointA, a);
            working.SetVector(PointB, b);
            receivingWater.SetPropertyBlock(working, 0);
            lastA = a; lastB = b; lastMaterial = target; applied = true; refresh = false;
            return true;
        }

        void Restore()
        {
            if (applied && receivingWater)
                receivingWater.SetPropertyBlock(original.isEmpty ? null : original, 0);
            applied = false; refresh = true; lastMaterial = null;
        }

        void OnDrawGizmosSelected()
        {
            if (!waterfallSheet || !waterfallSheet.sharedMaterial || !waterfallSheet.sharedMaterial.HasProperty(Size)) return;
            var size = waterfallSheet.sharedMaterial.GetVector(Size);
            var a = waterfallSheet.transform.TransformPoint(new Vector3(-size.x * .5f, -size.y * .5f, 0));
            var b = waterfallSheet.transform.TransformPoint(new Vector3( size.x * .5f, -size.y * .5f, 0));
            Gizmos.color = new Color(.6f, 1, 1);
            Gizmos.DrawLine(a, b); Gizmos.DrawWireSphere(a, .08f); Gizmos.DrawWireSphere(b, .08f);
        }
    }
}
