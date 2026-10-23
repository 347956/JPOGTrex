using System.Diagnostics;
using UnityEngine;

namespace JPOGTrex
{
    //Finds, validates and keeps track of losing the target player
    internal class TrexTargetingHelper
    {
        private readonly JPOGTrexAI trexAI;

        private readonly float attackRange = 8f;
        private readonly float maxSearchTime = 20.0f;
        private readonly float stopChaseHeight = 12f;
        private int visionRangeChase = 50;

        public bool IsMovingTowardsLastKnownPosition { get; private set; }
        public Vector3 LastKnownPosition { get; private set; }
        public float TimeSinceLostPlayer { get; private set; }
        public bool HasSearchTimedOut => TimeSinceLostPlayer >= maxSearchTime;
        public int VisionRangeChase => visionRangeChase;

        public TrexTargetingHelper(JPOGTrexAI trexAI)
        {
            this.trexAI = trexAI;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public void Configure(int visionRangeChase)
        {
            this.visionRangeChase = visionRangeChase;
        }

        public void Tick(float deltaTime)
        {
            if (IsMovingTowardsLastKnownPosition)
            {
                TimeSinceLostPlayer += deltaTime;
            }
        }

        public void ResetLostPlayer()
        {
            IsMovingTowardsLastKnownPosition = false;
            TimeSinceLostPlayer = 0.0f;
        }

        public bool FoundClosestPlayerInRange(float range = 15f, float senseRange = 5f)
        {
            trexAI.TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true, 60f);
            if (trexAI.targetPlayer == null)
            {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                trexAI.TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            if (trexAI.targetPlayer != null && Vector3.Distance(trexAI.transform.position, trexAI.targetPlayer.transform.position) < range && !trexAI.targetPlayer.isInHangarShipRoom)
            {
                LogIfDebugBuild("JPOGTrex: Spotted player in close proximity");
                return true;
            }
            trexAI.targetPlayer = null;
            return false;
        }

        //Returns false if there is a target, but it can no longer be targeted
        public bool IsTargetTargetable()
        {
            if (trexAI.targetPlayer != null && !trexAI.PlayerIsTargetable(trexAI.targetPlayer, false))
            {
                LogIfDebugBuild($"JPOGTrex: Player [{trexAI.targetPlayer.playerClientId}] is no longer targetable");
                return false;
            }
            return true;
        }

        public bool IsInGrabRange()
        {
            return trexAI.targetPlayer != null && Vector3.Distance(trexAI.transform.position, trexAI.targetPlayer.transform.position) <= attackRange;
        }

        public bool IsTargetTooHigh()
        {
            return trexAI.targetPlayer != null && Mathf.Abs(trexAI.transform.position.y - trexAI.targetPlayer.transform.position.y) > stopChaseHeight;
        }

        //The target is too far away and out of sight
        public bool IsTargetLostFromSight()
        {
            if (trexAI.targetPlayer == null)
            {
                return false;
            }
            Vector3 targetPosition = trexAI.targetPlayer.transform.position;
            return Vector3.Distance(trexAI.transform.position, targetPosition) > visionRangeChase && !trexAI.CheckLineOfSightForPosition(targetPosition);
        }

        //Drops the target and walks to the last position the target was seen
        public void StartMovingToLastKnownPosition()
        {
            if (trexAI.targetPlayer == null)
            {
                return;
            }
            LogIfDebugBuild($"JPOGTrex: Lost target player, distance = [{Vector3.Distance(trexAI.transform.position, trexAI.targetPlayer.transform.position)}] allowed distance = [{visionRangeChase}]");
            LastKnownPosition = trexAI.targetPlayer.transform.position;
            trexAI.targetPlayer = null;
            IsMovingTowardsLastKnownPosition = true;
            TimeSinceLostPlayer = 0.0f;
            trexAI.SetDestinationToPosition(LastKnownPosition);
        }

        //Returns true once when the last known position is reached
        public bool ReachedLastKnownPosition()
        {
            if (IsMovingTowardsLastKnownPosition && Vector3.Distance(trexAI.transform.position, LastKnownPosition) < 10f)
            {
                IsMovingTowardsLastKnownPosition = false;
                LogIfDebugBuild("JPOGTrex: Reached last known position");
                return true;
            }
            return false;
        }
    }
}
