using System;
using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;

namespace UnityStandardAssets.Characters.FirstPerson
{
    [RequireComponent(typeof (Rigidbody))]
    [RequireComponent(typeof (CapsuleCollider))]
    public class RigidbodyFirstPersonController : MonoBehaviour
    {
        [Serializable]
        public class MovementSettings
        {
            public float ForwardSpeed = 8.0f;   // Speed when walking forward
            public float BackwardSpeed = 4.0f;  // Speed when walking backwards
            public float StrafeSpeed = 4.0f;    // Speed when walking sideways
            public float RunMultiplier = 2.0f;   // Speed when sprinting
            public float AerialSlowDownMultiplier = 3.0f;  // Multiplier to SlowDown in the Air
	        public KeyCode RunKey = KeyCode.LeftShift;
            public float JumpForce = 10f;  // Force applied at each frame of jumping
            public float JumpThreshold = 0.25f; // Time during which the player can hold its jump
            public AnimationCurve SlopeCurveModifier = new AnimationCurve(new Keyframe(-90.0f, 1.0f), new Keyframe(0.0f, 1.0f), new Keyframe(90.0f, 0.0f));
            [HideInInspector] public float CurrentTargetSpeed = 8f;

            private bool m_Running;


            public void UpdateDesiredTargetSpeed(Vector2 input)
            {
	            if (input == Vector2.zero) return;
				if (input.x > 0 || input.x < 0)
				{
					//strafe
					CurrentTargetSpeed = StrafeSpeed;
				}
				if (input.y < 0)
				{
					//backwards
					CurrentTargetSpeed = BackwardSpeed;
				}
				if (input.y > 0)
				{
					//forwards
					//handled last as if strafing and moving forward at the same time forwards speed should take precedence
					CurrentTargetSpeed = ForwardSpeed;
				}

	            if (Input.GetKey(RunKey))
	            {
		            CurrentTargetSpeed *= RunMultiplier;
		            m_Running = true;
	            }
	            else
	            {
		            m_Running = false;
	            }
            }

            public bool Running
            {
                get { return m_Running; }
            }
        }


        [Serializable]
        public class AdvancedSettings
        {
            public float groundCheckDistance = 0.01f; // distance for checking if the controller is grounded ( 0.01f seems to work best for this )
            public float stickToGroundHelperDistance = 0.5f; // stops the character
            public float slowDownRate = 20f; // rate at which the controller comes to a stop when there is no input
            public bool airControl; // can the user control the direction that is being moved in the air
            [Tooltip("set it to 0.1 or more if you get stuck in wall")]
            public float shellOffset; //reduce the radius by that ratio to avoid getting stuck in wall (a value of 0.1f is nice)
        }


        public Camera cam;
        public MovementSettings movementSettings = new MovementSettings();
        public MouseLook mouseLook = new MouseLook();
        public AdvancedSettings advancedSettings = new AdvancedSettings();


        private Rigidbody m_RigidBody;
        private CapsuleCollider m_Capsule;
        private float m_YRotation;
        private Vector3 m_GroundContactNormal;
        private bool m_Jump, m_PreviouslyGrounded, m_Jumping, m_IsGrounded, m_CanSlowDown, m_CanJump, m_CanDoubleJump, m_Jumped;

        public float m_JumpDuration; // Duration of the current jump


        // m_Jumping : true if the player is currently jumping, false otherwise
        // m_CanSlowDown : true if the player can still slow down during his current aerial movement, false otherwise : if its horizontal mobility is already 0
        // m_CanDoubleJump : true if the player can currently doubleJump, false otherwise. Set to true only after that the player used its first jump
        // m_CanJump : true if the player can currently jump, false otherwise. Set to true when the player doesn't push the jump button. Set to false at the end of a jump.

        public Vector3 Velocity
        {
            get { return m_RigidBody.velocity; }
        }

        public bool Grounded
        {
            get { return m_IsGrounded; }
        }

        public bool Jumping
        {
            get { return m_Jumping; }
        }

        public bool Running
        {
            get
            {
				return movementSettings.Running;
            }
        }


        private void Start()
        {
            m_RigidBody = GetComponent<Rigidbody>();
            m_Capsule = GetComponent<CapsuleCollider>();
            mouseLook.Init (transform, cam.transform);
        }


        private void Update()
        {
            RotateView();

            if (CrossPlatformInputManager.GetButton("Jump") && m_CanJump)
            {
                m_Jump = true;
            } else if (!CrossPlatformInputManager.GetButton("Jump") && (m_IsGrounded || m_CanDoubleJump))
            {
                m_CanJump = true;
            }
           
        }


        private void FixedUpdate()
        {
            GroundCheck();
            Vector2 input = GetInput();

            if (Mathf.Abs(input.x) > float.Epsilon || Mathf.Abs(input.y) > float.Epsilon)
            {
                Vector3 desiredMove = cam.transform.forward * input.y + cam.transform.right * input.x;
                desiredMove = Vector3.ProjectOnPlane(desiredMove, m_GroundContactNormal).normalized;

                // always move along the camera forward as it is the direction that it being aimed at
                if (m_IsGrounded)
                {
                    desiredMove.x = desiredMove.x * movementSettings.CurrentTargetSpeed;
                    desiredMove.z = desiredMove.z * movementSettings.CurrentTargetSpeed;
                    desiredMove.y = desiredMove.y * movementSettings.CurrentTargetSpeed;
                    if (m_RigidBody.velocity.sqrMagnitude <
                        (movementSettings.CurrentTargetSpeed * movementSettings.CurrentTargetSpeed))
                    {
                        m_RigidBody.AddForce(desiredMove * SlopeMultiplier(), ForceMode.Impulse);
                    }
                } else if (advancedSettings.airControl && m_CanSlowDown)
                {
                    Vector3 aerialMove = Vector3.Project(desiredMove, m_RigidBody.velocity);

                    // The player should slow down, whatever direction he is currently going towards
                    if (OpposedTo(aerialMove, m_RigidBody.velocity))
                    {

                        Vector3 oldVelocity = m_RigidBody.velocity;
                        Vector3 slowDownForce = Vector3.Normalize(aerialMove) * movementSettings.AerialSlowDownMultiplier;
                        m_RigidBody.AddForce(slowDownForce, ForceMode.Impulse);

                        if (OpposedTo(m_RigidBody.velocity, oldVelocity))
                        {
                            m_RigidBody.velocity = new Vector3(0f, m_RigidBody.velocity.y, 0f);
                            m_CanSlowDown = false;
                        } 
                    }
                }
                
            }

            if (m_IsGrounded || m_CanDoubleJump || m_Jumping)
            {

                //Debug.Log("Value of m_CanDoubleJump : " + m_CanDoubleJump);

                if (m_Jump)
                {
                    // First frame of jumping
                    if (!m_Jumping)
                    {
                        m_RigidBody.drag = 0f;
                        m_Jumping = true;
                        m_JumpDuration = 0f;
                        if (m_CanDoubleJump)
                        {
                            m_CanDoubleJump = false;
                            m_RigidBody.velocity = new Vector3(m_RigidBody.velocity.x, 0f, m_RigidBody.velocity.z);
                        }
                    }
                    else
                    {
                        m_JumpDuration += Time.fixedDeltaTime;
                    }

                    m_RigidBody.AddForce(new Vector3(0f, movementSettings.JumpForce, 0f), ForceMode.Impulse);
                }

                if (m_IsGrounded)
                {
                    m_RigidBody.drag = 5f;
                }
                else
                {
                    // Condition to stop jumping
                    if (m_Jumping && (!m_Jump || m_JumpDuration >= movementSettings.JumpThreshold))
                    {
                        m_Jumping = false;
                        m_CanJump = false;
                        if (!m_Jumped)
                        {
                            m_Jumped = true;
                            m_CanDoubleJump = true;
                        }
                    }

                    m_RigidBody.drag = 0f;
                }

                //if (!m_Jumping && Mathf.Abs(input.x) < float.Epsilon && Mathf.Abs(input.y) < float.Epsilon && m_RigidBody.velocity.magnitude < 1f)
                //{
                //    m_RigidBody.Sleep();
                //}
            }
            else
            {
                m_RigidBody.drag = 0f;
                //if (m_PreviouslyGrounded && !m_Jumping)
                //{
                //    StickToGroundHelper();
                //}
            }

            m_Jump = false;
        }


        private float SlopeMultiplier()
        {
            float angle = Vector3.Angle(m_GroundContactNormal, Vector3.up);
            return movementSettings.SlopeCurveModifier.Evaluate(angle);
        }


        private void StickToGroundHelper()
        {
            RaycastHit hitInfo;
            if (Physics.SphereCast(transform.position, m_Capsule.radius * (1.0f - advancedSettings.shellOffset), Vector3.down, out hitInfo,
                                   ((m_Capsule.height/2f) - m_Capsule.radius) +
                                   advancedSettings.stickToGroundHelperDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                if (Mathf.Abs(Vector3.Angle(hitInfo.normal, Vector3.up)) < 85f)
                {
                    m_RigidBody.velocity = Vector3.ProjectOnPlane(m_RigidBody.velocity, hitInfo.normal);
                }
            }
        }


        private Vector2 GetInput()
        {
            
            Vector2 input = new Vector2
                {
                    x = CrossPlatformInputManager.GetAxis("Horizontal"),
                    y = CrossPlatformInputManager.GetAxis("Vertical")
                };
			movementSettings.UpdateDesiredTargetSpeed(input);
            return input;
        }


        private void RotateView()
        {
            //avoids the mouse looking if the game is effectively paused
            if (Mathf.Abs(Time.timeScale) < float.Epsilon) return;

            // get the rotation before it's changed
            float oldYRotation = transform.eulerAngles.y;

            mouseLook.LookRotation (transform, cam.transform);

            if (m_IsGrounded || advancedSettings.airControl)
            {
                // Rotate the rigidbody velocity to match the new direction that the character is looking
                Quaternion velRotation = Quaternion.AngleAxis(transform.eulerAngles.y - oldYRotation, Vector3.up);
                m_RigidBody.velocity = velRotation*m_RigidBody.velocity;
            }
        }

        /// sphere cast down just beyond the bottom of the capsule to see if the capsule is colliding round the bottom
        private void GroundCheck()
        {
            m_PreviouslyGrounded = m_IsGrounded;
            RaycastHit hitInfo;
            if (Physics.SphereCast(transform.position, m_Capsule.radius * (1.0f - advancedSettings.shellOffset), Vector3.down, out hitInfo,
                                   ((m_Capsule.height/2f) - m_Capsule.radius) + advancedSettings.groundCheckDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                m_IsGrounded = true;
                m_CanSlowDown = true;
                m_Jumped = false;
                m_GroundContactNormal = hitInfo.normal;
            }
            else
            {
                m_IsGrounded = false;
                m_GroundContactNormal = Vector3.up;
            }
            if (!m_PreviouslyGrounded && m_IsGrounded && m_Jumping)
            {
                m_Jumping = false;
            }
        }

        private Boolean OpposedTo(Vector3 vector1, Vector3 vector2)
        {
            float dotProduct = Vector3.Dot(vector1, vector2);
            float magnitudeProduct = vector1.magnitude * vector2.magnitude;
            return (dotProduct == -magnitudeProduct);
        }
    }
}
