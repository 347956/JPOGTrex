using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace JPOGTrex
{
    //Handles carrying, shaking and eating the bodies of killed players
    internal class TrexFeedingHelper
    {
        private readonly JPOGTrexAI trexAI;
        private readonly List<DeadBodyInfo> carryingBodies = new List<DeadBodyInfo>();
        private readonly HashSet<DeadBodyInfo> inedibleBodies = new HashSet<DeadBodyInfo>();
        private readonly int maxPlayersToEat = 2;
        private readonly float shakeDuration = 2.6f;
        private readonly float eatDuration = 5.7f;
        private Coroutine? shakeCoroutine;
        private Coroutine? eatCoroutine;
        private bool doneEating;

        public bool IsShaking { get; private set; }
        public bool IsEating { get; private set; }
        public bool IsHungry { get; private set; } = true;
        public int PlayersEaten { get; private set; }
        public bool HasBodyInMouth => carryingBodies.Exists(IsStillInMouth);
        public bool HasEdibleBodyInMouth => carryingBodies.Exists(body => IsStillInMouth(body) && !inedibleBodies.Contains(body));

        public TrexFeedingHelper(JPOGTrexAI trexAI)
        {
            this.trexAI = trexAI;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        //Returns true once after eating has finished
        public bool ConsumeDoneEating()
        {
            if (!doneEating)
            {
                return false;
            }
            doneEating = false;
            return true;
        }

        //Set locally by the owner before the RPC is sent, so its checks don't run before the RPC has arrived
        public void BeginShakeLocal()
        {
            IsShaking = true;
        }

        public void BeginEatingLocal()
        {
            IsEating = true;
        }

        //Runs on every client
        public void StartShake()
        {
            IsShaking = true;
            if (shakeCoroutine != null)
            {
                trexAI.StopCoroutine(shakeCoroutine);
            }
            shakeCoroutine = trexAI.StartCoroutine(ShakeGrabbedBody());
        }

        //Runs on every client
        public void StartEating()
        {
            LogIfDebugBuild($"JPOGTrex: beginning to eat player(s)");
            IsEating = true;
            if (eatCoroutine != null)
            {
                trexAI.StopCoroutine(eatCoroutine);
            }
            eatCoroutine = trexAI.StartCoroutine(EatPlayerCoroutine());
        }

        public void TakeBodyInMouth(int playerId, bool canBeEaten)
        {
            LogIfDebugBuild($"JPOGTrex: Taking boddy of player [{playerId}] in mouth");
            DeadBodyInfo killedPlayerBody = StartOfRound.Instance.allPlayerScripts[playerId].deadBody;
            if (killedPlayerBody == null)
            {
                return;
            }
            LogIfDebugBuild($"JPOGTrex: deadbody of player cause of death: [{killedPlayerBody.causeOfDeath}]");
            killedPlayerBody.attachedTo = trexAI.mouthGrip;
            killedPlayerBody.attachedLimb = killedPlayerBody.bodyParts[5];
            killedPlayerBody.matchPositionExactly = true;
            killedPlayerBody.MakeCorpseBloody();
            //Same as vanilla enemies, this stops the ship teleporter from taking the body out of the mouth
            SetHeldByEnemy(killedPlayerBody, true);

            //The T-rex Should be able to multikill so all bodies it grabs are added to the list
            carryingBodies.Add(killedPlayerBody);
            if (!canBeEaten)
            {
                inedibleBodies.Add(killedPlayerBody);
            }
            LogIfDebugBuild($"JPOGTrex: carryingBodies count = [{carryingBodies.Count}] || canBeEaten = [{canBeEaten}]");
        }

        public void DropCarriedBodies()
        {
            LogIfDebugBuild($"JPOGTrex: Dropping [{carryingBodies.Count}] bodies from mouth");
            //All grabbed bodies should be dropped by the Trex
            foreach (var carryingBody in carryingBodies)
            {
                if (carryingBody != null)
                {
                    SetHeldByEnemy(carryingBody, false);
                }
                if (!IsStillInMouth(carryingBody))
                {
                    continue;
                }
                carryingBody.speedMultiplier = 12f;
                carryingBody.attachedTo = null;
                carryingBody.attachedLimb = null;
                carryingBody.matchPositionExactly = false;
            }
            carryingBodies.Clear();
            inedibleBodies.Clear();
        }

        //A body can leave the mouth without us knowing, e.g. when it gets teleported to the ship
        private bool IsStillInMouth([NotNullWhen(true)] DeadBodyInfo? body)
        {
            return body != null && !body.deactivated && body.attachedTo == trexAI.mouthGrip;
        }

        private void SetHeldByEnemy(DeadBodyInfo body, bool heldByEnemy)
        {
            if (body.grabBodyObject != null)
            {
                body.grabBodyObject.isHeldByEnemy = heldByEnemy;
            }
        }

        private IEnumerator ShakeGrabbedBody()
        {
            trexAI.creatureAnimator.SetTrigger("grabbingPlayer");
            yield return new WaitForSeconds(shakeDuration);
            IsShaking = false;
            shakeCoroutine = null;
        }

        private IEnumerator EatPlayerCoroutine()
        {
            trexAI.creatureAnimator.SetTrigger("eatingPlayer");
            yield return new WaitForSeconds(eatDuration);
            IsEating = false;
            foreach (var carryingBody in carryingBodies)
            {
                if (!IsStillInMouth(carryingBody) || inedibleBodies.Contains(carryingBody))
                {
                    continue;
                }
                LogIfDebugBuild($"JPOGTrex: Deactivating body = [{carryingBody.playerObjectId}]");
                SetHeldByEnemy(carryingBody, false);
                carryingBody.DeactivateBody(false);
                PlayersEaten++;
            }
            //Drops the bodies that couldn't be eaten and clears the list to make sure no bodies remain
            DropCarriedBodies();
            IsHungry = PlayersEaten < maxPlayersToEat;
            doneEating = true;
            eatCoroutine = null;
        }
    }
}
