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
        private float lastSuspicionDecreaseTime;
        private float localPlayerTurnDistance;
        private float localSightCheckTimer;
        private float lastSightLogTime;
        //Same as vanilla: players this close are seen even outside the view cone, and fog limits the view
        private readonly float proximitySightRange = 3f;
        private readonly float foggyVisionRange = 30f;

        public TrexSuspicionHelper(JPOGTrexAI trexAI)
        {
            this.trexAI = trexAI;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public void Configure(float maxSuspicionLevel, float increaseRate, float decreaseRate, float decreaseInterval)
        {
            MaxSuspicionLevel = maxSuspicionLevel;
            this.increaseRate = increaseRate;
            this.decreaseRate = decreaseRate;
            this.decreaseInterval = decreaseInterval;
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
            bool inSight = CanSeePlayer(localPlayer, out string notSeenReason);
            bool moving = inSight && CheckIfPlayerIsMoving(localPlayer);
            LogSightCheck(localPlayer, inSight, moving, notSeenReason);
            if (moving)
            {
                LogIfDebugBuild($"JPGOTrex: Saw player [{localPlayer.playerClientId}] moving");
                trexAI.IncreaseSuspicionServerRpc((int)localPlayer.playerClientId);
            }
        }

        //Own version of EnemyAI.GetAllPlayersInLineOfSight for one player (same rules), so it's known exactly why a player isn't seen.
        //Foliage blocks the view, so players can hide in bushes
        private bool CanSeePlayer(PlayerControllerB player, out string notSeenReason)
        {
            if (!trexAI.PlayerIsTargetable(player))
            {
                notSeenReason = "not targetable";
                return false;
            }
            Vector3 eyePosition = trexAI.eye.position;
            Vector3 cameraPosition = player.gameplayCamera.transform.position;
            float range = trexAI.VisionRangeLength;
            if (trexAI.isOutside && !trexAI.enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
            {
                range = Mathf.Min(range, foggyVisionRange);
            }
            float distance = Vector3.Distance(eyePosition, cameraPosition);
            if (distance >= range)
            {
                notSeenReason = $"too far ({distance:0}/{range:0} m)";
                return false;
            }
            float angle = Vector3.Angle(trexAI.eye.forward, cameraPosition - eyePosition);
            bool closeBy = Vector3.Distance(trexAI.transform.position, player.transform.position) < proximitySightRange;
            if (angle >= trexAI.VisionRangeWidth && !closeBy)
            {
                notSeenReason = $"outside its view ({angle:0}/{trexAI.VisionRangeWidth} degrees)";
                return false;
            }
            int mask = StartOfRound.Instance.collidersRoomDefaultAndFoliage;
            if (Physics.Linecast(eyePosition, cameraPosition, out RaycastHit hit, mask, QueryTriggerInteraction.Ignore)
                || Physics.Linecast(cameraPosition, eyePosition, out hit, mask, QueryTriggerInteraction.Ignore))
            {
                notSeenReason = $"view blocked by {hit.collider.name} (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}, part of {hit.collider.transform.root.name})";
                return false;
            }
            notSeenReason = "";
            return true;
        }

        //Once per second, shows why the T-rex does or doesn't see the local player
        [Conditional("DEBUG")]
        private void LogSightCheck(PlayerControllerB localPlayer, bool inSight, bool moving, string notSeenReason)
        {
            if (Time.time - lastSightLogTime < 1f)
            {
                return;
            }
            lastSightLogTime = Time.time;
            string sight = inSight ? $"seen, moving = [{moving}]" : $"not seen: {notSeenReason}";
            LogIfDebugBuild($"JPOGTrex: Sight check || {sight} || timeSincePlayerMoving = [{localPlayer.timeSincePlayerMoving:0.00}] suspicion = [{SuspicionLevel}/{MaxSuspicionLevel}]");
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
