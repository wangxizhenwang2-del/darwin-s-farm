using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Darwin.Rendering
{
    /// <summary>URP 17.2 RenderGraph ink pass. Assign a serialized material to retain shader variants in builds.</summary>
    public sealed class MoebiusOutlineRendererFeature : ScriptableRendererFeature
    {
        public Material material;
        public bool showInSceneView = true;
        InkPass pass;

        public override void Create()
        {
            pass = new InkPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
            pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            // URP 17: ensures the sampled source is an intermediate texture, never the backbuffer.
            pass.requiresIntermediateTexture = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData;
            if (material == null || camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
                return;
            if (!showInSceneView && camera.isSceneViewCamera) return;
            // Apply once to the final camera in a stack. Transparent geometry has no depth/normal ink.
            if (!camera.resolveFinalTarget) return;
            pass.material = material;
            renderer.EnqueuePass(pass);
        }

        sealed class InkPass : ScriptableRenderPass
        {
            public Material material;
            sealed class PassData
            {
                public TextureHandle source;
                public Material material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid() ||
                    !resources.cameraNormalsTexture.IsValid()) return;
                var source = resources.activeColorTexture;
                var descriptor = renderGraph.GetTextureDesc(source);
                descriptor.name = "Moebius Ink Composite";
                descriptor.clearBuffer = false;
                descriptor.depthBufferBits = DepthBits.None;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.bindTextureMS = false;
                var destination = renderGraph.CreateTexture(descriptor);
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Moebius Ink", out var data))
                {
                    data.source = source;
                    data.material = material;
                    builder.UseTexture(source, AccessFlags.Read);
                    // Declare dependencies explicitly: these are sampled through URP's global texture bindings.
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, d.source, new Vector4(1,1,0,0), d.material, 0));
                }
                // One transient target; downstream post processing/final blit reads the new camera color.
                resources.cameraColor = destination;
            }
        }
    }
}
