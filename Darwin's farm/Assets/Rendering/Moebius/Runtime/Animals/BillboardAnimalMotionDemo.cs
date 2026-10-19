using UnityEngine;
using UnityEngine.Rendering;

namespace Darwin.Rendering
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BillboardAnimalMotionDemo : MonoBehaviour
    {
        public Camera viewCamera;
        public BillboardAnimalMotion[] animals;
        public bool animateInEditMode = true;
        public bool playing = true;
        [Range(.1f,3)] public float playbackSpeed = 1;
        [Min(0)] public float previewTime;
        double previous;
        double nextRepaint;
        public float Elapsed { get; private set; }
        void OnEnable()
        {
            previous=Clock();Elapsed=previewTime;
            RenderPipelineManager.beginCameraRendering+=FaceForCamera;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update+=EditorTick;
#endif
            Evaluate(Elapsed,viewCamera);
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering-=FaceForCamera;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update-=EditorTick;
#endif
        }
        void FaceForCamera(ScriptableRenderContext context,Camera camera)
        {
            if(camera.cameraType==CameraType.Game||camera.cameraType==CameraType.SceneView)
                Evaluate(playing?Elapsed:previewTime,camera);
        }
        static double Clock()
        {
#if UNITY_EDITOR
            if(!Application.isPlaying)return UnityEditor.EditorApplication.timeSinceStartup;
#endif
            return Time.timeAsDouble;
        }
        void LateUpdate() { if(Application.isPlaying)Advance(); }
        void Advance()
        {
            double now=Clock();float delta=(float)(now-previous);previous=now;
            if(playing)Elapsed+=delta*playbackSpeed;
            Evaluate(playing?Elapsed:previewTime,viewCamera);
        }
        public void Evaluate(float seconds,Camera camera=null)
        {
            if(animals==null)return;
            if(!camera)camera=viewCamera?viewCamera:Camera.main;
            foreach(var animal in animals)if(animal&&animal.isActiveAndEnabled)animal.Evaluate(Mathf.Max(0,seconds),camera);
        }
#if UNITY_EDITOR
        void EditorTick()
        {
            if(!this) {UnityEditor.EditorApplication.update-=EditorTick;return;}
            if(Application.isPlaying||UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode||!animateInEditMode||
                !isActiveAndEnabled||UnityEditor.EditorApplication.isCompiling||UnityEditor.EditorUtility.IsPersistent(this)||!gameObject.scene.isLoaded)return;
            double now=Clock();if(now<nextRepaint)return;nextRepaint=now+1.0/60;
            Advance();UnityEditor.EditorApplication.QueuePlayerLoopUpdate();UnityEditor.SceneView.RepaintAll();
        }
#endif
    }
}
