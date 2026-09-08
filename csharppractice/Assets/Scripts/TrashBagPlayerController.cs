using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TrashBagCharacter
{
    [RequireComponent(typeof(CharacterController))]
    public class TrashBagPlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [Tooltip("Running speed in m/s")]
        public float runSpeed = 5.0f;
        [Tooltip("Sprint speed multiplier")]
        public float sprintMultiplier = 1.4f;
        [Tooltip("Rotation smoothing speed towards camera forward")]
        public float turnSmoothSpeed = 12.0f;
        [Tooltip("Damping time for animator blend parameters")]
        public float animDampTime = 0.1f;

        [Header("Jump & Physics")]
        public float gravity = -20.0f;
        public float jumpHeight = 1.2f;
        public float groundCheckDistance = 0.2f;

        [Header("Control Modes")]
        [Tooltip("If true, character faces camera forward and strafes (4-way blend). If false, character rotates toward movement direction.")]
        public bool strafeMode = true;

        [Header("References")]
        public Animator animator;
        public Camera playerCamera;

        private CharacterController _cc;
        private Vector3 _verticalVelocity;
        private bool _isGrounded;

        // Animator parameter hashes
        private static readonly int ParamMoveX = Animator.StringToHash("MoveX");
        private static readonly int ParamMoveZ = Animator.StringToHash("MoveZ");
        private static readonly int ParamSpeed = Animator.StringToHash("Speed");
        private static readonly int ParamIsGrounded = Animator.StringToHash("IsGrounded");

        private float _currentMoveX;
        private float _currentMoveZ;
        private float _moveXVel;
        private float _moveZVel;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (playerCamera == null)
                playerCamera = Camera.main;
        }

        private void Update()
        {
            if (playerCamera == null)
                playerCamera = Camera.main;

            CheckGrounded();
            HandleInputAndMovement();
        }

        private void CheckGrounded()
        {
            _isGrounded = _cc.isGrounded;
            if (_isGrounded && _verticalVelocity.y < 0f)
            {
                _verticalVelocity.y = -2f; // Slight downward force to stay grounded on slopes
            }

            if (animator != null)
            {
                animator.SetBool(ParamIsGrounded, _isGrounded);
            }
        }

        private void HandleInputAndMovement()
        {
            float horizontal = 0f;
            float vertical = 0f;
            bool isSprinting = false;
            bool jumpPressed = false;

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) vertical += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) vertical -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) horizontal += 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) horizontal -= 1f;
                isSprinting = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
            }
#else
            horizontal = Input.GetAxisRaw("Horizontal");
            vertical = Input.GetAxisRaw("Vertical");
            isSprinting = Input.GetKey(KeyCode.LeftShift);
            jumpPressed = Input.GetButtonDown("Jump");
#endif

            Vector2 input = new Vector2(horizontal, vertical);
            float inputMagnitude = Mathf.Clamp01(input.magnitude);
            float speedMultiplier = isSprinting ? sprintMultiplier : 1.0f;

            // Camera-relative directions
            Vector3 camForward = Vector3.forward;
            Vector3 camRight = Vector3.right;
            if (playerCamera != null)
            {
                camForward = playerCamera.transform.forward;
                camForward.y = 0f;
                camForward.Normalize();

                camRight = playerCamera.transform.right;
                camRight.y = 0f;
                camRight.Normalize();
            }

            Vector3 moveDirectionWorld = (camForward * vertical + camRight * horizontal).normalized;

            if (strafeMode)
            {
                // In strafe mode, character smoothly faces the camera look direction
                if (camForward.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(camForward, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSmoothSpeed * Time.deltaTime);
                }

                // Animator parameters are local relative to character forward/right
                float targetMoveX = horizontal * (isSprinting ? 1.2f : 1.0f);
                float targetMoveZ = vertical * (isSprinting ? 1.2f : 1.0f);

                _currentMoveX = Mathf.SmoothDamp(_currentMoveX, targetMoveX, ref _moveXVel, animDampTime);
                _currentMoveZ = Mathf.SmoothDamp(_currentMoveZ, targetMoveZ, ref _moveZVel, animDampTime);
            }
            else
            {
                // Free look mode: character rotates in the movement direction
                if (moveDirectionWorld.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(moveDirectionWorld, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSmoothSpeed * Time.deltaTime);
                }

                _currentMoveX = Mathf.SmoothDamp(_currentMoveX, 0f, ref _moveXVel, animDampTime);
                _currentMoveZ = Mathf.SmoothDamp(_currentMoveZ, inputMagnitude, ref _moveZVel, animDampTime);
            }

            // Move the CharacterController
            Vector3 horizontalMove = moveDirectionWorld * (runSpeed * speedMultiplier * inputMagnitude);
            _cc.Move(horizontalMove * Time.deltaTime);

            // Handle Jump
            if (_isGrounded && jumpPressed)
            {
                _verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2.0f * gravity);
            }

            _verticalVelocity.y += gravity * Time.deltaTime;
            _cc.Move(_verticalVelocity * Time.deltaTime);

            // Update Animator
            if (animator != null)
            {
                animator.SetFloat(ParamMoveX, _currentMoveX);
                animator.SetFloat(ParamMoveZ, _currentMoveZ);
                animator.SetFloat(ParamSpeed, inputMagnitude * speedMultiplier);
            }
        }
    }
}
