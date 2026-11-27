using System.Diagnostics;
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
        //Part of the agent's acceleration used to plan braking towards the target. Higher brakes later, 1 would brake at the last possible moment
        private readonly float brakingStrength = 0.8f;
        private readonly float minChaseSpeed = 3f;
        //How far ahead of the target the T-rex aims when it is far away, this also makes up for the network delay
        private readonly float maxLeadTime = 0.4f;
        private readonly float leadFalloffDistance = 10f;
        private readonly float velocitySampleInterval = 0.2f;
        private readonly float destinationUpdateDistance = 0.5f;
        //Requesting a new path every frame makes the agent hesitate, so it's limited to this interval
        private readonly float pathUpdateInterval = 0.1f;
        private readonly float navMeshSnapRadius = 3f;
        private float maxChaseSpeed = 12f;

        private PlayerControllerB? trackedPlayer;
        private Vector3 lastSamplePosition;
        private float lastSampleTime;
        private Vector3 lastRequestedDestination;
        private float lastPathRequestTime = -100f;
        private float lastDebugLogTime;

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

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public void Reset()
        {
            trackedPlayer = null;
            TargetVelocity = Vector3.zero;
            lastPathRequestTime = -100f;
        }

        //Owner only, called every frame while chasing
        public void Tick()
        {
            PlayerControllerB? target = trexAI.targetPlayer;
            //A dead target is in the mouth, the kill slows the T-rex down itself
            if (target == null || target.isPlayerDead)
            {
                return;
            }
            SampleTargetVelocity(target);

            float gap = GetMouthGap(target);

            trexAI.agent.speed = GetChaseSpeed(gap);
            MoveTowards(GetLeadPosition(target, gap));

            //Compare the speed the T-rex should have with the speed it actually moves at
            if (Time.time - lastDebugLogTime >= 1f)
            {
                lastDebugLogTime = Time.time;
                LogIfDebugBuild($"JPOGTrex: Chase || speed = [{trexAI.agent.speed:0.0}] actual = [{trexAI.agent.velocity.magnitude:0.0}] targetSpeed = [{TargetVelocity.magnitude:0.0}] gap = [{gap:0.0}] pathPending = [{trexAI.agent.pathPending}] pathStatus = [{trexAI.agent.pathStatus}]");
            }
        }

        //Distance between the mouth and the target, measured from the mouth instead of the T-rex's center so it keeps closing in until it can actually bite
        private float GetMouthGap(PlayerControllerB target)
        {
            //The mouth "touches" the target once it's inside the bite zone
            trexAI.Grab.GetMouthSphere(out Vector3 mouthCenter, out float mouthReach);
            Vector3 mouthToTarget = target.transform.position - mouthCenter;
            mouthToTarget.y = 0f;
            //Once the target is behind the mouth (e.g. below the T-rex's head) it counts as reached, so the T-rex doesn't speed up and run past it
            Vector3 forward = trexAI.transform.forward;
            forward.y = 0f;
            if (Vector3.Dot(forward, mouthToTarget) < 0f)
            {
                return 0f;
            }
            return Mathf.Max(0f, mouthToTarget.magnitude - mouthReach);
        }

        private float GetChaseSpeed(float gap)
        {
            float targetSpeed = TargetVelocity.magnitude;
            float speedCap = targetSpeed >= sprintSpeedThreshold ? Mathf.Min(maxChaseSpeed, targetSpeed * sprintChaseRatio) : maxChaseSpeed;
            //Braking curve: the fastest it can close in while still being able to brake in time (v = sqrt(2 * deceleration * distance)),
            //so it keeps full speed until close to the target and doesn't run past it
            float closingSpeed = Mathf.Sqrt(2f * trexAI.agent.acceleration * brakingStrength * gap);
            float speed = targetSpeed + closingSpeed;
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
            //Also set the destination of EnemyAI, so the base DoAIInterval keeps moving towards the same point
            trexAI.destination = position;
            trexAI.moveTowardsDestination = true;
            bool hasMovedEnough = Vector3.Distance(position, lastRequestedDestination) >= destinationUpdateDistance;
            if (trexAI.agent.hasPath && (!hasMovedEnough || Time.time - lastPathRequestTime < pathUpdateInterval))
            {
                return;
            }
            lastRequestedDestination = position;
            lastPathRequestTime = Time.time;
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
