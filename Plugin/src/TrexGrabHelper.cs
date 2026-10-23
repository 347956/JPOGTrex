using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using GameNetcodeStuff;
using UnityEngine;

namespace JPOGTrex
{
    //Handles the bite/grab attack and killing the grabbed player(s)
    internal class TrexGrabHelper
    {
        private readonly JPOGTrexAI trexAI;
        private readonly List<int> hitPlayerIds = new List<int>();
        private readonly float grabDuration = 1.2f;
        //Indexes in StartOfRound.playerRagdolls
        private const int NormalDeathAnimation = 0;
        private const int HeadBurstDeathAnimation = 1;
        private const int SlicedInHalfDeathAnimation = 7;
        private readonly double slicedInHalfChance = 0.15;
        private readonly double headBurstChance = 0.05;
        //Time after a bite that hit before the T-rex can bite again
        private readonly float grabCooldown = 4f;
        //Server only, the players that are being killed right now, so the same player can't be killed twice
        private readonly HashSet<int> playersBeingKilled = new HashSet<int>();
        private Coroutine? grabCheckCoroutine;
        private int activeKills;
        private bool hitConnect;
        private float lastHitTime = -100f;

        public bool BeginningGrab { get; private set; }
        public bool InGrabAttack { get; private set; }
        public bool InKillAnimation => activeKills > 0;
        public bool IsBusy => BeginningGrab || InGrabAttack || InKillAnimation;
        public bool CanGrab => Time.time - lastHitTime >= grabCooldown;
        public int ConsecutiveMisses { get; private set; }

        public TrexGrabHelper(JPOGTrexAI trexAI)
        {
            this.trexAI = trexAI;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        //Owner only
        public void StartGrab()
        {
            BeginningGrab = true;
            trexAI.StartCoroutine(BeginGrab());
        }

        //Returns true once after a grab hit a player
        public bool ConsumeHitConnect()
        {
            if (!hitConnect)
            {
                return false;
            }
            hitConnect = false;
            return true;
        }

        //Runs on every client
        public void OnHitConnected()
        {
            hitConnect = true;
            lastHitTime = Time.time;
            ConsecutiveMisses = 0;
            LogIfDebugBuild($"JPOGTrex: hitConnect = [{hitConnect}]");
        }

        //Owner only
        public void RegisterMiss()
        {
            ConsecutiveMisses++;
            LogIfDebugBuild($"JPOGTrex: Missed bites in a row = [{ConsecutiveMisses}]");
        }

        public void ResetMisses()
        {
            ConsecutiveMisses = 0;
        }

        //Runs on every client
        public void OnStartGrabCheck()
        {
            hitPlayerIds.Clear();
            InGrabAttack = true;
            if (grabCheckCoroutine != null)
            {
                trexAI.StopCoroutine(grabCheckCoroutine);
            }
            grabCheckCoroutine = trexAI.StartCoroutine(CheckIfGrabHitDuringAnimation());
        }

        //Runs on every client
        public void OnStopGrabCheck()
        {
            LogIfDebugBuild("JPOGTrex: Stopping grab check Coroutine");
            InGrabAttack = false;
            if (grabCheckCoroutine != null)
            {
                trexAI.StopCoroutine(grabCheckCoroutine);
                grabCheckCoroutine = null;
            }
        }

        //Server only, returns false if this player is already being killed. Several players can be killed by the same bite
        public bool TryBeginKill(int playerId)
        {
            bool canKill = playersBeingKilled.Add(playerId);
            LogIfDebugBuild($"JPOGTrex: Trying to kill player [{playerId}] || canKill = [{canKill}]");
            return canKill;
        }

        //Server only, picked here so every client knows which ragdoll the body uses
        public int ChooseDeathAnimation()
        {
            double roll = trexAI.EnemyRandom.NextDouble();
            if (roll < slicedInHalfChance)
            {
                return SlicedInHalfDeathAnimation;
            }
            if (roll < slicedInHalfChance + headBurstChance)
            {
                return HeadBurstDeathAnimation;
            }
            return NormalDeathAnimation;
        }

        //Runs on every client
        public void StartKill(int playerId, int deathAnimation)
        {
            LogIfDebugBuild($"JPOGTrex: start Killing player [{playerId}] rpc");
            trexAI.StartCoroutine(KillPlayer(playerId, deathAnimation));
        }

        //Checks if the attack should hit the player (during the animation of the grab/bite)
        private void GrabAttackHitAnimation()
        {
            int playerLayer = 1 << 3;
            Collider[] hitColliders = Physics.OverlapBox(trexAI.mouthAttackHitBox.position, trexAI.mouthAttackHitBox.localScale, Quaternion.identity, playerLayer);
            bool hitPlayer = false;
            foreach (var player in hitColliders)
            {
                //Only returns the local player, so every client checks itself
                PlayerControllerB playerControllerB = trexAI.MeetsStandardPlayerCollisionConditions(player);
                if (playerControllerB == null || playerControllerB.isInHangarShipRoom)
                {
                    continue;
                }
                int playerId = (int)playerControllerB.playerClientId;
                if (!hitPlayerIds.Contains(playerId))
                {
                    LogIfDebugBuild($"JPOGTrex: Grab Attack Hit player: [{playerId}], calling to kill player");
                    trexAI.ResetHitTimer();
                    hitPlayer = true;
                    trexAI.KillPlayerServerRpc(playerId);
                    hitPlayerIds.Add(playerId);
                }
            }
            if (hitPlayer)
            {
                trexAI.SetHitConnectToTrueServerRpc();
            }
        }

        //Owner only
        private IEnumerator BeginGrab()
        {
            trexAI.StartGrabCheckServerRpc();
            LogIfDebugBuild("JPOGTrex: Beginning grab Attack!");
            trexAI.DoAnimationServerRpc("grabPlayer");
            trexAI.DoAnimationServerRpc("grabbedPlayer");
            yield return new WaitForSeconds(grabDuration);
            trexAI.StopGrabCheckServerRpc();
            LogIfDebugBuild("JPOGTrex: grab attack finished");
            BeginningGrab = false;
        }

        //Runs on every client
        private IEnumerator CheckIfGrabHitDuringAnimation()
        {
            while (InGrabAttack)
            {
                GrabAttackHitAnimation();
                yield return null;
            }
            grabCheckCoroutine = null;
        }

        //Runs on every client
        private IEnumerator KillPlayer(int playerId, int deathAnimation)
        {
            activeKills++;
            //The finally block runs on every early exit, so the counters can't get stuck (only StopAllCoroutines skips it, which only happens when the T-rex dies)
            try
            {
                if (trexAI.IsOwner)
                {
                    trexAI.agent.speed = Mathf.Clamp(trexAI.agent.speed, 0f, 2f);
                }
                LogIfDebugBuild($"JPOGTrex: begin Killing player: [{playerId}]");
                PlayerControllerB killPlayer = StartOfRound.Instance.allPlayerScripts[playerId];
                if (killPlayer == null || killPlayer.isPlayerDead)
                {
                    LogIfDebugBuild($"JPOGTrex: Player [{playerId}] is not valid or already dead.");
                    yield break;
                }
                if (GameNetworkManager.Instance.localPlayerController == killPlayer)
                {
                    killPlayer.KillPlayer(Vector3.zero, spawnBody: true, CauseOfDeath.Mauling, deathAnimation);
                }
                float startTime = Time.timeSinceLevelLoad;
                yield return new WaitUntil(() => killPlayer.deadBody != null || Time.timeSinceLevelLoad - startTime > 2f);
                if (killPlayer.deadBody == null)
                {
                    LogIfDebugBuild("JPOGTrex: Player body was not spawned or found withing 2 seconds");
                    killPlayer.inAnimationWithEnemy = null;
                    if (trexAI.targetPlayer == killPlayer)
                    {
                        trexAI.targetPlayer = null;
                    }
                    yield break;
                }
                //A body that is snapped in half is not eaten, only part of it could be removed
                trexAI.Feeding.TakeBodyInMouth(playerId, canBeEaten: deathAnimation != SlicedInHalfDeathAnimation);
                startTime = Time.timeSinceLevelLoad;
                Quaternion rotateTo = Quaternion.Euler(new Vector3(0f, RoundManager.Instance.YRotationThatFacesTheFarthestFromPosition(trexAI.transform.position + Vector3.up * 0.6f), 0f));
                Quaternion rotateFrom = trexAI.transform.rotation;
                while (Time.timeSinceLevelLoad - startTime < 2f)
                {
                    yield return null;
                    if (trexAI.IsOwner)
                    {
                        trexAI.transform.rotation = Quaternion.RotateTowards(rotateFrom, rotateTo, 60f * Time.deltaTime);
                    }
                }
            }
            finally
            {
                activeKills--;
                if (trexAI.IsServer)
                {
                    playersBeingKilled.Remove(playerId);
                }
            }
        }
    }
}
