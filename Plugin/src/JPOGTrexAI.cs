using System.Collections;
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
#pragma warning restore 0649
        private List<DeadBodyInfo> carryingBodies = new List<DeadBodyInfo>();
        private List<int> hitPlayerIds = new List<int>();
        private Coroutine? killPlayerCoroutine;
        private Coroutine? grabCheckCoroutine;
        private Coroutine? shakeCoroutine;
        private Coroutine? eatCoroutine;
        private float timeSinceHittingLocalPlayer;
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
        private PlayerControllerB? movingPlayer;
        private float localPlayerTurnDistance;
        private float localSightCheckTimer;
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
        private float decreaseSuspicionTimer;
        private float timeSinceLostPlayer = 0.0f;
        private float maxSearchtime = 20.0f;
        private float stopChaseHeight = 12f;
        private float previousSpeed;
        private Vector3 lastKnownPositionTargetPlayer;
        private bool isMovingTowardsLastKnownPosition;
        private bool inGrabAttack;
        private int maxPlayersToEat;
        private int playersEaten = 0;
        private float lastSuspicionDecreaseTime;
        private bool inAttackEnemyAnimation = false;
        private EnemyAI? targetEntity = null;
        private bool wasStunned;
        private float speedBeforeStun;

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

        public GrabbableObject? GetHeldObject()
        {
            return null;
        }

        public bool IsThreatDead()
        {
            return isEnemyDead;
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
            LogIfDebugBuild("JPOGTrex Spawned");
            timeSinceHittingLocalPlayer = 0;
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;
            if (IsOwner)
            {
                SetWalkingAnimationServerRpc(defaultSpeed);
                SwitchToBehaviourState((int)State.SearchingForPlayer);
            }
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
            if (isMovingTowardsLastKnownPosition)
            {
                timeSinceLostPlayer += Time.deltaTime;
            }
            timeSinceHittingLocalPlayer += Time.deltaTime;

            //Every client checks if its own player is seen moving, the owner only receives the result
            if (currentBehaviourStateIndex == (int)State.SearchingForPlayer)
            {
                localSightCheckTimer -= Time.deltaTime;
                if (localSightCheckTimer <= 0f)
                {
                    localSightCheckTimer = AIIntervalTime;
                    CheckLocalPlayerMovingInSight();
                }
            }

            if (!IsOwner)
            {
                return;
            }

            var state = currentBehaviourStateIndex;
            if (targetPlayer != null && (state == (int)State.GrabPlayer || state == (int)State.GrabbedPlayer || state == (int)State.GrabbingPlayer || state == (int)State.EatingPlayer || state == (int)State.SpottedPlayer)) {
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
            }
            if (stunNormalizedTimer > 0f)
            {
                if (!wasStunned)
                {
                    wasStunned = true;
                    speedBeforeStun = agent.speed;
                    agent.speed = 0f;
                    SetWalkingAnimationServerRpc(agent.speed);
                }
            }
            else if (wasStunned)
            {
                wasStunned = false;
                agent.speed = speedBeforeStun;
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
                        isMovingTowardsLastKnownPosition = false;
                        timeSinceLostPlayer = 0.0f;
                        agent.speed = defaultSpeed;
                        agent.autoBraking = true;
                        SetWalkingAnimationServerRpc(agent.speed);
                        StartSearch(transform.position);
                        previousState = State.SearchingForPlayer;
                    }
                    if (FoundClosestPlayerInRange())
                    {
                        LogIfDebugBuild("JPOGTrex: Beginning chase for player in close proximity");
                        StopSearch(currentSearch);
                        SwitchToBehaviourState((int)State.SpottedPlayer);
                        break;
                    }
                    DecreaseSuspicion();
                    if (movingPlayer != null && suspicionLevel >= maxSuspicionLevel)
                    {
                        targetPlayer = movingPlayer;
                        movingPlayer = null;
                        StopSearch(currentSearch);
                        SwitchToBehaviourState((int)State.SpottedPlayer);
                    }
                    break;

                case (int)State.SpottedPlayer:
                    if (previousState != State.SpottedPlayer && !isSniffingStarted)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [SpottedPlayer]");
                        agent.speed = 2f;
                        sniffing = true;
                        SetWalkingAnimationServerRpc(agent.speed);
                        StartCoroutine(FoundPlayer());
                        isSniffingStarted = true;
                        previousState = State.SpottedPlayer;
                    }
                    if (sniffing == false)
                    {
                        isSniffingStarted = false;
                        SwitchToBehaviourState((int)State.Roaring);
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
                    if (previousState != State.Roaring && !isRoaringStarted)
                    {
                        roaring = true;
                        LogIfDebugBuild("JPOGTrex: Roaring at Player!");
                        StartCoroutine(BeginChase(enemyRandom.Next(1, 4)));
                        isRoaringStarted = true;
                    }
                    if (roaring == false)
                    {
                        isRoaringStarted = false;
                        SwitchToBehaviourState((int)State.ChasingPlayer);
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
                    }
                    if (targetPlayer != null)
                    {
                        SetDestinationToPosition(targetPlayer.transform.position);
                        if (CheckForPlayersInRangeOfGrabAttack())
                        {
                            break;
                        }
                        if (targetPlayer.isInHangarShipRoom)
                        {
                            LogIfDebugBuild("JPOGTrex: TargetPlayer is inside the ship, stopping chase");
                            StopChase();
                            break;
                        }
                        if (Mathf.Abs(transform.position.y - targetPlayer.transform.position.y) > stopChaseHeight)
                        {
                            LogIfDebugBuild("JPOGTrex: TargetPlayer was too high to reach!");
                            StopChase();
                            break;
                        }
                    }
                    if (!CheckIfPlayerIsTargetable())
                    {
                        break;
                    }
                    if (targetPlayer != null &&
                        (Vector3.Distance(transform.position, targetPlayer.transform.position) > visionRangeChase && !CheckLineOfSightForPosition(targetPlayer.transform.position)))
                    {
                        LogIfDebugBuild($"JPOGTrex: Stop Target Player distance = [{Vector3.Distance(transform.position, targetPlayer.transform.position)}] allowed distance = [{visionRangeChase}]");
                        lastKnownPositionTargetPlayer = targetPlayer.transform.position;
                        targetPlayer = null;
                        isMovingTowardsLastKnownPosition = true;
                        timeSinceLostPlayer = 0.0f;
                        LogIfDebugBuild("JPOGTrex: Moving to last know positon of the targetPlayer");
                        SetDestinationToPosition(lastKnownPositionTargetPlayer);
                        break;
                    }
                    if (timeSinceLostPlayer >= maxSearchtime)
                    {
                        LogIfDebugBuild("JPOGTrex: Spent to long trying to reach last know position. returning to searching for player");
                        StopChase();
                        break;
                    }
                    if (isMovingTowardsLastKnownPosition && Vector3.Distance(transform.position, lastKnownPositionTargetPlayer) < 10f)
                    {
                        isMovingTowardsLastKnownPosition = false;
                        LogIfDebugBuild("JPOGTrex: Reached last known position. Checking for closest player in range");
                        if (!FoundClosestPlayerInRange())
                        {
                            LogIfDebugBuild("JPOGTrex: No players found. Return to searching for player.");
                            StopChase();
                            break;
                        }
                    }
                    if (targetPlayer == null && !isMovingTowardsLastKnownPosition)
                    {
                        LogIfDebugBuild("JPOGTrex: No players found in range. Return to searching for player.");
                        StopChase();
                    }
                    break;

                case (int)State.AttackingEntity:
                    previousState = State.AttackingEntity;
                    DoAnimationServerRpc("attackEnemy");
                    break;

                case (int)State.GrabPlayer:
                    if (previousState != State.GrabPlayer)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [GrabPlayer]");
                        previousState = State.GrabPlayer;
                        if (targetPlayer != null && targetPlayer.isInHangarShipRoom)
                        {
                            LogIfDebugBuild("JPOGTrex: TargetPlayer is inside the ship, stopping chase");
                            StopChase();
                            break;
                        }
                        if (timeSinceHittingLocalPlayer >= 4f)
                        {
                            beginningGrab = true;
                            StartCoroutine(BeginGrab());
                        }
                    }
                    if (!beginningGrab)
                    {
                        if (hitConnect)
                        {
                            LogIfDebugBuild("JPOGTrex: Hit connect, Switching to GrabbingPlayer");
                            agent.speed = defaultSpeed / 2;
                            SetWalkingAnimationServerRpc(agent.speed);
                            hitConnect = false;
                            SwitchToBehaviourState((int)State.GrabbingPlayer);
                        }
                        else if (!inGrabAttack)
                        {
                            LogIfDebugBuild("JPOGTrex: Hit did not connect, returning to chasingPlayer");
                            SwitchToBehaviourState((int)State.ChasingPlayer);
                        }
                    }
                    break;

                case (int)State.GrabbedPlayer:
                    DoAnimationServerRpc("grabbedPlayer");
                    previousState = State.GrabbedPlayer;
                    SwitchToBehaviourState((int)State.GrabbingPlayer);
                    break;

                case (int)State.GrabbingPlayer:
                    if (previousState != State.GrabbingPlayer)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [GrabbingPlayer]");
                        agent.speed = 0;
                        previousState = State.GrabbingPlayer;
                        movingTowardsTargetPlayer = false;
                        SetWalkingAnimationServerRpc(agent.speed);
                        //Set locally first so the checks below don't run before the RPC has arrived
                        shakingBoddies = true;
                        ShakingBodiesServerRpc();
                    }
                    if (!shakingBoddies && isHungry)
                    {
                        LogIfDebugBuild("JPOGTrex: Done shaking, switching to eat body");
                        SwitchToBehaviourState((int)State.EatingPlayer);
                    }
                    else if (!shakingBoddies && !isHungry)
                    {
                        LogIfDebugBuild("JPOGTrex: Done shaking, not hungry, dropping body");
                        DropcarriedBodyServerRpc();
                        SwitchToBehaviourState((int)State.SearchingForPlayer);
                    }
                    break;

                case (int)State.EatingPlayer:
                    if (previousState != State.EatingPlayer)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [EatingPlayer]");
                        agent.speed = 0f;
                        SetWalkingAnimationServerRpc(agent.speed);
                        previousState = State.EatingPlayer;
                        if (isHungry && !inEatingAnimation)
                        {
                            inEatingAnimation = true;
                            EatPlayerServerRpc();
                        }
                    }
                    if (doneEating)
                    {
                        LogIfDebugBuild("JPOGTrex: Done eating, returning to SearchingForPlayer");
                        doneEating = false;
                        SetSuspicionPercent(20f);
                        SwitchToBehaviourState((int)State.SearchingForPlayer);
                    }
                    break;

                case (int)State.Idle:
                    int rndIdle = enemyRandom.Next(4);
                    if (rndIdle == 1)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [Idle]");
                        agent.speed = 1f;
                        DoAnimationServerRpc("lookingIdle");
                        previousState = State.Idle;
                    }
                    else if (rndIdle == 2)
                    {
                        agent.speed = 1f;
                        DoAnimationServerRpc("sniffingIdle");
                        previousState = State.Idle;
                    }
                    else if (rndIdle == 3)
                    {
                        agent.speed = 0f;
                        DoAnimationServerRpc("eatingIdle01");
                        previousState = State.Idle;
                        SwitchToBehaviourState((int)State.Eating);
                    }
                    break;

                case (int)State.Eating:
                    DoAnimationServerRpc("eatingIdle02");
                    previousState = State.Idle;
                    SwitchToBehaviourState((int)State.Eating);
                    break;

                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    isRoaringStarted = false;
                    SwitchToBehaviourState((int)State.SearchingForPlayer);
                    break;
            }
        }

        //Network Stuff
        [ServerRpc(RequireOwnership = false)]
        private void IncreaseSuspicionServerRpc(int playerId)
        {
            IncreaseSuspicionClientRpc(playerId);
        }

        [ClientRpc]
        private void IncreaseSuspicionClientRpc(int playerId)
        {
            movingPlayer = StartOfRound.Instance.allPlayerScripts[playerId];
            IncreaseSuspicion();
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
        private void SetWalkingAnimationServerRpc(float speed)
        {
            SetWalkingAnimation(speed);
        }

        [ServerRpc(RequireOwnership = false)]
        public void DoAnimationServerRpc(string animationName)
        {
            DoAnimationClientRpc(animationName);
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
            StartGrabCheckClientRpc();
        }

        [ClientRpc]
        private void StartGrabCheckClientRpc()
        {
            StartGrabCheck();
        }

        [ServerRpc(RequireOwnership = false)]
        private void StopGrabCheckServerRpc()
        {
            StopGrabCheckClientRpc();
        }

        [ClientRpc]
        private void StopGrabCheckClientRpc()
        {
            StopGrabCheck();
        }

        [ClientRpc]
        public void CancelKillAnimationWithPlayerClientRpc(int playerObjectId)
        {
            LogIfDebugBuild($"JPOGTrex: CancelKillAnimationWIthPlayer");
            StartOfRound.Instance.allPlayerScripts[playerObjectId].inAnimationWithEnemy = null;
        }

        [ServerRpc(RequireOwnership = false)]
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
            if (killPlayerCoroutine != null)
            {
                LogIfDebugBuild($"JPOGTrex: In killPlayerCoroutine!, stopping previous killPlayerCoroutine!");
                StopCoroutine(killPlayerCoroutine);
            }
            LogIfDebugBuild($"JPOGTrex: start Killing player [{playerId}] rpc");
            killPlayerCoroutine = StartCoroutine(KillPlayer(playerId));
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

        [ServerRpc(RequireOwnership = false)]
        private void SetHitConnectToTrueServerRpc()
        {
            SetHitConnectToTrueClientRpc();
        }

        [ClientRpc]
        private void SetHitConnectToTrueClientRpc()
        {
            hitConnect = true;
            LogIfDebugBuild($"JPOGTrex: hitConnect = [{hitConnect}]");
        }

        //Methods & Logic

        //Player Detection

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
                return true;
            }
            targetPlayer = null;
            return false;
        }

        //Drops the current target and returns to searching with lowered suspicion
        private void StopChase()
        {
            targetPlayer = null;
            isMovingTowardsLastKnownPosition = false;
            timeSinceLostPlayer = 0.0f;
            SetSuspicionPercent(60f);
            SwitchToBehaviourState((int)State.SearchingForPlayer);
        }

        private void SetSuspicionPercent(float percent)
        {
            suspicionLevel = maxSuspicionLevel / 100f * percent;
        }

        private void IncreaseSuspicion()
        {
            suspicionLevel = Mathf.Clamp(suspicionLevel + increasRateSuspicion, 0, maxSuspicionLevel);
            // Reset decrease time to prevent immediate decrease
            lastSuspicionDecreaseTime = Time.time;
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

        //Runs on every client, but only checks the local player since the movement data is only accurate for that player
        private void CheckLocalPlayerMovingInSight()
        {
            PlayerControllerB localPlayer = GameNetworkManager.Instance.localPlayerController;
            if (localPlayer == null || localPlayer.isPlayerDead || localPlayer.isInHangarShipRoom)
            {
                return;
            }
            PlayerControllerB[] allPlayersInLineOfSight = GetAllPlayersInLineOfSight(visionRangeWidth, visionRangeSearching, eye, 3f, StartOfRound.Instance.collidersRoomDefaultAndFoliage);
            if (allPlayersInLineOfSight == null)
            {
                return;
            }
            foreach (var playerControllerB in allPlayersInLineOfSight)
            {
                if (playerControllerB == localPlayer)
                {
                    if (CheckIfPlayerIsmoving(localPlayer))
                    {
                        LogIfDebugBuild($"JPGOTrex: Saw player [{localPlayer.playerClientId}] moving");
                        IncreaseSuspicionServerRpc((int)localPlayer.playerClientId);
                    }
                    return;
                }
            }
        }

        private bool CheckIfPlayerIsmoving(PlayerControllerB playerToCheck)
        {
            localPlayerTurnDistance += StartOfRound.Instance.playerLookMagnitudeThisFrame;
            if (localPlayerTurnDistance > 0.1f && Vector3.Distance(playerToCheck.transform.position, base.transform.position) < 10f)
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
            if (playerToCheck.timeSincePlayerMoving < 0.05f)
            {
                return true;
            }
            return false;
        }

        //Checks if the player is in range for an attack, returns true if the T-rex switched to grabbing
        private bool CheckForPlayersInRangeOfGrabAttack()
        {
            if (targetPlayer == null)
            {
                LogIfDebugBuild("JPOGTrex: No target player assigned!");
                return false;
            }
            if (Vector3.Distance(transform.position, targetPlayer.transform.position) <= attackRange)
            {
                SwitchToBehaviourState((int)State.GrabPlayer);
                return true;
            }
            return false;
        }

        //Check is if the target is valid or not, returns false if the target was dropped
        private bool CheckIfPlayerIsTargetable()
        {
            if (targetPlayer != null && !PlayerIsTargetable(targetPlayer, false))
            {
                LogIfDebugBuild($"JPOGTrex: Player [{targetPlayer.playerClientId}] is no longer targetable");
                StopChase();
                return false;
            }
            return true;
        }

        private void StartGrabCheck()
        {
            hitPlayerIds.Clear();
            inGrabAttack = true;
            if (grabCheckCoroutine != null)
            {
                StopCoroutine(grabCheckCoroutine);
            }
            grabCheckCoroutine = StartCoroutine(CheckIfGrabHitDuringAnimation());
        }

        private void StopGrabCheck()
        {
            LogIfDebugBuild("JPOGTrex: Stopping grab check Coroutine");
            inGrabAttack = false;
            if (grabCheckCoroutine != null)
            {
                StopCoroutine(grabCheckCoroutine);
                grabCheckCoroutine = null;
            }
        }

        //Checks if the attack should hit the player (during the animation of the grab/bite)
        public void GrabAttackHitAnimation()
        {
            int playerLayer = 1 << 3;
            Collider[] hitColliders = Physics.OverlapBox(mouthAttackHitBox.position, mouthAttackHitBox.localScale, Quaternion.identity, playerLayer);
            bool hitPlayer = false;
            foreach (var player in hitColliders)
            {
                //Only returns the local player, so every client checks itself
                PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                if (playerControllerB == null)
                {
                    continue;
                }
                int playerId = (int)playerControllerB.playerClientId;
                if (!hitPlayerIds.Contains(playerId))
                {
                    LogIfDebugBuild($"JPOGTrex: Grab Attack Hit player: [{playerId}], calling to kill player");
                    timeSinceHittingLocalPlayer = 0f;
                    hitPlayer = true;
                    KillPlayerServerRpc(playerId);
                    hitPlayerIds.Add(playerId);
                }
            }
            if (hitPlayer)
            {
                SetHitConnectToTrueServerRpc();
            }
        }

        //Player body Interaction
        private void TakeBodyInMouth(int playerId)
        {
            LogIfDebugBuild($"JPOGTrex: Taking boddy of player [{playerId}] in mouth");
            DeadBodyInfo killedPlayerBody = StartOfRound.Instance.allPlayerScripts[playerId].deadBody;
            if (killedPlayerBody == null)
            {
                return;
            }
            LogIfDebugBuild($"JPOGTrex: deadbody of player cause of death: [{killedPlayerBody.causeOfDeath}]");
            killedPlayerBody.attachedTo = mouthGrip;
            killedPlayerBody.attachedLimb = killedPlayerBody.bodyParts[5];
            killedPlayerBody.matchPositionExactly = true;
            killedPlayerBody.MakeCorpseBloody();

            //The T-rex Should be able to multikill so all bodies it grabs are added to the list
            carryingBodies.Add(killedPlayerBody);
            LogIfDebugBuild($"JPOGTrex: carryingBodies count = [{carryingBodies.Count}]");
        }

        private void DropCarriedBody()
        {
            LogIfDebugBuild($"JPOGTrex: Dropping [{carryingBodies.Count}] bodies from mouth");
            //All grabbed bodies should be dropped by the Trex
            foreach (var carryingBody in carryingBodies)
            {
                if (carryingBody == null)
                {
                    continue;
                }
                carryingBody.speedMultiplier = 12f;
                carryingBody.attachedTo = null;
                carryingBody.attachedLimb = null;
                carryingBody.matchPositionExactly = false;
            }
            carryingBodies.Clear();
        }

        private void EatPlayer()
        {
            LogIfDebugBuild($"JPOGTrex: beginning to eat player(s)");
            inEatingAnimation = true;
            if (eatCoroutine != null)
            {
                StopCoroutine(eatCoroutine);
            }
            eatCoroutine = StartCoroutine(EatPlayerCoroutine());
        }

        private void ShakkingBodies()
        {
            shakingBoddies = true;
            if (shakeCoroutine != null)
            {
                StopCoroutine(shakeCoroutine);
            }
            shakeCoroutine = StartCoroutine(ShakeGrabbedBody());
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
            else if (distance <= 20f)
            {
                HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
            }
            else
            {
                HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
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

        public override void OnCollideWithEnemy(Collider other, EnemyAI? collidedEnemy = null)
        {
            base.OnCollideWithEnemy(other, collidedEnemy);
            if (!IsOwner || isEnemyDead || collidedEnemy == null)
            {
                return;
            }
            if (!collidedEnemy.isEnemyDead && CheckIfInNotAnimation())
            {
                targetEntity = collidedEnemy;
                LookAtTargetEntity();
                if (collidedEnemy.enemyType.SizeLimit == NavSizeLimit.MediumSpaces)
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
            return !inGrabAttack && !inKillAnimation && !inEatingAnimation && !beginningGrab && !inAttackEnemyAnimation;
        }

        public IEnumerator AttackEnemyLow()
        {
            inAttackEnemyAnimation = true;
            DoAnimationServerRpc("attackEnemyLow");
            yield return new WaitForSeconds(1.2f);
            if (targetEntity != null && !targetEntity.isEnemyDead)
            {
                targetEntity.HitEnemyOnLocalClient(15, playHitSFX: true);
            }
            inAttackEnemyAnimation = false;
        }

        public IEnumerator AttackEnemyHigh()
        {
            inAttackEnemyAnimation = true;
            DoAnimationServerRpc("attackEnemyHigh");
            yield return new WaitForSeconds(1.2f);
            if (targetEntity != null && !targetEntity.isEnemyDead)
            {
                targetEntity.HitEnemyOnLocalClient(10, playHitSFX: true);
            }
            inAttackEnemyAnimation = false;
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
            if (enemyHP <= 0)
            {
                if (IsOwner)
                {
                    KillEnemyOnOwnerClient();
                }
                return;
            }
            if (!IsOwner || playerWhoHit == null)
            {
                return;
            }
            //The T-rex should not suddenly start chasing a player during "important" animations such as eating or grabbing the player
            int state = currentBehaviourStateIndex;
            if (state == (int)State.EatingPlayer || state == (int)State.GrabbingPlayer || state == (int)State.GrabPlayer)
            {
                return;
            }
            targetPlayer = playerWhoHit;
            if (state != (int)State.ChasingPlayer)
            {
                suspicionLevel = maxSuspicionLevel;
                SwitchToBehaviourState((int)State.ChasingPlayer);
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            LogIfDebugBuild("JPOGTrex: T-rex has been killed");
            // We need to stop our coroutines (including the search), because the game does not do that by default.
            StopAllCoroutines();
            if (IsOwner)
            {
                SetWalkingAnimationServerRpc(0f);
            }
            creatureAnimator.SetTrigger("killEnemy");
            creatureVoice.Stop();
            creatureSFX.Stop();
            base.KillEnemy(destroy);
        }

        //Some Utility stuff

        //Simple method that sets the walking animation of the T-rex based on it's speed.
        //This way a more slowed down walking animation or sped up running animation can be applied.
        //Should be called after the speed of the current behaviour state has been set.
        //Runs on the server.
        private void SetWalkingAnimation(float currentSpeed)
        {
            if (currentSpeed == previousSpeed)
            {
                return;
            }
            previousSpeed = currentSpeed;
            if (currentSpeed == 0f)
            {
                DoAnimationClientRpc("stopWalk");
            }
            else if (currentSpeed <= 2f)
            {
                DoAnimationClientRpc("slowDown");
            }
            else if (currentSpeed <= 6f)
            {
                DoAnimationClientRpc("startWalk");
            }
            else if (currentSpeed <= 12f)
            {
                DoAnimationClientRpc("chasingRun");
            }
            else
            {
                DoAnimationClientRpc("speedUp");
            }
        }

        //These methods are called during certain frames in the animation
        private void PlayAudioClip(AudioClip audioClip)
        {
            creatureVoice.PlayOneShot(audioClip);
            WalkieTalkie.TransmitOneShotAudio(creatureVoice, audioClip);
        }
        private void PlayFootStepAudioClip(AudioClip audioClip)
        {
            creatureSFX.PlayOneShot(audioClip);
            WalkieTalkie.TransmitOneShotAudio(creatureSFX, audioClip);
            ShakeCamera();
        }
        private void PlayTrexRoarAudioClipt(AudioClip audioClip)
        {
            trexRoarSFX.PlayOneShot(audioClip, 2f);
            WalkieTalkie.TransmitOneShotAudio(trexRoarSFX, audioClip);
        }
        private void PlayTrexSubRoarAudioClipt(AudioClip audioClip)
        {
            trexSubRoarSFX.PlayOneShot(audioClip, 1f);
            WalkieTalkie.TransmitOneShotAudio(trexSubRoarSFX, audioClip);
        }

        //Coroutines

        //Owner only
        private IEnumerator FoundPlayer()
        {
            previousState = State.SpottedPlayer;
            LogIfDebugBuild("JPOGTrex: Sniffing");
            DoAnimationServerRpc("foundPlayer");
            yield return new WaitForSeconds(1.3f);
            LogIfDebugBuild("JPOGTrex: Sniffing finished");
            sniffing = false;
        }

        //Owner only
        private IEnumerator BeginChase(int animationNumber)
        {
            previousState = State.SpottedPlayer;
            LogIfDebugBuild($"JPOGTrex: Roaring [{animationNumber}]");
            DoAnimationServerRpc("beginChase0" + animationNumber);
            // Adjust wait time based on animationNumber
            float waitTime = animationNumber == 3 ? 3.6f : 3.1f;
            yield return new WaitForSeconds(waitTime);
            LogIfDebugBuild("JPOGTrex: Roaring finished");
            roaring = false;
        }

        //Owner only
        private IEnumerator BeginGrab()
        {
            StartGrabCheckServerRpc();
            LogIfDebugBuild("JPOGTrex: Beginning grab Attack!");
            DoAnimationServerRpc("grabPlayer");
            DoAnimationServerRpc("grabbedPlayer");
            yield return new WaitForSeconds(1.2f);
            StopGrabCheckServerRpc();
            LogIfDebugBuild("JPOGTrex: grab attack finished");
            beginningGrab = false;
        }

        //Runs on every client
        private IEnumerator CheckIfGrabHitDuringAnimation()
        {
            while (inGrabAttack) {
                GrabAttackHitAnimation();
                yield return null;
            }
            grabCheckCoroutine = null;
        }

        //Runs on every client
        private IEnumerator ShakeGrabbedBody()
        {
            creatureAnimator.SetTrigger("grabbingPlayer");
            yield return new WaitForSeconds(2.6f);
            shakingBoddies = false;
            shakeCoroutine = null;
        }

        //Runs on every client
        private IEnumerator EatPlayerCoroutine()
        {
            creatureAnimator.SetTrigger("eatingPlayer");
            yield return new WaitForSeconds(5.7f);
            inEatingAnimation = false;
            foreach (var carryingBody in carryingBodies)
            {
                if (carryingBody == null)
                {
                    continue;
                }
                LogIfDebugBuild($"JPOGTrex: Deactivating body = [{carryingBody.playerObjectId}]");
                carryingBody.DeactivateBody(false);
                playersEaten++;
            }
            //Clears the list to make sure no bodies remain
            carryingBodies.Clear();
            isHungry = playersEaten < maxPlayersToEat;
            doneEating = true;
            eatCoroutine = null;
        }

        //Runs on every client
        private IEnumerator KillPlayer(int playerId)
        {
            if (IsOwner)
            {
                agent.speed = Mathf.Clamp(agent.speed, 0f, 2f);
            }
            LogIfDebugBuild($"JPOGTrex: begin Killing player: [{playerId}]");
            PlayerControllerB killPlayer = StartOfRound.Instance.allPlayerScripts[playerId];
            if (killPlayer == null || killPlayer.isPlayerDead)
            {
                LogIfDebugBuild($"JPOGTrex: Player [{playerId}] is not valid or already dead.");
                inKillAnimation = false;
                killPlayerCoroutine = null;
                yield break;
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
                killPlayerCoroutine = null;
                yield break;
            }
            TakeBodyInMouth(playerId);
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
        }
    }
}
