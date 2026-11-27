using System.Collections;
using UnityEngine;

namespace JPOGTrex
{
    //Handles the T-rex attacking other enemies it collides with
    internal class TrexEnemyAttackHelper
    {
        private readonly JPOGTrexAI trexAI;
        private readonly float attackDelay = 1.2f;
        private EnemyAI? targetEntity;

        public bool IsAttacking { get; private set; }

        public TrexEnemyAttackHelper(JPOGTrexAI trexAI)
        {
            this.trexAI = trexAI;
        }

        //Owner only
        public void TryAttack(EnemyAI collidedEnemy)
        {
            if (collidedEnemy.isEnemyDead)
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
            trexAI.DoAnimationServerRpc(animationName);
            yield return new WaitForSeconds(attackDelay);
            if (targetEntity != null && !targetEntity.isEnemyDead)
            {
                targetEntity.HitEnemyOnLocalClient(damage, playHitSFX: true);
            }
            IsAttacking = false;
        }
    }
}
