using System;
using System.Collections;
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
        public Transform mouthGrip = null!;
        private DeadBodyInfo carryingBody = null!;
        private PlayerControllerB playerInGrabAnimation = null!;
#pragma warning restore 0649
        private Coroutine grabbingPlayerCoroutine;
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
                        DoAnimationClientRpc("startSearch");
                    }
                    if (FoundClosestPlayerInRange(25f, 3f))
                    {
                        LogIfDebugBuild("Start Target Player");
                        StopSearch(currentSearch);
                        previousState = State.SearchingForPlayer;
                        SwitchToBehaviourClientRpc((int)State.SpottedPlayer);
                        return;
                    }
                    previousState = State.SearchingForPlayer;
                    break;

                case (int)State.SpottedPlayer:
                    agent.speed = 0f;
                    if (previousState != State.SpottedPlayer && !isSniffingStarted)
                    {
                        sniffing = true;
                        LogIfDebugBuild("JPOGTrex: Spotted Player!");
                        SetWalkingAnimation(agent.speed);
                        StartCoroutine(FoundPlayer());
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
                    if (!TargetClosestPlayerInAnyCase() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 20 && !CheckLineOfSightForPosition(targetPlayer.transform.position)))
                    {
                        LogIfDebugBuild("Stop Target Player");
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        previousState = State.ChasingPlayer;
                        return;
                    }
                    SetDestinationToPosition(targetPlayer.transform.position);
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
                    }
                    //Logic To check if grab connected
                    bool hitConnect = true;
                    DoAnimationClientRpc("grabPlayer");
                    if (hitConnect != false)
                    {
                        previousState = State.GrabPlayer;
                        SwitchToBehaviourClientRpc((int)State.GrabbedPlayer);
                        break;
                    }
                    previousState = State.GrabPlayer;
                    break;

                case (int)State.GrabbedPlayer:
                    DoAnimationClientRpc("grabbedPlayer");
                    previousState = State.GrabbedPlayer;
                    SwitchToBehaviourClientRpc((int)State.GrabbingPlayer);
                    break;

                case (int)State.GrabbingPlayer:
                    agent.speed = 0.1f;
                    int enemyYRot = (int)transform.eulerAngles.y;
                    if (previousState != State.GrabbingPlayer)
                    {
                        SetWalkingAnimation(agent.speed);
                    }
                    if(previousState != State.GrabbingPlayer && inGrabbingAnimation == false)
                    {
                        BeginGrabbingPlayer(playerInGrabAnimation, transform.position, enemyYRot);
                        inGrabbingAnimation = true;

                    }
                    break;
                   

                case (int)State.EatingPlayer:
                    agent.speed = 0f;
                    if (previousState != State.EatingPlayer)
                    {
                        SetWalkingAnimation(agent.speed);
                    }
                    DoAnimationClientRpc("eatingPlayuer");
                    previousState = State.EatingPlayer;
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

        bool FoundClosestPlayerInRange(float range, float senseRange) {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if (targetPlayer == null) {
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
            if (targetPlayer == null) return false;
            return true;
        }


        //Simple method that sets the walking animation of the T-rex based on it's speed.
        //This way a more slowed down walking animation or sped up running animation can be applied.
        //Should be called after the speed of the current behaviour state has been set.
        public void SetWalkingAnimation(float currentSpeed)
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

/*        public void StickingInFrontOfPlayer() {
            // We only run this method for the host because I'm paranoid about randomness not syncing I guess
            // This is fine because the game does sync the position of the enemy.
            // Also the attack is a ClientRpc so it should always sync
            if (targetPlayer == null || !IsOwner) {
                return;
            }
            if (timeSinceNewRandPos > 0.7f) {
                timeSinceNewRandPos = 0;
                if (enemyRandom.Next(0, 5) == 0) {
                    // Attack
                    StartCoroutine(GrabAttack());
                }
                else {
                    // Go in front of player
                    positionRandomness = new Vector3(enemyRandom.Next(-2, 2), 0, enemyRandom.Next(-2, 2));
                    StalkPos = targetPlayer.transform.position - Vector3.Scale(new Vector3(-5, 0, -5), targetPlayer.transform.forward) + positionRandomness;
                }
                SetDestinationToPosition(StalkPos, checkForPath: false);
            }
        }*/


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
            yield return new WaitForSeconds(0.4f);
            DoAnimationClientRpc("grabbedPlayer");
            yield return new WaitForSeconds(1.2f);
            SwitchToBehaviourClientRpc((int)State.GrabbingPlayer);
        }

        /*        IEnumerator GrabAttack() {
                    SwitchToBehaviourClientRpc((int)State.GrabPlayer);
                    StalkPos = targetPlayer.transform.position;
                    SetDestinationToPosition(StalkPos);
                    yield return new WaitForSeconds(0.5f);
                    if (isEnemyDead) {
                        yield break;
                    }
                    yield return new WaitForSeconds(0.35f);
                    SwingAttackHitClientRpc();
                    // In case the player has already gone away, we just yield break (basically same as return, but for IEnumerator)
                    if (currentBehaviourStateIndex != (int)State.GrabbedPlayer)
                    {
                        yield break;
                    }
                    SwitchToBehaviourClientRpc((int)State.GrabbingPlayer);
                }*/

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

        /*        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1) {
                    base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
                    if (isEnemyDead) {
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
                }*/

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
                DoAnimationClientRpc("killEnemy");
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

            TakeBodyInMouth(killPlayer.deadBody);
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
            yield return new WaitForSeconds(3.01f);
            if (isHungry)
            {
                SwitchToBehaviourClientRpc((int)State.EatingPlayer);
                yield break;
            }
            DropCarriedBody();
            suspicionLevel = 2;
            inKillAnimation = false;
        }


        private void BeginGrabbingPlayer(PlayerControllerB playerBeingGrabbed, Vector3 enemyPosition, int enemyYRot)
        {
            
            inSpecialAnimationWithPlayer = playerBeingGrabbed;
            inSpecialAnimationWithPlayer.inSpecialInteractAnimation = true;
            inSpecialAnimationWithPlayer.inAnimationWithEnemy = this;
            if (grabbingPlayerCoroutine != null)
            {
                StopCoroutine(grabbingPlayerCoroutine);
            }
            grabbingPlayerCoroutine = StartCoroutine(GrabbingPlayerAnimation(playerBeingGrabbed, enemyPosition, enemyYRot));
        }

        private IEnumerator GrabbingPlayerAnimation(PlayerControllerB playerBeingGrabbed, Vector3 enemyPosition, int enemyYRot)
        {
            //lookingAtTarget = false;
            DoAnimationClientRpc("grabbingPlayer");
            inGrabbingAnimation = true;
            inSpecialAnimation = true;
            playerBeingGrabbed.isInElevator = false;
            playerBeingGrabbed.isInHangarShipRoom = false;
            playerBeingGrabbed.BreakLegsSFXClientRpc();
            playerBeingGrabbed.DropBlood(enemyPosition, true, true);
            StartCoroutine(KillPlayer(playerBeingGrabbed.currentSuitID));
            yield return new WaitForSeconds(2.7f);
            if (isHungry && carryingBody != null)
            {
                inGrabbingAnimation = false;
                inSpecialAnimation = true;
                StartCoroutine(EatPlayer(carryingBody));
            }
            inGrabbingAnimation = false;
            inSpecialAnimation = false;
        }

        private IEnumerator EatPlayer(DeadBodyInfo bodyToEat)
        {
            LogIfDebugBuild($"JPOGTrex: begin eating body [{bodyToEat.playerObjectId}]");
            inEatingAnimation = true;
            DoAnimationClientRpc("eatingPlayer");
/*            Vector3 startPostion  = base.transform.position;
            Quaternion starRoation = base.transform.rotation;*/

            bodyToEat.MakeCorpseBloody();
            yield return new WaitForSeconds(5.7f);
            bodyToEat.DeactivateBody(false);
        }


        private void TakeBodyInMouth(DeadBodyInfo body)
        {
            carryingBody = body;
            carryingBody.attachedTo = mouthGrip;
            carryingBody.attachedLimb = body.bodyParts[5];
            carryingBody.matchPositionExactly = true;
        }
        private void DropCarriedBody()
        {
            if(!(carryingBody == null))
            {
                carryingBody.speedMultiplier = 12f;
                carryingBody.attachedTo = null;
                carryingBody.attachedLimb = null;
                carryingBody.matchPositionExactly = false;
                carryingBody = null;
            }
        }



        [ClientRpc]
        public void DoAnimationClientRpc(string animationName) {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        public void GrabAttackHitClientRpc() {
            LogIfDebugBuild("GrabAttackHitClientRpc");
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