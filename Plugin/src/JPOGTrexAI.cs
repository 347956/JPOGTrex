using System.Collections;
using System.Diagnostics;
using GameNetcodeStuff;
using JPOGTrex.Configuration;
using Unity.Netcode;
using UnityEngine;

namespace JPOGTrex {


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

        internal TrexSuspicionHelper Suspicion { get; private set; } = null!;
        internal TrexTargetingHelper Targeting { get; private set; } = null!;
        internal TrexGrabHelper Grab { get; private set; } = null!;
        internal TrexFeedingHelper Feeding { get; private set; } = null!;
        internal TrexEnemyAttackHelper EnemyAttack { get; private set; } = null!;
        internal System.Random EnemyRandom { get; private set; } = null!;

        private float timeSinceHittingLocalPlayer;
        private bool isDeadAnimationDone;
        private float defaultSpeed = 6f;
        private State previousState = State.Idle;
        private bool roaring;
        private bool isRoaringStarted;
        private bool isSniffingStarted;
        private bool sniffing;
        private float previousSpeed;
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

        internal enum State
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

        private void CreateHelpers()
        {
            Suspicion = new TrexSuspicionHelper(this);
            Targeting = new TrexTargetingHelper(this);
            Grab = new TrexGrabHelper(this);
            Feeding = new TrexFeedingHelper(this);
            EnemyAttack = new TrexEnemyAttackHelper(this);
        }

        private void AssignConfigVariables()
        {
            PluginConfig config = PluginConfig.Instance;
            defaultSpeed = config.DefaultSpeed.Value;
            Suspicion.Configure(
                config.MaxSuspicionLevel.Value,
                config.SuspicionIncrement.Value,
                config.SuspicionDecrement.Value,
                config.SuspiciontDecreaseTime.Value,
                config.VisionRangeLength.Value,
                config.VisionRangeWidth.Value);
            Targeting.Configure(config.VisionRangeLength.Value - 20);
        }

        public override void Start()
        {
            CreateHelpers();
            AssignConfigVariables();
            agent.stoppingDistance = 0f;
            base.Start();
            LogIfDebugBuild("JPOGTrex Spawned");
            timeSinceHittingLocalPlayer = 0;
            EnemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
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
            Targeting.Tick(Time.deltaTime);
            timeSinceHittingLocalPlayer += Time.deltaTime;

            //Every client checks if its own player is seen moving, the owner only receives the result
            if (currentBehaviourStateIndex == (int)State.SearchingForPlayer)
            {
                Suspicion.TickLocalSightCheck(Time.deltaTime);
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
                        Targeting.ResetLostPlayer();
                        agent.speed = defaultSpeed;
                        agent.autoBraking = true;
                        SetWalkingAnimationServerRpc(agent.speed);
                        StartSearch(transform.position);
                        previousState = State.SearchingForPlayer;
                    }
                    if (Targeting.FoundClosestPlayerInRange())
                    {
                        LogIfDebugBuild("JPOGTrex: Beginning chase for player in close proximity");
                        StopSearch(currentSearch);
                        SwitchToBehaviourState((int)State.SpottedPlayer);
                        break;
                    }
                    Suspicion.DecreaseSuspicion();
                    if (Suspicion.TryTakeMovingPlayer(out PlayerControllerB movingPlayer) && Targeting.IsPlayerReachable(movingPlayer))
                    {
                        targetPlayer = movingPlayer;
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
                        StartCoroutine(BeginChase(EnemyRandom.Next(1, 4)));
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
                        Targeting.ResetLostPlayer();
                    }
                    if (targetPlayer != null)
                    {
                        SetDestinationToPosition(targetPlayer.transform.position);
                        Targeting.UpdateUnreachableTimer();
                        if (Targeting.IsInGrabRange())
                        {
                            SwitchToBehaviourState((int)State.GrabPlayer);
                            break;
                        }
                        if (targetPlayer.isInHangarShipRoom)
                        {
                            LogIfDebugBuild("JPOGTrex: TargetPlayer is inside the ship, stopping chase");
                            StopChase();
                            break;
                        }
                        if (Targeting.HasBeenUnreachableTooLong)
                        {
                            LogIfDebugBuild("JPOGTrex: TargetPlayer has been unreachable for too long!");
                            StopChase();
                            break;
                        }
                    }
                    if (!Targeting.IsTargetTargetable())
                    {
                        StopChase();
                        break;
                    }
                    if (Targeting.IsTargetLostFromSight())
                    {
                        Targeting.StartMovingToLastKnownPosition();
                        break;
                    }
                    if (Targeting.HasSearchTimedOut)
                    {
                        LogIfDebugBuild("JPOGTrex: Spent to long trying to reach last know position. returning to searching for player");
                        StopChase();
                        break;
                    }
                    if (Targeting.ReachedLastKnownPosition() && !Targeting.FoundClosestPlayerInRange())
                    {
                        LogIfDebugBuild("JPOGTrex: No players found. Return to searching for player.");
                        StopChase();
                        break;
                    }
                    if (targetPlayer == null && !Targeting.IsMovingTowardsLastKnownPosition)
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
                            Grab.StartGrab();
                        }
                    }
                    if (!Grab.BeginningGrab)
                    {
                        if (Grab.ConsumeHitConnect())
                        {
                            LogIfDebugBuild("JPOGTrex: Hit connect, Switching to GrabbingPlayer");
                            agent.speed = defaultSpeed / 2;
                            SetWalkingAnimationServerRpc(agent.speed);
                            SwitchToBehaviourState((int)State.GrabbingPlayer);
                        }
                        else if (!Grab.InGrabAttack)
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
                        Feeding.BeginShakeLocal();
                        ShakingBodiesServerRpc();
                    }
                    if (!Feeding.IsShaking)
                    {
                        if (Feeding.IsHungry && Feeding.HasBodyInMouth)
                        {
                            LogIfDebugBuild("JPOGTrex: Done shaking, switching to eat body");
                            SwitchToBehaviourState((int)State.EatingPlayer);
                        }
                        else
                        {
                            //Not hungry, or the body left the mouth (e.g. teleported), so there is nothing to eat
                            LogIfDebugBuild($"JPOGTrex: Done shaking, not eating || isHungry = [{Feeding.IsHungry}] || hasBodyInMouth = [{Feeding.HasBodyInMouth}]");
                            DropcarriedBodyServerRpc();
                            SwitchToBehaviourState((int)State.SearchingForPlayer);
                        }
                    }
                    break;

                case (int)State.EatingPlayer:
                    if (previousState != State.EatingPlayer)
                    {
                        LogIfDebugBuild("JPOGTrex: Entered behaviourState [EatingPlayer]");
                        agent.speed = 0f;
                        SetWalkingAnimationServerRpc(agent.speed);
                        previousState = State.EatingPlayer;
                        if (Feeding.IsHungry && !Feeding.IsEating)
                        {
                            Feeding.BeginEatingLocal();
                            EatPlayerServerRpc();
                        }
                    }
                    if (Feeding.ConsumeDoneEating())
                    {
                        LogIfDebugBuild("JPOGTrex: Done eating, returning to SearchingForPlayer");
                        Suspicion.SetPercent(20f);
                        SwitchToBehaviourState((int)State.SearchingForPlayer);
                    }
                    break;

                case (int)State.Idle:
                    int rndIdle = EnemyRandom.Next(4);
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

        //Drops the current target and returns to searching with lowered suspicion
        private void StopChase()
        {
            targetPlayer = null;
            Targeting.ResetLostPlayer();
            Suspicion.SetPercent(60f);
            SwitchToBehaviourState((int)State.SearchingForPlayer);
        }

        public bool CheckIfInNotAnimation()
        {
            return !Grab.IsBusy && !Feeding.IsEating && !EnemyAttack.IsAttacking;
        }

        internal void ResetHitTimer()
        {
            timeSinceHittingLocalPlayer = 0f;
        }

        //Network Stuff
        [ServerRpc(RequireOwnership = false)]
        internal void IncreaseSuspicionServerRpc(int playerId)
        {
            IncreaseSuspicionClientRpc(playerId);
        }

        [ClientRpc]
        private void IncreaseSuspicionClientRpc(int playerId)
        {
            Suspicion.OnPlayerSeenMoving(playerId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void EatPlayerServerRpc()
        {
            EatPlayerClientRpc();
        }

        [ClientRpc]
        private void EatPlayerClientRpc()
        {
            Feeding.StartEating();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ShakingBodiesServerRpc()
        {
            ShakingBodiesClientRpc();
        }

        [ClientRpc]
        private void ShakingBodiesClientRpc()
        {
            Feeding.StartShake();
        }

        [ServerRpc(RequireOwnership = false)]
        public void DropcarriedBodyServerRpc()
        {
            DropcarriedBodyClientRpc();
        }

        [ClientRpc]
        public void DropcarriedBodyClientRpc()
        {
            Feeding.DropCarriedBodies();
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
        internal void StartGrabCheckServerRpc()
        {
            StartGrabCheckClientRpc();
        }

        [ClientRpc]
        private void StartGrabCheckClientRpc()
        {
            Grab.OnStartGrabCheck();
        }

        [ServerRpc(RequireOwnership = false)]
        internal void StopGrabCheckServerRpc()
        {
            StopGrabCheckClientRpc();
        }

        [ClientRpc]
        private void StopGrabCheckClientRpc()
        {
            Grab.OnStopGrabCheck();
        }

        [ServerRpc(RequireOwnership = false)]
        internal void SetHitConnectToTrueServerRpc()
        {
            SetHitConnectToTrueClientRpc();
        }

        [ClientRpc]
        private void SetHitConnectToTrueClientRpc()
        {
            Grab.OnHitConnected();
        }

        [ServerRpc(RequireOwnership = false)]
        public void KillPlayerServerRpc(int playerId)
        {
            if (Grab.TryBeginKill())
            {
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
            Grab.StartKill(playerId);
        }

        [ClientRpc]
        public void CancelKillAnimationWithPlayerClientRpc(int playerObjectId)
        {
            LogIfDebugBuild($"JPOGTrex: CancelKillAnimationWIthPlayer");
            StartOfRound.Instance.allPlayerScripts[playerObjectId].inAnimationWithEnemy = null;
        }

        //Collisions & damage

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
            if (CheckIfInNotAnimation())
            {
                EnemyAttack.TryAttack(collidedEnemy);
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
                Suspicion.SetMax();
                SwitchToBehaviourState((int)State.ChasingPlayer);
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            LogIfDebugBuild("JPOGTrex: T-rex has been killed");
            // We need to stop our coroutines (including the search), because the game does not do that by default.
            StopAllCoroutines();
            //Release any bodies so they can be picked up or teleported again
            Feeding.DropCarriedBodies();
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
    }
}
