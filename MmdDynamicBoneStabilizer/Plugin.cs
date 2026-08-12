using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using CharaAnime;
using HarmonyLib;
using Studio;
using UnityEngine;

namespace MmdDynamicBoneStabilizer
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("Countd360.CharaAnime.KKS", "2.8")]
    [BepInProcess("CharaStudio.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "codex.koikatu.mmddynamicbonestabilizer";
        public const string Name = "MMD DynamicBone Stabilizer";
        public const string Version = "1.2.2";

        private const int RefreshIntervalFrames = 1800;
        private const int DiscoveryIntervalFrames = 600;
        private const int CleanupIntervalFrames = 300;
        private const int SeekResetCooldownFrames = 30;

        private static readonly string[] MotionAnchorNames =
        {
            "cf_j_root",
            "cf_j_hips",
            "cf_j_head"
        };

        private static readonly FieldInfo OciTargetField =
            AccessTools.Field(typeof(MmddPoseController), "ociTarget");
        private static readonly FieldInfo DynamicBoneParticlesField =
            AccessTools.Field(typeof(DynamicBone), "m_Particles");
        private static readonly FieldInfo DynamicBoneVer02ParticlesField =
            AccessTools.Field(typeof(DynamicBone_Ver02), "Particles");
        private static FieldInfo _dynamicBoneParticleTransformField;
        private static FieldInfo _dynamicBoneParticleParentField;
        private static FieldInfo _dynamicBoneVer02ParticleTransformField;
        private static FieldInfo _dynamicBoneVer02ParticleParentField;

        private readonly Dictionary<int, CharacterPhysics> _characters =
            new Dictionary<int, CharacterPhysics>();
        private readonly Dictionary<int, int> _controllerCharacterIds =
            new Dictionary<int, int>();
        private readonly Dictionary<int, CharacterPhysics> _dynamicBoneOwners =
            new Dictionary<int, CharacterPhysics>();

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _restoreSkippedFrameCompensation;
        private ConfigEntry<bool> _stabilizeUnityCloth;
        private ConfigEntry<bool> _stabilizeSkirtColliders;
        private ConfigEntry<float> _skirtSphereRadiusScale;
        private ConfigEntry<float> _seekResetDistance;
        private ConfigEntry<float> _seekResetAngle;
        private ConfigEntry<float> _motionDetectionDistance;
        private ConfigEntry<float> _motionDetectionAngle;
        private ConfigEntry<bool> _continuousMotionStabilization;
        private ConfigEntry<int> _motionHoldFrames;
        private ConfigEntry<bool> _logTransitions;
        private Harmony _harmony;
        private int _lastDiscoveryFrame = -DiscoveryIntervalFrames;
        private int _lastCleanupFrame;

        internal static Plugin Instance { get; private set; }
        internal bool ManualPhysicsPass { get; private set; }

        private void Awake()
        {
            Instance = this;

            _enabled = Config.Bind("General", "Enabled", true,
                "人物运动时自动稳定头发和衣物；MMD 播放时额外将 DynamicBone 排到姿势写入之后。");
            _restoreSkippedFrameCompensation = Config.Bind("General",
                "Restore skipped-frame compensation", true,
                "恢复 Dynamic Bones Fix 屏蔽的高帧率跳帧补偿。建议保持开启。");
            _stabilizeUnityCloth = Config.Bind("General", "Stabilize Unity Cloth", true,
                "人物运动时自动抑制衣物世界速度尖峰并启用连续碰撞。建议保持开启。");
            _stabilizeSkirtColliders = Config.Bind("General", "Stabilize skirt colliders", true,
                "人物运动时让裙骨逐帧求解，并抑制 KKPE 球形碰撞体造成的反复穿入/推出。");
            _skirtSphereRadiusScale = Config.Bind("General", "Skirt sphere radius scale", 0.90f,
                new ConfigDescription("只在人物运动或 MMD 播放期间缩放裙骨引用的球形碰撞体半径。",
                    new AcceptableValueRange<float>(0.70f, 1f)));
            _motionDetectionDistance = Config.Bind("Motion detection", "Position threshold", 0.0005f,
                new ConfigDescription("身体锚点单帧移动超过该距离时判定人物正在运动。",
                    new AcceptableValueRange<float>(0.0001f, 0.02f)));
            _motionDetectionAngle = Config.Bind("Motion detection", "Rotation threshold", 0.1f,
                new ConfigDescription("身体锚点单帧旋转超过该角度时判定人物正在运动。",
                    new AcceptableValueRange<float>(0.02f, 5f)));
            _continuousMotionStabilization = Config.Bind("Motion detection",
                "Continuous stabilization after motion", true,
                "首次检测到人物运动后持续保持稳定，避免反复切换物理参数造成卡顿。");
            _motionHoldFrames = Config.Bind("Motion detection", "Hold frames", 15,
                new ConfigDescription("关闭持续稳定时，检测不到新运动后继续保持稳定的帧数。",
                    new AcceptableValueRange<int>(1, 120)));
            _seekResetDistance = Config.Bind("Recovery", "Seek reset distance", 0.18f,
                new ConfigDescription("稳定身体锚点单帧移动超过该距离时重置物理。",
                    new AcceptableValueRange<float>(0.05f, 1f)));
            _seekResetAngle = Config.Bind("Recovery", "Seek reset angle", 55f,
                new ConfigDescription("稳定身体锚点单帧旋转超过该角度时重置物理。",
                    new AcceptableValueRange<float>(15f, 180f)));
            _logTransitions = Config.Bind("Diagnostics", "Log transitions", true,
                "仅在人物/MMD 稳定器启停或自动重置时记录日志。");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo(Name + " v" + Version +
                           " loaded (automatic, no tuning required).");
        }

        private void Update()
        {
            if (Time.frameCount - _lastCleanupFrame < CleanupIntervalFrames)
                return;

            _lastCleanupFrame = Time.frameCount;
            CleanupDestroyedCharacters();
        }

        private void LateUpdate()
        {
            if (!_enabled.Value)
            {
                DeactivateAll();
                return;
            }

            DiscoverCharactersIfNeeded();
            foreach (CharacterPhysics state in _characters.Values)
            {
                if (state.CharacterRoot == null ||
                    (state.Controller != null && state.Controller.Enable))
                    continue;
                UpdateMotionStabilization(state);
            }
        }

        private void OnDestroy()
        {
            foreach (CharacterPhysics state in _characters.Values)
            {
                RestoreCloth(state);
                RestoreSkirtPhysics(state);
            }
            if (_harmony != null)
                _harmony.UnpatchSelf();
            _characters.Clear();
            _controllerCharacterIds.Clear();
            _dynamicBoneOwners.Clear();
            Instance = null;
        }

        internal bool AllowAutomaticLateUpdate(Component dynamicBone)
        {
            if (ManualPhysicsPass || !_enabled.Value || dynamicBone == null)
                return true;

            CharacterPhysics state;
            return !_dynamicBoneOwners.TryGetValue(dynamicBone.GetInstanceID(), out state) ||
                   !state.IsMmdActive || state.CharacterRoot == null ||
                   state.Controller == null || !state.Controller.Enable;
        }

        internal bool ShouldRestoreSkippedFrameCompensation(Component dynamicBone)
        {
            if (!_enabled.Value || !_restoreSkippedFrameCompensation.Value || dynamicBone == null)
                return false;
            if (ManualPhysicsPass)
                return true;

            CharacterPhysics state;
            return _dynamicBoneOwners.TryGetValue(dynamicBone.GetInstanceID(), out state) &&
                   (state.IsMmdActive || state.IsMotionActive) && state.CharacterRoot != null;
        }

        internal void RunAfterMmdPose(MmddPoseController controller)
        {
            if (!_enabled.Value || controller == null)
                return;

            CharacterPhysics state = GetOrCreateState(controller);
            if (state == null)
                return;

            if (!controller.Enable)
            {
                bool wasMmdActive = state.IsMmdActive;
                if (wasMmdActive && _logTransitions.Value)
                    Logger.LogInfo("MMD DynamicBone stabilization paused for " + state.CharacterName + ".");
                state.IsMmdActive = false;
                UpdateMotionStabilization(state);
                if (wasMmdActive && !state.IsMotionActive)
                    RestoreStabilization(state);
                return;
            }

            bool activation = !state.IsMmdActive;
            state.IsMmdActive = true;
            state.IsMotionActive = false;
            bool refreshed = RefreshComponentsIfNeeded(state, activation);
            if (activation || refreshed || !state.SettingsApplied)
                ApplyStabilization(state);

            if (activation)
            {
                ResetAll(state);
                ClearClothMotion(state);
                CaptureMotionAnchorPoses(state);
                if (_logTransitions.Value)
                {
                    Logger.LogInfo("MMD DynamicBone stabilization active for " + state.CharacterName +
                                   ": hair/clothes=" + state.DynamicBones.Length +
                                   ", ver02=" + state.DynamicBonesVer02.Length +
                                   ", unityCloth=" + state.ClothComponents.Length +
                                   ", skirtChains=" + state.SkirtChainCount +
                                   ", skirtSpheres=" + state.SkirtSphereCount +
                                   ", motionAnchors=" + state.MotionAnchors.Length + ".");
                }
            }
            else
            {
                ResetOnMotionAnchorJump(state);
            }

            ManualPhysicsPass = true;
            try
            {
                RunDynamicBones(state.DynamicBones);
                RunDynamicBonesVer02(state.DynamicBonesVer02);
            }
            finally
            {
                ManualPhysicsPass = false;
            }

        }

        internal void RemoveController(MmddPoseController controller)
        {
            if (controller == null)
                return;
            int controllerId = controller.GetInstanceID();
            int characterId;
            CharacterPhysics state;
            if (_controllerCharacterIds.TryGetValue(controllerId, out characterId) &&
                _characters.TryGetValue(characterId, out state))
            {
                state.Controller = null;
                state.IsMmdActive = false;
                if (!state.IsMotionActive)
                    RestoreStabilization(state);
            }
            _controllerCharacterIds.Remove(controllerId);
        }

        private CharacterPhysics GetOrCreateState(MmddPoseController controller)
        {
            ObjectCtrlInfo target = OciTargetField == null
                ? null
                : OciTargetField.GetValue(controller) as ObjectCtrlInfo;
            OCIChar character = target as OCIChar;
            if (character == null || character.charInfo == null)
            {
                Logger.LogWarning("MMD pose controller has no character target; stabilization skipped.");
                return null;
            }

            Transform characterRoot = character.charInfo.transform;
            string characterName = character.treeNodeObject == null
                ? character.charInfo.name
                : character.treeNodeObject.textName;
            CharacterPhysics state = GetOrCreateState(characterRoot, characterName);
            state.Controller = controller;
            _controllerCharacterIds[controller.GetInstanceID()] = characterRoot.GetInstanceID();
            return state;
        }

        private CharacterPhysics GetOrCreateState(Transform characterRoot, string characterName)
        {
            if (characterRoot == null)
                return null;
            int id = characterRoot.GetInstanceID();
            CharacterPhysics state;
            if (_characters.TryGetValue(id, out state))
            {
                if (!string.IsNullOrEmpty(characterName))
                    state.CharacterName = characterName;
                return state;
            }

            state = new CharacterPhysics
            {
                CharacterRoot = characterRoot,
                CharacterName = string.IsNullOrEmpty(characterName) ? characterRoot.name : characterName
            };
            _characters[id] = state;
            RefreshComponentsIfNeeded(state, true);
            CaptureMotionAnchorPoses(state);
            return state;
        }

        private void DiscoverCharactersIfNeeded()
        {
            if (Time.frameCount - _lastDiscoveryFrame < DiscoveryIntervalFrames)
                return;

            _lastDiscoveryFrame = Time.frameCount;
            ChaControl[] characters = UnityEngine.Object.FindObjectsOfType<ChaControl>();
            for (int i = 0; i < characters.Length; i++)
            {
                ChaControl character = characters[i];
                if (character != null)
                    GetOrCreateState(character.transform, character.name);
            }
        }

        private void UpdateMotionStabilization(CharacterPhysics state)
        {
            if (state == null || state.CharacterRoot == null ||
                state.LastMotionSampleFrame == Time.frameCount)
                return;

            state.LastMotionSampleFrame = Time.frameCount;
            bool refreshed = RefreshComponentsIfNeeded(state, false);

            bool moved = false;
            bool jumped = false;
            bool hasPreviousPose = false;
            for (int i = 0; i < state.MotionAnchors.Length; i++)
            {
                Transform anchor = state.MotionAnchors[i];
                if (anchor == null)
                    continue;
                RootPose previous;
                if (!state.MotionAnchorPoses.TryGetValue(anchor.GetInstanceID(), out previous))
                    continue;
                hasPreviousPose = true;
                float distance = Vector3.Distance(previous.Position, anchor.position);
                float angle = Quaternion.Angle(previous.Rotation, anchor.rotation);
                if (distance > _motionDetectionDistance.Value || angle > _motionDetectionAngle.Value)
                    moved = true;
                if (distance > _seekResetDistance.Value || angle > _seekResetAngle.Value)
                    jumped = true;
            }

            if (moved && hasPreviousPose)
            {
                state.HasDetectedMotion = true;
                state.LastMotionFrame = Time.frameCount;
            }
            bool shouldBeActive = state.HasDetectedMotion &&
                                  (_continuousMotionStabilization.Value ||
                                   Time.frameCount - state.LastMotionFrame <= _motionHoldFrames.Value);
            bool activation = shouldBeActive && !state.IsMotionActive;
            bool deactivation = !shouldBeActive && state.IsMotionActive;
            state.IsMotionActive = shouldBeActive;

            if (state.IsMotionActive)
            {
                if (activation || refreshed || !state.SettingsApplied)
                    ApplyStabilization(state);
                bool resetForJump = jumped && !activation &&
                                    Time.frameCount - state.LastSeekResetFrame >=
                                    SeekResetCooldownFrames;
                if (resetForJump)
                {
                    ResetAll(state);
                    ClearClothMotion(state);
                    state.LastSeekResetFrame = Time.frameCount;
                }
                else if (activation)
                    ClearClothMotion(state);
                CaptureMotionAnchorPoses(state);

                if (activation && _logTransitions.Value)
                {
                    Logger.LogInfo("Character motion stabilization active for " + state.CharacterName +
                                   ": hair/clothes=" + state.DynamicBones.Length +
                                   ", ver02=" + state.DynamicBonesVer02.Length +
                                   ", unityCloth=" + state.ClothComponents.Length +
                                   ", skirtChains=" + state.SkirtChainCount +
                                   ", skirtSpheres=" + state.SkirtSphereCount +
                                   ", motionAnchors=" + state.MotionAnchors.Length + ".");
                }
                if (resetForJump && _logTransitions.Value)
                {
                    Logger.LogInfo("Character jump/switch detected for " + state.CharacterName +
                                   "; reset all DynamicBone chains once.");
                }
            }
            else
            {
                CaptureMotionAnchorPoses(state);
                if (deactivation)
                {
                    RestoreStabilization(state);
                    if (_logTransitions.Value)
                        Logger.LogInfo("Character motion stabilization paused for " +
                                       state.CharacterName + ".");
                }
            }
        }

        private bool RefreshComponentsIfNeeded(CharacterPhysics state, bool force)
        {
            if (!force && Time.frameCount - state.LastRefreshFrame < RefreshIntervalFrames)
                return false;

            RemoveDynamicBoneOwnership(state);
            state.LastRefreshFrame = Time.frameCount;
            state.DynamicBones = state.CharacterRoot == null
                ? new DynamicBone[0]
                : state.CharacterRoot.GetComponentsInChildren<DynamicBone>(true);
            state.DynamicBonesVer02 = state.CharacterRoot == null
                ? new DynamicBone_Ver02[0]
                : state.CharacterRoot.GetComponentsInChildren<DynamicBone_Ver02>(true);
            state.ClothComponents = state.CharacterRoot == null
                ? new Cloth[0]
                : state.CharacterRoot.GetComponentsInChildren<Cloth>(true);
            state.MotionAnchors = FindMotionAnchors(state.CharacterRoot);
            RegisterDynamicBoneOwnership(state);
            CountSkirtPhysics(state);
            state.MotionAnchorPoses.Clear();
            return true;
        }

        private static Transform[] FindMotionAnchors(Transform characterRoot)
        {
            if (characterRoot == null)
                return new Transform[0];

            Transform[] all = characterRoot.GetComponentsInChildren<Transform>(true);
            var anchors = new List<Transform>(MotionAnchorNames.Length);
            for (int n = 0; n < MotionAnchorNames.Length; n++)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    Transform candidate = all[i];
                    if (candidate != null && candidate.name == MotionAnchorNames[n])
                    {
                        anchors.Add(candidate);
                        break;
                    }
                }
            }
            return anchors.ToArray();
        }

        private void RemoveDynamicBoneOwnership(CharacterPhysics state)
        {
            foreach (int id in state.DynamicBoneIds)
            {
                CharacterPhysics owner;
                if (_dynamicBoneOwners.TryGetValue(id, out owner) &&
                    ReferenceEquals(owner, state))
                    _dynamicBoneOwners.Remove(id);
            }
            state.DynamicBoneIds.Clear();
        }

        private void RegisterDynamicBoneOwnership(CharacterPhysics state)
        {
            for (int i = 0; i < state.DynamicBones.Length; i++)
            {
                DynamicBone bone = state.DynamicBones[i];
                if (bone != null)
                {
                    _dynamicBoneOwners[bone.GetInstanceID()] = state;
                    state.DynamicBoneIds.Add(bone.GetInstanceID());
                }
            }
            for (int i = 0; i < state.DynamicBonesVer02.Length; i++)
            {
                DynamicBone_Ver02 bone = state.DynamicBonesVer02[i];
                if (bone != null)
                {
                    _dynamicBoneOwners[bone.GetInstanceID()] = state;
                    state.DynamicBoneIds.Add(bone.GetInstanceID());
                }
            }
        }

        private static void RunDynamicBones(DynamicBone[] components)
        {
            for (int i = 0; i < components.Length; i++)
            {
                DynamicBone bone = components[i];
                if (bone == null || !bone.enabled || !bone.gameObject.activeInHierarchy)
                    continue;
                DynamicBoneLateUpdatePatch.CallOriginal(bone);
            }
        }

        private static void RunDynamicBonesVer02(DynamicBone_Ver02[] components)
        {
            for (int i = 0; i < components.Length; i++)
            {
                DynamicBone_Ver02 bone = components[i];
                if (bone == null || !bone.enabled || !bone.gameObject.activeInHierarchy)
                    continue;
                DynamicBoneVer02LateUpdatePatch.CallOriginal(bone);
            }
        }

        private void ResetOnMotionAnchorJump(CharacterPhysics state)
        {
            bool jumped = false;
            if (Time.frameCount - state.LastSeekResetFrame >= SeekResetCooldownFrames)
            {
                for (int i = 0; i < state.MotionAnchors.Length; i++)
                {
                    Transform anchor = state.MotionAnchors[i];
                    if (anchor == null)
                        continue;
                    RootPose previous;
                    if (!state.MotionAnchorPoses.TryGetValue(anchor.GetInstanceID(), out previous))
                        continue;
                    if (Vector3.Distance(previous.Position, anchor.position) > _seekResetDistance.Value ||
                        Quaternion.Angle(previous.Rotation, anchor.rotation) > _seekResetAngle.Value)
                    {
                        jumped = true;
                        break;
                    }
                }
            }

            if (jumped)
            {
                ResetAll(state);
                ClearClothMotion(state);
                state.LastSeekResetFrame = Time.frameCount;
                if (_logTransitions.Value)
                {
                    Logger.LogInfo("MMD seek/switch detected for " + state.CharacterName +
                                   "; reset all DynamicBone chains once.");
                }
            }
            CaptureMotionAnchorPoses(state);
        }

        private void ApplyClothStabilization(CharacterPhysics state)
        {
            if (!_stabilizeUnityCloth.Value)
                return;

            for (int i = 0; i < state.ClothComponents.Length; i++)
            {
                Cloth cloth = state.ClothComponents[i];
                if (cloth == null)
                    continue;
                int id = cloth.GetInstanceID();
                if (!state.OriginalCloth.ContainsKey(id))
                    state.OriginalCloth[id] = new ClothSettings(cloth);

                // MMD's realtime pose can contain small frame-to-frame velocity spikes.
                // Keep most of the authored cloth feel, but prevent those world-space
                // spikes and fast collider crossings from exciting the whole garment.
                cloth.damping = Mathf.Max(cloth.damping, 0.18f);
                cloth.worldVelocityScale = Mathf.Clamp(cloth.worldVelocityScale, 0f, 0.35f);
                cloth.worldAccelerationScale = Mathf.Clamp(cloth.worldAccelerationScale, 0f, 0.25f);
                cloth.useContinuousCollision = 1f;
                cloth.sleepThreshold = 0f;
            }
        }

        private static void ClearClothMotion(CharacterPhysics state)
        {
            for (int i = 0; i < state.ClothComponents.Length; i++)
            {
                Cloth cloth = state.ClothComponents[i];
                if (cloth != null && cloth.enabled && cloth.gameObject.activeInHierarchy)
                    cloth.ClearTransformMotion();
            }
        }

        private static void RestoreCloth(CharacterPhysics state)
        {
            for (int i = 0; i < state.ClothComponents.Length; i++)
            {
                Cloth cloth = state.ClothComponents[i];
                if (cloth == null)
                    continue;
                ClothSettings original;
                if (state.OriginalCloth.TryGetValue(cloth.GetInstanceID(), out original))
                    original.Apply(cloth);
            }
            state.OriginalCloth.Clear();
        }

        private void ApplySkirtStabilization(CharacterPhysics state)
        {
            if (!_stabilizeSkirtColliders.Value)
                return;
            float radiusScale = Mathf.Clamp(_skirtSphereRadiusScale.Value, 0.70f, 1f);
            for (int i = 0; i < state.DynamicBones.Length; i++)
            {
                DynamicBone bone = state.DynamicBones[i];
                if (!IsLikelySkirtChain(bone))
                    continue;
                int boneId = bone.GetInstanceID();
                if (!state.OriginalUpdateRates.ContainsKey(boneId))
                    state.OriginalUpdateRates[boneId] = bone.m_UpdateRate;
                // DynamicBone's authored zero-rate mode means one solve per rendered
                // frame, removing 60 Hz collide/skip alternation at high MMD FPS.
                bone.m_UpdateRate = 0f;
                if (bone.m_Colliders == null)
                    continue;
                for (int c = 0; c < bone.m_Colliders.Count; c++)
                {
                    DynamicBoneCollider collider = bone.m_Colliders[c];
                    if (!IsSphereCollider(collider))
                        continue;
                    int colliderId = collider.GetInstanceID();
                    float originalRadius;
                    if (!state.OriginalColliderRadii.TryGetValue(colliderId, out originalRadius))
                    {
                        originalRadius = collider.m_Radius;
                        state.OriginalColliderRadii[colliderId] = originalRadius;
                        state.ColliderReferences[colliderId] = collider;
                    }
                    collider.m_Radius = originalRadius * radiusScale;
                }
            }
        }

        private static void RestoreSkirtPhysics(CharacterPhysics state)
        {
            for (int i = 0; i < state.DynamicBones.Length; i++)
            {
                DynamicBone bone = state.DynamicBones[i];
                if (bone == null)
                    continue;
                float updateRate;
                if (state.OriginalUpdateRates.TryGetValue(bone.GetInstanceID(), out updateRate))
                    bone.m_UpdateRate = updateRate;
            }
            foreach (KeyValuePair<int, DynamicBoneCollider> pair in state.ColliderReferences)
            {
                DynamicBoneCollider collider = pair.Value;
                float radius;
                if (collider != null && state.OriginalColliderRadii.TryGetValue(pair.Key, out radius))
                    collider.m_Radius = radius;
            }
            state.OriginalUpdateRates.Clear();
            state.OriginalColliderRadii.Clear();
            state.ColliderReferences.Clear();
        }

        private static void RestoreStabilization(CharacterPhysics state)
        {
            RestoreCloth(state);
            RestoreSkirtPhysics(state);
            state.SettingsApplied = false;
        }

        private void ApplyStabilization(CharacterPhysics state)
        {
            ApplyClothStabilization(state);
            ApplySkirtStabilization(state);
            state.SettingsApplied = true;
        }

        private void DeactivateAll()
        {
            foreach (CharacterPhysics state in _characters.Values)
            {
                if (!state.IsMmdActive && !state.IsMotionActive)
                    continue;
                RestoreStabilization(state);
                state.IsMmdActive = false;
                state.IsMotionActive = false;
                state.HasDetectedMotion = false;
            }
        }

        private static void CountSkirtPhysics(CharacterPhysics state)
        {
            var spheres = new HashSet<int>();
            int chains = 0;
            for (int i = 0; i < state.DynamicBones.Length; i++)
            {
                DynamicBone bone = state.DynamicBones[i];
                if (!IsLikelySkirtChain(bone))
                    continue;
                chains++;
                if (bone.m_Colliders == null)
                    continue;
                for (int c = 0; c < bone.m_Colliders.Count; c++)
                {
                    DynamicBoneCollider collider = bone.m_Colliders[c];
                    if (IsSphereCollider(collider))
                        spheres.Add(collider.GetInstanceID());
                }
            }
            state.SkirtChainCount = chains;
            state.SkirtSphereCount = spheres.Count;
        }

        private static bool IsSphereCollider(DynamicBoneCollider collider)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy &&
                   collider.m_Radius > 0f && collider.m_Height <= collider.m_Radius + 0.0001f;
        }

        private static bool IsLikelySkirtChain(DynamicBone bone)
        {
            if (bone == null || bone.m_Root == null)
                return false;
            string root = bone.m_Root.name.ToLowerInvariant();
            string path = TransformPath(bone.transform).ToLowerInvariant();
            return root.Contains("skirt") || root.Contains("suso") ||
                   root.Contains("sk_") || root.StartsWith("cf_j_sk") ||
                   path.Contains("skirt") || path.Contains("ct_clothes") ||
                   path.Contains("dress");
        }

        private static string TransformPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;
            string path = transform.name;
            Transform parent = transform.parent;
            int depth = 0;
            while (parent != null && depth++ < 12)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private void ResetAll(CharacterPhysics state)
        {
            for (int i = 0; i < state.DynamicBones.Length; i++)
            {
                DynamicBone bone = state.DynamicBones[i];
                if (bone == null || state.UnsafeResetIds.Contains(bone.GetInstanceID()))
                    continue;
                string reason;
                if (!CanSafelyResetParticles(bone, DynamicBoneParticlesField,
                    ref _dynamicBoneParticleTransformField,
                    ref _dynamicBoneParticleParentField,
                    "m_Transform", "m_ParentIndex", out reason))
                {
                    QuarantineUnsafeReset(state, bone, reason, null);
                    continue;
                }
                try
                {
                    bone.ResetParticlesPosition();
                }
                catch (Exception ex)
                {
                    QuarantineUnsafeReset(state, bone,
                        "ResetParticlesPosition threw", ex);
                }
            }
            for (int i = 0; i < state.DynamicBonesVer02.Length; i++)
            {
                DynamicBone_Ver02 bone = state.DynamicBonesVer02[i];
                if (bone == null || state.UnsafeResetIds.Contains(bone.GetInstanceID()))
                    continue;
                string reason;
                if (!CanSafelyResetParticles(bone, DynamicBoneVer02ParticlesField,
                    ref _dynamicBoneVer02ParticleTransformField,
                    ref _dynamicBoneVer02ParticleParentField,
                    "Transform", "ParentIndex", out reason))
                {
                    QuarantineUnsafeReset(state, bone, reason, null);
                    continue;
                }
                try
                {
                    bone.ResetParticlesPosition();
                }
                catch (Exception ex)
                {
                    QuarantineUnsafeReset(state, bone,
                        "ResetParticlesPosition threw", ex);
                }
            }
            state.MotionAnchorPoses.Clear();
        }

        private static bool CanSafelyResetParticles(object component,
            FieldInfo particlesField, ref FieldInfo transformField,
            ref FieldInfo parentField, string transformFieldName,
            string parentFieldName, out string reason)
        {
            reason = null;
            if (particlesField == null)
            {
                reason = "particle-list metadata unavailable";
                return false;
            }
            IList particles = particlesField.GetValue(component) as IList;
            if (particles == null)
            {
                reason = "particle list is null";
                return false;
            }
            for (int i = 0; i < particles.Count; i++)
            {
                object particle = particles[i];
                if (particle == null)
                {
                    reason = "particle " + i + " is null";
                    return false;
                }
                Type particleType = particle.GetType();
                if (transformField == null || transformField.DeclaringType != particleType)
                    transformField = AccessTools.Field(particleType, transformFieldName);
                if (parentField == null || parentField.DeclaringType != particleType)
                    parentField = AccessTools.Field(particleType, parentFieldName);
                if (transformField == null || parentField == null)
                {
                    reason = "particle metadata unavailable";
                    return false;
                }

                Transform transform = transformField.GetValue(particle) as Transform;
                if (transform != null)
                    continue;
                object parentValue = parentField.GetValue(particle);
                if (!(parentValue is int))
                {
                    reason = "particle " + i + " parent index is invalid";
                    return false;
                }
                int parentIndex = (int)parentValue;
                if (parentIndex < 0 || parentIndex >= particles.Count)
                {
                    reason = "particle " + i + " parent index " + parentIndex +
                             " is outside 0.." + (particles.Count - 1);
                    return false;
                }
                object parent = particles[parentIndex];
                if (parent == null || transformField.GetValue(parent) as Transform == null)
                {
                    reason = "particle " + i + " has no valid parent transform";
                    return false;
                }
            }
            return true;
        }

        private void QuarantineUnsafeReset(CharacterPhysics state, Component bone,
            string reason, Exception ex)
        {
            if (bone == null || !state.UnsafeResetIds.Add(bone.GetInstanceID()))
                return;
            Logger.LogWarning("Skipped unsafe DynamicBone reset for " +
                              state.CharacterName + " at " + TransformPath(bone.transform) +
                              ": " + reason +
                              (ex == null ? "." : " (" + ex.GetType().Name +
                               ": " + ex.Message + ").") +
                              " This chain remains active; only forced resets are quarantined.");
        }

        private static void CaptureMotionAnchorPoses(CharacterPhysics state)
        {
            for (int i = 0; i < state.MotionAnchors.Length; i++)
            {
                Transform anchor = state.MotionAnchors[i];
                if (anchor != null)
                    state.MotionAnchorPoses[anchor.GetInstanceID()] = new RootPose(anchor);
            }
        }

        private void CleanupDestroyedCharacters()
        {
            var dead = new List<int>();
            foreach (KeyValuePair<int, CharacterPhysics> pair in _characters)
            {
                if (pair.Value.CharacterRoot == null)
                    dead.Add(pair.Key);
            }
            for (int i = 0; i < dead.Count; i++)
            {
                CharacterPhysics state;
                if (_characters.TryGetValue(dead[i], out state))
                {
                    RestoreStabilization(state);
                    RemoveDynamicBoneOwnership(state);
                }
                _characters.Remove(dead[i]);
            }

            if (dead.Count == 0)
                return;
            var deadControllers = new List<int>();
            foreach (KeyValuePair<int, int> pair in _controllerCharacterIds)
            {
                if (dead.Contains(pair.Value))
                    deadControllers.Add(pair.Key);
            }
            for (int i = 0; i < deadControllers.Count; i++)
                _controllerCharacterIds.Remove(deadControllers[i]);
        }

        private sealed class CharacterPhysics
        {
            public MmddPoseController Controller;
            public Transform CharacterRoot;
            public string CharacterName;
            public DynamicBone[] DynamicBones = new DynamicBone[0];
            public DynamicBone_Ver02[] DynamicBonesVer02 = new DynamicBone_Ver02[0];
            public Cloth[] ClothComponents = new Cloth[0];
            public Transform[] MotionAnchors = new Transform[0];
            public readonly Dictionary<int, ClothSettings> OriginalCloth =
                new Dictionary<int, ClothSettings>();
            public readonly Dictionary<int, float> OriginalUpdateRates =
                new Dictionary<int, float>();
            public readonly Dictionary<int, float> OriginalColliderRadii =
                new Dictionary<int, float>();
            public readonly Dictionary<int, DynamicBoneCollider> ColliderReferences =
                new Dictionary<int, DynamicBoneCollider>();
            public readonly Dictionary<int, RootPose> MotionAnchorPoses =
                new Dictionary<int, RootPose>();
            public readonly HashSet<int> DynamicBoneIds = new HashSet<int>();
            public readonly HashSet<int> UnsafeResetIds = new HashSet<int>();
            public int LastRefreshFrame;
            public int LastSeekResetFrame;
            public int LastMotionSampleFrame = -1;
            public int LastMotionFrame;
            public int SkirtChainCount;
            public int SkirtSphereCount;
            public bool HasDetectedMotion;
            public bool SettingsApplied;
            public bool IsMmdActive;
            public bool IsMotionActive;
        }

        private struct ClothSettings
        {
            private readonly float _damping;
            private readonly float _worldVelocityScale;
            private readonly float _worldAccelerationScale;
            private readonly float _continuousCollision;
            private readonly float _sleepThreshold;

            public ClothSettings(Cloth cloth)
            {
                _damping = cloth.damping;
                _worldVelocityScale = cloth.worldVelocityScale;
                _worldAccelerationScale = cloth.worldAccelerationScale;
                _continuousCollision = cloth.useContinuousCollision;
                _sleepThreshold = cloth.sleepThreshold;
            }

            public void Apply(Cloth cloth)
            {
                cloth.damping = _damping;
                cloth.worldVelocityScale = _worldVelocityScale;
                cloth.worldAccelerationScale = _worldAccelerationScale;
                cloth.useContinuousCollision = _continuousCollision;
                cloth.sleepThreshold = _sleepThreshold;
            }
        }

        private struct RootPose
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;

            public RootPose(Transform transform)
            {
                Position = transform.position;
                Rotation = transform.rotation;
            }
        }

        [HarmonyPatch(typeof(MmddPoseController), "LateUpdate")]
        private static class MmdPoseLateUpdatePatch
        {
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(MmddPoseController __instance)
            {
                if (Instance != null)
                    Instance.RunAfterMmdPose(__instance);
            }
        }

        [HarmonyPatch(typeof(MmddPoseController), "OnDestroy")]
        private static class MmdPoseDestroyPatch
        {
            [HarmonyPrefix]
            private static void Prefix(MmddPoseController __instance)
            {
                if (Instance != null)
                    Instance.RemoveController(__instance);
            }
        }

        [HarmonyPatch(typeof(DynamicBone), "LateUpdate")]
        internal static class DynamicBoneLateUpdatePatch
        {
            [HarmonyPrefix]
            private static bool Prefix(DynamicBone __instance)
            {
                return Instance == null || Instance.AllowAutomaticLateUpdate(__instance);
            }

            [HarmonyReversePatch]
            [HarmonyPatch(typeof(DynamicBone), "LateUpdate")]
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static void CallOriginal(DynamicBone instance)
            {
                throw new NotSupportedException("Harmony reverse patch was not applied.");
            }
        }

        [HarmonyPatch(typeof(DynamicBone_Ver02), "LateUpdate")]
        internal static class DynamicBoneVer02LateUpdatePatch
        {
            [HarmonyPrefix]
            private static bool Prefix(DynamicBone_Ver02 __instance)
            {
                return Instance == null || Instance.AllowAutomaticLateUpdate(__instance);
            }

            [HarmonyReversePatch]
            [HarmonyPatch(typeof(DynamicBone_Ver02), "LateUpdate")]
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static void CallOriginal(DynamicBone_Ver02 instance)
            {
                throw new NotSupportedException("Harmony reverse patch was not applied.");
            }
        }

        [HarmonyPatch(typeof(DynamicBone), "SkipUpdateParticles")]
        private static class DynamicBoneSkippedFramePatch
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(DynamicBone __instance)
            {
                if (Instance == null || !Instance.ShouldRestoreSkippedFrameCompensation(__instance))
                    return true;
                CallOriginal(__instance);
                return false;
            }

            [HarmonyReversePatch]
            [HarmonyPatch(typeof(DynamicBone), "SkipUpdateParticles")]
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void CallOriginal(DynamicBone instance)
            {
                throw new NotSupportedException("Harmony reverse patch was not applied.");
            }
        }

        [HarmonyPatch(typeof(DynamicBone_Ver02), "SkipUpdateParticles")]
        private static class DynamicBoneVer02SkippedFramePatch
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(DynamicBone_Ver02 __instance)
            {
                if (Instance == null || !Instance.ShouldRestoreSkippedFrameCompensation(__instance))
                    return true;
                CallOriginal(__instance);
                return false;
            }

            [HarmonyReversePatch]
            [HarmonyPatch(typeof(DynamicBone_Ver02), "SkipUpdateParticles")]
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void CallOriginal(DynamicBone_Ver02 instance)
            {
                throw new NotSupportedException("Harmony reverse patch was not applied.");
            }
        }
    }
}
