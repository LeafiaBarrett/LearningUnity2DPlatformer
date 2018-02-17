using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityStandardAssets._2D
{
    public class PlatformerCharacter2D : MonoBehaviour
    {
        [SerializeField] private float priv_MaxSpeed = 10f;                    // The fastest the player can travel in the x axis.
        [SerializeField] private float priv_JumpSpeed = 20f;                  // Amount of force added when the player jumps.
        [Range(0, 1)] [SerializeField] private float priv_CrouchSpeed = .36f;  // Amount of maxSpeed applied to crouching movement. 1 = 100%
        [SerializeField] private bool priv_AirControl = false;                 // Whether or not a player can steer while jumping;
        [SerializeField] private LayerMask priv_WhatIsGround;                  // A mask determining what is ground to the character

        const float const_GroundedRadius = .2f; // Radius of the overlap circle to determine if grounded
        private bool priv_Grounded;            // Whether or not the player is grounded.
        private Animator priv_Anim;            // Reference to the player's animator component.
        private Rigidbody2D priv_Rigidbody2D;
        private bool priv_FacingRight = true;  // For determining which way the player is currently facing.
        public CapsuleCollider2D quickCapsule;

        public int airborneFrames = 0;
        public int airborneGracePeriod = 4; // How many frames the player can be airborne and still jump
        public int jumpBufferFrames = 0;
        public int jumpBufferReset = 4;     // How long the input buffer is for pressing jump before landing
        public float avgNormAngle;
        private bool priv_jumpInput = false;

        public Vector2 myCeilingCollisionCenter;
        public Vector2 myCeilingCollisionSize;
        public Vector2 capsuleBottom;
        public List<RaycastHit2D> rayHits;
        //checks for crouching
        public Vector2 crouchCheckPosition;
        public Vector2 crouchCheckSize;
        public CapsuleDirection2D crouchCheckDirection;

		//Whim: Moved these properties here. Physics checks should be merged in one place.
		public float playerGravity = -9.81f;
		public float fallMultiplier = 2.5f;    
		public float lowJumpMultiplier = 5f;
		public float holdJumpFallMultiplier = 2.5f;

		private Vector2 myVelocity;
		private float myWallFollow = 0f;
		public bool SettingRigidbodyPhysics;

		public float SlipAngle = 50;
		public float LandAngle = 50;

		public Vector2 GetSetExample
		{
			get
			{
				//Let's return the velocity
				return (SettingRigidbodyPhysics)?
					priv_Rigidbody2D.velocity:
					myVelocity;
			}

			set
			{
				//Let's set the velocity
				if (SettingRigidbodyPhysics)
				{
					priv_Rigidbody2D.velocity = value;
				}
				else
					myVelocity = value;
			}
		}

        public Vector2 GetCrouchCheckPos
        {
            get
            {
                return crouchCheckPosition + (Vector2)transform.position;
            }
        }

        private void Awake()
        {
			myVelocity = Vector2.zero;
            // Setting up references.
            priv_Anim = GetComponent<Animator>();
            priv_Rigidbody2D = GetComponent<Rigidbody2D>();
            quickCapsule = GetComponent<CapsuleCollider2D>();
            crouchCheckPosition = new Vector3(
                quickCapsule.offset.x * Mathf.Abs(transform.lossyScale.x),
                (quickCapsule.offset.y + quickCapsule.size.y * 0.0125f) * transform.lossyScale.y,
                transform.position.z); 
            crouchCheckSize = new Vector2 (
                quickCapsule.size.x * 0.95f * Mathf.Abs(transform.lossyScale.x), 
                quickCapsule.size.y * 0.975f * transform.lossyScale.y);
            crouchCheckDirection = quickCapsule.direction;
        }

        private void FixedUpdate()
        {
            //Debug.Log(myVelocity.x);
            //Debug.Log( -Mathf.Cos(SlipAngle * Mathf.Deg2Rad));

            //We now only set the object to grounded by collision. This works more reliably
            capsuleBottom = new Vector3(
                quickCapsule.offset.x * Mathf.Abs(transform.lossyScale.x) + transform.position.x,
                quickCapsule.offset.y * transform.lossyScale.y + transform.position.y,
                transform.position.z); //Whim: Forgot to remove Transform Point here. This caused the center to be far off into the distance
            Vector2 quickTotalNormals = Vector2.zero;
            int quickTotal = 0;
            rayHits = new List<RaycastHit2D>();
            
			//Whim: A parameter to find the shortest distance from a wall
			myWallFollow = Mathf.Infinity;

			if (priv_Grounded) foreach (RaycastHit2D repHit in
                Physics2D.CapsuleCastAll(
					(Vector2)capsuleBottom, //origin
                    new Vector2(quickCapsule.size.x * Mathf.Abs(transform.lossyScale.x), quickCapsule.size.y * transform.lossyScale.y), //size
                    quickCapsule.direction, //capsuleDirection
                    0f, //angle (it may be 90)
                    Vector2.down, //raycast direction
                    0.5f, //Raycast distance. A very small value. You can check distance later anyway
                    Physics2D.GetLayerCollisionMask(LayerMask.NameToLayer("PlayerLayer"))  //Collision mask
                )
            )
            {

				Vector2 quickNormal = -repHit.normal; //Whim: Turns out the normal given by the cast is correct.
				//Debug.Log(quickNormal);
                if (quickNormal.y > -Mathf.Cos(SlipAngle * Mathf.Deg2Rad) || repHit.distance <= -0.1f) 
                    continue;

				if (repHit.distance < myWallFollow)
					myWallFollow = repHit.distance;
                rayHits.Add(repHit);
                quickTotalNormals += repHit.normal;
                quickTotal++;
                
            }

            if(rayHits.Count == 0)
            {
                priv_Grounded = false;
				myWallFollow = 0f;
            }
            else
            {
                priv_Grounded = true;
                avgNormAngle = Mathf.Atan2(quickTotalNormals.y / (float)quickTotal, quickTotalNormals.x / (float)quickTotal) - Mathf.PI/2f;
                myWallFollow = (myWallFollow > 0.015 ? myWallFollow : 0f);
            }

			priv_Anim.SetBool("Ground", priv_Grounded);
			// This tracks how long the character's been in the air
            // There's a threshold for this frame counter during which the player can still jump
            // Theoretically this could allow for more than 1 jump in midair, but it's too small a timespan to be of much use anyhow
            if (!priv_Grounded)
            {
                airborneFrames++;
            }
            else
            {
                airborneFrames = 0;
            }
            // Handles the jump buffer
            // If a player presses jump right before landing, this ensures the command still goes through
            // This makes the game feel more responsive
            if (priv_jumpInput)
            {
                jumpBufferFrames++;
            }
            if (jumpBufferFrames > jumpBufferReset)
            {
                jumpBufferFrames = 0;
                priv_jumpInput = false;
                Debug.Log("reset");
            }

            // Set the vertical animation
			priv_Anim.SetFloat("vSpeed", myVelocity.y);
        }

		//Whim: Added this overload to match their structure. Accepts a "hold parameter" to regulate jump height
		public void Move(float move, bool crouch, bool jump)
		{
			Move(move, crouch, jump, false);
		}

		public void Move(float move, bool crouch, bool jump, bool jumpHold)
        {
			//This lets you see the differences between using Unity's and your own physics checks.
			//For instance, if you used your own, you'd need to add your own ceiling collisions
			if (SettingRigidbodyPhysics)
				myVelocity = priv_Rigidbody2D.velocity;

            if (priv_Grounded && Physics2D.OverlapCapsule(
                GetCrouchCheckPos,
                crouchCheckSize,
                crouchCheckDirection,
                0f,
                priv_WhatIsGround
            ))
            {

                //Debug.Log();
                crouch = true;
            }

            Vector2 myGravity = (priv_Grounded)?
				Vector2.zero:
				((myVelocity.y < 0 )?
					new Vector2(0f,playerGravity * fallMultiplier):
					((jumpHold)?
						new Vector2(0f,playerGravity * holdJumpFallMultiplier):
						new Vector2(0f,playerGravity * lowJumpMultiplier)));

            // Set whether or not the character is crouching in the animator
            priv_Anim.SetBool("Crouch", crouch);

            // Reduce the speed if crouching by the crouchSpeed multiplier 
            if (priv_Anim.GetBool("Crouch"))
            {
                move = (crouch ? move * priv_CrouchSpeed : move);
            }

            //only control the player if grounded or airControl is turned on
            if (priv_Grounded || priv_AirControl)
            {
                // The Speed animator parameter is set to the absolute value of the horizontal input.
                priv_Anim.SetFloat("Speed", Mathf.Abs(move));

                // Move the character
                if (priv_Grounded)
                {
					//Debug.Log(Mathf.Cos(avgNormAngle));
					myVelocity = new Vector2(move * priv_MaxSpeed * Mathf.Cos(avgNormAngle), move * priv_MaxSpeed * Mathf.Sin(avgNormAngle));
                }
                else
                {
					myVelocity = new Vector2(move * priv_MaxSpeed, myVelocity.y);
                }

                // If the input is moving the player right and the player is facing left...
                if (move > 0 && !priv_FacingRight)
                {
                    // ... flip the player.
                    Flip();
                }
                // Otherwise if the input is moving the player left and the player is facing right...
                else if (move < 0 && priv_FacingRight)
                {
                    // ... flip the player.
                    Flip();
                }
            }

			myVelocity += myGravity * Time.deltaTime;

            // If the player should jump...
            if (jump && !crouch)
            {
                // Tell the game that a jump input is waiting
                priv_jumpInput = true;
                if (airborneFrames > airborneGracePeriod)
                {
                    Debug.Log("air jump attempt");
                }
            }
            if (priv_jumpInput && (priv_Grounded || airborneFrames <= airborneGracePeriod))
            {
                // Add a vertical force to the player.
                myWallFollow = 0f;
                Debug.Log(airborneFrames);
                priv_jumpInput = false;
                jumpBufferFrames = 0;
                priv_Grounded = false;
                priv_Anim.SetBool("Ground", false);
                // Set the player's vertical speed to 0 before applying the jump force
                // Prevents jumps from having a pitiful height when done during the grace period
                // Also fixes a bug where jumping on the same frame as landing caused the same kind of pathetic jump height
				myVelocity = new Vector2(myVelocity.x, priv_JumpSpeed);
            }

			//Whim: How we set the player's move is dependant on the physics check
			if (!SettingRigidbodyPhysics)
			{
				priv_Rigidbody2D.MovePosition(transform.position + new Vector3(0f,-myWallFollow,0f) + (Vector3)myVelocity*Time.deltaTime);
			}
			else
			{
				priv_Rigidbody2D.velocity = myVelocity;
				transform.position += new Vector3(0f,-myWallFollow,0f);
			}
            Debug.Log(myWallFollow);
        }


        private void Flip()
        {
            // Switch the way the player is labelled as facing.
            priv_FacingRight = !priv_FacingRight;

            // Multiply the player's x local scale by -1.
            Vector3 theScale = transform.localScale;
            theScale.x *= -1;
            transform.localScale = theScale;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(new Vector3(transform.position.x + myCeilingCollisionCenter.x, transform.position.y + myCeilingCollisionCenter.y, transform.position.z), new Vector3(myCeilingCollisionSize.x, myCeilingCollisionSize.y, 0f));
        }

		//It's typically better to have the collision checks made on the frame, here. It's also more accurate for jump through walls
		void OnCollisionStay2D(Collision2D itsCollision)
		{
			if (itsCollision.enabled && !priv_Grounded && airborneFrames > 1)
			{
				Vector2 quickTotalNormal = Vector2.zero;
				int quickNormalCount = 0;
				foreach(ContactPoint2D repPoint in itsCollision.contacts)
				{
					quickTotalNormal += repPoint.normal;
					quickNormalCount++;
				}

				Vector2 quickAverageNormal = (quickTotalNormal / (float)quickNormalCount).normalized;
				if (quickAverageNormal.y > Mathf.Cos(LandAngle * Mathf.Deg2Rad))
					priv_Grounded = true;
			}
		}

        void OnCollisionEnter2D(Collision2D itsCollision)
        {

        }
    }
}
