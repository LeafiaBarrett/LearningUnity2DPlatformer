using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityStandardAssets._2D
{
	public class PlatformerCharacter2D : MonoBehaviour
	{
		[SerializeField] private float myMaxSpeed = 10f;					// The fastest the player can travel.
		[SerializeField] private float priv_JumpSpeed = 20f;				// Amount of force added when the player jumps.
		[Range(0, 1)] [SerializeField] private float priv_CrouchSpeed = .36f;  // Amount of maxSpeed applied to crouching movement. 1 = 100%
		[SerializeField] private bool priv_AirControl = false;				// Whether or not a player can steer while jumping;
		[SerializeField] private LayerMask priv_WhatIsGround;               // A mask determining what is ground to the character
		[SerializeField] private LayerMask priv_WhatIsCeiling;				// Determines what prevents the player from standing up

		const float const_GroundedRadius = .2f; // Radius of the overlap circle to determine if grounded
		private bool priv_Grounded;				// Whether or not the player is grounded.
		private Animator priv_Anim;				// Reference to the player's animator component.
		private Rigidbody2D priv_Rigidbody2D;
		private bool priv_FacingRight = true;  // For determining which way the player is currently facing.
		public CapsuleCollider2D quickCapsule;

		public int airborneFrames = 0;
		public int airborneGracePeriod = 4; // How many frames the player can be airborne and still jump
		public int jumpBufferFrames = 0;
		public int jumpBufferReset = 4;  // How long the input buffer is for pressing jump before landing
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
		private float myOriginalMaxSpeed;

		//angles used to separate collisions into normal, steep, too steep, walljump wall, no-walljump wall, and ceiling
		public float ceilingAngle = 135f;
		public float noJumpWallAngle = 100f;
		public float wallAngle = 80f;
		public float slipAngle = 60f;
		public float steepAngle = 30f;
		public float speedUpAngle = 15f; //if the slope is more than this, the player speeds up when running downhill

		//player state enums
		public enum PlayerStateGrounded
		{
			Idle,
			Run,
			Slip //sliding down a slope after losing footing 
		}
		public enum PlayerStateAerial
		{
			No,
			Jump, //upwards
			Fall //downwards
		}
		public enum PlayerStateCrouch
		{
			No,
			Crouch //Run and Crouch produces crouch walking
		}

		private PlayerStateGrounded stateGrounded;
		private PlayerStateAerial stateAerial;
		private PlayerStateCrouch stateCrouch;

		public Vector2 GetSetVelocity
		{
			get
			{
				//Let's return the velocity
				return myVelocity;
			}

			set
			{
				//Let's set the velocity
				myVelocity = value;
			}
		}

		//checking position for if the player can stand up
		public Vector2 GetCrouchCheckPos
		{
			get
			{
				return crouchCheckPosition + (Vector2)transform.position;
			}
		}

		//check facing direction, which is tied to player scale
		bool FacingLeft
		{
			get { return (transform.localScale.x < 0); }
		}

		public PlayerStateCrouch SetStateCrouch
		{
			set
			{
				switch (value)
				{
					case PlayerStateCrouch.No:
						priv_Anim.SetBool("Crouch", false);
						break;
					case PlayerStateCrouch.Crouch:
						priv_Anim.SetBool("Crouch", true);
						break;
				}
				stateCrouch = value;
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
			crouchCheckSize = new Vector2(
				quickCapsule.size.x * 0.95f * Mathf.Abs(transform.lossyScale.x),
				quickCapsule.size.y * 0.975f * transform.lossyScale.y);
			crouchCheckDirection = quickCapsule.direction;
			//not necessary, but initializing the enums here to make myself feel better
			stateGrounded = PlayerStateGrounded.Idle;
			stateAerial = PlayerStateAerial.No;
			stateCrouch = PlayerStateCrouch.No;
			myOriginalMaxSpeed = myMaxSpeed;
		}

		private void FixedUpdate()
		{
			//We now only set the object to grounded by collision. This works more reliably
			capsuleBottom = new Vector3(
				quickCapsule.offset.x * Mathf.Abs(transform.lossyScale.x) + transform.position.x,
				quickCapsule.offset.y * transform.lossyScale.y + transform.position.y,
				transform.position.z); //Whim: Forgot to remove Transform Point here. This caused the center to be far off into the distance
			Vector2 quickNormal = Vector2.zero;
			Vector2 quickTotalNormals = Vector2.zero;
			Vector2 quickAvgNormal = Vector2.zero;
			int quickTotal = 0;
			rayHits = new List<RaycastHit2D>();

			//Whim: A parameter to find the shortest distance from a wall
			myWallFollow = Mathf.Infinity;

			if (priv_Grounded)
			{
				myMaxSpeed = myOriginalMaxSpeed;
				foreach (RaycastHit2D repHit in Physics2D.CapsuleCastAll(
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
					quickNormal = -repHit.normal; //Whim: Turns out the normal given by the cast is correct.
					if (quickNormal.y > -Mathf.Cos(slipAngle * Mathf.Deg2Rad) || repHit.distance <= -0.1f)
						continue;

					if (repHit.distance < myWallFollow)
						myWallFollow = repHit.distance;
					rayHits.Add(repHit);
					quickTotalNormals += repHit.normal;
					quickTotal++;
				}
			}

			if (rayHits.Count == 0)
			{
				if (priv_Grounded == true)
				{
					myVelocity = new Vector2(myVelocity.x, 0f);
				}
				priv_Grounded = false;
				myWallFollow = 0f;
			}
			else
			{
				quickAvgNormal = quickTotalNormals / quickTotal;
				priv_Grounded = true;
				stateAerial = PlayerStateAerial.No;
				avgNormAngle = Mathf.Atan2(quickTotalNormals.y / (float)quickTotal, quickTotalNormals.x / (float)quickTotal) - Mathf.PI / 2f;
				myWallFollow = (myWallFollow > 0.015 ? myWallFollow : 0f);
				if (quickAvgNormal.y < Mathf.Cos(steepAngle * Mathf.Deg2Rad) && myVelocity.y > 0)
				{
					myMaxSpeed = myOriginalMaxSpeed / 2f + (myOriginalMaxSpeed * (1f - Mathf.InverseLerp(steepAngle, slipAngle, Mathf.Abs(avgNormAngle) * Mathf.Rad2Deg)) / 2f);
				}
				if (quickAvgNormal.y >= -Mathf.Cos(speedUpAngle * Mathf.Deg2Rad) && myVelocity.y < 0)
				{
					myMaxSpeed = myOriginalMaxSpeed + (myOriginalMaxSpeed * (Mathf.InverseLerp(speedUpAngle, slipAngle, Mathf.Abs(avgNormAngle) * Mathf.Rad2Deg)) / 3f);
				}
				//Debug.Log("" + Mathf.Round(myVelocity.magnitude * 100f) / 100f + ", " + Mathf.Round(myMaxSpeed * 100f) / 100f + ", " + Mathf.Round(Mathf.InverseLerp(40f, 60f, avgNormAngle * Mathf.Rad2Deg) * 100f) / 100f + ", " + Mathf.Round(quickAvgNormal.y * 100f) / 100f);
			}
			Debug.Log(avgNormAngle);

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
			if (priv_Grounded && Physics2D.OverlapCapsule(
				GetCrouchCheckPos,
				crouchCheckSize,
				crouchCheckDirection,
				0f,
				priv_WhatIsCeiling
			))
			{
				crouch = true;
			}

			Vector2 myGravity = (priv_Grounded) ?
				Vector2.zero :
				((myVelocity.y < 0) ?
					new Vector2(0f, playerGravity * fallMultiplier) :
					((jumpHold) ?
						new Vector2(0f, playerGravity * holdJumpFallMultiplier) :
						new Vector2(0f, playerGravity * lowJumpMultiplier)));

			// Set whether or not the character is crouching in the animator
			priv_Anim.SetBool("Crouch", crouch);
			if (crouch)
				SetStateCrouch = PlayerStateCrouch.Crouch;
			else
				SetStateCrouch = PlayerStateCrouch.No;

			// Reduce the speed if crouching by the crouchSpeed multiplier 
			if (stateCrouch == PlayerStateCrouch.Crouch)
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
					myVelocity = new Vector2(
						move * myMaxSpeed * Mathf.Cos(avgNormAngle), 
						move * myMaxSpeed * Mathf.Sin(avgNormAngle));
					if (myVelocity.x != 0)
						stateGrounded = PlayerStateGrounded.Run;
					else
						stateGrounded = PlayerStateGrounded.Idle;
				}
				else
				{
					myVelocity = new Vector2(move * myMaxSpeed, myVelocity.y);
					if (myVelocity.y >= 0)
						stateAerial = PlayerStateAerial.Jump;
					else
						stateAerial = PlayerStateAerial.Fall;
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
				myVelocity = new Vector2(myVelocity.x, priv_JumpSpeed);
			}

			//Whim: How we set the player's move is dependant on the physics check
			priv_Rigidbody2D.velocity = myVelocity;
			transform.position += new Vector3(0f, -myWallFollow, 0f);
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
				foreach (ContactPoint2D repPoint in itsCollision.contacts)
				{
					quickTotalNormal += repPoint.normal;
					quickNormalCount++;
				}

				Vector2 quickAverageNormal = (quickTotalNormal / (float)quickNormalCount).normalized;
				if (quickAverageNormal.y > Mathf.Cos(slipAngle * Mathf.Deg2Rad))
				{
					priv_Grounded = true;
					airborneFrames = 0;
				}
			}
		}

		void OnCollisionEnter2D(Collision2D itsCollision)
		{

		}
	}
}
