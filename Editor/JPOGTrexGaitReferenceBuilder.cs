using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JPOGBuild
{
    /// <summary>
    /// The walk and run clips play on the additive "Walking layer", on top of the breathing idle of the base layer.
    /// Unity measures an additive clip against its own first frame unless it has a reference pose. The first frame of
    /// the gait clips is mid-stride, so the difference between that stride and the idle pose stayed on the legs:
    /// they ran with bent joints and the right foot and toes tilted up.
    /// This builds a one-frame reference pose for each gait clip. The bones of the gait mask use the base layer's idle
    /// pose, so on those bones the layer reproduces the authored walk and run. The other bones keep the clip's first frame,
    /// so the synced "Upper Body Gait" layer, which plays the same clips, stays as it is.
    /// Run it again after changing the gait clips, the idle clip or the model's default pose.
    /// Batch mode: -executeMethod JPOGBuild.JPOGTrexGaitReferenceBuilder.Build
    /// </summary>
    public static class JPOGTrexGaitReferenceBuilder
    {
        const string Prefab = "JPOGTrex";
        const string Animator = "JPOGTRexAnimator";
        const string LegGaitLayer = "Walking layer";
        const string BaseIdleState = "BreathingAnimation";
        const string ReferenceSuffix = "_AdditiveRef";

        [MenuItem("JPOG/Build JPOGTrex gait reference poses")]
        public static void Build()
        {
            var controller = LoadByName<UnityEditor.Animations.AnimatorController>(Animator, "t:AnimatorController");
            var prefab = LoadByName<GameObject>(Prefab, "t:Prefab");

            var layers = controller.layers;
            var gaitLayer = layers.FirstOrDefault(l => l.name == LegGaitLayer)
                ?? throw new Exception("[JPOGTrex] Animator layer " + LegGaitLayer + " not found");
            if (gaitLayer.blendingMode != UnityEditor.Animations.AnimatorLayerBlendingMode.Additive)
                throw new Exception("[JPOGTrex] " + LegGaitLayer + " is not additive, a reference pose isn't needed");
            if (gaitLayer.avatarMask == null)
                throw new Exception("[JPOGTrex] " + LegGaitLayer + " has no avatar mask");
            var gaitBones = new HashSet<string>();
            for (int i = 0; i < gaitLayer.avatarMask.transformCount; i++)
                if (gaitLayer.avatarMask.GetTransformActive(i))
                    gaitBones.Add(gaitLayer.avatarMask.GetTransformPath(i));

            var idleState = layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == BaseIdleState)
                ?? throw new Exception("[JPOGTrex] Base layer state " + BaseIdleState + " not found");
            var idleClip = idleState.motion as AnimationClip
                ?? throw new Exception("[JPOGTrex] " + BaseIdleState + " has no animation clip");

            var gaitClips = new List<AnimationClip>();
            CollectClips(gaitLayer.stateMachine, gaitClips);

            // The base pose the gait layer is added to: the model's default pose with the idle's first frame on top.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var animator = instance.GetComponentInChildren<UnityEngine.Animator>()
                    ?? throw new Exception("[JPOGTrex] " + Prefab + " has no Animator");
                Transform root = animator.transform;
                idleClip.SampleAnimation(root.gameObject, 0f);

                foreach (var clip in gaitClips.Distinct())
                    BuildReference(clip, root, gaitBones);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
            AssetDatabase.SaveAssets();
        }

        static void BuildReference(AnimationClip clip, Transform root, HashSet<string> gaitBones)
        {
            string clipPath = AssetDatabase.GetAssetPath(clip);
            string refPath = Path.GetDirectoryName(clipPath).Replace('\\', '/') + "/" + clip.name + ReferenceSuffix + ".anim";
            var reference = AssetDatabase.LoadAssetAtPath<AnimationClip>(refPath);
            if (reference == null)
            {
                reference = new AnimationClip();
                AssetDatabase.CreateAsset(reference, refPath);
            }
            reference.ClearCurves();
            reference.frameRate = clip.frameRate;

            int fromIdle = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                float value = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0f);
                Transform bone = root.Find(binding.path);
                if (bone != null && gaitBones.Contains(binding.path) && binding.type == typeof(Transform)
                    && TryGetTransformValue(bone, binding.propertyName, out float baseValue))
                {
                    value = baseValue;
                    fromIdle++;
                }
                // Two keys, so Unity keeps the curve as a constant instead of dropping a single-key clip.
                AnimationUtility.SetEditorCurve(reference, binding, AnimationCurve.Constant(0f, 1f / clip.frameRate, value));
            }
            reference.EnsureQuaternionContinuity();
            EditorUtility.SetDirty(reference);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.hasAdditiveReferencePose = true;
            settings.additiveReferencePoseClip = reference;
            settings.additiveReferencePoseTime = 0f;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            Debug.Log("[JPOGTrex] " + clip.name + ": reference pose " + refPath + " (" + fromIdle + " curves from the idle pose)");
        }

        static bool TryGetTransformValue(Transform bone, string property, out float value)
        {
            Quaternion r = bone.localRotation;
            Vector3 p = bone.localPosition;
            Vector3 s = bone.localScale;
            switch (property)
            {
                case "m_LocalRotation.x": value = r.x; return true;
                case "m_LocalRotation.y": value = r.y; return true;
                case "m_LocalRotation.z": value = r.z; return true;
                case "m_LocalRotation.w": value = r.w; return true;
                case "m_LocalPosition.x": value = p.x; return true;
                case "m_LocalPosition.y": value = p.y; return true;
                case "m_LocalPosition.z": value = p.z; return true;
                case "m_LocalScale.x": value = s.x; return true;
                case "m_LocalScale.y": value = s.y; return true;
                case "m_LocalScale.z": value = s.z; return true;
                default: value = 0f; return false;
            }
        }

        static void CollectClips(UnityEditor.Animations.AnimatorStateMachine machine, List<AnimationClip> clips)
        {
            foreach (var child in machine.states) CollectClips(child.state.motion, clips);
            foreach (var child in machine.stateMachines) CollectClips(child.stateMachine, clips);
        }

        static void CollectClips(Motion motion, List<AnimationClip> clips)
        {
            if (motion is AnimationClip clip) clips.Add(clip);
            else if (motion is UnityEditor.Animations.BlendTree tree)
                foreach (var child in tree.children) CollectClips(child.motion, clips);
        }

        static T LoadByName<T>(string name, string filter) where T : UnityEngine.Object
        {
            string path = AssetDatabase.FindAssets(name + " " + filter)
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name && p.Contains("/JPOGTrex/"));
            if (path == null) throw new Exception("[JPOGTrex] " + name + " not found");
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }
    }
}
