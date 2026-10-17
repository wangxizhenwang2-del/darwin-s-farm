using UnityEngine;
using UnityEngine.Rendering;

namespace Darwin.Rendering
{
    // Shadow passes use a light view matrix. Keep the billboard facing the rendering
    // camera in every pass, including orthographic Game and Scene view cameras.
    public static class MoebiusBillboardCameraGlobals
    {
        static readonly int Position = Shader.PropertyToID("_MoebiusBillboardCameraPosition");
        static readonly int Forward = Shader.PropertyToID("_MoebiusBillboardCameraForward");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Initialize()
        {
            RenderPipelineManager.beginCameraRendering -= SetCamera;
            RenderPipelineManager.beginCameraRendering += SetCamera;
        }

        static void SetCamera(ScriptableRenderContext context, Camera camera)
        {
            Vector3 position = camera.transform.position;
            Vector3 forward = camera.transform.forward;
            Shader.SetGlobalVector(Position, new Vector4(position.x, position.y, position.z, camera.orthographic ? 1 : 0));
            Shader.SetGlobalVector(Forward, new Vector4(forward.x, forward.y, forward.z, 1));
        }
    }
}
