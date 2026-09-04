using System.Collections;
using UnityEngine;

using Random = UnityEngine.Random;
using Debug = UnityEngine.Debug;

namespace CE6127.Tanks.AI
{
    /// <summary>
    /// Class <c>PatrollingState</c> represents the state of the tank when it is patrolling.
    /// </summary>
    internal class PatrollingState : BaseState
    {
        private TankSM m_TankSM;        // Reference to the tank state machine.
        private Vector3 m_Destination;  // Destination for the tank to move to.

        /// <summary>
        /// Constructor <c>PatrollingState</c> constructor.
        /// </summary>
        public PatrollingState(TankSM tankStateMachine) : base("Patrolling", tankStateMachine) => m_TankSM = (TankSM)m_StateMachine;

        /// Method <c>Enter</c> on enter.
        public override void Enter()
        {
            base.Enter();
            m_TankSM.NavMeshAgent.isStopped = false;
            // m_TankSM.StartCoroutine(Patrolling());
        }
        

        /// Method <c>Update</c> update logic.
        public override void Update()
        {
            /* base.Update();
            if (m_TankSM.Target != null)
            {
                var dist = Vector3.Distance(m_TankSM.transform.position, m_TankSM.Target.position);
                if (dist <= m_TankSM.StopDistance) // ... Obviously this doesn't make much sense,
                                                   // but it's just for demonstration purposes.
                    m_StateMachine.ChangeState(m_TankSM.m_States.Idle);
            }

            if (Time.time >= m_TankSM.NavMeshUpdateDeadline)
            {
                m_TankSM.NavMeshUpdateDeadline = Time.time + m_TankSM.PatrolNavMeshUpdate;
                m_TankSM.NavMeshAgent.SetDestination(m_Destination);
            }
            */
            
            if (m_TankSM.Target == null)
                return;
            
            // 进入停火/转向距离
            float dist = Vector3.Distance(
                m_TankSM.transform.position,
                m_TankSM.Target.position);
            Vector3 hold = PlatoonUtility.GetHoldPoint(m_TankSM);
            float distHold = Vector3.Distance(m_TankSM.transform.position, hold);

            var agent = m_TankSM.NavMeshAgent;
            bool pathOk = agent.hasPath
                          && agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathComplete
                          && agent.remainingDistance <= 4.5f;
            bool atPost = distHold <= 8f;
            bool inFireRange = dist <= m_TankSM.TargetDistance && dist >= 11f;
            bool canSee = m_TankSM.HasClearShotToTarget();

            // 必须真看得见才停车瞄准。直线近但隔着大楼时继续绕，不要原地转向。
            if (canSee && inFireRange && (atPost || pathOk))
            {
                m_TankSM.NavMeshAgent.stoppingDistance = 2f;
                m_TankSM.NavMeshAgent.updateRotation = false;
                m_StateMachine.ChangeState(m_TankSM.m_States.Idle);
                return;
            }

            if (canSee && inFireRange)
            {
                m_TankSM.NavMeshAgent.updateRotation = false;
                m_TankSM.FaceLeadTarget();
                m_TankSM.TryFireAtTarget();
            }
            else
            {
                m_TankSM.NavMeshAgent.updateRotation = true;
            }

            // 节流：不要每帧 SetDestination
            if (Time.time >= m_TankSM.NavMeshUpdateDeadline)
            {
                m_TankSM.NavMeshUpdateDeadline =
                    Time.time + m_TankSM.TargetNavMeshUpdate;

                if (!canSee)
                    m_TankSM.MoveToClearShot();
                else
                {
                    m_TankSM.NavMeshAgent.stoppingDistance = 2f;
                    m_TankSM.NavMeshAgent.SetDestination(hold);
                }
            }
        }

        /// Method <c>Exit</c> on exiting PatrollingState.
        public override void Exit()
        {
            base.Exit();

            /*
            m_TankSM.StopCoroutine(Patrolling());
            */
        }

        /// Coroutine <c>Patrolling</c> patrolling coroutine.
        IEnumerator Patrolling()
        {
            while (true)
            {
                var destination = Random.insideUnitCircle * Random.Range(m_TankSM.PatrolMaxDist.x, m_TankSM.PatrolMaxDist.y);
                m_Destination = m_TankSM.transform.position + new Vector3(destination.x, 0f, destination.y);

                float waitInSec = Random.Range(m_TankSM.PatrolWaitTime.x, m_TankSM.PatrolWaitTime.y);
                yield return new WaitForSeconds(waitInSec);
            }
        }
    }
}
