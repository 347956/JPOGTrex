using System.Collections;
using UnityEngine;

namespace JPOGTrex
{
    //Handles the T-rex attacking other enemies it collides with
    internal class TrexEnemyAttackHelper
    {
        private readonly JPOGTrexAI trexAI;
        private const float AttackRecoverySeconds = 1.2f;
        private EnemyAI? targetEntity;
        private int pendingDamage;

        public bool IsAttacking { get; private set; }

        public TrexEnemyAttackHelper(JPOGTrexAI trexAI)
        {
            this.trexAI = trexAI;
        }

        //Owner only
        public void TryAttack(EnemyAI collidedEnemy)
        {
            if (IsAttacking || collidedEnemy.isEnemyDead)
            {
                return;
            }
            targetEntity = collidedEnemy;
            trexAI.turnCompass.LookAt(targetEntity.transform.position);
            if (collidedEnemy.enemyType.SizeLimit == NavSizeLimit.MediumSpaces)
            {
                trexAI.StartCoroutine(AttackEnemy("attackEnemyHigh", 10));
            }
            else
            {
                trexAI.StartCoroutine(AttackEnemy("attackEnemyLow", 15));
            }
        }

        private IEnumerator AttackEnemy(string animationName, int damage)
        {
            IsAttacking = true;
            pendingDamage = damage;
            // Evaluate the contact event locally even if the broadcast is delayed.
            trexAI.creatureAnimator.SetTrigger(animationName);
            trexAI.DoAnimationServerRpc(animationName, playedOnOwner: true);
            yield return new WaitForSeconds(AttackRecoverySeconds);
            pendingDamage = 0;
            targetEntity = null;
            IsAttacking = false;
        }

        public void HitAtContact()
        {
            if (pendingDamage == 0)
            {
                return;
            }

            int damage = pendingDamage;
            pendingDamage = 0;
            if (targetEntity != null && !targetEntity.isEnemyDead)
            {
                targetEntity.HitEnemyOnLocalClient(damage, playHitSFX: true);
            }
        }
    }
}
