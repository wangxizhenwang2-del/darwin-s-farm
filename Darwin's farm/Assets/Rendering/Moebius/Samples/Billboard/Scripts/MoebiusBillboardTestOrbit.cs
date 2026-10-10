using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Darwin.Rendering
{
    /// <summary>Camera-only controls for the visual test. Billboard orientation stays entirely in the shader.</summary>
    public sealed class MoebiusBillboardTestOrbit : MonoBehaviour
    {
        public Transform target;
        public Renderer testWall;
        public float yaw = 20;
        [Range(-10,75)] public float pitch = 12;
        public float distance = 8;
        public float turnSpeed = 65;
        void LateUpdate()
        {
            if (target == null) return;
            float horizontal = 0, vertical = 0, zoom = 0;
            #if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.oKey.wasPressedThisFrame && testWall != null) testWall.enabled = !testWall.enabled;
                horizontal = (keyboard.rightArrowKey.isPressed ? 1 : 0)-(keyboard.leftArrowKey.isPressed ? 1 : 0);
                vertical = (keyboard.upArrowKey.isPressed ? 1 : 0)-(keyboard.downArrowKey.isPressed ? 1 : 0);
                zoom = (keyboard.pageDownKey.isPressed ? 1 : 0)-(keyboard.pageUpKey.isPressed ? 1 : 0);
            }
            #elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.O) && testWall != null) testWall.enabled = !testWall.enabled;
            horizontal = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0)-(Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            vertical = (Input.GetKey(KeyCode.UpArrow) ? 1 : 0)-(Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
            zoom = (Input.GetKey(KeyCode.PageDown) ? 1 : 0)-(Input.GetKey(KeyCode.PageUp) ? 1 : 0);
            #endif
            yaw += horizontal*turnSpeed*Time.deltaTime;
            pitch = Mathf.Clamp(pitch+vertical*turnSpeed*Time.deltaTime,-10,75);
            distance = Mathf.Clamp(distance+zoom*4*Time.deltaTime,2,20);
            var center = target.position+Vector3.up*0.9f;
            transform.position = center+Quaternion.Euler(pitch,yaw,0)*new Vector3(0,0,-distance);
            transform.LookAt(center,Vector3.up);
        }
    }
}
