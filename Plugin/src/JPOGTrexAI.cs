﻿using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using GameNetcodeStuff;
using JPOGTrex.Configuration;
using Unity.Netcode;
using UnityEngine;

namespace JPOGTrex {

    // You may be wondering, how does the JPOGTrex know it is from class JPOGTrexAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.

    class JPOGTrexAI : EnemyAI, IVisibleThreat
    {
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
        public Transform mouthGrip = null!;
        public Transform mouthAttackHitBox = null!;
        public AudioSource trexRoarSFX = null!;
        public AudioSource trexSubRoarSFX = null!;
        private Transform? mouthBone = null!;
        private List<DeadBodyInfo>carryingBodies = new List<DeadBodyInfo>();
        private List<int> hitPlayerIds = new List<int>();
        private GameObject modelD = null!;
        public float scrutiny = 1f;
#pragma warning restore 0649
        private Coroutine? killPlayerCoroutine = null!;
        private float timeSinceHittingLocalPlayer;
        private float timeSinceNewRandPos;
        private Vector3 positionRandomness;
        private System.Random enemyRandom = null!;
        private bool isDeadAnimationDone;
        private bool isHungry = true;
        private float defaultSpeed = 6f;
        private State previousState = State.Idle;
        private bool inKillAnimation;
        private bool inEatingAnimation;
        private bool roaring;
        private bool isRoaringStarted;
        private bool isSniffingStarted;
        private bool sniffing;
        private bool beginningGrab;
        private bool hitConnect;
        private PlayerControllerB? movingPlayer = null!;
        private float localPlayerTurnDistance;
        private float attackRange;
        private bool doneEating = false;
        private bool shakingBoddies;
        private int visionRangeSearching = 70;
        private int visionRangeWidth = 60;
        private int visionRangeChase = 50;
        private float suspicionLevel;
        private float maxSuspicionLevel;
        private float increasRateSuspicion;
        private float decreaseRateSuspicion;
        private float timeSinceSeeingPlayerMove;
        private float decreaseSuspicionTimer;
        private float timeSinceLostPlayer = 0.0f;
        private float maxSearchtime = 20.0f;
        private float stopChaseHeight = 12f;
        private float previousSpeed;
        private Vector3 lastKnownPositionTargetPlayer;
        private bool isMovingTowardsLastKnownPosition;
        private bool foundPlayerInCloseProx = false;
        private bool inGrabAttack;
        private int maxPlayersToEat;
        private int playersEaten = 0;
        private float stoppingThreshold = 9;
        private float lastSuspicionDecreaseTime;
        private bool inAttackEnemyAnimation = false;
        private EnemyAI? targetEntity = null;
        private Vector3 previousForward;
        private float turnSpeed;

        ThreatType IVisibleThreat.type => ThreatType.ForestGiant;

        public int GetThreatLevel(Vector3 seenByPosition)
        {
            return 18;
        }

        public int GetInterestLevel()
        {
            return 0;
        }

        public Transform GetThreatLookTransform()
        {
            return eye;
        }

        public Transform GetThreatTransform()
        {
            return base.transform;
        }

        public Vector3 GetThreatVelocity()
        {
            if (base.IsOwner)
            {
                return agent.velocity;
            }
            return Vector3.zero;
        }

        public float GetVisibility()
        {
            if(isEnemyDead)
            {
                return 0f;
            }
            if(agent.velocity.sqrMagnitude > 0f)
            {
                return 1f;
            }
            return 0.75f;
        }

        public int SendSpecialBehaviour(int id)
        {
            return 0;
        }

        enum State
        {
            SearchingForPlayer,
            SpottedPlayer,
            ChasingPlayer,
            Roaring,
            AttackingEntity,
            GrabPlayer,
            GrabbedPlayer,
            GrabbingPlayer,
            EatingPlayer,
            Eating,
            Idle
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        private void AssignConfigVariables()
        {
            visionRangeSearching = PluginConfig.Instance.VisionRangeLength.Value;
            visionRangeChase = PluginConfig.Instance.VisionRangeLength.Value - 20;
            visionRangeWidth = PluginConfig.Instance.VisionRangeWidth.Value;
            defaultSpeed = PluginConfig.Instance.DefaultSpeed.Value;
            maxSuspicionLevel = PluginConfig.Instance.MaxSuspicionLevel.Value;
            increasRateSuspicion = PluginConfig.Instance.SuspicionIncrement.Value;
            decreaseRateSuspicion = PluginConfig.Instance.SuspicionDecrement.Value;
            decreaseSuspicionTimer = PluginConfig.Instance.SuspiciontDecreaseTime.Value;
            maxPlayersToEat = 2;
        }

        public override void Start()
        {
            AssignConfigVariables();
            agent.stoppingDistance = 0f;
            base.Start();
            attackRange = 8f;
            //SetBonesServerRpc();
            LogIfDebugBuild("JPOGTrex Spawned");
            timeSinceHittingLocalPlayer = 0;
            SetWalkingAnimationServerRpc(defaultSpeed);
            timeSinceNewRandPos = 0;
            positionRandomness = new Vector3(0, 0, 0);
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;
            SwitchToBehaviourStateServerRpc((int)State.SearchingForPlayer);
        }

        public override void Update() {
            base.Update();
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
            if (enemyHP <= 0 && !isEnemyDead)
            {
                // Our death sound will be played through creatureVoice when KillEnemy() is called.
                // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                // so we don't need to call a death animation ourselves.
                // We need to stop our search coroutine, because the game does not do that by default.
                StopAllCoroutines();
                StopCoroutine(searchCoroutine);
                KillEnemyOnOwnerClient();
                return;
            }
            if (isMovingTowardsLastKnownPosition == true)
            {
                timeSinceLostPlayer += Time.deltaTime;
                LogIfDebugBuild($"JPOGTrex: time since losing the player = [{timeSinceLostPlayer}]");
            }
            timeSinceHittingLocalPlayer += Time.deltaTime;
            timeSinceNewRandPos += Time.deltaTime;
            timeSinceSeeingPlayerMove += Time.deltaTime;

            var state = currentBehaviourStateIndex;
            if (targetPlayer != null && (state == (int)State.GrabPlayer || state == (int)State.GrabbedPlayer || state == (int)State.GrabbingPlayer || state == (int)State.EatingPlayer || state == (int)State.SpottedPlayer || state == (int)State.SpottedPlayer)) {
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
            }
            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
                SetWalkingAnimationServerRpc(agent.speed);
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
                    if (previousState != State.SearchingForPlayer)
                    {
                        StopAllCoroutines();
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [SearchingForPlayer]");
                        movingTowardsTargetPlayer = false;
                        agent.speed = defaultSpeed;
                        agent.autoBraking = true;
                        SetWalkingAnimationServerRpc(agent.speed);
                        StartSearch(transform.position);
                        previousState = State.SearchingForPlayer;
                    }
                    FoundClosestPlayerInRangeServerRpc();
                    if (foundPlayerInCloseProx)
                    {
                        LogIfDebugBuild("JPOGTrex: Beginning chase for player in close proximity");
                        StopSearch(currentSearch);
                        foundPlayerInCloseProx = false;
                        SwitchToBehaviourStateServerRpc((int)State.SpottedPlayer);
                        break;
                    }
                    //FoundClosestPlayerInRangeServerRpc();
                    CheckLineOfSightServerRpc();
                    DecreaseSuspicionServerRpc();
                    //LogIfDebugBuild($"JPOGTrex: seconds since a player was moving = [{timeSinceSeeingPlayerMove}]");
                    if (movingPlayer != null && suspicionLevel == maxSuspicionLevel)
                    {
                        //LogIfDebugBuild($"JPOGTrex: Last moving player [{movingPlayer.playerClientId}] will be set as the player to target. Suspicion level = [{suspicionLevel}]");
                        targetPlayer = movingPlayer;
                        //LogIfDebugBuild($"JPOGTrex: Set player [{targetPlayer.playerClientId}] as the target player!");
                        movingPlayer = null;
                        StopSearch(currentSearch);
                        SwitchToBehaviourStateServerRpc((int)State.SpottedPlayer);
                        break;
                    }
                    break;

                case (int)State.SpottedPlayer:
                    if (previousState != State.SpottedPlayer && !isSniffingStarted)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [SpottedPlayer]");
                        agent.speed = 2f;
                        sniffing = true;
                        LogIfDebugBuild("JPOGTrex: Spotted Player!");
                        SetWalkingAnimationServerRpc(agent.speed);
                        StartCoroutine(FoundPlayer());
                        //TrexStartsChasingPlayerEffect(targetPlayer);
                        isSniffingStarted = true;
                        previousState = State.SpottedPlayer;

                    }
                    if (sniffing == false)
                    {
                        isSniffingStarted = false;
                        SwitchToBehaviourStateServerRpc((int)State.Roaring);
                    }
                    break;

                case (int)State.Roaring:
                    //Because the T-rex's walking animation gets set in the spotted player phase we do not want to set it again to avoid bugging animations
                    if(previousState != State.SpottedPlayer && previousState != State.Roaring)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [Roaring]");
                        agent.speed = 0f;
                        SetWalkingAnimationServerRpc(agent.speed);
                        previousState = State.Roaring;
                    } 
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
                        isRoaringStarted = false;
                        SwitchToBehaviourStateServerRpc((int)State.ChasingPlayer);
                    }
                    break;


                case (int)State.ChasingPlayer:
                    if (previousState != State.ChasingPlayer)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [ChasingPlayer]");
                        agent.speed = defaultSpeed * 2f;
                        previousState = State.ChasingPlayer;
                        movingTowardsTargetPlayer = true;
                        SetWalkingAnimationServerRpc(agent.speed);
                        //targetPlayer.JumpToFearLevel(0.5f);
                    }
                    if (targetPlayer != null)
                    {
                        SetDestinationToPosition(targetPlayer.transform.position);
                        //HandleBraking();
                        CheckForPlayersInRangeOfGrabAttackServerRpc();
                        if (targetPlayer.isInHangarShipRoom)
                        {
                            targetPlayer = null;
                            suspicionLevel = maxSuspicionLevel / 100 * 60;
                            timeSinceLostPlayer = 0.0f;
                            LogIfDebugBuild($"JPOGTrex: TargetPlayer is inside the ship, stopping chase");
                            SwitchToBehaviourServerRpc((int)State.SearchingForPlayer);
                        }
                    }
                    if (targetPlayer != null)
                    {
                        float heightDifference = Mathf.Abs(transform.position.y - targetPlayer.transform.position.y);
                        //LogIfDebugBuild($"JPOGTrex: Height Difference between targetPlayer: [{heightDifference}]");
                        if (heightDifference > stopChaseHeight)
                        {
                            targetPlayer = null;
                            isMovingTowardsLastKnownPosition = false;
                            suspicionLevel = maxSuspicionLevel / 100 * 60;
                            timeSinceLostPlayer = 0.0f;
                            LogIfDebugBuild($"JPOGTrex: TargetPlayer was too high to reach!");
                            SwitchToBehaviourServerRpc((int)State.SearchingForPlayer);
                        }
                    }
                    CheckIfPlayerIsTargetableServerRpc();
                    if (targetPlayer != null &&
                        (Vector3.Distance(transform.position, targetPlayer.transform.position) > visionRangeChase && !CheckLineOfSightForPosition(targetPlayer.transform.position)))
                    {
                        LogIfDebugBuild($"JPOGTrex: Stop Target Player distance = [{Vector3.Distance(transform.position, targetPlayer.transform.position)}] allowed distance = [70]");
                        lastKnownPositionTargetPlayer = targetPlayer.transform.position;
                        targetPlayer = null;
                        isMovingTowardsLastKnownPosition = true;
                        LogIfDebugBuild($"JPOGTrex: Moving to last know positon of the targetPlayer");
                        SetDestinationToPosition(lastKnownPositionTargetPlayer);
                        return;
                    }
                    if(timeSinceLostPlayer >= maxSearchtime)
                    {
                        LogIfDebugBuild("JPOGTrex: Spent to long trying to reach last know position. returning to searching for player");
                        isMovingTowardsLastKnownPosition = false;
                        suspicionLevel = maxSuspicionLevel / 100 * 60;
                        timeSinceLostPlayer = 0.0f;
                        SwitchToBehaviourServerRpc((int)State.SearchingForPlayer);
                    }
                    if (isMovingTowardsLastKnownPosition && Vector3.Distance(transform.position, lastKnownPositionTargetPlayer) < 10f)
                    {
                        isMovingTowardsLastKnownPosition= false;
                        LogIfDebugBuild("JPOGTrex: Reached last known position. Checking for closest player in range"); 
                        if (!FoundClosestPlayerInRange())
                        {
                            LogIfDebugBuild("JPOGTrex: No players found. Return to searching for player.");
                            suspicionLevel = maxSuspicionLevel / 100 * 60;
                            StartSearch(transform.position);
                            SwitchToBehaviourStateServerRpc((int)State.SearchingForPlayer);
                            return;
                        }
                    }
                    if(targetPlayer == null && !isMovingTowardsLastKnownPosition)
                    {
                        LogIfDebugBuild("JPOGTrex: No players found in range. Return to searching for player.");
                        suspicionLevel= maxSuspicionLevel / 100 * 60;
                        SwitchToBehaviourStateServerRpc((int)State.SearchingForPlayer);
                    }
                    break;

                case (int)State.AttackingEntity:
                    previousState = State.AttackingEntity;
                    DoAnimationClientRpc("attackEnemy");
                    previousState = State.AttackingEntity;
                    break;

                case (int)State.GrabPlayer:
                    if (previousState != State.GrabPlayer)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [GrabPlayer]");
                        previousState = State.GrabPlayer;
                        if (timeSinceHittingLocalPlayer >= 4f)
                        {
                            beginningGrab = true;
                            StartCoroutine(BeginGrab());
                        }
                        if (targetPlayer != null)
                        {
                            //SetDestinationToPosition(targetPlayer.transform.position);
                            //HandleBraking();
                            if (targetPlayer.isInHangarShipRoom)
                            {
                                targetPlayer = null;
                                suspicionLevel = maxSuspicionLevel / 100 * 60;
                                timeSinceLostPlayer = 0.0f;
                                LogIfDebugBuild($"JPOGTrex: TargetPlayer is inside the ship, stopping chase");
                                SwitchToBehaviourServerRpc((int)State.SearchingForPlayer);
                            }
                        }
                    }
                    LogIfDebugBuild($"JPOGTrex: Checking if the grab attack has finished and if any player(s) are hit. | beginningGrb = [{beginningGrab}] | inGrabAttack = [{inGrabAttack}] | hitConnect = [{hitConnect}]");
                    if (!beginningGrab)
                    {
                        LogIfDebugBuild($"JPOGTrex: grab Attack finished checking if hit connected");
                        if (hitConnect)
                        {
                            LogIfDebugBuild("JPOGTrex: Hit connect, Switching to GrabbingPlayer");
                            agent.speed = defaultSpeed / 2;
                            SetWalkingAnimationServerRpc(agent.speed);
                            hitConnect = false;
                            SwitchToBehaviourStateServerRpc((int)State.GrabbingPlayer);
                        }
                        else if (!hitConnect && !inGrabAttack)
                        {
                            LogIfDebugBuild("JPOGTrex: Hit did not connect, returning to chasingPlayer");
                            hitConnect = false;
                            SwitchToBehaviourStateServerRpc((int)State.ChasingPlayer);
                        }
                    }
                    break;

                case (int)State.GrabbedPlayer:
                    DoAnimationClientRpc("grabbedPlayer");
                    previousState = State.GrabbedPlayer;
                    SwitchToBehaviourStateServerRpc((int)State.GrabbingPlayer);
                    break;

                case (int)State.GrabbingPlayer:
                    if (previousState != State.GrabbingPlayer)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [GrabbingPlayer]");
                        agent.speed = 0;
                        previousState = State.GrabbingPlayer;
                        movingTowardsTargetPlayer = false;
                        SetWalkingAnimationServerRpc(agent.speed);
                        //KillPlayerServerRpc((int)targetPlayer.playerClientId);
                        ShakingBodiesServerRpc();
                    }
                    if (!shakingBoddies && isHungry)
                    {
                        LogIfDebugBuild($"JPOGTrex: shakingBoddies = [{shakingBoddies}] || isHungry =[{isHungry}] || Switching to eat body");
                        SwitchToBehaviourStateServerRpc((int)State.EatingPlayer);
                    }
                    else if(!shakingBoddies && !isHungry)
                    {
                        LogIfDebugBuild($"JPOGTrex: shakingBoddies = [{shakingBoddies}] || isHungry =[{isHungry}] || dropping body");
                        DropcarriedBodyServerRpc();
                        SwitchToBehaviourStateServerRpc((int)State.SearchingForPlayer);
                    }
                    break;                   

                case (int)State.EatingPlayer:
                    if (previousState != State.EatingPlayer)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [EatingPlayer]");
                        agent.speed = 0f;
                        SetWalkingAnimationServerRpc(agent.speed);
                        previousState = State.EatingPlayer;
                    }
                    if (isHungry)
                    {
                        LogIfDebugBuild($"JPOGTrex: inEatingAnimation = [{inEatingAnimation}] || isHungry =[{isHungry}] || beginning to eat body");
                        EatPlayerServerRpc();
                    }
                    if (doneEating)
                    {
                        LogIfDebugBuild($"JPOGTrex: doneEating = [{doneEating}] || returning to SearchingForPlayer");
                        doneEating = false; 
                        suspicionLevel = maxSuspicionLevel / 100 * 20;
                        SwitchToBehaviourStateServerRpc((int)State.SearchingForPlayer);
                    }
                    break;

                case (int)State.Idle:
                    int rndIdle = enemyRandom.Next(4);
                    if (rndIdle == 1)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [Idle]");
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
                        SwitchToBehaviourStateServerRpc((int)State.Eating);
                        break;
                    }
                    break;

                case (int)State.Eating:
                    DoAnimationClientRpc("eatingIdle02");
                    previousState = State.Idle;
                    SwitchToBehaviourStateServerRpc((int)State.Eating);
                    break;

                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    isRoaringStarted = false;
                    SwitchToBehaviourStateServerRpc((int)State.SearchingForPlayer);
                    break;
            }
        }

        //Network Stuff
        [ServerRpc(RequireOwnership = false)]
        private void SwitchToBehaviourStateServerRpc(int state)
        {
            SwitchToBehaviourClientRpc(state);
        }

        [ServerRpc(RequireOwnership = false)]
        private void IncreaseSuspicionServerRpc()
        {
            IncreaseSuspicionClientRpc();
        }

        [ClientRpc]
        private void IncreaseSuspicionClientRpc()
        {
            IncreaseSuspicion();
        }


        [ServerRpc(RequireOwnership = false)]
        private void DecreaseSuspicionServerRpc()
        {
            DecreaseSuspicionClientRpc();
        }

        [ClientRpc]
        private void DecreaseSuspicionClientRpc()
        {
            DecreaseSuspicion();
        }

        [ServerRpc(RequireOwnership = false)]
        private void EatPlayerServerRpc()
        {
            EatPlayerClientRpc();
        }

        [ClientRpc]
        private void EatPlayerClientRpc()
        {
            EatPlayer();
        }

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
        private void CheckIfPlayerIsTargetableServerRpc()
        {
            CheckIfPlayerIsTargetableClientRpc();
        }

        [ClientRpc]
        private void CheckIfPlayerIsTargetableClientRpc()
        {
            CheckIfPlayerIsTargetable();
        }

        [ServerRpc(RequireOwnership = false)]
        private void CheckIfPlayerIsReachableServerRpc()
        {
            CheckIfPlayerIsReachableClientRpc();
        }

        [ClientRpc]
        private void CheckIfPlayerIsReachableClientRpc()
        {
            CheckIfPlayerIsReachable();
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


        [ServerRpc(RequireOwnership = false)]
        private void CheckLineOfSightDuringChaseServerRpc()
        {
            CheckLineOfSightDuringChaseClientRpc();
        }

        [ClientRpc]
        private void CheckLineOfSightDuringChaseClientRpc()
        {
            CheckLineOfSightDuringChase();
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetWalkingAnimationServerRpc(float speed)
        {
            SetWalkingAnimation(speed);
            //SetWalkingAnimationClientRpc(speed);
        }

        [ClientRpc]
        private void SetWalkingAnimationClientRpc(float speed)
        {

        }

        [ServerRpc(RequireOwnership = false)]
        private void SetBonesServerRpc()
        {
            SetBonesClientRpc();
        }

        [ClientRpc]
        private void SetBonesClientRpc()
        {
            SetBones();
        }

        [ClientRpc]
        private void UpdateMouthGripLocationToTargetBoneLocationClientRpc()
        {
            //This method updates the position and rotation of the mouth grip to match that of the mouth bone. Since it involves updating the transform of a GameObject
            //this operation should be consistent across all clients.
            UpdateMouthGripLocationToTargetBoneLocation();
        }

        [ServerRpc(RequireOwnership = false)]
        private void CheckForPlayersInRangeOfGrabAttackServerRpc()
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
        private void StartGrabCheckServerRpc()
        {
            inGrabAttack = true;
            StartGrabCheckClientRpc();
        }

        [ClientRpc]
        private void StartGrabCheckClientRpc()
        {
            StartCoroutine(CheckIfGrabHitDuringAnimation());
        }

        [ServerRpc(RequireOwnership = false)]
        private void StopGrabCheckServerRpc()
        {
            StopGrabCheckClientRpc();
        }

        [ClientRpc]
        private void StopGrabCheckClientRpc()
        {
            LogIfDebugBuild($"JPOGTrex: Calling to stop grab check");
            StopGrabCheckCoroutine();
        }

        [ServerRpc(RequireOwnership = false)]
        public void GrabAttackHitServerRpc()
        {
            GrabAttackHit();
        }

        [ClientRpc]
        public void CancelKillAnimationWithPlayerClientRpc(int playerObjectId)
        {
            LogIfDebugBuild($"JPOGTrex: CancelKillAnimationWIthPlayer");
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
        }

        [ServerRpc]
        private void ShakingBodiesServerRpc()
        {
            ShakingBodiesClientRpc();
        }

        [ClientRpc]
        private void ShakingBodiesClientRpc()
        {
            ShakkingBodies();
        }

        [ServerRpc(RequireOwnership = false)]
        public void KillPlayerServerRpc(int playerId)
        {
            LogIfDebugBuild($"JPOGTrex: Checking if in killAnimation || inKillAnimation ={inKillAnimation}");
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
            LogIfDebugBuild($"JPOGTrex: Checking KillPlayerCoroutine");
            if (killPlayerCoroutine != null)
            {
                LogIfDebugBuild($"JPOGTrex: In killPlayerCoroutine!, stopping previous killPlayerCoroutine!");
                StopCoroutine(killPlayerCoroutine);
            }
            LogIfDebugBuild($"JPOGTrex: start Killing player [{playerId}] rpc");
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

        bool TargetClosestPlayerInAnyCase()
        {
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

        private bool FoundClosestPlayerInRange(float range = 15f, float senseRange = 5f)
        {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true, 60f);
            if (targetPlayer == null)
            {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            if (targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range && !targetPlayer.isInHangarShipRoom)
            {
                LogIfDebugBuild("JPOGTrex: Spotted player in close proximity");
                foundPlayerInCloseProx = true;  
                return true;
            }
            else
            {
                targetPlayer = null;
                return false;
            }
        }


        private void CheckLineOfSightDuringChase()
        {
            PlayerControllerB[] allPlayersInLineOfSight = GetAllPlayersInLineOfSight(visionRangeWidth, visionRangeChase, eye, 3f, StartOfRound.Instance.collidersRoomDefaultAndFoliage);
            if (allPlayersInLineOfSight != null)
            {
                LogIfDebugBuild($"JPOGTrex: Checking LOS in chase. allPlayersInLineOfSight length: {allPlayersInLineOfSight.Length}");
                LogIfDebugBuild($"JPOGTrex: Checking LOS in chase. allPlayerScripts length: {StartOfRound.Instance.allPlayerScripts.Length}");
                foreach (var playerControllerB in allPlayersInLineOfSight)
                {
                    // Perform null checks on playerControllerB and targetPlayer
                    if (playerControllerB == null || targetPlayer == null)
                    {
                        LogIfDebugBuild($"JPOGTrex: Checking LOS in chase. No targetPlayer or player in LOS");
                        continue;
                    }
                    LogIfDebugBuild($"JPOGTrex: Checking LOS in chase. targetPlayer's id = [{targetPlayer.playerClientId}] || player in Line of sight id = [{playerControllerB.playerClientId}]");
                    if (playerControllerB.playerClientId == targetPlayer.playerClientId)
                    {
                        LogIfDebugBuild($"JPOGTrex: Target player and player in LOS matched, staying on target.");
                        targetPlayer = playerControllerB;
                        continue;
                    }
                }
                if (targetPlayer != null && !IsPlayerInLineOfSight(targetPlayer))
                {
                    LogIfDebugBuild($"JPOGTrex: Target player and player in LOS matched, staying on target.");
                    targetPlayer = null;
                }
            }
        }
        private bool IsPlayerInLineOfSight(PlayerControllerB player)
        {
            return CheckLineOfSightForPosition(player.transform.position);
        }
        private void IncreaseSuspicion()
        {
            suspicionLevel = Mathf.Clamp(suspicionLevel + increasRateSuspicion, 0, maxSuspicionLevel);
            timeSinceSeeingPlayerMove = 0;
            LogIfDebugBuild($"JPOGTrex: Suspicion level increased. New value = [{suspicionLevel}]");
        }
        private void DecreaseSuspicion()
        {
            if(Time.time - lastSuspicionDecreaseTime >= decreaseSuspicionTimer)
            {
                suspicionLevel = Mathf.Clamp(suspicionLevel - decreaseRateSuspicion, 0, maxSuspicionLevel);
                lastSuspicionDecreaseTime = Time.time;
                LogIfDebugBuild($"JPOGTrex: Suspicion level decreased. New value = [{suspicionLevel}]");
            }
        }

        private void CheckLineOfSight()
        {
            PlayerControllerB[] allPlayersInLineOfSight = GetAllPlayersInLineOfSight(visionRangeWidth, visionRangeSearching, eye, 3f, StartOfRound.Instance.collidersRoomDefaultAndFoliage);
            if (allPlayersInLineOfSight != null)
            {
                //LogIfDebugBuild($"JPOGTrex: allPlayersInLineOfSight length: {allPlayersInLineOfSight.Length}"); 
                //LogIfDebugBuild($"JPOGTrex: allPlayerScripts length: {StartOfRound.Instance.allPlayerScripts.Length}");
                //LogIfDebugBuild("Looking for moving players in line of sight");
                foreach (var playerControllerB in allPlayersInLineOfSight)
                {
                    if (playerControllerB != null && !playerControllerB.isPlayerDead && !isInsidePlayerShip)
                    {
                        //LogIfDebugBuild($"JPOGTrex: Checking player with ID {playerControllerB.playerClientId}");
                        if (CheckIfPlayerIsmoving(playerControllerB))
                        {
                            IncreaseSuspicionServerRpc();
                            LogIfDebugBuild($"JPGOTrex: Saw player [{playerControllerB.playerClientId}] moving");
                            movingPlayer = playerControllerB;
                            timeSinceSeeingPlayerMove += Time.deltaTime;
                            lastSuspicionDecreaseTime = Time.time; // Reset decrease time to prevent immediate decrease

                            //TODO add adrinaline effect to players that are seen moving
                            //TrexSeePlayerEffect();
                            break;
                        }                        
                    }
                }
            }
        }

        //Check wether this will need an Rpc or not
        private bool CheckIfPlayerIsmoving(PlayerControllerB playerToCheck)
        {
            //LogIfDebugBuild($"JPOGTrex: Checking if player {playerToCheck.playerClientId} is moving");
            localPlayerTurnDistance += StartOfRound.Instance.playerLookMagnitudeThisFrame;
            if (localPlayerTurnDistance > 0.1f && Vector3.Distance(playerToCheck.transform.position, base.transform.position) < 10f)
            {
                //LogIfDebugBuild($"JPOGTrex: Player {playerToCheck.playerClientId} is moving (turn distance)");
                return true;
            }
            if (playerToCheck.performingEmote)
            {
                //LogIfDebugBuild($"JPOGTrex: Player {playerToCheck.playerClientId} is moving (performing emote)");
                return true;
            }
            if (Time.realtimeSinceStartup - StartOfRound.Instance.timeAtMakingLastPersonalMovement < 0.25f)
            {
                //LogIfDebugBuild($"JPOGTrex: Player {playerToCheck.playerClientId} is moving (recent personal movement)");
                return true;
            }
            if (playerToCheck.timeSincePlayerMoving < 0.05f)
            {
                //LogIfDebugBuild($"JPOGTrex: Player {playerToCheck.playerClientId} is moving (recent movement)");
                return true;
            }
            //LogIfDebugBuild($"JPOGTrex: Player {playerToCheck.playerClientId} is not moving");
            return false;
        }    


        //Checks if the player is in range for an attack
        private void CheckForPlayersInRangeOfGrabAttack()
        {
            //LogIfDebugBuild("JPOGTrex: Checking if Player can be grabbed");
            if(movingPlayer != null || targetPlayer != null)
            {
                float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);
                if(distanceToPlayer <= attackRange)
                {
                    //LogIfDebugBuild($"JPOGTrex: Grab attack can hit player [{targetPlayer.playerClientId}]!");
                    SwitchToBehaviourStateServerRpc((int)State.GrabPlayer);
                }
                else
                {
                    //LogIfDebugBuild("JPOGTrex: No players to hit!");
                }
            }
            else
            {
                LogIfDebugBuild("JPOGTrex: No target player assigned!");
            }
        }
        //Check is if the target is valid or not
        private void CheckIfPlayerIsTargetable()
        {
            if (targetPlayer != null)
            {
                if (!PlayerIsTargetable(targetPlayer, false))
                {
                    LogIfDebugBuild($"JPOGTrex: Player [{targetPlayer.playerClientId}] is no longer targetable");
                    targetPlayer = null;
                    suspicionLevel = 60;
                    SwitchToBehaviourStateServerRpc((int)State.SearchingForPlayer);
                    return;
                }
            }
        }

        //Checks if the player's position can be reached.
        //Needs work
        private void CheckIfPlayerIsReachable()
        {

            if (targetPlayer != null)
            {
                if (!agent.CalculatePath(targetPlayer.transform.position, path1))
                {
                    LogIfDebugBuild($"JPOGTrex: The position of Player [{targetPlayer.playerClientId}] is not reachable");
                    LogIfDebugBuild($"JPOGTrex: Path = [{path1.status}]");
                    targetPlayer = null;
                    suspicionLevel = 60;
                    SwitchToBehaviourStateServerRpc((int)State.SearchingForPlayer);
                    return;
                }
            }
        }

        //Checks if the attack should hit the player
        public void GrabAttackHit()
        {
            LogIfDebugBuild("JPOGTrex: Checking if Grab attack hit");
            float playerIsHittable = 10f;
            if(targetPlayer != null)
            {
                float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);
                LogIfDebugBuild($"JPOGTrex: Distance to player = [{distanceToPlayer}]");
                if (distanceToPlayer <= playerIsHittable)
                {

                    LogIfDebugBuild($"JPOGTrex: The target Player distance = [{distanceToPlayer}] || Required distance to hit = [{playerIsHittable}]");
                    LogIfDebugBuild($"JPOGTrex: grab attack has hit player: [{targetPlayer.playerClientId}]!");
                    timeSinceHittingLocalPlayer = 0f;
                    hitConnect = true;
                }
                else
                {
                    LogIfDebugBuild("Grab attack hit 0 players");
                    hitConnect = false;
                }
            }
            else
            {
                LogIfDebugBuild("JPOGTrex: No target player assigned!");
            }
        }

        public void OnAnimationEventStartGrabCheck()
        {
            if (IsOwner)
            {
                inGrabAttack = true;
                StartCoroutine(CheckIfGrabHitDuringAnimation());
                StartGrabCheckServerRpc();
            }
        }
        public void OnAnimationEventEndGrabCheck()
        {
            if (IsOwner)
            {
                inGrabAttack = false;
                StopGrabCheckCoroutine();
                StopGrabCheckServerRpc();
            }
        }

        private void StopGrabCheckCoroutine()
        {
            inGrabAttack = false;
            LogIfDebugBuild("JPOGTrex: Stopping grab check Coroutine");
            StopCoroutine(CheckIfGrabHitDuringAnimation());
        }

        //Checks if the attack should hit the player (during the animation of the grab/bite)
        public void GrabAttackHitAnimation()
        {
            //LogIfDebugBuild("JPOGTrex: Checking if Grab attack animation hit player(s)");
            int playerLayer = 1 << 3;
            Collider[] hitColliders = Physics.OverlapBox(mouthAttackHitBox.position, mouthAttackHitBox.localScale, Quaternion.identity, playerLayer);
            bool hitPlayer = false;
            if (hitColliders.Length > 0)
            {
                foreach (var player in hitColliders)
                {
                    PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);                    
                    if (playerControllerB != null)
                    {
                        int playerId = (int)playerControllerB.playerClientId;
                        LogIfDebugBuild($"JPOGTrex: Checking if player: [{playerId}] is not yet hit");
                        if (!hitPlayerIds.Contains((int)playerControllerB.playerClientId))
                        {
                            LogIfDebugBuild($"JPOGTrex: Grab Attack Hit player: [{playerControllerB.playerClientId}]");
                            timeSinceHittingLocalPlayer = 0f;
                            hitPlayer = true;
                            LogIfDebugBuild($"JPOGTrex: hitPlayer = [{hitPlayer}]");
                            LogIfDebugBuild($"JPOGTrex: calling to kill player: [{playerControllerB.playerClientId}]");
                            KillPlayerServerRpc((int)playerControllerB.playerClientId);
                            hitPlayerIds.Add((int)playerControllerB.playerClientId);
                        }
                        else
                        {
                            LogIfDebugBuild($"JPOGTrex: Player [{playerId}] has already been hit");
                        }
                    }
                    else
                    {
                        LogIfDebugBuild("JPOGTrex: PlayerControllerB is null or player does not meet collision conditions");
                    }
                }
            }
            if (hitPlayer)
            {
                LogIfDebugBuild($"JPOGTrex: hitPlayer = [{hitPlayer}] > setting hitConnect to true");
                SetHitConnectToTrueServerRpc();
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetHitConnectToTrueServerRpc()
        {
            SetHitConnectToTrueClientRpc();
        }
        [ClientRpc]
        private void SetHitConnectToTrueClientRpc()
        {
            SetHitConnectToTrue();
        }

        private void SetHitConnectToTrue()
        {
            hitConnect = true;
            LogIfDebugBuild($"JPOGTrex: hitConnect = [{hitConnect}]");
        }

        //Player body Interaction
        private void TakeBodyInMouth(int playerId)
        {
            LogIfDebugBuild($"JPOGTrex: Taking boddy of player [{playerId}] in mouth");
            DeadBodyInfo killedPlayerBody = StartOfRound.Instance.allPlayerScripts[playerId].deadBody;
            LogIfDebugBuild($"JPOGTrex: deadbody of player cause of death: [{killedPlayerBody.causeOfDeath}]");
            if (killedPlayerBody != null)
            {
                killedPlayerBody.attachedTo = mouthGrip;
                killedPlayerBody.attachedLimb = killedPlayerBody.bodyParts[5];
                killedPlayerBody.matchPositionExactly = true;
                killedPlayerBody.MakeCorpseBloody();

                //The T-rex Should be able to multikill so all bodies it grabs are added to the list

                LogIfDebugBuild($"JPOGTrex: Adding dead body to carryingBodies");
                carryingBodies.Add(killedPlayerBody);
                LogIfDebugBuild($"JPOGTrex: carryingBodies count = [{carryingBodies.Count}]");
            }
        }
        private void HandleBraking()
        {
            if (targetPlayer != null)
            {
                float distanceToTarget = Vector3.Distance(agent.transform.position, targetPlayer.transform.position);

                if (distanceToTarget < stoppingThreshold)
                {
                    LogIfDebugBuild($"JPOGTrex: Target within Stopping treshold enabling auto braking");
                    agent.stoppingDistance = 7;
                    agent.autoBraking = true;
                    LogIfDebugBuild($"JPOGTrex:Auto Braking was set to: [{agent.autoBraking}]");
                    SetWalkingAnimationServerRpc(agent.speed);
                }
                else
                {
                    LogIfDebugBuild($"JPOGTrex: Target outside stopping treshold disabling auto braking");
                    agent.stoppingDistance = 0;
                    agent.autoBraking = false;
                    LogIfDebugBuild($"JPOGTrex:Auto Braking was set to: [{agent.autoBraking}]");
                    agent.speed = defaultSpeed * 2f;
                    SetWalkingAnimationServerRpc(agent.speed);
                }
            }
        }

        private void DropCarriedBody()
        {
            LogIfDebugBuild($"JPOGTrex: Checking if Trex has bodies in mouth");
            if (carryingBodies.Count > 0)
            {
                LogIfDebugBuild($"JPOGTrex: Caryring [{carryingBodies.Count}] bodies in mouth");
                //All grabbed bodies should be dropped by the Trex
                foreach (var carryingBody in carryingBodies)
                {
                    if (carryingBody == null)
                    {
                        LogIfDebugBuild("JPOGTrex: Found a null body in the carryingBodies list!");
                        continue;
                    }
                    LogIfDebugBuild($"JPOGTrex: Dropping body from mouth");
                    carryingBody.speedMultiplier = 12f;
                    carryingBody.attachedTo = null;
                    carryingBody.attachedLimb = null;
                    carryingBody.matchPositionExactly = false;
                }
                //Clear the list of bodies bein
                carryingBodies.Clear();
            }
        }

        private void EatPlayer()
        {
            LogIfDebugBuild($"JPOGTrex: beginning to eat player(s)");

            LogIfDebugBuild($"JPOGTrex: `Checking inEatingAnimation = [{inEatingAnimation}] || Checking isHungry = [{isHungry}]");
            if (!inEatingAnimation && isHungry)
            {
                inEatingAnimation = true;
                //Starts the eating animation
                StartCoroutine(EatPlayerCoroutine());
            }
        }


        //Effects on player
        private void ShakeCamera()
        {
            PlayerControllerB playerController = GameNetworkManager.Instance.localPlayerController;
            float distance = Vector3.Distance(transform.position, playerController.transform.position);
            if (distance > 40f)
            {
                return;
            }
            else if (distance <= 5f)
            {
                HUDManager.Instance.ShakeCamera(ScreenShakeType.VeryStrong);
            }
            else if (distance > 5 && distance <= 20)
            {
                HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
            }
            else if (distance > 20f && distance <= 40f)
            {
                HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
            }
        }

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
        private void TrexSeePlayerEffect()
        {
            PlayerControllerB playerControllerB = GameNetworkManager.Instance.localPlayerController;
            if (playerControllerB == null) { 
                return;
            }
            if (playerControllerB.isPlayerDead || playerControllerB.isInsideFactory)
            {
                return;
            }
            if (playerControllerB == movingPlayer)
            {
                LogIfDebugBuild($"JPOGTrex: increasing fear level of player = [{playerControllerB.playerClientId}]");
                GameNetworkManager.Instance.localPlayerController.IncreaseFearLevelOverTime(0.2f);
                return;
            }
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
            if (timeSinceHittingLocalPlayer < 1f)
            {
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null && !playerControllerB.isInHangarShipRoom)
            {
                LogIfDebugBuild("JPOGTrex: Collision with Player!");
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.DamagePlayer(20);
            }
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null)
        {
            base.OnCollideWithEnemy(other, collidedEnemy);
            if (isEnemyDead)
            {
                return;
            }
            if (!collidedEnemy.isEnemyDead && CheckIfInNotAnimation())
            {
                targetEntity = collidedEnemy;
                LookAtTargetEntityServerRpc();
                if (collidedEnemy.enemyType.SizeLimit == NavSizeLimit.SmallSpaces)
                {
                    StartCoroutine(AttackEnemyLow());
                }
                else if(collidedEnemy.enemyType.SizeLimit == NavSizeLimit.MediumSpaces)
                {
                    StartCoroutine(AttackEnemyHigh());
                }
                else
                {
                    StartCoroutine(AttackEnemyLow());
                }
            }
        }

        public bool CheckIfInNotAnimation()
        {
            if(!inGrabAttack && !inKillAnimation && !inEatingAnimation && !beginningGrab && !inAttackEnemyAnimation)
            {
                return true;
            }
            else
            {
                return false;
            }
        }


        public IEnumerator AttackEnemyLow()
        {
            inAttackEnemyAnimation = true;
            DoAnimationClientRpc("attackEnemyLow");
            yield return new WaitForSeconds(1.2f);
            if (targetEntity != null)
            {
                if(targetEntity.enemyType)
                targetEntity.HitEnemy(15,null, true, -1);
            }
            inAttackEnemyAnimation = false;
            yield break;
        }
        public IEnumerator AttackEnemyHigh()
        {
            inAttackEnemyAnimation = true;
            DoAnimationClientRpc("attackEnemyHigh");
            yield return new WaitForSeconds(1.2f);
            if (targetEntity != null)
            {
                targetEntity.HitEnemy(10, null, true, -1);
            }
            inAttackEnemyAnimation = false;
            yield break;
        }

        [ServerRpc(RequireOwnership = false)]
        private void LookAtTargetEntityServerRpc()
        {
            LookAtTargetEntityClientRpc();
        }
        [ClientRpc]
        private void LookAtTargetEntityClientRpc()
        {
            LookAtTargetEntity();
        }
        private void LookAtTargetEntity()
        {
            if (targetEntity != null)
            {
                turnCompass.LookAt(targetEntity.transform.position);
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            //If the T-rex is dead nothing happens
            if (isEnemyDead)
            {
                return;
            }
            enemyHP -= force;
            if (enemyHP > 0)
            {
                //The T-rex should not suddenly start chasing a player during "important" animations such as eating or grabbing the player 
                if (previousBehaviourStateIndex == (int)State.EatingPlayer || previousBehaviourStateIndex == (int)State.GrabbingPlayer || previousBehaviourStateIndex == (int)State.GrabPlayer)
                {
                    return;
                }
                targetPlayer = playerWhoHit;
                if (previousBehaviourStateIndex != (int)State.ChasingPlayer)
                {
                    suspicionLevel = maxSuspicionLevel;
                    SwitchToBehaviourServerRpc((int)State.ChasingPlayer);
                }
            }
        }

/*        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null)
        {
            LogIfDebugBuild($"JPOGTrex: Collided with enemy [{collidedEnemy.name}]");
            base.OnCollideWithEnemy(other, collidedEnemy);
            if (!collidedEnemy.isEnemyDead && !InAttackOnEnemyLow) {
                BeginAttackOnEnemyLow();
                collidedEnemy.HitEnemy(10);
            
            }
        }*/

/*        private IEnumerator BeginAttackOnEnemyLow()
        {
            InAttackOnEnemyLow = true;
            DoAnimationClientRpc("attackEnemyLow");
            yield return new WaitForSeconds(1.2f);
            InAttackOnEnemyLow = false;
        }*/

        private void ShakkingBodies()
        {
            if (!shakingBoddies)
            {
                shakingBoddies = true;
                StartCoroutine(ShakeGrabbedBody());
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            LogIfDebugBuild("JPOGTrex: T-rex has been killed");
            SetWalkingAnimationServerRpc(0f);
            DoAnimationClientRpc("killEnemy");
            creatureVoice.Stop();
            creatureSFX.Stop();
            base.KillEnemy(destroy);
        }

        //Some Utility stuff

        //Simple method that sets the walking animation of the T-rex based on it's speed.
        //This way a more slowed down walking animation or sped up running animation can be applied.
        //Should be called after the speed of the current behaviour state has been set.
        //This should make it more configurable
        private void SetWalkingAnimation(float currentSpeed)
        {
           if(currentSpeed == previousSpeed)
            {
                return;
            }
            if (currentSpeed == 0f)
            {
                LogIfDebugBuild($"JPOGTrex: Speed = [{currentSpeed}] || animation = [stop walking]");
                DoAnimationClientRpc("stopWalk");
                previousSpeed = currentSpeed;
                return;
            }
            else if (currentSpeed > 2f && currentSpeed <= 6f)
            {
                LogIfDebugBuild($"JPOGTrex: Speed = [{currentSpeed}] || animation = [walking]");
                DoAnimationClientRpc("startWalk");
                previousSpeed = currentSpeed;
                return;
            }
            else if (currentSpeed > 6f && currentSpeed <= 12f)
            {
                LogIfDebugBuild($"JPOGTrex: Speed = [{currentSpeed}] || animation = [running]");
                DoAnimationClientRpc("chasingRun");
                previousSpeed = currentSpeed;
                return;
            }
            else if (currentSpeed > 0f && currentSpeed <= 2f)
            {
                LogIfDebugBuild($"JPOGTrex: Speed = [{currentSpeed}] || animation = [walking-slow]");
                DoAnimationClientRpc("slowDown");
                previousSpeed = currentSpeed;
                return;
            }
            else if (currentSpeed > 12f)
            {
                LogIfDebugBuild($"JPOGTrex: Speed = [{currentSpeed}] || animation = [running-fast]");
                DoAnimationClientRpc("speedUp");
                previousSpeed = currentSpeed;
                return;
            }
        }

        //Loads the model and calls the methods to set the mouthgrip with the bone.
        private void SetBones()
        {
            modelD = GameObject.Find("D");
            if (modelD != null)
            {
                LogIfDebugBuild($"trying to find target bone for the mouthBone from: [{modelD}]");
                mouthBone = FindChildRecursive(modelD.transform, "Mouth");
                if (mouthBone != null)
                {
                    LogIfDebugBuild($"found targetbone {mouthBone.name}");
                }
                else
                {
                    LogIfDebugBuild("no targetbone found!");
                }
            }
        }
        //Search Through the model to find the bone that will be used to update Mouthgrip's transform
        private Transform? FindChildRecursive(Transform parent, string childName)
        {
            LogIfDebugBuild($"Child name to search for: [{childName}]");
            foreach (Transform child in parent)
            {
                LogIfDebugBuild($"{child.name}");
                if (child.name == childName)
                {
                    LogIfDebugBuild($"found matching bone:[{child.name}] + [{childName}]");
                    return child;
                }
                Transform? found = FindChildRecursive(child, childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
        //This is should update the mouthgrip transform of the Trex to transform of the bone.
        //Effectively Making it look like the player's body is attached/grabbed by the T-rex's mouth during the animation instead of blinking/warping to the static location of the mouthgrip as seen in Unity
        //Making this more generic could make it usefull to add a collider/hitbox for the mouth during the animation, this way we can check if the player is hit during the animation and killing them.
        //This should make the kill feel smoother instead of a delayed death because you were in a collision box at some point.
        private void UpdateMouthGripLocationToTargetBoneLocation()
        {
            if(mouthBone != null)
            {
                mouthGrip.transform.position = mouthBone.transform.position;
                mouthGrip.transform.rotation = mouthBone.transform.rotation;
            }
        }

        //These methods are called during certain frames in the animation
        private void PlayAudioClip(AudioClip audioClip)
        {
            //LogIfDebugBuild("JPOGTrex: Playing audio clip through CreatureVoice");
            creatureVoice.PlayOneShot(audioClip);
            WalkieTalkie.TransmitOneShotAudio(creatureVoice, audioClip);
        }
        private void PlayFootStepAudioClip(AudioClip audioClip)
        {
            //LogIfDebugBuild("JPOGTrex: Playing audio clip through CreatureSFX");
            creatureSFX.PlayOneShot(audioClip);
            WalkieTalkie.TransmitOneShotAudio(creatureSFX, audioClip);
            ShakeCamera();
        }
        private void PlayTrexRoarAudioClipt(AudioClip audioClip)
        {
            //LogIfDebugBuild("JPOGTrex: Playing audio clip through TrexRoarSFX");
            trexRoarSFX.PlayOneShot(audioClip, 2f);
            WalkieTalkie.TransmitOneShotAudio(trexRoarSFX, audioClip);
        }
        private void PlayTrexSubRoarAudioClipt(AudioClip audioClip)
        {
            //LogIfDebugBuild("JPOGTrex: Playing audio clip through TrexSubRoarSFX");
            trexSubRoarSFX.PlayOneShot(audioClip, 1f);
            WalkieTalkie.TransmitOneShotAudio(trexRoarSFX, audioClip);
        }


        //Coroutines

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
            StartGrabCheckServerRpc();
            LogIfDebugBuild("JPOGTrex: Beginning grab Attack!");
            DoAnimationClientRpc("grabPlayer");
            //GrabAttackHitServerRpc();
            DoAnimationClientRpc("grabbedPlayer");
            yield return new WaitForSeconds(1.2f);
            StopGrabCheckServerRpc();
            LogIfDebugBuild("JPOGTrex: grab attack finished stting grabAttack to false");
            beginningGrab = false;
            LogIfDebugBuild($"JPOGTrex: beginningGrab = [{beginningGrab}]");
            LogIfDebugBuild("JPOGTrex: grab attack finished clearing hitPlayerIds list");
            hitPlayerIds.Clear();
            yield break;
        }

        private IEnumerator CheckIfGrabHitDuringAnimation()
        {

            LogIfDebugBuild($"JPOGTrex: Beginning CheckIfGrabHitDuringAnimation Coroutine");
            inGrabAttack = true;
            while (inGrabAttack) {
                GrabAttackHitAnimation();
                yield return null;
            }
            LogIfDebugBuild($"JPOGTrex: CheckIfGrabHitDuringAnimation Coroutine has Ended | inGrabAttack = [{inGrabAttack}]");
        }

        private IEnumerator ShakeGrabbedBody()
        {
            DoAnimationClientRpc("grabbingPlayer");
            yield return new WaitForSeconds(2.6f);
            shakingBoddies = false;
            yield break;
        }

        private IEnumerator EatPlayerCoroutine()
        {
            DoAnimationClientRpc("eatingPlayer");
            yield return new WaitForSeconds(5.7f);
            //bodyToEat.DeactivateBody(false);
            inEatingAnimation = false;
            isHungry = false;
            if (carryingBodies != null && carryingBodies.Count > 0)
            {
                foreach (var carryingBody in carryingBodies)
                {
                    LogIfDebugBuild($"JPOGTrex: Deactivating body = [{carryingBody.playerObjectId}]");
                    carryingBody.DeactivateBody(false);
                    playersEaten++;
                }
                //Clears the list to make sure no bodies remain
                LogIfDebugBuild($"JPOGTrex: Clearing CarryingBodies list");
                carryingBodies.Clear();
            }
            doneEating = true;
            yield break;
        }

        private IEnumerator KillPlayer(int playerId)
        {
            if (IsOwner)
            {
                agent.speed = Mathf.Clamp(agent.speed, 2f, 0f);
            }
            LogIfDebugBuild($"JPOGTrex: begin Killing player: [{playerId}]");
            PlayerControllerB killPlayer = StartOfRound.Instance.allPlayerScripts[playerId];
            if (killPlayer == null || killPlayer.isPlayerDead)
            {
                LogIfDebugBuild($"JPOGTrex: Player [{playerId}] is not valid or already dead.");
                yield break;
            }
            if (!isEnemyDead)
            {
                LogIfDebugBuild("JPOGTrex: T-rex is still alive, killing player Continues");
                //DoAnimationClientRpc("killEnemy");
            }
            if (GameNetworkManager.Instance.localPlayerController == killPlayer)
            {
                int killAnimation = enemyRandom.Next(0, 2) == 0 ? 1 : 7;
                killPlayer.KillPlayer(Vector3.zero, spawnBody: true, CauseOfDeath.Mauling, killAnimation);
            }
            float startTime = Time.timeSinceLevelLoad;
            yield return new WaitUntil(() => killPlayer.deadBody != null || Time.timeSinceLevelLoad - startTime > 2f);
            if (killPlayer.deadBody == null)
            {
                LogIfDebugBuild("JPOGTrex: Player body was not spawned or found withing 2 seconds");
                killPlayer.inAnimationWithEnemy = null;
                inKillAnimation = false;
                targetPlayer = null;
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
            inKillAnimation = false;
            killPlayerCoroutine = null;
            yield break;
        }

        public GrabbableObject GetHeldObject()
        {
            throw new System.NotImplementedException();
        }

        public bool IsThreatDead()
        {
            throw new System.NotImplementedException();
        }
    }
}