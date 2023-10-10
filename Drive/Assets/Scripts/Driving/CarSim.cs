using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
public enum CarSoundType
{
    SQUEAK,
    RATTLE,
    SCRAPE,
}

enum GearChangeDirection
{
    UP,
    DOWN
}

enum CarState
{
    ON,
    OFF
}

enum Gear
{
    REVERSE,
    NEUTRAL,
    FIRST,
    SECOND,
    THIRD,
    FOURTH,
    FITH,
    SIXTH
}

public enum WheelLocation
{
    FRONT_LEFT,
    FRONT_RIGHT,
    REAR_LEFT,
    REAR_RIGHT,
}

enum WheelDriveMode
{
    RWD,
    FWD,
    AWD
}

public class CarSim : MonoBehaviour
{
    static CarSim instance;
    public static CarSim Instance
    {
        get { return instance; }
    }

    //Engine runtime values
    float currentRpm;
    Gear currentGear;

    //Engine base values
    [Header("Engine RPM adjustments")]
    [SerializeField] float idleRpm = 1000.0f;
    [SerializeField] float rpmAddition = 500.0f;
    [SerializeField] float rpmDeduction = 100.0f;

    //Engine max values
    [Header("Engine maximum values")]
    [SerializeField] float maxRpm = 5000.0f;
    [SerializeField] Gear maxGear;
    [SerializeField] int maxRandomRpmShift; //Range where we may switch up before hitting maxRpm

    //Movement/physics values
    [Header("Movement values")]
    [SerializeField] float maxAccelerationForce = 500.0f;
    [SerializeField] float maxBrakeForce = 300.0f;
    [SerializeField] float maxTurnAngle = 15.0f;

    [SerializeField] AudioClip engineSound;
    [SerializeField] CarSound[] miscCarSounds = new CarSound[2];

    CarState carState;
    [Header("Wheels that are driven")]
    [SerializeField] WheelDriveMode wheelDriveMode; //Front, rear or all wheel drive
    AudioSource loopedSourceRef;

    [SerializeField] GameObject steeringWheel;

    const float RPM_TO_PITCH_FACTOR = 2000.0f;

    [SerializeField] WheelPair[] wheelPairs = new WheelPair[4];

    //We store lots of these so we can choose a random misc car sound to play when needed
    class CarSound
    {
        public AudioClip sound;
        public CarSoundType soundType;
        public CarSound(AudioClip Sound, CarSoundType SoundType)
        {
            sound = Sound;
            soundType = SoundType;
        }
    }

    [Serializable]
    struct WheelPair
    {
        public WheelCollider wheelCollider;
        public GameObject wheelMesh;
        public WheelLocation wheelLocation;
    }

    void Start()
    {
        if (instance != null)
        {
            Destroy(this);
        }
        else
        {
            instance = this;
        }

        StartEngine();
    }

    void PlayCarSoundOfType(CarSoundType TypeToPlay)
    {
        CarSound Found = miscCarSounds.Where(x => x.soundType == TypeToPlay).FirstOrDefault();
        if (Found != null)
        {
            AudioManager.Instance.PlayAudioClip(Found.sound);
        }
    }

    void SwitchGear(GearChangeDirection Direction)
    {
        switch (Direction)
        {
            case GearChangeDirection.UP:
                {
                    if (currentGear + 1 < maxGear)
                    {
                        currentGear++;
                        currentRpm = idleRpm + UnityEngine.Random.Range(0.0f, maxRandomRpmShift);
                        return;
                    }
                    break;
                }

            case GearChangeDirection.DOWN:
                {
                    if (currentGear - 1 > 0)
                    {
                        currentGear--;
                        currentRpm = maxRpm - UnityEngine.Random.Range(0.0f, maxRandomRpmShift);
                        return;
                    }
                    break;
                }
        }
    }

    void FixedUpdate()
    {
        if (carState != CarState.ON)
        {
            return;
        }

        TickEngine();
        GetInput();
        UpdateWheelMeshes();
    }

    void UpdateWheelMeshes()
    {
        if (wheelPairs.Length == 0)
        {
            return;
        }

        for (int w = 0; w < wheelPairs.Length; w++)
        {
            Quaternion ColRotation;
            wheelPairs[w].wheelCollider.GetWorldPose(out _, out ColRotation);
            wheelPairs[w].wheelMesh.transform.rotation = ColRotation;
        }
    }

    void UpdateWheelColliders(WheelDriveMode DriveMode, float AccelerationForce = 0.0f, float BrakeForce = 0.0f, float TurnForce = 0.0f)
    {
        switch (DriveMode)
        {
            case WheelDriveMode.RWD:
                WheelCollider Rl = wheelPairs.Where(x => x.wheelLocation == WheelLocation.REAR_LEFT).FirstOrDefault().wheelCollider;
                WheelCollider Rr = wheelPairs.Where(x => x.wheelLocation == WheelLocation.REAR_RIGHT).FirstOrDefault().wheelCollider;
                if (!Rl || !Rr)
                {
                    Debug.Log("Rear left and/or right pairs are null!");
                    return;
                }
                Rl.motorTorque = AccelerationForce;
                Rr.motorTorque = AccelerationForce;
                Rl.brakeTorque = BrakeForce;
                Rr.brakeTorque = BrakeForce;
                Rr.steerAngle = TurnForce;
                Rl.steerAngle = TurnForce;
                break;
            case WheelDriveMode.FWD:
                WheelCollider Fl = wheelPairs.Where(x => x.wheelLocation == WheelLocation.FRONT_LEFT).FirstOrDefault().wheelCollider;
                WheelCollider Fr = wheelPairs.Where(x => x.wheelLocation == WheelLocation.FRONT_RIGHT).FirstOrDefault().wheelCollider;
                if (!Fl || !Fr)
                {
                    Debug.Log("Front left and/or right pairs are null!");
                    return;
                }
                Fl.motorTorque = AccelerationForce;
                Fr.motorTorque = AccelerationForce;
                Fl.brakeTorque = BrakeForce;
                Fr.brakeTorque = BrakeForce;
                Fr.steerAngle = TurnForce;
                Fl.steerAngle = TurnForce;
                break;
            case WheelDriveMode.AWD:
                UpdateWheelColliders(WheelDriveMode.FWD, AccelerationForce, BrakeForce);
                UpdateWheelColliders(WheelDriveMode.RWD, AccelerationForce, BrakeForce);
                break;
        }
    }

    void StartEngine()
    {
        carState = CarState.ON;
        AudioManager.Instance.PlayLoopedAudioClip(engineSound, out loopedSourceRef);
        currentGear = Gear.NEUTRAL;
        currentRpm = idleRpm;
    }

    void StopEngine()
    {
        carState = CarState.OFF;
        AudioManager.Instance.PlayLoopedAudioClip(engineSound, EndLoop: true);
        loopedSourceRef = null;
        currentRpm = 0.0f;
        currentGear = Gear.NEUTRAL;
    }

    void TickEngine()
    {
        if (currentRpm + UnityEngine.Random.Range(0.0f, maxRandomRpmShift) >= maxRpm)
        {
            SwitchGear(GearChangeDirection.UP);
        }
        else if (currentRpm - UnityEngine.Random.Range(0.0f, maxRandomRpmShift) <= idleRpm)
        {
            SwitchGear(GearChangeDirection.DOWN);
        }

        if (loopedSourceRef)
        {
            loopedSourceRef.pitch = currentRpm / RPM_TO_PITCH_FACTOR;
        }
    }

    void GetInput()
    {
        //TODO move to input mgr
        if (Input.GetKey(KeyCode.W))
        {
            //if (currentGear == Gear.NEUTRAL)
            //{
            //    SwitchGear(GearChangeDirection.UP);
            //}
            if (currentRpm < maxRpm)
            {
                currentRpm += rpmAddition/* * Time.deltaTime*/;
            }
            UpdateWheelColliders(wheelDriveMode, maxAccelerationForce * Input.GetAxis("Vertical"));
        }
        if (Input.GetKey(KeyCode.S))
        {
            //if (currentGear == Gear.NEUTRAL /*&& speed ==0 goto reverse*/)
            //{
            //    SwitchGear(GearChangeDirection.DOWN);
            //    return;
            //}
            if (currentRpm > idleRpm)
            {
                currentRpm -= rpmAddition /** Time.deltaTime*/;
            }
            UpdateWheelColliders(wheelDriveMode, maxBrakeForce * Input.GetAxis("Vertical"));
        }

        //Turning
        float TurnAmount = maxTurnAngle * Input.GetAxis("Horizontal");
        UpdateWheelColliders(WheelDriveMode.FWD, TurnForce: TurnAmount);
        if (steeringWheel)
        {
            steeringWheel.transform.Rotate(new(0.0f, 1.0f, 0.0f), TurnAmount);
        }
        if (currentRpm > idleRpm)
        {
            currentRpm -= rpmDeduction;
        }
    }
}
