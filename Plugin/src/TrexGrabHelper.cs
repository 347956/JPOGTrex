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
        private Coroutine? grabCheckCoroutine;
        private Coroutine? killPlayerCoroutine;
        private bool hitConnect;

        public bool BeginningGrab { get; private set; }
        public bool InGrabAttack { get; private set; }
        public bool InKillAnimation { get; private set; }
        public bool IsBusy => BeginningGrab || InGrabAttack || InKillAnimation;

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

        public void OnHitConnected()
        {
            hitConnect = true;
            LogIfDebugBuild($"JPOGTrex: hitConnect = [{hitConnect}]");
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

        //Server only, returns false if the T-rex is already busy killing a player
        public bool TryBeginKill()
        {
            LogIfDebugBuild($"JPOGTrex: Checking if in killAnimation || inKillAnimation ={InKillAnimation}");
            if (InKillAnimation)
            {
                return false;
            }
            InKillAnimation = true;
            return true;
        }

        //Runs on every client
        public void StartKill(int playerId)
        {
            if (killPlayerCoroutine != null)
            {
                LogIfDebugBuild($"JPOGTrex: In killPlayerCoroutine!, stopping previous killPlayerCoroutine!");
                trexAI.StopCoroutine(killPlayerCoroutine);
            }
            LogIfDebugBuild($"JPOGTrex: start Killing player [{playerId}] rpc");
            killPlayerCoroutine = trexAI.StartCoroutine(KillPlayer(playerId));
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
                if (playerControllerB == null)
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
        private IEnumerator KillPlayer(int playerId)
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
                InKillAnimation = false;
                killPlayerCoroutine = null;
                yield break;
            }
            if (GameNetworkManager.Instance.localPlayerController == killPlayer)
            {
                int killAnimation = trexAI.EnemyRandom.Next(0, 2) == 0 ? 1 : 7;
                killPlayer.KillPlayer(Vector3.zero, spawnBody: true, CauseOfDeath.Mauling, killAnimation);
            }
            float startTime = Time.timeSinceLevelLoad;
            yield return new WaitUntil(() => killPlayer.deadBody != null || Time.timeSinceLevelLoad - startTime > 2f);
            if (killPlayer.deadBody == null)
            {
                LogIfDebugBuild("JPOGTrex: Player body was not spawned or found withing 2 seconds");
                killPlayer.inAnimationWithEnemy = null;
                InKillAnimation = false;
                trexAI.targetPlayer = null;
                killPlayerCoroutine = null;
                yield break;
            }
            trexAI.Feeding.TakeBodyInMouth(playerId);
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
            InKillAnimation = false;
            killPlayerCoroutine = null;
        }
    }
}
