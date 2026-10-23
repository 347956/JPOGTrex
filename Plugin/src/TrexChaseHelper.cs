using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.AI;

namespace JPOGTrex
{
    //Moves the T-rex during a chase. It aims a bit ahead of the target, and its speed depends on the distance and on how fast the target moves,
    //so it doesn't brake too early, doesn't overshoot up close, and sprinting players with stamina can still get away.
    internal class TrexChaseHelper
    {
        private readonly JPOGTrexAI trexAI;

        //Players walk at 4.6 and sprint at up to ~10.35 (4.6 * 2.25), anything faster than this counts as sprinting
        private readonly float sprintSpeedThreshold = 6f;
        //The T-rex's speed compared to a sprinting player, below 1 lets players with stamina get away
        private readonly float sprintChaseRatio = 0.9f;
        //How much faster than the target the T-rex moves for every meter it is outside of bite range
        private readonly float closingSpeedPerMeter = 0.5f;
        private readonly float minChaseSpeed = 3f;
        //How far ahead of the target the T-rex aims when it is far away, this also makes up for the network delay
        private readonly float maxLeadTime = 0.4f;
        private readonly float leadFalloffDistance = 10f;
        private readonly float velocitySampleInterval = 0.2f;
        private readonly float destinationUpdateDistance = 0.5f;
        private readonly float navMeshSnapRadius = 3f;
        private float maxChaseSpeed = 12f;

        private PlayerControllerB? trackedPlayer;
        private Vector3 lastSamplePosition;
        private float lastSampleTime;

        //Horizontal velocity of the target, measured from its position so it works for every player
        public Vector3 TargetVelocity { get; private set; }

        public TrexChaseHelper(JPOGTrexAI trexAI)
        {
            this.trexAI = trexAI;
        }

        public void Configure(float maxChaseSpeed)
        {
            this.maxChaseSpeed = maxChaseSpeed;
        }

        public void Reset()
        {
            trackedPlayer = null;
            TargetVelocity = Vector3.zero;
        }

        //Owner only, called every frame while chasing
        public void Tick()
        {
            PlayerControllerB? target = trexAI.targetPlayer;
            if (target == null)
            {
                return;
            }
            SampleTargetVelocity(target);

            Vector3 toTarget = target.transform.position - trexAI.transform.position;
            toTarget.y = 0f;
            float gap = Mathf.Max(0f, toTarget.magnitude - trexAI.Targeting.AttackRange);

            trexAI.agent.speed = GetChaseSpeed(gap);
            MoveTowards(GetLeadPosition(target, gap));
        }

        private float GetChaseSpeed(float gap)
        {
            float targetSpeed = TargetVelocity.magnitude;
            float speedCap = targetSpeed >= sprintSpeedThreshold ? Mathf.Min(maxChaseSpeed, targetSpeed * sprintChaseRatio) : maxChaseSpeed;
            //Far away the T-rex runs at the cap, close by it only moves a bit faster than the target so it doesn't run past it
            float speed = targetSpeed + gap * closingSpeedPerMeter;
            return Mathf.Clamp(speed, Mathf.Min(minChaseSpeed, speedCap), speedCap);
        }

        private Vector3 GetLeadPosition(PlayerControllerB target, float gap)
        {
            //No lead close by, otherwise the T-rex would aim past the target
            float leadTime = maxLeadTime * Mathf.Clamp01(gap / leadFalloffDistance);
            return target.transform.position + TargetVelocity * leadTime;
        }

        private void MoveTowards(Vector3 position)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit navMeshHit, navMeshSnapRadius, trexAI.agent.areaMask))
            {
                position = navMeshHit.position;
            }
            if (trexAI.agent.hasPath && Vector3.Distance(position, trexAI.destination) < destinationUpdateDistance)
            {
                return;
            }
            //Also set the destination of EnemyAI, so the base DoAIInterval keeps moving towards the same point
            trexAI.destination = position;
            trexAI.moveTowardsDestination = true;
            trexAI.agent.SetDestination(position);
        }

        private void SampleTargetVelocity(PlayerControllerB target)
        {
            Vector3 position = target.transform.position;
            if (target != trackedPlayer)
            {
                trackedPlayer = target;
                lastSamplePosition = position;
                lastSampleTime = Time.time;
                TargetVelocity = Vector3.zero;
                return;
            }
            float elapsed = Time.time - lastSampleTime;
            if (elapsed < velocitySampleInterval)
            {
                return;
            }
            Vector3 delta = position - lastSamplePosition;
            delta.y = 0f;
            //Smoothed, because positions of other players only arrive a few times per second
            TargetVelocity = Vector3.Lerp(TargetVelocity, delta / elapsed, 0.5f);
            lastSamplePosition = position;
            lastSampleTime = Time.time;
        }
    }
}
