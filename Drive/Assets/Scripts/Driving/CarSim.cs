using System;
using System.Collections;
using System.Linq;
using UnityEngine;
public enum CarSoundType
{
    SQUEAK,
    RATTLE,
    SCRAPE,
    SHIFT,
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
    const float MAX_STEERING_WHEEL_TURN_ANGLE = 30.0f;
    const float SHIFT_BLOCK_TIMER = 2.5f;

    [SerializeField] WheelPair[] wheelPairs = new WheelPair[4];

    [SerializeField] Rigidbody carRididbody;

    //We store lots of these so we can choose a random misc car sound to play when needed
    [Serializable]
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

    AudioSource PlayCarSoundOfType(CarSoundType TypeToPlay)
    {
        CarSound[] Found = miscCarSounds.Where(x => x.soundType == TypeToPlay).ToArray();
        if (Found == null)
        {
            return null;
        }
        int Rand = UnityEngine.Random.Range(0, Found.Length - 1);
        AudioSource ToReturn = null;
        if (Found != null)
        {
            AudioManager.Instance.PlayAudioClip(Found[Rand].sound, out ToReturn);
        }

        return ToReturn;
    }

    void SwitchGear(GearChangeDirection Direction, bool ForceReverse = false)
    {
        if (changingGear)
        {
            return;
        }

        if (!ForceReverse)
        {
            switch (Direction)
            {
                case GearChangeDirection.UP:
                    {
                        if (currentGear + 1 > maxGear)
                        {
                            return;
                        }
                        break;
                    }

                case GearChangeDirection.DOWN:
                    {
                        if (currentGear - 1 <= 0)
                        {
                            return;
                        }
                        break;
                    }
            }
        }
       
        StartCoroutine(WaitForShift(PlayCarSoundOfType(CarSoundType.SHIFT), Direction, ForceReverse));
    }

    //DON'T CALL THIS DIRECTLY!! Call SwitchGearInstead
    void ActualShift(GearChangeDirection Direction, bool ForceReverse = false)
    {
        if (ForceReverse)
        {
            currentGear = Gear.REVERSE;
            return;
        }
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
                        if (currentGear != Gear.NEUTRAL)
                        {
                            currentRpm = maxRpm - UnityEngine.Random.Range(0.0f, maxRandomRpmShift);
                        }
                        return;
                    }
                    break;
                }
        }
    }

    IEnumerator WaitForShift(AudioSource ToListen, GearChangeDirection Direction, bool ForceReverse = false)
    {
        if (!ToListen)
        {
            yield break;
        }
        changingGear = true;
        yield return new WaitUntil(()=> !ToListen.isPlaying);
        ActualShift(Direction, ForceReverse);
        yield return new WaitForSeconds(SHIFT_BLOCK_TIMER);
        changingGear = false;
    }

    bool changingGear = false;

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
        if (loopedSourceRef)
        {
            loopedSourceRef.pitch = currentRpm / RPM_TO_PITCH_FACTOR;
        }

        if (changingGear)
        {
            return;
        }

        if (currentRpm + UnityEngine.Random.Range(0.0f, maxRandomRpmShift) >= maxRpm)
        {
            SwitchGear(GearChangeDirection.UP);
        }
        else if (currentRpm - UnityEngine.Random.Range(0.0f, maxRandomRpmShift) <= idleRpm)
        {
            SwitchGear(GearChangeDirection.DOWN);
        }
    }

    int countAngle = 0;
    void GetInput()
    {
        //TODO move to input mgr
        if (Input.GetKey(KeyCode.W))
        {
            if (!changingGear)
            {
                if (currentGear == Gear.NEUTRAL || currentGear == Gear.REVERSE)
                {
                    SwitchGear(GearChangeDirection.UP);
                    return;
                }
            }
            if (currentRpm < maxRpm)
            {
                currentRpm += rpmAddition/* * Time.deltaTime*/;
            }
            UpdateWheelColliders(wheelDriveMode, maxAccelerationForce * Input.GetAxis("Vertical"));
        }
        if (Input.GetKey(KeyCode.S))
        {
            if (currentGear == Gear.NEUTRAL && CarStopped())
            {
                SwitchGear(GearChangeDirection.DOWN, ForceReverse: true);
                return;
            }
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
            bool CanTurn = false;
            if (TurnAmount > 0)
            {
                if (countAngle < MAX_STEERING_WHEEL_TURN_ANGLE)
                {
                    countAngle++;   //Track turn manually as dealing with rots is a pain in the proverbial backside on firebase
                    CanTurn = true;
                }
            }
            else if (TurnAmount < 0)
            {
                if (countAngle > -MAX_STEERING_WHEEL_TURN_ANGLE)
                {
                    countAngle--;
                    CanTurn = true;
                }
            }
            if (CanTurn)
            {
                steeringWheel.transform.Rotate(new(0.0f, 1.0f, 0.0f), TurnAmount);
            }
        }
        if (currentRpm > idleRpm)
        {
            currentRpm -= rpmDeduction;
        }
    }

    bool CarStopped()
    {
        return Mathf.Approximately(Mathf.Round(carRididbody.velocity.x), 0.0f) && Mathf.Approximately(Mathf.Round(carRididbody.velocity.y), 0.0f);
    }
}
