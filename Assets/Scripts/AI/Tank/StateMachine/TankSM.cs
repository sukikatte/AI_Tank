/// <remarks>
/// <para>
/// Reflection should be used cautiously due to its performance overhead and the loss of compile-time type safety.
/// It is slower compared to direct access via properties, methods, or fields since it involves runtime type inspection.
/// Moreover, reflection can lead to less maintainable and harder-to-debug code, as it bypasses standard access
/// mechanisms and encapsulation principles.
/// </para>
/// <para>
/// * Performance: Reflection is considerably slower than direct field access, impacting application performance.
/// * Encapsulation: It bypasses access modifiers, potentially breaking encapsulation and leading to unintended consequences.
/// * Maintainability: Code using reflection can be less readable and harder to maintain, especially for developers unfamiliar with it.
/// * Type safety: Reflection bypasses compile-time type checks, increasing the risk of runtime errors.
/// </para>
/// <para>
/// Instead of reflection, it is recommended to use classical getters and setters or public properties to access
/// and manipulate field values. These provide better performance, type safety, and allow for encapsulation.
/// Reflection may be useful in scenarios where dynamic type access is required, such as in frameworks or libraries,
/// but should be avoided in general application logic.
/// </para>
/// </remarks>

using System.Linq;
using UnityEngine;
using UnityEngine.AI;

using Random = UnityEngine.Random;
using Debug = UnityEngine.Debug;

namespace CE6127.Tanks.AI
{
    /// <summary>
    /// Class <c>TankSM</c> state machine for the tank.
    /// </summary>
    internal class TankSM : StateMachine
    {
        protected internal struct States
        {
            // States:
            public IdleState Idle;
            public PatrollingState Patrolling;

            internal States(TankSM sm)
            {
                Idle = new IdleState(sm);
                Patrolling = new PatrollingState(sm);
            }
        }

        public States m_States;
        [HideInInspector] public GameManager GameManager;           // Reference to the GameManager.
        [HideInInspector] public NavMeshAgent NavMeshAgent;         // Reference to the NavMeshAgent.
        [Header("Patrolling")]
        [Tooltip("Minimum and maximum time delay for patrolling wait.")]
        public Vector2 PatrolWaitTime = new(1.5f, 3.5f);            // A minimum and maximum time delay for patrolling wait.
        [Tooltip("Minimum and maximum circumradius of the area to patrol at a given update time.")]
        public Vector2 PatrolMaxDist = new(15f, 30f);               // A minimum and maximum circumradius of the area to patrol.
        [Range(0f, 2f)] public float PatrolNavMeshUpdate = 0.2f;    // A delay between each parolling path update.
        [Header("Targeting")]
        [Tooltip("Minimum and maximum range for the targeting range.")]
        public Vector2 StartToTargetDist = new(28f, 35f);           // A minimum and maximum range for the targeting range.
        [HideInInspector] public float TargetDistance;              // The distance between the tank and the target.
        [Tooltip("Minimum and maximum range for the stopping range.")]
        public Vector2 StopAtTargetDist = new(18f, 22f);            // A minimum and maximum range for the stopping range.
        [HideInInspector] public float StopDistance;                // The distance between the tank and the target.
        [Range(0f, 2f)] public float TargetNavMeshUpdate = 0.2f;    // A delay between each targeting path update.
        [Header("Blending")]
        [Range(0f, 1f)] public float OrientSlerpScalar = 0.2f;      // A scalar for the slerp.
        // [Header("Target")]
        [HideInInspector] public Transform Target;                  // Reference to the target's transform.
        // [Header("NavMesh")]
        [HideInInspector] public float NavMeshUpdateDeadline;       // The time when the next path update is due.
        [Header("Firing")]
        [Tooltip("Minimum and maximum cooldown time delay between each firing in seconds.")]
        public Vector2 FireInterval = new(0.7f, 2.5f);              // A minimum and maximum cooldown time delay between each firing.
        [Tooltip("Force given to the shell if the fire button is not held, and the force given to the shell if the fire button is held for the max charge time in seconds.")]
        
        public Vector2 LaunchForceMinMax = new(6.5f, 30f);          // The force given to the shell if the fire button is not held, and the force given to the shell if the fire button is held for the max charge time.
        [Header("References")]

        [Tooltip("Prefab")] public Rigidbody Shell;                 // Prefab of the shell.
        [Tooltip("Transform")] public Transform FireTransform;      // A child of the tank where the shells are spawned.
        // public Slider AimSlider;                                 // A child of the tank that displays the current launch force.
        [Header("Firing Audio")]
        public AudioSource SFXAudioSource;                          // Reference to the audio source used to play the shooting audio. NB: different to the movement audio source.
        // public AudioClip ShotChargingAudioClip;                  // Audio that plays when each shot is charging up.
        public AudioClip ShotFiringAudioClip;                       // Audio that plays when each shot is fired.
        
        private bool m_Started = false; // Whether the tank has started moving.
        private Rigidbody m_Rigidbody;  // Reference used to the tank's regidbody.
        private TankSound m_TankSound;  // Reference used to play sound effects.

        /// <summary>
        /// Method <c>MoveTurnSound</c> returns the current tank's velocity.
        /// </summary>
        private Vector2 MoveTurnSound() => new Vector2(Mathf.Abs(NavMeshAgent.velocity.x), Mathf.Abs(NavMeshAgent.velocity.z));

        /// <summary>
        /// Method <c>GetInitialState</c> returns the initial state of the state machine.
        /// </summary>
        protected override BaseState GetInitialState() => m_States.Idle;

        /// <summary>
        /// Method <c>SetNavMeshAgent</c> sets the NavMeshAgent's speed and angular speed.
        /// </summary>
        private void SetNavMeshAgent()
        {
            NavMeshAgent.speed = GameManager.Speed;
            NavMeshAgent.angularSpeed = GameManager.AngularSpeed;
        }

        /// <summary>
        /// Method <c>SetStopDistanceToZero</c> sets the NavMeshAgent's stopping distance to zero.
        /// </summary>
        public void SetStopDistanceToZero() => NavMeshAgent.stoppingDistance = 0f;

        /// <summary>
        /// Method <c>SetStopDistanceToTarget</c> sets the NavMeshAgent's stopping distance to the target's distance.
        /// </summary>
        public void SetStopDistanceToTarget() => NavMeshAgent.stoppingDistance = StopDistance;

        /// <summary>
        /// Method <c>Awake</c> is called when the script instance is being loaded.
        /// </summary>
        private void Awake()
        {
            m_States = new States(this);

            GameManager = GameManager.Instance;

            m_Rigidbody = GetComponent<Rigidbody>();
            NavMeshAgent = GetComponent<NavMeshAgent>();
            m_TankSound = GetComponent<TankSound>();

            SetNavMeshAgent();

            // AI 瞄准：加快转向，减少转正前的空窗（不改坦克移速）。
            OrientSlerpScalar = 0.5f;

            TargetDistance = 32f;
            StopDistance = 15f;

            SetStopDistanceToTarget();

            var tankManagers = GameManager.PlayerPlatoon.Tanks.Take(1);
            if (tankManagers.Count() != 0)
                Target = tankManagers.First().Instance.transform;
            else
                Debug.LogError("'Player Platoon' is empty!");
        }

        /// <summary>
        /// Method <c>OnEnable</c> is called when the object becomes enabled and active.
        /// </summary>
        private void OnEnable()
        {
            // When the tank is turned on, make sure it's not kinematic.
            m_Rigidbody.isKinematic = false;
        }

        /// <summary>
        /// Method <c>Start</c> is called on the frame when a script is enabled just before any of the Update methods are called the first time.
        /// </summary>
        private new void Start()
        {
            // base.Start(); // Moved to Update.

            m_TankSound.MoveTurnInputCalc += MoveTurnSound;
        }

        /// <summary>
        /// Method <c>OnDisable</c> is called when the behaviour becomes disabled or inactive.
        /// </summary>
        private void OnDisable()
        {
            // When the tank is turned off, set it to kinematic so it stops moving.
            m_Rigidbody.isKinematic = true;

            m_TankSound.MoveTurnInputCalc -= MoveTurnSound;
        }

        /// <summary>
        /// Method <c>Update</c> is called every frame, if the MonoBehaviour is enabled.
        /// </summary>
        private new void Update()
        {
            if (!m_Started && GameManager.IsRoundPlaying)
            {
                m_Started = true;
                base.Start();
            }
            else if (GameManager.IsRoundPlaying)
            {
                base.Update();
            }
            else
            {
                m_Started = false;
                StopAllCoroutines();
            }
        }

        /// <summary>
        /// Method <c>LaunchProjectile</c> instantiate and launch the shell.
        /// </summary>
        public void LaunchProjectile(float launchForce = 1f)
        {
            launchForce = Mathf.Min(Mathf.Max(LaunchForceMinMax.x, launchForce), LaunchForceMinMax.y);

            // Set the fired flag so only Fire is only called once.
            // m_Fired = true;

            // Create an instance of the shell and store a reference to it's rigidbody.
            Rigidbody shellInstance = Instantiate(Shell, FireTransform.position, FireTransform.rotation) as Rigidbody;

            // Set the shell's velocity to the launch force in the fire position's forward direction.
            shellInstance.velocity = launchForce * FireTransform.forward; ;

            // Change the clip to the firing clip and play it.
            SFXAudioSource.clip = ShotFiringAudioClip;
            SFXAudioSource.Play();
        }
        
        [HideInInspector] public float NextFireTime; // 下次允许开火的时间
        private float m_PeekSide = 1f;               // 绕障时偏好的左右侧，卡住会翻转。
        private float m_LastMoveTime;                // 上次位置有明显变化的时间。
        private Vector3 m_LastMovePos;               // 用于判断是否卡住。
        /// <summary>
        /// AI：根据与玩家的水平距离，在 LaunchForceMinMax 内估算发射力度。
        /// </summary>
        public float ComputeLaunchForce(float horizontalDist)
        {
            const float barrelDeg = 10f;
            float g = Mathf.Abs(Physics.gravity.y); // 9.81
            float sin2 = Mathf.Sin(2f * barrelDeg * Mathf.Deg2Rad);
            if (sin2 < 0.001f)
                return LaunchForceMinMax.x;

            float force = Mathf.Sqrt(horizontalDist * g / sin2);
            return Mathf.Clamp(force, LaunchForceMinMax.x, LaunchForceMinMax.y);
        }
        /// <summary>
        /// AI：从世界坐标一点到玩家是否无遮挡。
        /// </summary>
        public bool HasClearShotFrom(Vector3 from)
        {
            if (Target == null)
                return false;

            Vector3 origin = from + Vector3.up * 1.2f;
            Vector3 aimPoint = Target.position + Vector3.up;
            Vector3 dir = aimPoint - origin;
            float dist = dir.magnitude;
            if (dist < 0.5f)
                return true;

            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                dir.normalized,
                dist,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                Transform t = hit.collider.transform;
                if (t == transform || t.IsChildOf(transform))
                    continue;
                if (t == Target || t.IsChildOf(Target))
                    return true;
                return false;
            }
            return true;
        }

        /// <summary>
        /// AI：炮口到玩家之间是否无遮挡。碰到墙/友军则不应开火，否则会打墙自伤。
        /// </summary>
        public bool HasClearShotToTarget()
        {
            if (FireTransform == null)
                return false;
            return HasClearShotFrom(FireTransform.position + FireTransform.forward * 0.6f);
        }

        /// <summary>
        /// AI：在 NavMesh 上测试一点是否有完整路径，并返回路程长度。
        /// </summary>
        private bool TryReachablePoint(Vector3 desired, out Vector3 sampled, out float pathLength)
        {
            sampled = desired;
            pathLength = float.MaxValue;
            if (!NavMesh.SamplePosition(desired, out NavMeshHit navHit, 14f, NavMesh.AllAreas))
                return false;

            sampled = navHit.position;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(transform.position, sampled, NavMesh.AllAreas, path))
                return false;
            if (path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2)
                return false;

            pathLength = 0f;
            for (int i = 1; i < path.corners.Length; ++i)
                pathLength += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return true;
        }

        /// <summary>
        /// AI：视线被挡住时绕开。优先去「走得到且看得见玩家」的点，避免在大楼前停住干转。
        /// </summary>
        public void MoveToClearShot()
        {
            if (Target == null || NavMeshAgent == null)
                return;

            SetStopDistanceToZero();
            NavMeshAgent.isStopped = false;
            NavMeshAgent.updateRotation = true;

            Vector3 toTarget = Target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f)
                return;

            if (m_LastMoveTime <= 0f)
            {
                m_PeekSide = (GetInstanceID() & 1) == 0 ? 1f : -1f;
                m_LastMovePos = transform.position;
                m_LastMoveTime = Time.time;
            }
            else if ((transform.position - m_LastMovePos).sqrMagnitude > 0.35f)
            {
                m_LastMovePos = transform.position;
                m_LastMoveTime = Time.time;
            }
            else if (Time.time - m_LastMoveTime > 0.7f)
            {
                m_PeekSide = -m_PeekSide;
                m_LastMoveTime = Time.time;
            }

            Vector3 right = Vector3.Cross(Vector3.up, toTarget.normalized);
            Vector3 back = -toTarget.normalized;
            Vector3 hold = PlatoonUtility.GetHoldPoint(this);

            Vector3[] wrapAround =
            {
                Target.position + right * (m_PeekSide * 22f),
                Target.position + right * (-m_PeekSide * 22f),
                Target.position + right * (m_PeekSide * 30f),
                Target.position + right * (-m_PeekSide * 30f),
                transform.position + back * 10f + right * (m_PeekSide * 12f),
                transform.position + back * 10f + right * (-m_PeekSide * 12f),
                hold,
                Target.position + right * (m_PeekSide * 14f),
                Target.position
            };

            // 先要看得见玩家的绕点；没有再退而求能走的远点。
            if (TryPickReachable(wrapAround, out Vector3 best, true))
            {
                NavMeshAgent.SetDestination(best);
                return;
            }
            if (TryPickReachable(wrapAround, out best, false))
            {
                NavMeshAgent.SetDestination(best);
                return;
            }

            NavMeshAgent.SetDestination(Target.position);
        }

        /// <summary>
        /// AI：候选点须可到达；可选要求该点能看见玩家。挡住时忽略身边过近的点，防止原地干转。
        /// </summary>
        private bool TryPickReachable(Vector3[] candidates, out Vector3 best, bool requireLos)
        {
            best = default;
            float bestLen = float.MaxValue;
            bool found = false;
            for (int i = 0; i < candidates.Length; ++i)
            {
                if (!TryReachablePoint(candidates[i], out Vector3 sampled, out float len))
                    continue;
                if ((sampled - transform.position).sqrMagnitude < 25f)
                    continue;
                if (requireLos && !HasClearShotFrom(sampled))
                    continue;
                if (len < bestLen)
                {
                    bestLen = len;
                    best = sampled;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// AI：预判玩家短时间后的位置，用于转向和选 force。
        /// </summary>
        public Vector3 GetLeadAimPoint()
        {
            if (Target == null)
                return transform.position;

            Vector3 pos = Target.position;
            var rb = Target.GetComponent<Rigidbody>();
            Vector3 vel = rb != null ? rb.velocity : Vector3.zero;
            vel.y = 0f;

            Vector3 flat = pos - transform.position;
            flat.y = 0f;
            float horiz = flat.magnitude;
            float force = ComputeLaunchForce(Mathf.Max(horiz, 1f));
            float speedH = Mathf.Max(force * Mathf.Cos(10f * Mathf.Deg2Rad), 4f);
            float flight = horiz / speedH;
            return pos + vel * flight;
        }

        /// <summary>
        /// AI：水平转向预判点。
        /// </summary>
        public void FaceLeadTarget()
        {
            if (Target == null)
                return;
            Vector3 lookPos = GetLeadAimPoint() - transform.position;
            lookPos.y = 0f;
            if (lookPos.sqrMagnitude < 0.01f)
                return;
            var rot = Quaternion.LookRotation(lookPos);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, OrientSlerpScalar);
        }

        /// <summary>
        /// AI：对准预判点且有视线则开火。溅射允许约 15° 偏差；冷却用 0.7s。
        /// </summary>
        public void TryFireAtTarget()
        {
            if (Target == null)
                return;
            if (Time.time < NextFireTime)
                return;
            if (!HasClearShotToTarget())
                return;

            Vector3 toAim = GetLeadAimPoint() - transform.position;
            toAim.y = 0f;
            if (toAim.sqrMagnitude < 0.01f)
                return;

            float angle = Vector3.Angle(transform.forward, toAim);
            if (angle > 15f)
                return;

            float dist = toAim.magnitude;
            float force = ComputeLaunchForce(dist);
            LaunchProjectile(force);

            NextFireTime = Time.time + FireInterval.x;
        }
    }
}
