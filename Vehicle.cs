﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;




[RequireComponent(typeof(Player))]
[RequireComponent(typeof(Rigidbody))]

public class Vehicle : MonoBehaviour
{

    /* Manages AG-Vehicle Physics*/

    // 2Do:
    // - clean up
    // - change all those "input" publics to Serialize-field-privates.
    // - use private + get{} for variables that are "read only"
    // - move all remaining audio-events to audio-script!


    public bool groundContact = false;
    public bool roadContact = false;


    private float currentMaxSpeed = 100f;
    public float currentGravity = 5;
    public float standardGravity = 5; // <- from GM

    public Vector3 gravityDirection = Vector3.down;
    private float currentBanking = 0;
    // private Vector3 velocity;
    public float currentAcceleration = 0; // for engine-audio
    public float currentSpeed = 0;
    public float overDriveBoost = 0;        // to boostscript...
    public LayerMask groundLayer;

    [Header("Transforms")]
    public GameObject vehicleModel;
    public Transform[] hoverPoints;
    public GameObject collisionParticle;
    // public ParticleSystem dustParticles; // to modify particles if inflight etc.



    private VehicleAudio audioScript;



    [HideInInspector]
    public Player player;   // <- now holds the vehiclestats aswell. does this make sense? i'm not sure yet
                            // this really depends on how i will load the player/vehicle, as in 
                            // how the whole player setup will work.
    private Rigidbody rb;

    public CameraManager cameraScript;

    private Spring bankingSpring = new Spring(); // move to Top



    //---------------------------------------
    void Start()
    {
        rb = GetComponent<Rigidbody>(); // required
        player = GetComponent<Player>(); // required
        audioScript = GetComponent<VehicleAudio>(); 
        // set initial gravity:
        gravityDirection = Vector3.down;
        currentGravity = standardGravity;

        
    }
    //----------------------------------------------------------------
    // Physics 
    //----------------------------------------------------------------
    #region physics
    void FixedUpdate()
    {
        RaycastHit hit;
        Vector3 upForce;
        float strength;
        groundContact = false;
        roadContact = false;
        Vector3 normal = Vector3.zero;



        for (int i = 0; i < hoverPoints.Length; i++)
        {
            if (Physics.Raycast(hoverPoints[i].position, transform.up * -1, out hit, player.vehicle.hoverHeight * 4f, groundLayer))
            {
                strength = (player.vehicle.hoverHeight - hit.distance) / player.vehicle.hoverHeight * 100f;

                if (strength < 0) strength *= player.vehicle.holdForce;
                else strength *= player.vehicle.liftForce * Mathf.Abs(strength / 2);

                upForce = transform.up * strength;

                rb.AddForceAtPosition(upForce, hoverPoints[i].position);
                normal += hit.normal;
                groundContact = true;
                if (hit.transform.tag == "raceTrack") roadContact = true;
                Debug.DrawRay(hoverPoints[i].position, upForce / 10f, Color.red);

            }
        }

                normal.Normalize(); // averaged up-vector

        ApplyDrag(groundContact);
        if (groundContact)        // align to ground!
        {
            AlignRotation(normal);
        }
        else // flying
        {
            ApplyGravity(gravityDirection);
            AlignRotation(-gravityDirection);
        }

        // increase max speed if you are on the racetrack!
        if (roadContact) currentMaxSpeed = player.vehicle.maxSpeed + player.vehicle.maxSpeed * player.vehicle.roadSpeedIncrease;
        else currentMaxSpeed = player.vehicle.maxSpeed;

        // charge booster
        UpdateBoost(); // just move this to the boostscript already...
        UpdateBanking();

        // update variables
        currentSpeed = rb.velocity.magnitude;
    }
    //---------------------------------------
    // align to current up-axis
    private void AlignRotation(Vector3 up)
    {
        Quaternion rotationCorrection = Quaternion.FromToRotation(transform.up, up) * transform.rotation;
        rb.MoveRotation(Quaternion.Lerp(rb.rotation, rotationCorrection, Time.fixedDeltaTime * 10));
    }

    //---------------------------------------
    // move to boost-script

    private void UpdateBoost()
    {
        if (currentSpeed > currentMaxSpeed && currentMaxSpeed > 0)
            GetComponent<VehicleBoost>().ChargeBoost();
        overDriveBoost = GetComponent<VehicleBoost>().energyPool;
        player.vehicle.maxOverDriveBoost = GetComponent<VehicleBoost>().energyPoolMax; // no need to set every time...! -> move to start
    }

    //---------------------------------------
    private void ApplyGravity(Vector3 dir)
    {
        rb.velocity += dir * currentGravity;
        //        rb.AddForce(dir * gravity, ForceMode.Acceleration);
    }
    //---------------------------------------
    private void ApplyDrag(bool isGrounded)
    {
        Vector3 vel = transform.InverseTransformDirection(rb.velocity);
        if (isGrounded)
        {
            vel = Vector3.Scale(vel, (Vector3.one - player.vehicle.drag));
        }
        else // flying
        {
            vel *= (1f - player.vehicle.airDrag);
        }

        vel = transform.TransformDirection(vel);
        rb.velocity = vel;
        rb.angularVelocity *= 1f - player.vehicle.angularDrag;

        // forward alignment!
        // rotate velocity vector towards the ships forward! 
        float side = Vector3.Dot(transform.right, rb.velocity.normalized);
        rb.velocity += transform.forward * Mathf.Abs(side) * player.vehicle.forwardAlignmentSpeed;
        rb.velocity -= transform.right * side * player.vehicle.forwardAlignmentSpeed;

    }
    #endregion
    //----------------------------------------------------------------
    // steering
    //----------------------------------------------------------------
    #region steering
    public void Accelerate(float amount)
    {

        if (!player.isInputEnabled) return; // shouldn't even be called from inputScript, but just to be sure
        if (rb.velocity.magnitude < currentMaxSpeed - player.vehicle.acceleration)
            //            rb.AddForce(transform.forward * acceleration * amount);
            rb.velocity += transform.forward * player.vehicle.acceleration * amount;

        currentAcceleration = amount;
    }
    //---------------------------------------
    public void Deccelerate(float amount)
    { //0->1
        Vector3 vel = transform.InverseTransformDirection(rb.velocity);
        if (vel.z > 0)
            // rb.AddForce(transform.forward * -decceleration * amount);
            rb.velocity -= transform.forward * player.vehicle.decceleration * amount;
    }

    //---------------------------------------
    public void Turn(float amount)
    {
        if (!player.isInputEnabled) return;
        // some lerping might be nice?
        Quaternion rot = rb.rotation;
        rot *= Quaternion.Euler(0, player.vehicle.turnSpeed * amount, 0);
        rb.MoveRotation(rot);
        Bank(amount);
    }
    //---------------------------------------
    public void Pitch(float amount)
    {
        // if (!isInputEnabled) return; // nope, always allow this, so the player can confirm that inputs are working!
        // only pitch when flying! // if i do that, then i maybe shoudln't align to gravity direction? ... hmmmm....
        // if (groundContact) return;
        Quaternion rot = rb.rotation;
        rot *= Quaternion.Euler(-player.vehicle.turnSpeed * amount, 0, 0);
        rb.MoveRotation(rot);
    }
    //---------------------------------------
    public void Strafe(float amount) // strave? -> check your spelling!
    {
        if (!player.isInputEnabled) return;
        // rb.AddForce(transform.right * amount * straveSpeed);
        // rb.MovePosition(transform.position + transform.right * amount * straveSpeed); // !?? warum kippt des!?
        rb.velocity += transform.right * player.vehicle.straveSpeed * amount;
        SpringBank(-amount);
    }
    //---------------------------------------
    public void Boost()
    {
        if (!player.isInputEnabled) return;
        if (GetComponent<VehicleBoost>().ApplyBoost() < 0) return;

        if (GetComponent<CameraManager>())
            GetComponent<CameraManager>().Boost();
        if (cameraScript != null) cameraScript.Boost();
        // play boost audio
        audioScript.Turbo();
    }

    //---------------------------------------
    // player controlled gravity
    public void EnableGravity()
    {
        if (!player.isInputEnabled) return;
        if (!groundContact) return; // gravity already applied
        ApplyGravity(gravityDirection);// actually there's really no need to pass the direction...!
        //        rb.AddForce(gravityDirection * gravity * 1f, ForceMode.Acceleration);
    }
    //---------------------------------------
    // remove the spring, use animation blend tree for this...! much clean, very simple, WOW!

    private void SpringBank(float amount)
    {
        currentBanking += bankingSpring.Update(amount);
    }
    private void Bank(float amount)
    {
        // float bankAmount = 20;
        currentBanking += amount;
    }
    private void UpdateBanking()
    {
        float bankAmount = currentBanking * 20f;
        //  bankAmount = Mathf.Clamp(bankAmount, -20, 20);
        //vehicleModel.transform.eulerAngles = transform.eulerAngles + Vector3.forward * bankAmount;
        // currentBanking = 0;

        bankAmount = -currentBanking * 0.5f + 0.5f;

        vehicleModel.GetComponent<Animator>().SetFloat("Bank", bankAmount);
        currentBanking = 0;
    }

    #endregion

    //---------------------------------------
    public float GetSpeed() { return currentSpeed; }
    //---------------------------------------
    public void Respawn()
    {
        // todo:
        // disable input, trigger animation, on animation-end: re-enable inputs

        Transform spawnPoint = GetComponent<CurrentPositionManager>().GetRespawnPoint();
        rb.velocity *= 0.0f;
        rb.angularVelocity *= 0.0f;
        rb.rotation = spawnPoint.rotation;
        rb.position = spawnPoint.position + Vector3.up * 5; // remove z-offset! (and place waypoints properly!)

        // move to new player.reset() function?
        player.health = player.vehicle.maxHealth;

        // reset gravity?

        // reset Boost
        foreach (Boost b in GetComponentsInChildren<Boost>())
        {
            b.RemoveBoost();
        }

        //play respawnaudio - will move to new respawn Routine / maybe even called from a respawn animation!

        audioScript.Respawn();

        /*
                FMOD.Studio.EventInstance rspwn = FMODUnity.RuntimeManager.CreateInstance(respawnSoundEvent);
                rspwn.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(gameObject, rb));
                rspwn.start();
                rspwn.release();
                */

        Debug.Log(player.name + " respawned");

    }
    //---------------------------------------

    void OnCollisionEnter(Collision col)
    {
        // audio
        audioScript.Collision( col.relativeVelocity.magnitude);

        //particles
        for (int i = 0; i < col.contacts.Length; i++)
        {
            GameObject p = Instantiate(collisionParticle, col.contacts[i].point, Quaternion.identity) as GameObject;
            p.transform.parent = this.transform;

        }

        // player health
        if (player.isRacing)
        {
            if (col.impulse.magnitude < 1000)
                player.health -= col.impulse.magnitude * 0.1f;
            else player.health -= 100; // maximum damage
        }
        if (player.health <= 0)
        {
            Respawn();
        }
        if (col.transform.tag == "deathZone") // map/sector boundaries
        {
            Respawn();
        }
    }
}
