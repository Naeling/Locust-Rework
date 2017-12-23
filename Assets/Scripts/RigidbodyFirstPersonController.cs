using System;
using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;

namespace UnityStandardAssets.Characters.FirstPerson
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class RigidbodyFirstPersonController : MonoBehaviour
    {
        [Serializable]
        public class MovementSettings
        {
            public float ForwardSpeed = 8.0f;   // Speed when walking forward
            public float BackwardSpeed = 4.0f;  // Speed when walking backwards
            public float StrafeSpeed = 4.0f;    // Speed when walking sideways
            public float WallRunMaxSpeed = 24f;    // Speed when running on a wall
            public float RunMultiplier = 2.0f;   // Speed when sprinting
            public float WallRunForce = 100f; // Multiplier to define the force applied when starting a wall run, depending on the current velocity
            public float AerialSlowDownMultiplier = 3.0f;  // Multiplier to SlowDown in the Air
            public float WallRunSlowDownMultiplier = 0.3f; // Multiplier to slow down when starting a wall run

            public KeyCode RunKey = KeyCode.LeftShift;
            public float JumpForce = 10f;  // Force applied at each frame of jumping
            public float JumpThreshold = 0.25f; // Time during which the player can hold its jump
            public float WallRunThreshold = 6.0f; // Minimum speed for a wall Run to be initiated
            public float RotationSpeedOnWall = 30f; // Speed of the player's rotation when starting a wall run
            public float WallRunAngle = 15f; // Angle of inclination with the wall while wall running
            public float MaxWallRunCompensationForce = 8.01f; // Maximum force applied to compensate the gravity while wall running
            public float WallRunDrag = 0.15f; // Horizontal slow down force
            public float WallRunFallingThresholdSpeed = 15f;   // Speed below which the player progressively falls of the wall
            public float WallRunNoCompensationThresholdSpeed = 10f;   // Speed below which no more composation force is applied during a wall run
            public float WallJumpHorizontalForce = 30f;  // Force applied to leave the wall when wall jumping
            public float WallRunCoolDown = 0.4f; // Cooldown applied after a wall jump to prevent the player from triggerring a wall run again before leaving the wall
            public float MaxTurboPoints = 100f;  // Maximum turbo points that a player can have
            public float TurboReloadMultiplier;  // Multiplier to modify the turbo's reloading speed
            public float turboConsumptionMultiplier;  // Multiplier to modify the turbo's consumption speed


            public AnimationCurve SlopeCurveModifier = new AnimationCurve(new Keyframe(-90.0f, 1.0f), new Keyframe(0.0f, 1.0f), new Keyframe(90.0f, 0.0f));
            [HideInInspector] public float CurrentTargetSpeed = 8f;

            private bool m_Running;

            // Only used to determine a speed on the ground
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

                //if (Input.GetKey(RunKey))
                if (true)
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

        enum WallDirection { Right, Left };


        public Camera cam;
        public MovementSettings movementSettings = new MovementSettings();
        public MouseLook mouseLook = new MouseLook();
        public AdvancedSettings advancedSettings = new AdvancedSettings();

        public LayerMask wallLayer; // layer for the walls detection

        private Rigidbody m_RigidBody;
        private CapsuleCollider m_Capsule;
        private float m_YRotation;
        private Vector3 m_GroundContactNormal;
        private bool m_Jump, m_PreviouslyGrounded, m_Jumping, m_IsGrounded, m_CanSlowDown, m_CanJump, m_CanDoubleJump, m_Jumped, m_IsWallRunning;

        private float m_JumpDuration; // Duration of the current jump
        private float radius;
        private float rayCastLengthCheck; // Length for the wall ray cast
        private Vector3 wallRunForward;  // Forward vector of the wall currently running on
        private Vector3 wallRunNormal;  // Normal vector of the wall currently running on
        private float wallRunCoolDown; // Time before the player can perform a wall run again
        private Boolean mustConsiderDirectionInput;   // Should the code be checking for the input's direction when jumping ? 
        private Boolean m_Turbo;  // True if the player input a turbo
        private float turboPoints;   // Current number of turbo points
        private Boolean m_Immobilized;  // True if the player is currently immobilized

        // m_Jumping : true if the player is currently jumping, false otherwise
        // m_CanSlowDown : true if the player can still slow down during his current aerial movement, false otherwise : if its horizontal mobility is already 0
        // m_CanDoubleJump : true if the player can currently doubleJump, false otherwise. Set to true only after that the player used its first jump
        // m_CanJump : true if the player can currently jump, false otherwise. Set to true when the player doesn't push the jump button. Set to false at the end of a jump.
        // m_IsWallRunning : true if the player is currently wall running, false otherwise

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
            radius = m_Capsule.radius;
            rayCastLengthCheck = radius + 0.5f;
            mouseLook.Init(transform, cam.transform);
        }


        private void Update()
        {
            if (!m_IsWallRunning)
            {
                RotateView();
            }

            // Jump input
            if ((CrossPlatformInputManager.GetButton("Jump") || CrossPlatformInputManager.GetAxis("Jump") == 1) && m_CanJump)
            {
                m_Jump = true;
            }
            else if ((!CrossPlatformInputManager.GetButton("Jump") && CrossPlatformInputManager.GetAxis("Jump") == 0) && (m_IsGrounded || m_CanDoubleJump || m_IsWallRunning))
            {
                m_CanJump = true;
                m_Jump = false;
            }
            else
            {
                m_Jump = false;
            }

            // Turbo input
            if ((CrossPlatformInputManager.GetButton("Turbo") || CrossPlatformInputManager.GetAxis("Turbo") == 1) && !m_Turbo)
            {
                m_Turbo = true;
            }
            else
            {
                m_Turbo = false;
                // Reload Turbo
                if (turboPoints < movementSettings.MaxTurboPoints && !m_Turbo)
                {
                    turboPoints += Time.deltaTime * movementSettings.TurboReloadMultiplier;
                    if (turboPoints > movementSettings.MaxTurboPoints)
                        turboPoints = movementSettings.MaxTurboPoints;
                }
            }

            // WallRun time
            if (wallRunCoolDown > 0)
            {
                wallRunCoolDown -= Time.deltaTime;
                if (wallRunCoolDown < 0)
                {
                    wallRunCoolDown = 0f;
                }
            }    
        }


        private void FixedUpdate()
        {
            if (m_Immobilized)
            {
                return;
            }

            GroundCheck();
            Vector2 input = GetInput();
            Boolean wannaMove = (Mathf.Abs(input.x) > float.Epsilon || Mathf.Abs(input.y) > float.Epsilon);
            Vector3 desiredMove = new Vector3();

            // ***** START OF DIRECTION SECTION ***** //

            if (wannaMove || m_IsWallRunning)
            {

                desiredMove = cam.transform.forward * input.y + cam.transform.right * input.x;
                desiredMove = Vector3.ProjectOnPlane(desiredMove, m_GroundContactNormal).normalized;

                // GROUNDED MOVEMENTS
                if (m_IsGrounded)
                {
                    float turboMultiplier;
                    // TODO remove the hard int
                    turboMultiplier = m_Turbo ? 1.3f : 1f;
                    desiredMove *= movementSettings.CurrentTargetSpeed * turboMultiplier; 
                    if (m_RigidBody.velocity.sqrMagnitude <
                        (movementSettings.CurrentTargetSpeed * movementSettings.CurrentTargetSpeed))
                    {
                        m_RigidBody.AddForce(desiredMove * SlopeMultiplier(), ForceMode.Impulse);
                    }
                }
                // AIR MOVEMENTS
                else if (advancedSettings.airControl && m_CanSlowDown && !m_IsWallRunning)
                {
                    Vector3 aerialMove = Vector3.Project(desiredMove, m_RigidBody.velocity);

                    if (OpposedTo(aerialMove, m_RigidBody.velocity))
                    {

                        Vector3 oldVelocity = m_RigidBody.velocity;
                        aerialMove *= movementSettings.AerialSlowDownMultiplier;
                        m_RigidBody.AddForce(aerialMove, ForceMode.Impulse);

                        if (OpposedTo(m_RigidBody.velocity, oldVelocity))
                        {
                            m_RigidBody.velocity = new Vector3(0f, m_RigidBody.velocity.y, 0f);
                            m_CanSlowDown = false;
                        }
                    }
                }
                // WALLRUN MOVEMENTS
                else if (m_IsWallRunning && !m_Jump) 
                {
                    if (m_RigidBody.velocity.y < 0f)
                    {
                        // TODO look for a better solution by tweeking the gravity value used for the player
                        Vector3 compensationForce = Vector3.up * movementSettings.MaxWallRunCompensationForce;
                        // TODO project the velocity on the forward direction before checking 
                        if (m_RigidBody.velocity.magnitude > movementSettings.WallRunFallingThresholdSpeed)
                        {
                            m_RigidBody.AddForce(compensationForce, ForceMode.Impulse);
                        }
                    }
                }
            }

            // ***** END OF DIRECTION SECTION ***** //

            // ***** START OF THE JUMP SECTION ***** //

            if (CanJump())
            {

                if (m_Jump)
                {
                    // FIRST FRAME OF JUMPING
                    if (!m_Jumping)
                    {
                        m_Jumping = true;
                        m_JumpDuration = 0f;
                        // DOUBLE JUMP
                        if (m_CanDoubleJump)
                        {
                            m_CanDoubleJump = false;
                            m_RigidBody.velocity = new Vector3(m_RigidBody.velocity.x, 0f, m_RigidBody.velocity.z);
                            if (wannaMove && mustConsiderDirectionInput)
                            {
                                // TODO prevent backwards movements
                                float angle = Vector3.SignedAngle(m_RigidBody.velocity, desiredMove, Vector3.up);
                                m_RigidBody.velocity = Quaternion.AngleAxis(angle, Vector3.up) * m_RigidBody.velocity;
                            }
                        }
                        // WALL JUMP
                        else if (m_IsWallRunning)
                        {
                            wallRunCoolDown = movementSettings.WallRunCoolDown;
                            m_IsWallRunning = false;
                            m_CanDoubleJump = true;
                            //mustConsiderDirectionInput = false;
                            Vector3 horizontalForce = wallRunNormal * movementSettings.WallJumpHorizontalForce;
                            // TODO Consider to add a forward force to give the player a boost in speed
                            m_RigidBody.AddForce(horizontalForce, ForceMode.Impulse);
                        }
                    }
                    else
                    {
                        m_JumpDuration += Time.fixedDeltaTime;
                    }
                    m_RigidBody.AddForce(new Vector3(0f, movementSettings.JumpForce, 0f), ForceMode.Impulse);
                }

                if (!m_IsGrounded)
                {
                    // CONDITION TO STOP JUMPING
                    if (m_Jumping && (!m_Jump || m_JumpDuration >= movementSettings.JumpThreshold))
                    {
                        m_Jumping = false;
                        m_CanJump = false;
                        if (!m_Jumped)
                        {
                            m_Jumped = true;
                            m_CanDoubleJump = true;
                            mustConsiderDirectionInput = true;
                        }
                    }
                }
                else
                {   
                    //TODO Add an input check
                    if (turboPoints > 0)
                    {
                        turboPoints -= Time.fixedDeltaTime * movementSettings.turboConsumptionMultiplier;
                        if (turboPoints < 0)
                        {
                            turboPoints = 0f;
                        }
                    }

                }
            }

            // ***** END OF THE JUMP SECTION ***** //

            // ***** START OF THE WALLRUN INITIATION SECTION ***** //

            if (CanWallRun())
            {
                wallRunForward = GetWallForward();
                wallRunNormal = GetWallNormal();

                Vector3 velocityOnWall = Vector3.Project(m_RigidBody.velocity, wallRunForward);

                if (velocityOnWall.magnitude > movementSettings.WallRunThreshold)
                {
                    m_IsWallRunning = true;
                    // PATCH HERE FOR A MORE REALISTIC SLOW DOWN FEELING
                    if (m_RigidBody.velocity.y < 0f)
                    {
                        m_RigidBody.velocity = new Vector3(m_RigidBody.velocity.x, 0f, m_RigidBody.velocity.z);
                    }
                    if (velocityOnWall.sqrMagnitude < (movementSettings.WallRunMaxSpeed * movementSettings.WallRunMaxSpeed))
                    {
                        Vector3 accelerationForce = wallRunForward * movementSettings.WallRunForce;
                        accelerationForce *= 1f - (velocityOnWall.magnitude - movementSettings.WallRunThreshold) / (movementSettings.WallRunMaxSpeed - movementSettings.WallRunThreshold);
                        m_RigidBody.AddForce(accelerationForce, ForceMode.Impulse);
                    }
                }
            }

            // ***** END OF THE WALLRUN INITIATION SECTION ***** //


            // ***** START OF ROTATION CODE ***** //

            if (m_IsWallRunning)
            {
                float angleYAxis = Vector3.SignedAngle(m_RigidBody.transform.forward, wallRunForward, Vector3.up);
                float rotationYAxis = 0f;

                if (Math.Abs(angleYAxis) > float.Epsilon)
                {
                    if (Math.Abs(angleYAxis) < movementSettings.RotationSpeedOnWall * Time.fixedDeltaTime)
                    {
                        rotationYAxis = angleYAxis;
                    }
                    else
                    {
                        rotationYAxis = movementSettings.RotationSpeedOnWall;
                        if (angleYAxis < 0)
                        {
                            rotationYAxis = -rotationYAxis;
                        }
                    }
                    transform.Rotate(Vector3.up * Time.fixedDeltaTime * rotationYAxis);
                }

                Vector3 targetInclination = Vector3.up;
                if (IsWallToDirection(WallDirection.Right))
                {
                    targetInclination = Quaternion.AngleAxis(movementSettings.WallRunAngle, wallRunForward) * targetInclination;
                }
                else if (IsWallToDirection(WallDirection.Left))
                {
                    targetInclination = Quaternion.AngleAxis(-movementSettings.WallRunAngle, wallRunForward) * targetInclination;
                }

                float angleXAxis = Vector3.SignedAngle(m_RigidBody.transform.up, targetInclination, wallRunForward);
                float rotationXAxis = 0f;

                if (Math.Abs(angleXAxis) < movementSettings.RotationSpeedOnWall * Time.fixedDeltaTime)
                {
                    rotationXAxis = angleXAxis;
                }
                else
                {
                    rotationXAxis = movementSettings.RotationSpeedOnWall;
                    if (angleXAxis < 0)
                    {
                        rotationXAxis = -rotationXAxis;
                    }
                }
                transform.Rotate(wallRunForward * Time.fixedDeltaTime * rotationXAxis, Space.World);
            }

            // ***** END OF ROTATION CODE ***** //

            // DRAG CONTROL
            if (m_IsGrounded)
            {
                m_RigidBody.drag = 5f;
            }
            else if (m_IsWallRunning)
            {
                m_RigidBody.drag = movementSettings.WallRunDrag;
            }
            else
            {
                m_RigidBody.drag = 0f;
            }
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
                                   ((m_Capsule.height / 2f) - m_Capsule.radius) +
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

        // Pas possible de bouger le point de vue sans bouger le personnage.
        // Tourner la caméra revient à tourner le personnage.
        // La caméra EST le personnage.
        private void RotateView()
        {
            //avoids the mouse looking if the game is effectively paused
            if (Mathf.Abs(Time.timeScale) < float.Epsilon) return;

            // get the rotation before it's changed
            float oldYRotation = transform.eulerAngles.y;

            mouseLook.LookRotation(transform, cam.transform);

            // this part is ok when grounded.
            // When in the air, it's more than discutable 
            if (m_IsGrounded || advancedSettings.airControl)
            {
                // Rotate the rigidbody velocity to match the new direction that the character is looking
                Quaternion velRotation = Quaternion.AngleAxis(transform.eulerAngles.y - oldYRotation, Vector3.up);
                m_RigidBody.velocity = velRotation * m_RigidBody.velocity;
            }
        }

        /// sphere cast down just beyond the bottom of the capsule to see if the capsule is colliding round the bottom
        private void GroundCheck()
        {
            m_PreviouslyGrounded = m_IsGrounded;
            RaycastHit hitInfo;
            if (Physics.SphereCast(transform.position, m_Capsule.radius * (1.0f - advancedSettings.shellOffset), Vector3.down, out hitInfo,
                                   ((m_Capsule.height / 2f) - m_Capsule.radius) + advancedSettings.groundCheckDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                m_IsGrounded = true;
                m_CanSlowDown = true;
                m_IsWallRunning = false;
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
            // Projection des vecteur dans le plan XZ, Projection du vecteur joueur sur le vecteur mur, Normalisation des vecteurs, calcul du produit vectoriel
            vector1 = Vector3.Normalize(Vector3.ProjectOnPlane(vector1, Vector3.up));
            vector2 = Vector3.Normalize(Vector3.Project(Vector3.ProjectOnPlane(vector2, Vector3.up), vector1));

            return (Vector3.Dot(Vector3.Normalize(vector1), Vector3.Normalize(vector2)) == -1);
        }

        public Boolean IsWallToLeftOrRight()
        {
            return (IsWallToDirection(WallDirection.Left) || IsWallToDirection(WallDirection.Right));
        }

        private Boolean IsWallToDirection(WallDirection d)
        {
            Vector3 direction = new Vector3();
            if (d == WallDirection.Right)
            {
                direction = transform.right;
            }
            else if (d == WallDirection.Left)
            {
                direction = -transform.right;
            }
            bool wall = Physics.Raycast(new Vector3(transform.position.x, transform.position.y, transform.position.z), direction, rayCastLengthCheck, wallLayer);
            return wall;
        }

        private Vector3 GetWallForward()
        {
            Vector3 wallForward = new Vector3();
            if (IsWallToDirection(WallDirection.Right))
            {
                wallForward = GetWallForward(WallDirection.Right);
            } else if (IsWallToDirection(WallDirection.Left))
            {
                wallForward = GetWallForward(WallDirection.Left);
            }
            return wallForward;
        }

        private Vector3 GetWallForward(WallDirection d)
        {
            RaycastHit hit;
            GameObject wallInContact;
            Vector3 wallForwardDirection = new Vector3();
            Vector3 direction = new Vector3();
            if (d == WallDirection.Left)
            {
                direction = -transform.right;
            }
            else if (d == WallDirection.Right)
            {
                direction = transform.right;
            }
            if (Physics.Raycast(new Vector3(transform.position.x, transform.position.y, transform.position.z), direction, out hit, 10f * rayCastLengthCheck, wallLayer))
            {
                wallInContact = hit.collider.gameObject;
                if (OpposedTo(wallInContact.transform.forward, cam.transform.forward))
                {
                    wallForwardDirection = -wallInContact.transform.forward;
                }
                else
                {
                    wallForwardDirection = wallInContact.transform.forward;
                }
            }
            return wallForwardDirection;
        }

        private Vector3 GetWallNormal()
        {
            Vector3 wallNormal = new Vector3();
            if (IsWallToDirection(WallDirection.Right))
            {
                wallNormal = GetWallNormal(WallDirection.Right);
            } else if (IsWallToDirection(WallDirection.Left))
            {
                wallNormal = GetWallNormal(WallDirection.Left);
            }
            return wallNormal;
        }

        // return the direction of the wall normal vector
        private Vector3 GetWallNormal(WallDirection d)
        {
            RaycastHit hit;
            GameObject wallInContact;
            Vector3 direction = new Vector3();
            Vector3 normal = new Vector3();

            if (d == WallDirection.Right)
            {
                direction = cam.transform.right;
            }
            else if (d == WallDirection.Left)
            {
                direction = -cam.transform.right;
            }

            if (Physics.Raycast(new Vector3(transform.position.x, transform.position.y, transform.position.z), direction, out hit, 10f * rayCastLengthCheck, wallLayer))
            {
                wallInContact = hit.collider.gameObject;
                normal = wallInContact.transform.right;
            }
            return normal;
        }

        private bool CanJump()
        {
            return (m_IsGrounded || m_CanDoubleJump || m_Jumping || m_IsWallRunning);
        }

        private bool CanWallRun()
        {
            return (!m_IsGrounded && IsWallToLeftOrRight() && !m_IsWallRunning && wallRunCoolDown == 0f);
        }
    }
}
