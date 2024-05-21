﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GameNetcodeStuff;
using LethalLib.Modules;
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
        public Transform mouthGrip = null!;
        public SphereCollider mouthAttackHitBox = null!;
        private Transform? mouthBone;
        private DeadBodyInfo carryingBody = null!;
        private List<DeadBodyInfo>carryingBodies = new List<DeadBodyInfo>();
        private int hittAblePlayers = 0;
        public float scrutiny = 1f;
        public float[] playerStealthMeters = new float[4];
#pragma warning restore 0649
        private Coroutine grabbingPlayerCoroutine;
        private Coroutine killPlayerCoroutine;
        public int suspicionLevel;
        private float timeSinceHittingLocalPlayer;
        private float timeSinceNewRandPos;
        private Vector3 positionRandomness;
        private Vector3 StalkPos;
        private System.Random enemyRandom = null!;
        private bool isDeadAnimationDone;
        private bool isHungry;
        private bool grabbingPlayer;
        private float defaultSpeed = 4f;
        private State previousState = State.Idle;
        private bool inKillAnimation;
        private bool inGrabbingAnimation;
        private bool inEatingAnimation;
        private bool roaring;
        private bool isRoaringStarted;
        private bool isSniffingStarted;
        private bool sniffing;
        private bool beginningGrab;
        private bool hitConnect;
        private bool lostPlayerInChase;
        private PlayerControllerB chasingPlayer;
        private float localPlayerTurnDistance;
        private bool spottedPlayer;

        enum State
        {
            SearchingForPlayer,
            ChasingPlayer,
            AttackingEntity,
            GrabPlayer,
            GrabbedPlayer,
            GrabbingPlayer,
            EatingPlayer,
            SpottedPlayer,
            Roaring,
            Idle,
            Eating
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start() {
            base.Start();
            SetBones();
            LogIfDebugBuild("JPOGTrex Spawned");
            timeSinceHittingLocalPlayer = 0;
            SetWalkingAnimation(defaultSpeed);
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

        private void SetBones()
        {
            GameObject model = GameObject.Find("D");
            if (model != null)
            {
                LogIfDebugBuild($"trying to find target bone for the mouthBone from: [{model}]");
                mouthBone = FindChildRecursive(model.transform, "Mouth");
            }
            if (mouthBone != null)
            {
                LogIfDebugBuild($"found targetbone {mouthBone.name}");
            }
            else
            {
                LogIfDebugBuild("no targetbone found!");
            }
        }

        public override void Update() {
            base.Update();
            UpdateMouthGripLocationToTargetBoneLocation();
            if (isEnemyDead) {
                // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
                if (!isDeadAnimationDone) {
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
            if (targetPlayer != null && (state == (int)State.GrabPlayer || state == (int)State.GrabbedPlayer || state == (int)State.GrabbingPlayer)) {
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
            }
            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
                SetWalkingAnimation(agent.speed);
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
                    agent.speed = defaultSpeed;
                    if (previousState != State.SearchingForPlayer)
                    {
                        SetWalkingAnimation(agent.speed);
                        previousState = State.SearchingForPlayer;
                    }
                    FoundClosestPlayerInRangeServerRpc();
                    CheckLineOfSightServerRpc();
                    if (targetPlayer != null)
                    {
                        LogIfDebugBuild("Start Target Player");
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.SpottedPlayer);
                        break;
                    }
                    break;

                case (int)State.SpottedPlayer:
                    agent.speed = 0f;
                    if (previousState != State.SpottedPlayer && !isSniffingStarted)
                    {
                        sniffing = true;
                        LogIfDebugBuild("JPOGTrex: Spotted Player!");
                        SetWalkingAnimation(agent.speed);
                        StartCoroutine(FoundPlayer());
                        TrexStartsChasingPlayerEffect(targetPlayer);
                        isSniffingStarted = true;

                    }
                    if (sniffing == false)
                    {
                        SwitchToBehaviourClientRpc((int)State.Roaring);
                        previousState = State.SpottedPlayer;
                        isSniffingStarted = false;
                    }
                    break;

                case (int)State.Roaring:
                    //Because the T-rex's walking animation gets set in the spotted player phase we do not want to set it again to avoid bugging animations
                    if(previousState != State.SpottedPlayer && previousState != State.Roaring)
                    {
                        SetWalkingAnimation(agent.speed);
                    } 
                    agent.speed = 0f;
                    int chaseAnimationNmbr = enemyRandom.Next(1,4);
                    if (previousState != State.Roaring && !isRoaringStarted)
                    {
                        roaring = true;
                        LogIfDebugBuild("JPOGTrex: Roaring at Player!");
                        StartCoroutine(BeginChase(chaseAnimationNmbr));
                        isRoaringStarted = true;
                    }
                    if (roaring == false)
                    {
                        SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                        previousState = State.Roaring;
                        isRoaringStarted = false;
                    }
                    break;


                case (int)State.ChasingPlayer:
                    agent.speed = defaultSpeed * 2f;
                    if (previousState != State.ChasingPlayer)
                    {
                        SetWalkingAnimation(agent.speed);
                    }
                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (!TargetClosestPlayerInAnyCase() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 40 && !CheckLineOfSightForPosition(targetPlayer.transform.position)))
                    {
                        LogIfDebugBuild("Stop Target Player");
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        previousState = State.ChasingPlayer;
                        targetPlayer = null;
                        return;
                    }
                    SetDestinationToPosition(targetPlayer.transform.position);
                    CheckForPlayersInRangeOfGrabAttackServerRpc();
                    if(hittAblePlayers > 0)
                    {
                        SwitchToBehaviourClientRpc((int)State.GrabPlayer);
                        previousState = State.ChasingPlayer;
                        hittAblePlayers = 0;
                        return;
                    }

                    previousState = State.ChasingPlayer;
                    break;

                case (int)State.AttackingEntity:
                    previousState = State.AttackingEntity;
                    DoAnimationClientRpc("attackEnemy");
                    previousState = State.AttackingEntity;
                    break;

                case (int)State.GrabPlayer:
                    agent.speed = defaultSpeed / 2;
                    if (previousState != State.GrabPlayer)
                    {
                        SetWalkingAnimation(agent.speed);
                        previousState = State.GrabPlayer;
                    }
                    previousState = State.GrabPlayer;
                    if (!beginningGrab && timeSinceHittingLocalPlayer >= 4f)
                    {
                        beginningGrab = true;
                        StartCoroutine(BeginGrab());
                    }
                    if (!hitConnect)
                    {
                        StopCoroutine(BeginGrab());
                        SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                        break;
                    }
                    if(!beginningGrab && hitConnect)
                    {
                        StopCoroutine(BeginGrab());
                        SwitchToBehaviourClientRpc((int)State.GrabbingPlayer);
                        hitConnect = false;
                        break;
                    }
                    break;

                case (int)State.GrabbedPlayer:
                    DoAnimationClientRpc("grabbedPlayer");
                    previousState = State.GrabbedPlayer;
                    SwitchToBehaviourClientRpc((int)State.GrabbingPlayer);
                    break;

                case (int)State.GrabbingPlayer:
                    if(previousState != State.GrabbingPlayer)
                    {
                        LogIfDebugBuild("JPOGTrex: GrabbingPlayer State");
                        SetWalkingAnimation(agent.speed);
                        StartCoroutine(ShakeGrabbedBody());
                    }
                    break;
                   

                case (int)State.EatingPlayer:
                    agent.speed = 0f;
                    if (previousState != State.EatingPlayer)
                    {
                        SetWalkingAnimation(agent.speed);
                        previousState = State.EatingPlayer;
                    }
                    if(previousState != State.EatingPlayer && isHungry)
                    {
                        inEatingAnimation = true;
                        StartCoroutine(EatPlayer(carryingBody));
                        isHungry = false;
                    }
                    if (!inEatingAnimation)
                    {
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    }
                    break;

                case (int)State.Idle:
                    int rndIdle = enemyRandom.Next(4);
                    if (rndIdle == 1)
                    {
                        agent.speed = 1f;
                        DoAnimationClientRpc("lookingIdle");
                        previousState = State.Idle;
                        break;
                    }
                    else if (rndIdle == 2)
                    {
                        agent.speed = 1f;
                        DoAnimationClientRpc("sniffingIdle");
                        previousState = State.Idle;
                        break;
                    }
                    else if (rndIdle == 3)
                    {
                        agent.speed = 0f;
                        DoAnimationClientRpc("eatingIdle01");
                        previousState = State.Idle;
                        SwitchToBehaviourClientRpc((int)State.Eating);
                        break;
                    }
                    break;

                case (int)State.Eating:
                    DoAnimationClientRpc("eatingIdle02");
                    previousState = State.Idle;
                    SwitchToBehaviourClientRpc((int)State.Eating);
                    break;

                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    isRoaringStarted = false;
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    break;
            }
        }

        //Copied from the gian but edited
        //The Trex should spot players but less far and well as the giant
        //Going for the don't move = can't see trope
        [ServerRpc(RequireOwnership = false)]
        private void CheckLineOfSightServerRpc(){

            CheckLineOfSightClientRpc();
        }
        [ClientRpc]
        private void CheckLineOfSightClientRpc()
        {
            CheckLineOfSight();
        }


        [ServerRpc(RequireOwnership = false)]
        private void CheckIfPlayerIsMovingServerRpc(int playerId)
        {

            CheckIfPlayerIsMovingClientRpc(playerId);
        }
        [ClientRpc]
        private void CheckIfPlayerIsMovingClientRpc(int playerId)
        {
            CheckIfPlayerIsmoving();
        }

        [ServerRpc(RequireOwnership = false)]
        private void FoundClosestPlayerInRangeServerRpc()
        {
            FoundClosestPlayerInRangeClientRpc();
        }
        [ClientRpc]
        private void FoundClosestPlayerInRangeClientRpc()
        {
            FoundClosestPlayerInRange();
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
            if (targetPlayer == null) return false;
            return true;
        }


        //Simple method that sets the walking animation of the T-rex based on it's speed.
        //This way a more slowed down walking animation or sped up running animation can be applied.
        //Should be called after the speed of the current behaviour state has been set.
        private void SetWalkingAnimation(float currentSpeed)
        {
            if (currentSpeed == 0f)
            {
                LogIfDebugBuild($"Current Speed = [{currentSpeed}] beginning animation: \"stopWalk\".");
                DoAnimationClientRpc("stopWalk");
                return;
            }
            else if (currentSpeed <= 4f && currentSpeed > 1f)
            {
                LogIfDebugBuild($"Current Speed = [{currentSpeed}] beginning animation: \"startWalk\".");
                DoAnimationClientRpc("startWalk");
                return;
            }
            else if (currentSpeed > 4f && currentSpeed <= 6f)
            {
                LogIfDebugBuild($"Current Speed = [{currentSpeed}] beginning animation: \"chasingRun\".");
                DoAnimationClientRpc("chasingRun");
                return;
            }
            else if (currentSpeed > 0f && currentSpeed <= 1f)
            {
                LogIfDebugBuild($"Current Speed = [{currentSpeed}] beginning animation: \"slowDown\".");
                DoAnimationClientRpc("slowDown");
                return;
            }
            else if (currentSpeed > 6f)
            {
                LogIfDebugBuild($"Current Speed = [{currentSpeed}] beginning animation: \"speedUp\".");
                DoAnimationClientRpc("speedUp");
                return;
            }
        }



        //This method will go trhough all "grab" logic as the grab attack consits of 2 stages
        public void startGrabAttack(Collider other)
        {
            if (targetNode == null || !IsOwner)
            {
                return;
            }
            base.OnCollideWithPlayer(other);
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other, inKillAnimation);
            if (!(playerControllerB))
            {
                return;
            }
            BeginGrab();
        }

        private IEnumerator FoundPlayer()
        {

            previousState = State.SpottedPlayer;
            LogIfDebugBuild("JPOGTrex: Sniffing");
            DoAnimationClientRpc("foundPlayer");
            yield return new WaitForSeconds(1.3f);
            LogIfDebugBuild("JPOGTrex: Sniffing finished");
            sniffing = false;
            yield break;
        }

        private IEnumerator BeginChase(int animationNumber)
        {
            previousState = State.SpottedPlayer;
            string animationName = "beginChase0" + animationNumber.ToString();
            LogIfDebugBuild($"JPOGTrex: Roaring [{animationNumber}]");
            DoAnimationClientRpc(animationName);
            // Adjust wait time based on animationNumber
            float waitTime = animationNumber == 3 ? 3.6f : 3.1f;
            yield return new WaitForSeconds(waitTime);
            LogIfDebugBuild("JPOGTrex: Roaring finished");
            roaring = false;
            yield break;
        }

        private IEnumerator BeginGrab()
        {
            LogIfDebugBuild("JPOGTrex: BeginningGrab");
            DoAnimationClientRpc("grabPlayer");
            DoAnimationClientRpc("grabbedPlayer");
            GrabAttackHitServerRpc();
            yield return new WaitForSeconds(1.2f);
            beginningGrab = false;
            yield break;
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
            if (timeSinceHittingLocalPlayer < 1f)
            {
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null)
            {
                LogIfDebugBuild("Example Enemy Collision with Player!");
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.DamagePlayer(20);
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead)
            {
                return;
            }
            enemyHP -= force;
            if (IsOwner)
            {
                if (enemyHP <= 0 && !isEnemyDead)
                {
                    // Our death sound will be played through creatureVoice when KillEnemy() is called.
                    // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                    // so we don't need to call a death animation ourselves.

                    StopCoroutine(BeginGrab());
                    // We need to stop our search coroutine, because the game does not do that by default.
                    StopCoroutine(searchCoroutine);
                    KillEnemyOnOwnerClient();
                }
            }
        }
            

        private IEnumerator ShakeGrabbedBody()
        {
            DoAnimationClientRpc("grabbingPlayer");
            yield return new WaitForSeconds(2.6f);
            if(isHungry && carryingBody != null)
            {
                SwitchToBehaviourClientRpc((int)State.GrabbingPlayer);
                yield break;
            }
            else
            {
                DropcarriedBodyServerRpc();
            }
        }

        private IEnumerator EatPlayer(DeadBodyInfo bodyToEat)
        {
            if(bodyToEat == null)
            {
                yield break;
            }
            LogIfDebugBuild($"JPOGTrex: begin eating body [{bodyToEat.playerObjectId}]");
            DoAnimationClientRpc("eatingPlayer");
            bodyToEat.MakeCorpseBloody();
            yield return new WaitForSeconds(5.7f);
            bodyToEat.DeactivateBody(false);
            inEatingAnimation = false;
            yield break;
        }
        public override void KillEnemy(bool destroy = false)
        {
            DoAnimationClientRpc("killEnemy");
            creatureVoice.Stop();
            creatureSFX.Stop();
            base.KillEnemy(destroy);
        }

        //Search Through the model to find the bone that will be used to update Mouthgrip's transform
        private Transform FindChildRecursive(Transform parent, string childName)
        {
            Transform toreturn = new Transform();
            LogIfDebugBuild($"Child name to search for: [{childName}]");
            foreach (Transform child in parent)
            {
                LogIfDebugBuild($"{child.name}"); 
                if (child.name == childName)
                {
                    LogIfDebugBuild($"found matching bone:[{child.name}] + [{childName}]");
                    return child;
                }
                Transform found = FindChildRecursive(child, childName);
                if (found != null)
                {
                    toreturn = found;
                    return found;
                }
            }
            if(toreturn != null)
            {
                return toreturn;
            }
            else
            {
                return null;
            }
        }
        //This is should update the mouthgrip transform of the Trex to transform of the bone.
        //Effectively Making it look like the player's body is attached/grabbed by the T-rex's mouth during the animation instead of blinking/warping to the static location of the mouthgrip as seen in Unity
        //Making this more generic could make it usefull to add a collider/hitbox for the mouth during the animation, this way we can check if the player is hit during the animation and killing them.
        //This should make the kill feel smoother instead of a delayed death because you were in a collision box at some point.
        private void UpdateMouthGripLocationToTargetBoneLocation()
        {
            //LogIfDebugBuild("Updating MouthGripPosition");
            mouthGrip.transform.position = mouthBone.transform.position;
            mouthGrip.transform.rotation = mouthBone.transform.rotation;
            //LogIfDebugBuild($"Mouth grip: position = [{mouthGrip.transform.position}] | rotation = [{mouthGrsip.transform.rotation}]");
        }

        //Networking stuff and killPlayer



        [ServerRpc(RequireOwnership = false)]
        private void CheckForPlayersInRangeOfGrabAttackServerRpc()
        {
            CheckForPlayersInRangeOfGrabAttackClientRPC();
        }


        [ClientRpc]
        private void CheckForPlayersInRangeOfGrabAttackClientRPC()
        {
            CheckForPlayersInRangeOfGrabAttack();
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }



        [ServerRpc(RequireOwnership = false)]
        public void GrabAttackHitServerRpc()
        {
            GrabAttackHitClientRpc();
        }

        [ClientRpc]
        public void GrabAttackHitClientRpc()
        {
            GrabAttackHit();
        }

        [ClientRpc]
        public void CancelKillAnimationWithPlayerClientRpc(int playerObjectId)
        {
            StartOfRound.Instance.allPlayerScripts[playerObjectId].inAnimationWithEnemy = null;
        }

        [ServerRpc(RequireOwnership = false)]
        public void BeginGrabAttackServerRpc()
        {
            BeginGrabAttackClientRpc();
        }
        [ClientRpc]
        public void BeginGrabAttackClientRpc()
        {
            StartCoroutine(BeginGrab());
        }
        [ServerRpc(RequireOwnership = false)]
        public void StopGrabAttackServerRpc()
        {
            StopGrabAttackClientRpc();
        }
        [ClientRpc]
        public void StopGrabAttackClientRpc()
        {
            StopCoroutine(BeginGrab());
            beginningGrab = false;
        }


        [ServerRpc(RequireOwnership = false)]
        public void KillPlayerServerRpc(int playerId)
        {
            if (!inKillAnimation)
            {
                inKillAnimation = true;
                KillPlayerClientRpc(playerId);
            }
            else
            {
                CancelKillAnimationWithPlayerClientRpc(playerId);
            }
        }

        [ClientRpc]
        public void KillPlayerClientRpc(int playerId)
        {
            LogIfDebugBuild("Kill player rpc");
            if (killPlayerCoroutine != null)
            {
                StopCoroutine(killPlayerCoroutine);
            }
            killPlayerCoroutine = StartCoroutine(KillPlayer(playerId));
        }

        [ServerRpc(RequireOwnership = false)]
        public void TakeBodyInMouthServerRpc(int killPlayerId)
        {
            TakeBodyInMouthClientRpc(killPlayerId);
        }

        [ClientRpc]
        public void TakeBodyInMouthClientRpc(int killPlayerId)
        {
            TakeBodyInMouth(killPlayerId);
        }

        [ServerRpc(RequireOwnership = false)]
        public void DropcarriedBodyServerRpc()
        {
            DropcarriedBodyClientRpc();
        }
        [ClientRpc]
        public void DropcarriedBodyClientRpc()
        {
            DropCarriedBody();
        }



        //Methods & Logic


        //Player Detection
        private bool FoundClosestPlayerInRange(float range = 15f, float senseRange = 3f)
        {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if (targetPlayer == null)
            {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }


        private void CheckLineOfSight()
        {
            PlayerControllerB[] allPlayersInLineOfSight = GetAllPlayersInLineOfSight(30f, 70, eye, 3f, StartOfRound.Instance.collidersRoomDefaultAndFoliage);
            if (allPlayersInLineOfSight != null)
            {
                //LogIfDebugBuild("Looking for moving players in line of sight");
                for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
                {
                    //TrexSeePlayerEffect(allPlayersInLineOfSight[i]);
                    if (CheckIfPlayerIsmoving())
                    {
                        LogIfDebugBuild($"JPGOTrex: Saw player [{allPlayersInLineOfSight[i]}] moving");
                        targetPlayer = allPlayersInLineOfSight[i];
                        spottedPlayer = true;
                    }
                }
            }
        }

        //Check wether this will need an Rpc or not
        private bool CheckIfPlayerIsmoving()
        {
            LogIfDebugBuild("JPOGTrex:Checking if player is moving");
            localPlayerTurnDistance += StartOfRound.Instance.playerLookMagnitudeThisFrame;
            if (localPlayerTurnDistance > 0.1f && Vector3.Distance(GameNetworkManager.Instance.localPlayerController.transform.position, base.transform.position) < 10f)
            {

                return true;
            }
            if (GameNetworkManager.Instance.localPlayerController.performingEmote)
            {
                return true;
            }
            if (Time.realtimeSinceStartup - StartOfRound.Instance.timeAtMakingLastPersonalMovement < 0.25f)
            {
                return true;
            }
            if (GameNetworkManager.Instance.localPlayerController.timeSincePlayerMoving < 0.05f)
            {
                return true;
            }
            return false;
        }


        //Attack Area and hit Detection
        private void CheckForPlayersInRangeOfGrabAttack()
        {
            int playerLayer = 1 << 3;
            LogIfDebugBuild("JPOGTrex: Checking if Player can be grabbed");
            Collider[] hitColliders = Physics.OverlapBox(attackArea.position, attackArea.localScale, Quaternion.identity, playerLayer);
            if (hitColliders.Length > 0)
            {
                foreach (var player in hitColliders)
                {
                    PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null)
                    {
                        LogIfDebugBuild($"JPOGTrex: Grab attack can hit player [{playerControllerB.playerClientId}]!");
                        hittAblePlayers++;
                    }
                }
            }
            else
            {
                LogIfDebugBuild("JPOGTrex: No players to hit!");
            }
        }

        public void GrabAttackHit()
        {
            LogIfDebugBuild("JPOGTrex: Checking if grab attack hit player");
            int playerLayer = 1 << 3; // This can be found from the game's Asset Ripper output in Unity
            Collider[] hitColliders = Physics.OverlapBox(attackArea.position, attackArea.localScale, Quaternion.identity, playerLayer);
            if (hitColliders.Length > 0)
            {
                foreach (var player in hitColliders)
                {
                    PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null)
                    {
                        LogIfDebugBuild($"JPOGTrex: grab attack hit player: [{playerControllerB.playerClientId}]!");
                        timeSinceHittingLocalPlayer = 0f;
                        LogIfDebugBuild($"JPOGTrex: Killing player: [{playerControllerB.playerClientId}]!");
                        StartCoroutine(KillPlayer((int)playerControllerB.playerClientId));
                    }
                }
                hitConnect = true;
            }
            else
            {
                LogIfDebugBuild("Grab attack hit 0 players");
                hitConnect = false;
            }
        }

        //Player body Interaction
        private void TakeBodyInMouth(int playerId)
        {
            PlayerControllerB killPlayer = StartOfRound.Instance.allPlayerScripts[playerId];           
            if (killPlayer != null)
            {
                DeadBodyInfo body = killPlayer.deadBody;
                body.attachedTo = mouthGrip;
                body.attachedLimb = body.bodyParts[5];
                body.matchPositionExactly = true;

                //The T-rex Should be able to multikill so all bodies it grabs are added to the list
                carryingBodies.Add(body);
            }
        }

        private void DropCarriedBody()
        {
            if (carryingBodies.Count > 0)
            {
                //All grabbed bodies should be dropped by the Trex
                foreach (DeadBodyInfo carryingbody in carryingBodies)
                {
                    carryingBody.speedMultiplier = 12f;
                    carryingBody.attachedTo = null;
                    carryingBody.attachedLimb = null;
                    carryingBody.matchPositionExactly = false;
                    carryingBody = null;
                }
                //Clear the list of bodies bein
                carryingBodies.Clear();
            }
        }

        private IEnumerator KillPlayer(int playerId)
        {
            if (IsOwner)
            {
                agent.speed = Mathf.Clamp(agent.speed, 2f, 0f);
            }
            LogIfDebugBuild($"Killing player: [{playerId}]");
            PlayerControllerB killPlayer = StartOfRound.Instance.allPlayerScripts[playerId];
            if (!isEnemyDead)
            {
                LogIfDebugBuild("JPOGTrex: T-rex is still alive");
                //DoAnimationClientRpc("killEnemy");
            }
            if (GameNetworkManager.Instance.localPlayerController == killPlayer)
            {
                killPlayer.KillPlayer(Vector3.zero, spawnBody: true, CauseOfDeath.Mauling);
            }
            float startTime = Time.timeSinceLevelLoad;
            yield return new WaitUntil(() => killPlayer.deadBody != null || Time.timeSinceLevelLoad - startTime > 2f);
            if (killPlayer.deadBody == null)
            {
                LogIfDebugBuild("JPOGTrex: Player body was not spawned or found withing 2 seconds");
                killPlayer.inAnimationWithEnemy = null;
                inKillAnimation = false;
                yield break;
            }
            TakeBodyInMouthClientRpc(playerId);
            startTime = Time.timeSinceLevelLoad;
            Quaternion rotateTo = Quaternion.Euler(new Vector3(0f, RoundManager.Instance.YRotationThatFacesTheFarthestFromPosition(base.transform.position + Vector3.up * 0.6f), 0f));
            Quaternion rotateFrom = base.transform.rotation;
            while (Time.timeSinceLevelLoad - startTime < 2f)
            {
                yield return null;
                if (base.IsOwner)
                {
                    base.transform.rotation = Quaternion.RotateTowards(rotateFrom, rotateTo, 60f * Time.deltaTime);
                }
            }
            suspicionLevel = 2;
            inKillAnimation = false;
            killPlayerCoroutine = null;
            yield break;
        }

        //Effects on player

        private void TrexStartsChasingPlayerEffect(PlayerControllerB playerControllerB)
        {
            if (playerControllerB.isPlayerDead || playerControllerB.isInsideFactory)
            {
                return;
            }
            if (currentBehaviourStateIndex == (int)State.SearchingForPlayer && playerControllerB == GameNetworkManager.Instance.localPlayerController)
            {
                targetPlayer.JumpToFearLevel(10.0f);
                return;
            }
        }
        private void TrexSeePlayerEffect(PlayerControllerB playerControllerB)
        {
            if (playerControllerB.isPlayerDead || playerControllerB.isInsideFactory || playerControllerB != null)
            {
                return;
            }
            if (playerControllerB == GameNetworkManager.Instance.localPlayerController && playerControllerB != null)
            {
                playerControllerB.IncreaseFearLevelOverTime(2.0f);
                return;
            }
            bool flag = false;
            if (!playerControllerB.isInHangarShipRoom && CheckLineOfSightForPosition(playerControllerB.gameplayCamera.transform.position, 45f, 70) && playerControllerB != null)
            {
                if (Vector3.Distance(base.transform.position, playerControllerB.transform.position) < 15f)
                {
                    playerControllerB.JumpToFearLevel(0.7f);
                }
                else
                {
                    playerControllerB.JumpToFearLevel(0.4f);
                }
            }
        }
    }
}