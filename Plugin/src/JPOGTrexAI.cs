﻿using System.Collections;
using System.Diagnostics;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace JPOGTrex {

    // You may be wondering, how does the JPOGTrex know it is from class JPOGTrexAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.

    class JPOGTrexAI : EnemyAI
    {
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
        #pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
        #pragma warning restore 0649
        float timeSinceHittingLocalPlayer;
        float timeSinceNewRandPos;
        Vector3 positionRandomness;
        Vector3 StalkPos;
        System.Random enemyRandom = null!;
        bool isDeadAnimationDone;
        bool isHungry;
        float defaultSpeed = 4f;
enum State
        {
            SearchingForPlayer,
            ChasingPlayer,
            AttackingEntity,
            GrabPlayer,
            GrabbedPlayer,
            GrabbingPlayer,
            EatingPlayer,
            Searching,
            Idle,
            Eating
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start() {
            base.Start();
            LogIfDebugBuild("JPOGTrex Spawned");
            timeSinceHittingLocalPlayer = 0;
            creatureAnimator.SetTrigger("");
            SetWalkingAnimation(0f);
            timeSinceNewRandPos = 0;
            positionRandomness = new Vector3(0, 0, 0);
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;
            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            currentBehaviourStateIndex = (int)State.SearchingForPlayer;
            // We make the enemy start searching. This will make it start wandering around.
            StartSearch(transform.position);
        }

        public override void Update() {
            base.Update();
            if(isEnemyDead){
                // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
                if(!isDeadAnimationDone){ 
                    LogIfDebugBuild("Stopping enemy voice with janky code.");
                    isDeadAnimationDone = true;
                    creatureVoice.Stop();
                    creatureVoice.PlayOneShot(dieSFX);
                }
                return;
            }
            timeSinceHittingLocalPlayer += Time.deltaTime;
            timeSinceNewRandPos += Time.deltaTime;
            
            var state = currentBehaviourStateIndex;
            if(targetPlayer != null && (state == (int)State.GrabPlayer || state == (int)State.GrabbedPlayer || state == (int)State.GrabbingPlayer)){
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
            }
            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
                SetWalkingAnimation(defaultSpeed);
            }
        }

        public override void DoAIInterval() {
            
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            };

            switch (currentBehaviourStateIndex)
            {

                case (int)State.SearchingForPlayer:
                    agent.speed = 0f;
                    SetWalkingAnimation(defaultSpeed);
                    DoAnimationClientRpc("startSearch");
                    if (FoundClosestPlayerInRange(25f, 3f))
                    {
                        LogIfDebugBuild("Start Target Player");
                        StopSearch(currentSearch);
                        DoAnimationClientRpc("foundPlayer");
                        SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                        return;
                    }
                    break;

                case (int)State.ChasingPlayer:
                    agent.speed = 0f;
                    SetWalkingAnimation(defaultSpeed);
                    int chaseAnimationNmbr = enemyRandom.Next(4);
                    if (chaseAnimationNmbr == 1 ){
                        LogIfDebugBuild($"Current State = [{State.ChasingPlayer}] beginning animation: \"beginChase01\".");
                        DoAnimationClientRpc("beginchase0" + chaseAnimationNmbr.ToString());
                    }
                    else if( chaseAnimationNmbr == 2)
                    {
                        LogIfDebugBuild($"Current State = [{State.ChasingPlayer}] beginning animation: \"beginChase02\".");
                        DoAnimationClientRpc("beginchase0" + chaseAnimationNmbr.ToString());
                    }
                    else if( chaseAnimationNmbr == 3)
                    {
                        LogIfDebugBuild($"Current State = [{State.ChasingPlayer}] beginning animation: \"beginChase03\".");
                        DoAnimationClientRpc("beginchase0" + chaseAnimationNmbr.ToString());
                    }
                    else
                    {
                        DoAnimationClientRpc("beginchase0" + chaseAnimationNmbr.ToString());
                    }
                    agent.speed = defaultSpeed * 2f;
                    SetWalkingAnimation(defaultSpeed);
                    LogIfDebugBuild($"Current State = [{State.ChasingPlayer}] beginning animation: \"chasingTarget\".");
                    DoAnimationClientRpc("chasingTarget");
                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (!TargetClosestPlayerInAnyCase() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 20 && !CheckLineOfSightForPosition(targetPlayer.transform.position)))
                    {
                        LogIfDebugBuild("Stop Target Player");
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        return;
                    }
                    SetDestinationToPosition(targetPlayer.transform.position);
                    break;

                case (int)State.AttackingEntity:
                    LogIfDebugBuild($"Current State = [{State.AttackingEntity}] beginning animation: \"attackEnemy\".");
                    DoAnimationClientRpc("attackEnemy");
                    break;

                case (int)State.GrabPlayer:
                    agent.speed = defaultSpeed / 2;
                    SetWalkingAnimation(defaultSpeed);
                    LogIfDebugBuild($"Current State = [{State.GrabPlayer}] beginning animation: \"grabPlayer\".");
                    //Logic To check if grab connected
                    bool hitConnect = true;
                    DoAnimationClientRpc("grabPlayer");
                    if (hitConnect != false)
                    {
                        SwitchToBehaviourClientRpc((int)State.GrabbedPlayer);
                    }
                    break;

                case (int)State.GrabbedPlayer:
                    LogIfDebugBuild($"Current State = [{State.GrabbedPlayer}] beginning animation: \"grabbedPlayer\".");
                    DoAnimationClientRpc("grabbedPlayer");

                    SwitchToBehaviourClientRpc((int)State.GrabbingPlayer);
                    break;

                case (int)State.GrabbingPlayer:
                    LogIfDebugBuild($"Current State = [{State.GrabbingPlayer}] beginning animation: \"grabbingPlayer\".");
                    DoAnimationClientRpc("grabbingPlayer");
                    //If T-rex is hungry, it should eat the target (like Giant), otherwise drop it (like blind dog)
                    if (isHungry)
                    {
                        SwitchToBehaviourClientRpc((int)State.EatingPlayer);
                        break;
                    }
                    else
                    {
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        break;
                    }

                case (int)State.EatingPlayer:
                    LogIfDebugBuild($"Current State = [{State.EatingPlayer}] beginning animation: \"eatingPlayuer\".");
                    DoAnimationClientRpc("eatingPlayuer");
                    break;

                case (int)State.Idle:
                    int rndIdle = enemyRandom.Next(4);
                    if (rndIdle == 1)
                    {
                        DoAnimationClientRpc("breathingIdle");
                    }
                    else if (rndIdle == 2)
                    {
                        DoAnimationClientRpc("sneezingIdle");
                    }
                    else if (rndIdle == 3)
                    {
                        DoAnimationClientRpc("eatingIdle01");
                        SwitchToBehaviourClientRpc((int)State.Eating);
                    }
                    break;

                case (int)State.Eating:
                    LogIfDebugBuild($"Current State = [{State.Eating}] beginning animation: \"eatingIdle02\".");
                    DoAnimationClientRpc("eatingIdle02");
                    SwitchToBehaviourClientRpc((int)State.Eating);
                    break;

                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        }

        bool FoundClosestPlayerInRange(float range, float senseRange) {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if(targetPlayer == null){
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }
        
        bool TargetClosestPlayerInAnyCase() {
            mostOptimalDistance = 2000f;
            targetPlayer = null;
            for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
            {
                tempDist = Vector3.Distance(transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
                if (tempDist < mostOptimalDistance)
                {
                    mostOptimalDistance = tempDist;
                    targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
                }
            }
            if(targetPlayer == null) return false;
            return true;
        }


        //Simple method that sets the walking animation of the T-rex based on it's speed.
        //This way a more slowed down walking animation or sped up running animation can be applied.
        //Should be called after the speed of the current behaviour state has been set.
        public void SetWalkingAnimation(float currentSpeed)
        {
            if(currentSpeed == 0f)
            {
                LogIfDebugBuild($"Current Speed = [{currentSpeed}] beginning animation: \"stopWalk\".");
                DoAnimationClientRpc("stopWalk");
                return;
            }
            else if(currentSpeed <= 4f && currentSpeed >1f)
            {
                LogIfDebugBuild($"Current Speed = [{currentSpeed}] beginning animation: \"startWalk\".");
                DoAnimationClientRpc("startWalk");
                return;
            }
            else if(currentSpeed > 4f && currentSpeed  <= 6f)
            {
                LogIfDebugBuild($"Current Speed = [{currentSpeed}] beginning animation: \"chasingRun\".");
                DoAnimationClientRpc("chasingRun");
                return;
            }
            else if (currentSpeed > 0f && currentSpeed <= 1f )
            {
                LogIfDebugBuild($"Current Speed = [{currentSpeed}] beginning animation: \"slowDown\".");
                DoAnimationClientRpc("slowDown");
                return;
            }
            else if(currentSpeed > 6f)
            {
                LogIfDebugBuild($"Current Speed = [{currentSpeed}] beginning animation: \"speedUp\".");
                DoAnimationClientRpc("speedUp");
                return;
            }
        }

        public void StickingInFrontOfPlayer() {
            // We only run this method for the host because I'm paranoid about randomness not syncing I guess
            // This is fine because the game does sync the position of the enemy.
            // Also the attack is a ClientRpc so it should always sync
            if (targetPlayer == null || !IsOwner) {
                return;
            }
            if(timeSinceNewRandPos > 0.7f){
                timeSinceNewRandPos = 0;
                if(enemyRandom.Next(0, 5) == 0){
                    // Attack
                    StartCoroutine(GrabAttack());
                }
                else{
                    // Go in front of player
                    positionRandomness = new Vector3(enemyRandom.Next(-2, 2), 0, enemyRandom.Next(-2, 2));
                    StalkPos = targetPlayer.transform.position - Vector3.Scale(new Vector3(-5, 0, -5), targetPlayer.transform.forward) + positionRandomness;
                }
                SetDestinationToPosition(StalkPos, checkForPath: false);
            }
        }

        IEnumerator GrabAttack() {
            SwitchToBehaviourClientRpc((int)State.GrabPlayer);
            StalkPos = targetPlayer.transform.position;
            SetDestinationToPosition(StalkPos);
            yield return new WaitForSeconds(0.5f);
            if(isEnemyDead){
                yield break;
            }
            DoAnimationClientRpc("swingAttack");
            yield return new WaitForSeconds(0.35f);
            SwingAttackHitClientRpc();
            // In case the player has already gone away, we just yield break (basically same as return, but for IEnumerator)
            if(currentBehaviourStateIndex != (int)State.GrabbedPlayer)
            {
                yield break;
            }
            SwitchToBehaviourClientRpc((int)State.GrabbingPlayer);
        }

        public override void OnCollideWithPlayer(Collider other) {
            if (timeSinceHittingLocalPlayer < 1f) {
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null)
            {
                LogIfDebugBuild("JPOGTrex Collision with Player!");
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.DamagePlayer(20);
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1) {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if(isEnemyDead){
                return;
            }
            enemyHP -= force;
            if (IsOwner) {
                if (enemyHP <= 0 && !isEnemyDead) {
                    // Our death sound will be played through creatureVoice when KillEnemy() is called.
                    // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                    // so we don't need to call a death animation ourselves.

                    StopCoroutine(GrabAttack());
                    // We need to stop our search coroutine, because the game does not do that by default.
                    StopCoroutine(searchCoroutine);
                    KillEnemyOnOwnerClient();
                }
            }
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName) {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        public void SwingAttackHitClientRpc() {
            LogIfDebugBuild("SwingAttackHitClientRPC");
            int playerLayer = 1 << 3; // This can be found from the game's Asset Ripper output in Unity
            Collider[] hitColliders = Physics.OverlapBox(attackArea.position, attackArea.localScale, Quaternion.identity, playerLayer);
            if(hitColliders.Length > 0){
                foreach (var player in hitColliders){
                    PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null)
                    {
                        LogIfDebugBuild("Swing attack hit player!");
                        timeSinceHittingLocalPlayer = 0f;
                        playerControllerB.DamagePlayer(40);
                    }
                }
            }
        }
    }
}