using UnityEditor;

namespace Darwin.Rendering.Editor
{
    [InitializeOnLoad]
    static class MoebiusBillboardCameraInitialization
    {
        static MoebiusBillboardCameraInitialization()
        {
            MoebiusBillboardCameraGlobals.Initialize();
        }
    }
}
