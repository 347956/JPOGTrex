using System.Diagnostics;
using GameNetcodeStuff;
using UnityEngine;

namespace JPOGTrex
{
    //Keeps track of how suspicious the T-rex is and checks if the local player is seen moving
    internal class TrexSuspicionHelper
    {
        private readonly JPOGTrexAI trexAI;

        public float SuspicionLevel { get; private set; }
        public float MaxSuspicionLevel { get; private set; }
        public PlayerControllerB? MovingPlayer { get; private set; }

        private float increaseRate;
        private float decreaseRate;
        private float decreaseInterval;
        private int visionRangeLength;
        private int visionRangeWidth;
        private float lastSuspicionDecreaseTime;
        private float localPlayerTurnDistance;
        private float localSightCheckTimer;

        public TrexSuspicionHelper(JPOGTrexAI trexAI)
        {
            this.trexAI = trexAI;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public void Configure(float maxSuspicionLevel, float increaseRate, float decreaseRate, float decreaseInterval, int visionRangeLength, int visionRangeWidth)
        {
            MaxSuspicionLevel = maxSuspicionLevel;
            this.increaseRate = increaseRate;
            this.decreaseRate = decreaseRate;
            this.decreaseInterval = decreaseInterval;
            this.visionRangeLength = visionRangeLength;
            this.visionRangeWidth = visionRangeWidth;
        }

        public void SetPercent(float percent)
        {
            SuspicionLevel = MaxSuspicionLevel / 100f * percent;
        }

        public void SetMax()
        {
            SuspicionLevel = MaxSuspicionLevel;
        }

        //Called on every client when a player reported being seen moving
        public void OnPlayerSeenMoving(int playerId)
        {
            MovingPlayer = StartOfRound.Instance.allPlayerScripts[playerId];
            SuspicionLevel = Mathf.Clamp(SuspicionLevel + increaseRate, 0, MaxSuspicionLevel);
            // Reset decrease time to prevent immediate decrease
            lastSuspicionDecreaseTime = Time.time;
            LogIfDebugBuild($"JPOGTrex: Suspicion level increased. New value = [{SuspicionLevel}]");
        }

        public void DecreaseSuspicion()
        {
            if (Time.time - lastSuspicionDecreaseTime >= decreaseInterval)
            {
                SuspicionLevel = Mathf.Clamp(SuspicionLevel - decreaseRate, 0, MaxSuspicionLevel);
                lastSuspicionDecreaseTime = Time.time;
                LogIfDebugBuild($"JPOGTrex: Suspicion level decreased. New value = [{SuspicionLevel}]");
            }
        }

        //Returns the last moving player once suspicion is maxed out
        public bool TryTakeMovingPlayer(out PlayerControllerB player)
        {
            player = MovingPlayer!;
            if (MovingPlayer == null || SuspicionLevel < MaxSuspicionLevel)
            {
                return false;
            }
            MovingPlayer = null;
            return true;
        }

        //Runs on every client, throttled to the AI interval
        public void TickLocalSightCheck(float deltaTime)
        {
            localSightCheckTimer -= deltaTime;
            if (localSightCheckTimer <= 0f)
            {
                localSightCheckTimer = trexAI.AIIntervalTime;
                CheckLocalPlayerMovingInSight();
            }
        }

        //Only checks the local player since the movement data is only accurate for that player
        private void CheckLocalPlayerMovingInSight()
        {
            PlayerControllerB localPlayer = GameNetworkManager.Instance.localPlayerController;
            if (localPlayer == null || localPlayer.isPlayerDead || localPlayer.isInHangarShipRoom)
            {
                return;
            }
            PlayerControllerB[] allPlayersInLineOfSight = trexAI.GetAllPlayersInLineOfSight(visionRangeWidth, visionRangeLength, trexAI.eye, 3f, StartOfRound.Instance.collidersRoomDefaultAndFoliage);
            if (allPlayersInLineOfSight == null)
            {
                return;
            }
            foreach (var playerControllerB in allPlayersInLineOfSight)
            {
                if (playerControllerB == localPlayer)
                {
                    if (CheckIfPlayerIsMoving(localPlayer))
                    {
                        LogIfDebugBuild($"JPGOTrex: Saw player [{localPlayer.playerClientId}] moving");
                        trexAI.IncreaseSuspicionServerRpc((int)localPlayer.playerClientId);
                    }
                    return;
                }
            }
        }

        private bool CheckIfPlayerIsMoving(PlayerControllerB playerToCheck)
        {
            localPlayerTurnDistance += StartOfRound.Instance.playerLookMagnitudeThisFrame;
            if (localPlayerTurnDistance > 0.1f && Vector3.Distance(playerToCheck.transform.position, trexAI.transform.position) < 10f)
            {
                localPlayerTurnDistance = 0f;
                return true;
            }
            if (playerToCheck.performingEmote)
            {
                return true;
            }
            if (Time.realtimeSinceStartup - StartOfRound.Instance.timeAtMakingLastPersonalMovement < 0.25f)
            {
                return true;
            }
            return playerToCheck.timeSincePlayerMoving < 0.05f;
        }
    }
}
