using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TrashBagCharacter
{
    public class TrashBagCameraController : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;
        public Vector3 targetOffset = new Vector3(0f, 0.7f, 0f);

        [Header("Orbit Settings")]
        public float distance = 2.5f;
        public float minDistance = 1.0f;
        public float maxDistance = 5.0f;
        public float mouseSensitivityX = 1.5f;
        public float mouseSensitivityY = 1.2f;
        public float minPitch = -20f;
        public float maxPitch = 60f;

        [Header("Collision Avoidance")]
        public LayerMask obstacleLayers = ~0;
        public float collisionRadius = 0.2f;

        private float _currentYaw;
        private float _currentPitch = 15f;
        private float _targetYaw;
        private float _targetPitch = 15f;

        private void Start()
        {
            if (target != null)
            {
                _targetYaw = target.eulerAngles.y;
                _currentYaw = _targetYaw;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            float mouseDeltaX = 0f;
            float mouseDeltaY = 0f;
            float scroll = 0f;
            bool escapePressed = false;
            bool leftClickPressed = false;

#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                escapePressed = true;

            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                    leftClickPressed = true;

                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    mouseDeltaX = delta.x * 0.1f;
                    mouseDeltaY = delta.y * 0.1f;

                    float scrollValue = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scrollValue) > 0.01f)
                        scroll = Mathf.Sign(scrollValue) * 0.5f;
                }
            }
#else
            if (Input.GetKeyDown(KeyCode.Escape))
                escapePressed = true;
            if (Input.GetMouseButtonDown(0))
                leftClickPressed = true;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                mouseDeltaX = Input.GetAxis("Mouse X");
                mouseDeltaY = Input.GetAxis("Mouse Y");
                scroll = Input.GetAxis("Mouse ScrollWheel") * 2f;
            }
#endif

            // Cursor lock toggle
            if (escapePressed)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            else if (leftClickPressed && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                _targetYaw += mouseDeltaX * mouseSensitivityX;
                _targetPitch -= mouseDeltaY * mouseSensitivityY;
                _targetPitch = Mathf.Clamp(_targetPitch, minPitch, maxPitch);
            }

            if (Mathf.Abs(scroll) > 0.001f)
            {
                distance = Mathf.Clamp(distance - scroll, minDistance, maxDistance);
            }
        }

        private void LateUpdate()
        {
            if (target == null) return;

            _currentYaw = Mathf.LerpAngle(_currentYaw, _targetYaw, Time.deltaTime * 20f);
            _currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, Time.deltaTime * 20f);

            Quaternion rotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
            Vector3 focusPoint = target.position + targetOffset;

            // Desired position
            Vector3 desiredPosition = focusPoint - (rotation * Vector3.forward * distance);

            // Obstacle collision check
            Ray ray = new Ray(focusPoint, (desiredPosition - focusPoint).normalized);
            float maxRayDist = Vector3.Distance(focusPoint, desiredPosition);
            if (Physics.SphereCast(ray, collisionRadius, out RaycastHit hit, maxRayDist, obstacleLayers, QueryTriggerInteraction.Ignore))
            {
                desiredPosition = ray.GetPoint(Mathf.Max(minDistance, hit.distance - 0.1f));
            }

            transform.position = desiredPosition;
            transform.LookAt(focusPoint);
        }
    }
}
