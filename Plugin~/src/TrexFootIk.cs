using UnityEngine;

namespace JPOGTrex
{
    internal sealed class TrexFootIk
    {
        private readonly Transform root;
        private readonly Leg left;
        private readonly Leg right;
        private readonly int groundMask;
        private readonly RaycastHit[] groundHits = new RaycastHit[8];

        private TrexFootIk(Transform root, Leg left, Leg right)
        {
            this.root = root;
            this.left = left;
            this.right = right;
            groundMask = LayerMask.GetMask("Default", "Room", "Colliders", "NavigationSurface", "MiscLevelGeometry", "Terrain");
        }

        public static TrexFootIk? Create(Animator animator)
        {
            Transform hips = animator.transform.Find("TRex/Scene_Root/D");
            if (hips == null)
            {
                return null;
            }

            Leg? left = Leg.Find(hips, "L");
            Leg? right = Leg.Find(hips, "R");
            return left != null && right != null ? new TrexFootIk(animator.transform, left, right) : null;
        }

        public void RestoreAnimatedPose()
        {
            left.RestoreAnimatedPose();
            right.RestoreAnimatedPose();
        }

        public void Reset()
        {
            left.Reset();
            right.Reset();
        }

        public void Apply(float gaitPhase, float speed, float turn, bool allowPlanting, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            float scale = Mathf.Abs(root.lossyScale.y);
            RaycastHit reference = default;
            bool supported = allowPlanting && TryGround(root.position, scale, out reference);
            float horizontalLock = Mathf.Lerp(1f, 0.3f, Mathf.Abs(turn));
            left.Apply(this, supported ? ContactWeight(gaitPhase, speed) : 0f, reference.point.y, horizontalLock, scale, deltaTime);
            right.Apply(this, supported ? ContactWeight(gaitPhase - 0.5f, speed) : 0f, reference.point.y, horizontalLock, scale, deltaTime);
        }

        private bool TryGround(Vector3 position, float scale, out RaycastHit ground)
        {
            float rootHeight = root.position.y;
            position.y = rootHeight + 0.9f * scale;
            int count = Physics.RaycastNonAlloc(position, Vector3.down, groundHits, 2.2f * scale,
                groundMask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            ground = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                if (hit.distance >= nearest || hit.normal.y < 0.6f || hit.collider.transform.IsChildOf(root))
                {
                    continue;
                }

                nearest = hit.distance;
                ground = hit;
            }

            return !float.IsPositiveInfinity(nearest);
        }

        private static float ContactWeight(float phase, float speed)
        {
            // The authored left contact starts at the wrap, with the right half a cycle later.
            float stepPhase = Mathf.Repeat(phase + 0.03f, 1f);
            float running = Mathf.InverseLerp(6f, 12f, speed);
            float liftPhase = Mathf.Lerp(0.49f, 0.42f, running);
            float plant = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.105f, stepPhase));
            float lift = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(liftPhase - 0.10f, liftPhase, stepPhase));
            return Mathf.Lerp(1f, plant * lift, Mathf.InverseLerp(0.25f, 2f, speed));
        }

        private sealed class Leg
        {
            private readonly Transform thigh;
            private readonly Transform calf;
            private readonly Transform foot;
            private readonly Transform toe;
            private Quaternion animatedThigh;
            private Quaternion animatedCalf;
            private Quaternion animatedFoot;
            private bool poseApplied;
            private Vector3 anchor;
            private bool planted;
            private float weight;
            private Vector3 offset;
            private Quaternion tilt = Quaternion.identity;

            private Leg(Transform thigh, Transform calf, Transform foot, Transform toe)
            {
                this.thigh = thigh;
                this.calf = calf;
                this.foot = foot;
                this.toe = toe;
            }

            public static Leg? Find(Transform hips, string side)
            {
                Transform? thigh = hips.Find($"D_Thigh.{side}");
                Transform? calf = thigh?.Find($"D_Calf.{side}");
                Transform? foot = calf?.Find($"D_HorseLink.{side}/D_Foot.{side}");
                Transform? toe = foot?.Find($"D_Toe0.{side}");
                return thigh != null && calf != null && foot != null && toe != null
                    ? new Leg(thigh, calf, foot, toe)
                    : null;
            }

            public void RestoreAnimatedPose()
            {
                if (!poseApplied)
                {
                    return;
                }

                thigh.localRotation = animatedThigh;
                calf.localRotation = animatedCalf;
                foot.localRotation = animatedFoot;
                poseApplied = false;
            }

            public void Reset()
            {
                RestoreAnimatedPose();
                planted = false;
                weight = 0f;
                offset = Vector3.zero;
                tilt = Quaternion.identity;
            }

            public void Apply(TrexFootIk rig, float contact, float groundHeight, float horizontalLock, float scale, float deltaTime)
            {
                Vector3 toePosition = toe.position;
                Vector3 horizontal = Vector3.ProjectOnPlane(anchor - toePosition, Vector3.up);
                float maxLockDistance = 0.55f * scale;
                if (contact < 0.2f || horizontal.sqrMagnitude > 4f * maxLockDistance * maxLockDistance)
                {
                    planted = false;
                }

                if (!planted && contact >= 0.65f)
                {
                    anchor = toePosition;
                    horizontal = Vector3.zero;
                    planted = true;
                }

                horizontal = planted ? Vector3.ClampMagnitude(horizontal, maxLockDistance) * horizontalLock : Vector3.zero;
                Vector3 targetOffset = Vector3.zero;
                Quaternion targetTilt = Quaternion.identity;
                if (contact > 0f && rig.TryGround(toePosition + horizontal, scale, out RaycastHit ground))
                {
                    targetOffset = horizontal;
                    // Preserve the authored floor clearance, applying the terrain height difference to the pose.
                    targetOffset.y = Mathf.Clamp(ground.point.y - groundHeight, -0.65f * scale, 0.85f * scale);
                    targetTilt = Quaternion.RotateTowards(Quaternion.identity,
                        Quaternion.FromToRotation(Vector3.up, ground.normal), 22f);
                }
                else
                {
                    planted = false;
                    contact = 0f;
                }

                weight = Mathf.MoveTowards(weight, contact, deltaTime / 0.09f);
                float response = 1f - Mathf.Exp(-24f * deltaTime);
                offset = Vector3.Lerp(offset, targetOffset, response);
                tilt = Quaternion.Slerp(tilt, targetTilt, response);
                if (weight <= 0.001f)
                {
                    return;
                }

                Quaternion slope = Quaternion.Slerp(Quaternion.identity, tilt, weight);
                if (offset.sqrMagnitude < 0.000001f && slope.w > 0.9999999f)
                {
                    return;
                }

                Vector3 targetFoot = toePosition + offset * weight - slope * (toePosition - foot.position);
                Solve(targetFoot, slope * foot.rotation);
            }

            private void Solve(Vector3 target, Quaternion footRotation)
            {
                Vector3 hip = thigh.position;
                Vector3 upperLeg = calf.position - hip;
                float upperLength = upperLeg.magnitude;
                float lowerLength = Vector3.Distance(calf.position, foot.position);
                Vector3 toTarget = target - hip;
                Vector3 direction = toTarget.normalized;
                Vector3 bend = Vector3.ProjectOnPlane(upperLeg, direction).normalized;
                if (upperLength < 0.01f || lowerLength < 0.01f || direction == Vector3.zero || bend == Vector3.zero)
                {
                    return;
                }

                float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.01f,
                    upperLength + lowerLength - 0.01f);
                float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
                float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
                Vector3 wantedKnee = hip + direction * along + bend * height;
                Vector3 reachableFoot = hip + direction * distance;

                animatedThigh = thigh.localRotation;
                animatedCalf = calf.localRotation;
                animatedFoot = foot.localRotation;
                poseApplied = true;

                thigh.rotation = Quaternion.FromToRotation(upperLeg, wantedKnee - hip) * thigh.rotation;
                calf.rotation = Quaternion.FromToRotation(foot.position - calf.position, reachableFoot - calf.position) * calf.rotation;
                foot.rotation = footRotation;
            }
        }
    }
}
