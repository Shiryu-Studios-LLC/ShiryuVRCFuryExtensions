using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using com.vrcfury.api;
using com.vrcfury.api.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace ShiryuStudios.VRCFuryExtensions.Editor {
    public sealed class ShiryuBuildResult {
        public readonly List<string> messages = new List<string>();
        public int warnings;
        public int errors;
        public bool Success { get { return errors == 0; } }

        public void Info(string message) { messages.Add(message); }
        public void Warn(string message) { warnings++; messages.Add("WARN " + message); }
        public void Error(string message) { errors++; messages.Add("ERROR " + message); }
    }

    public static class ShiryuVRCFuryBuilder {
        private const string GeneratedAssetRoot = "Assets/ShiryuGenerated/VRCFuryExtensions";

        [MenuItem("Shiryu Studios/VRCFury Extensions/Rebuild Selected Profile")]
        private static void RebuildSelectedMenu() {
            var profile = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<ShiryuAvatarProfile>() : null;
            if (profile == null) profile = UnityEngine.Object.FindObjectOfType<ShiryuAvatarProfile>();
            if (profile == null) {
                EditorUtility.DisplayDialog("Shiryu VRCFury Extensions", "Select an avatar containing a ShiryuAvatarProfile first.", "OK");
                return;
            }
            var result = ShiryuAvatarBuildPipeline.Rebuild(profile, true, true);
            Debug.Log("[Shiryu VRCFury Extensions] Build " + (result.Success ? "complete" : "failed") + "\n" + string.Join("\n", result.messages.ToArray()), profile);
        }

        public static ShiryuBuildResult Rebuild(ShiryuAvatarProfile profile, bool clearGeneratedRoot) {
            var result = new ShiryuBuildResult();
            if (profile == null) {
                result.Error("Profile is null.");
                return result;
            }
            var avatar = profile.AvatarRoot;
            if (avatar == null) {
                result.Error("Profile has no avatar root.");
                return result;
            }

            var customRoot = FindDirectChild(avatar, profile.generatedRootName);
            if (customRoot == null) {
                customRoot = new GameObject(string.IsNullOrWhiteSpace(profile.generatedRootName) ? "Shiryu Customization" : profile.generatedRootName.Trim());
                Undo.RegisterCreatedObjectUndo(customRoot, "Create Shiryu customization root");
                customRoot.transform.SetParent(avatar.transform, false);
            }
            return BuildInto(profile, customRoot, clearGeneratedRoot);
        }

        public static ShiryuBuildResult BuildInto(ShiryuAvatarProfile profile, GameObject customRoot, bool clearGeneratedRoot) {
            var result = new ShiryuBuildResult();
            if (profile == null || customRoot == null) {
                result.Error("Profile or generated root is null.");
                return result;
            }
            var avatar = profile.AvatarRoot;
            if (avatar == null) {
                result.Error("Profile has no avatar root.");
                return result;
            }

            var validation = ShiryuReferenceUtility.Validate(profile);
            foreach (var message in validation) {
                if (message.severity == ShiryuIssueSeverity.Error) result.Error(message.message);
                else if (message.severity == ShiryuIssueSeverity.Warning) result.Warn(message.message);
            }
            if (result.errors > 0) return result;

            Undo.RegisterFullObjectHierarchyUndo(avatar, "Rebuild Shiryu VRCFury Extensions");
            if (clearGeneratedRoot) ClearChildren(customRoot);

            var builtControls = 0;
            foreach (var control in (profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null)) {
                try {
                    BuildControl(profile, customRoot, control, result);
                    builtControls++;
                } catch (Exception ex) {
                    result.Error("Control " + control.displayName + " failed: " + ex.Message);
                }
            }

            try {
                BuildAuxiliaryController(profile, customRoot, result);
            } catch (Exception ex) {
                result.Error("Auxiliary preset/discrete controller failed: " + ex.Message);
            }

            ShiryuReferenceUtility.RefreshAllMetadata(profile);
            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(avatar);
            EditorSceneManager.MarkSceneDirty(avatar.scene);
            AssetDatabase.SaveAssets();
            result.Info("Generic builder: " + builtControls + " controls from scene-bound profile '" + profile.profileName + "'.");
            return result;
        }

        private static void BuildControl(ShiryuAvatarProfile profile, GameObject customRoot, ShiryuControlDefinition control, ShiryuBuildResult result) {
            if (control == null || string.IsNullOrWhiteSpace(control.menuPath) || string.IsNullOrWhiteSpace(control.parameter)) return;
            var groupRoot = EnsureHierarchy(customRoot, string.IsNullOrWhiteSpace(control.category) ? "Controls/Other" : control.category);
            var holderName = string.IsNullOrWhiteSpace(control.generatedObjectName)
                ? (control.kind == ShiryuControlKind.Radial || control.kind == ShiryuControlKind.DiscreteRadial ? "Radial - " : "Toggle - ") + control.displayName
                : control.generatedObjectName;
            var holder = Holder(groupRoot, holderName);
            RemoveVrcFuryComponents(holder);

            FuryToggle fury = FuryComponents.CreateToggle(holder);
            fury.SetMenuPath(control.menuPath);
            fury.SetGlobalParameter(control.parameter);
            if (control.saved) fury.SetSaved();
            if (control.defaultOn && !control.IsRadial) fury.SetDefaultOn();
            if (control.exclusiveOffState) fury.SetExclusiveOffState();
            if (!string.IsNullOrWhiteSpace(control.exclusiveGroup)) fury.AddExclusiveTag(control.exclusiveGroup.Trim());
            if (control.IsRadial) fury.SetSlider();

            ShiryuVRCFuryBridge.ConfigureExtendedToggle(holder, control);
            if (control.menuIcon != null) ShiryuVRCFuryBridge.SetMenuIcon(holder, control.menuIcon);

            // A Discrete Radial uses this VRCFury toggle only for its radial menu/global parameter.
            // Its step actions are authored into the auxiliary controller below.
            if (control.kind != ShiryuControlKind.DiscreteRadial) {
                var actions = fury.GetActions();
                if (control.driveTargetObject && control.targetObject != null) actions.AddTurnOn(control.targetObject);

                if (control.objectActions != null) {
                    foreach (var action in control.objectActions.Where(x => x != null && x.target != null)) {
                        if (action.activeWhenEnabled) actions.AddTurnOn(action.target);
                        else ShiryuVRCFuryBridge.AddObjectToggle(holder, action.target, false);
                    }
                }

                if (control.blendshapeActions != null) {
                    foreach (var action in control.blendshapeActions.Where(x => x != null && x.renderer != null && !string.IsNullOrWhiteSpace(x.blendShape))) {
                        var index = action.renderer.sharedMesh != null ? action.renderer.sharedMesh.GetBlendShapeIndex(action.blendShape) : -1;
                        if (index >= 0) {
                            action.renderer.SetBlendShapeWeight(index, action.disabledValue);
                            EditorUtility.SetDirty(action.renderer);
                            actions.AddBlendshape(action.blendShape, action.enabledValue, action.renderer);
                        }
                    }
                }

                if (control.materialActions != null) {
                    foreach (var action in control.materialActions.Where(x => x != null && x.renderer != null && !string.IsNullOrWhiteSpace(x.propertyName)))
                        ShiryuVRCFuryBridge.AddMaterialProperty(holder, action, false);
                }
                if (control.materialSwapActions != null) {
                    foreach (var action in control.materialSwapActions.Where(x => x != null && x.renderer != null && x.enabledMaterial != null))
                        ShiryuVRCFuryBridge.AddMaterialSwap(holder, action);
                }

                if (control.useDissolve && control.targetObject != null) {
                    var renderers = control.targetObject.GetComponentsInChildren<Renderer>(true)
                        .Where(r => r != null && r.sharedMaterials.Any(m => m != null && m.HasProperty("_DissolveAlpha"))).ToArray();
                    foreach (var renderer in renderers) {
                        var on = new ShiryuMaterialAction {
                            renderer = renderer,
                            propertyName = "_DissolveAlpha",
                            valueType = ShiryuMaterialValueType.Float,
                            disabledFloat = 1f,
                            enabledFloat = 0f
                        };
                        ShiryuVRCFuryBridge.AddMaterialProperty(holder, on, false);
                        ShiryuVRCFuryBridge.AddTransitionMaterialProperty(holder, renderer, "_DissolveAlpha", 1f);
                    }
                    ShiryuVRCFuryBridge.EnableTransition(holder, control.transitionInSeconds, control.transitionOutSeconds);
                }
            }

            result.Info(ControlTypeLabel(control.kind) + ": " + control.menuPath + " -> " + control.parameter +
                (!string.IsNullOrWhiteSpace(control.exclusiveGroup) ? " [exclusive " + control.exclusiveGroup + "]" : "") +
                (control.targetObject != null ? " [target " + ShiryuReferenceUtility.GetHierarchyPath(control.targetObject.transform, profile.AvatarRoot.transform) + "]" : ""));
        }

        private static string ControlTypeLabel(ShiryuControlKind kind) {
            switch (kind) {
                case ShiryuControlKind.Radial: return "Radial";
                case ShiryuControlKind.DiscreteRadial: return "Discrete radial";
                case ShiryuControlKind.Button: return "Button";
                default: return "Toggle";
            }
        }

        private static void BuildAuxiliaryController(ShiryuAvatarProfile profile, GameObject customRoot, ShiryuBuildResult result) {
            var hasDiscrete = profile.controls != null && profile.controls.Any(x => x != null && x.kind == ShiryuControlKind.DiscreteRadial && x.discreteOptions != null && x.discreteOptions.Count > 0);
            var hasPresets = profile.presets != null && profile.presets.Count > 0;
            var hasRules = profile.entryRules != null && profile.entryRules.Count > 0;
            if (!hasDiscrete && !hasPresets && !hasRules) return;

            EnsureAssetFolder(GeneratedAssetRoot);
            var avatarFolder = GeneratedAssetRoot + "/" + Sanitize(profile.AvatarRoot.name);
            EnsureAssetFolder(avatarFolder);
            var profileName = string.IsNullOrWhiteSpace(profile.profileName) ? "AvatarProfile" : profile.profileName;
            var assetName = Sanitize(profileName) + "_Driver.controller";
            var controllerPath = avatarFolder + "/" + assetName;
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null) AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            foreach (var control in (profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.parameter))) {
                EnsureControllerParameter(controller, control);
            }

            if (hasDiscrete) {
                foreach (var control in profile.controls.Where(x => x != null && x.kind == ShiryuControlKind.DiscreteRadial && x.discreteOptions != null && x.discreteOptions.Count > 0))
                    BuildDiscreteLayer(profile, controller, control, result);
            }

            if (hasPresets) {
                foreach (var preset in profile.presets.Where(x => x != null)) BuildPreset(profile, customRoot, controller, preset, result);
                if (profile.enableRandomizePresets && profile.presets.Count > 1) BuildRandomizer(profile, customRoot, controller, result);
            }

            if (hasRules) {
                foreach (var rule in profile.entryRules.Where(x => x != null)) BuildEntryRule(profile, controller, rule, result);
            }

            var controllerHolder = Holder(EnsureHierarchy(customRoot, "Presets"), "Shiryu Generic Driver");
            RemoveVrcFuryComponents(controllerHolder);
            FuryFullController full = FuryComponents.CreateFullController(controllerHolder);
            full.AddController(controller, VRCAvatarDescriptor.AnimLayerType.FX);
            foreach (var parameter in CollectGlobalParameters(profile).Distinct(StringComparer.Ordinal)) full.AddGlobalParam(parameter);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            result.Info("Auxiliary controller: " + controllerPath + " (presets/discrete radials/entry rules).");
        }

        private static IEnumerable<string> CollectGlobalParameters(ShiryuAvatarProfile profile) {
            foreach (var control in (profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.parameter))) yield return control.parameter;
            foreach (var preset in (profile.presets ?? new List<ShiryuPresetDefinition>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.parameter))) yield return preset.parameter;
            if (profile.enableRandomizePresets && !string.IsNullOrWhiteSpace(profile.randomizeParameter)) yield return profile.randomizeParameter;
            foreach (var rule in (profile.entryRules ?? new List<ShiryuControlEntryRule>()).Where(x => x != null && x.values != null))
                foreach (var value in rule.values.Where(x => x != null && !string.IsNullOrWhiteSpace(x.parameter))) yield return value.parameter;
        }

        private static void EnsureControllerParameter(AnimatorController controller, ShiryuControlDefinition control) {
            if (controller.parameters.Any(p => p.name == control.parameter)) return;
            var type = control.IsRadial ? AnimatorControllerParameterType.Float : AnimatorControllerParameterType.Bool;
            controller.AddParameter(control.parameter, type);
            var parameter = controller.parameters.FirstOrDefault(p => p.name == control.parameter);
            if (parameter == null) return;
            if (type == AnimatorControllerParameterType.Float) parameter.defaultFloat = Mathf.Clamp01(control.defaultValue);
            else parameter.defaultBool = control.defaultOn;
        }

        private static void BuildDiscreteLayer(ShiryuAvatarProfile profile, AnimatorController controller, ShiryuControlDefinition control, ShiryuBuildResult result) {
            var options = control.discreteOptions.Where(x => x != null).OrderBy(x => x.center).ToList();
            if (options.Count == 0) return;
            if (!controller.parameters.Any(p => p.name == control.parameter)) EnsureControllerParameter(controller, control);

            controller.AddLayer("Shiryu Discrete - " + control.displayName);
            var layer = controller.layers[controller.layers.Length - 1];
            var sm = layer.stateMachine;
            var states = new List<AnimatorState>();

            for (var i = 0; i < options.Count; i++) {
                var option = options[i];
                var state = sm.AddState(string.IsNullOrWhiteSpace(option.label) ? "Option " + i : option.label);
                var clip = CreateDiscreteClip(profile, control, option, i);
                AssetDatabase.AddObjectToAsset(clip, controller);
                state.motion = clip;
                state.writeDefaultValues = false;
                states.Add(state);
            }

            var defaultIndex = ClosestOptionIndex(options, Mathf.Clamp01(control.defaultValue));
            sm.defaultState = states[defaultIndex];
            var boundaries = CalculateBoundaries(options);
            const float epsilon = 0.0001f;
            for (var i = 0; i < states.Count; i++) {
                var transition = sm.AddAnyStateTransition(states[i]);
                transition.hasExitTime = false;
                transition.duration = 0f;
                transition.canTransitionToSelf = false;
                if (i > 0) transition.AddCondition(AnimatorConditionMode.Greater, boundaries[i - 1] - epsilon, control.parameter);
                if (i < states.Count - 1) transition.AddCondition(AnimatorConditionMode.Less, boundaries[i] + epsilon, control.parameter);
            }
            result.Info("Discrete radial: " + control.displayName + " -> " + options.Count + " configured steps.");
        }

        private static List<float> CalculateBoundaries(IList<ShiryuDiscreteOption> options) {
            var boundaries = new List<float>();
            for (var i = 0; i < options.Count - 1; i++) boundaries.Add((Mathf.Clamp01(options[i].center) + Mathf.Clamp01(options[i + 1].center)) * 0.5f);
            return boundaries;
        }

        private static int ClosestOptionIndex(IList<ShiryuDiscreteOption> options, float value) {
            var best = 0;
            var distance = float.MaxValue;
            for (var i = 0; i < options.Count; i++) {
                var d = Mathf.Abs(Mathf.Clamp01(options[i].center) - value);
                if (d < distance) { distance = d; best = i; }
            }
            return best;
        }

        private static AnimationClip CreateDiscreteClip(ShiryuAvatarProfile profile, ShiryuControlDefinition control, ShiryuDiscreteOption option, int index) {
            var clip = new AnimationClip { name = Sanitize(control.displayName) + " - " + (string.IsNullOrWhiteSpace(option.label) ? index.ToString() : option.label), frameRate = 60f };
            var avatar = profile.AvatarRoot;
            if (option.objectActions != null) foreach (var action in option.objectActions.Where(x => x != null && x.target != null)) {
                var path = AnimationUtility.CalculateTransformPath(action.target.transform, avatar.transform);
                SetFloatCurve(clip, path, typeof(GameObject), "m_IsActive", action.activeWhenEnabled ? 1f : 0f);
            }
            if (option.blendshapeActions != null) foreach (var action in option.blendshapeActions.Where(x => x != null && x.renderer != null && !string.IsNullOrWhiteSpace(x.blendShape))) {
                var path = AnimationUtility.CalculateTransformPath(action.renderer.transform, avatar.transform);
                SetFloatCurve(clip, path, typeof(SkinnedMeshRenderer), "blendShape." + action.blendShape, action.enabledValue);
            }
            if (option.materialActions != null) foreach (var action in option.materialActions.Where(x => x != null && x.renderer != null && !string.IsNullOrWhiteSpace(x.propertyName))) {
                AddMaterialCurves(clip, avatar, action);
            }
            if (option.materialSwapActions != null) foreach (var action in option.materialSwapActions.Where(x => x != null && x.renderer != null && x.enabledMaterial != null)) {
                if (action.materialSlot < 0 || action.materialSlot >= action.renderer.sharedMaterials.Length) continue;
                var path = AnimationUtility.CalculateTransformPath(action.renderer.transform, avatar.transform);
                var binding = EditorCurveBinding.PPtrCurve(path, action.renderer.GetType(), "m_Materials.Array.data[" + action.materialSlot + "]");
                AnimationUtility.SetObjectReferenceCurve(clip, binding, new[] { new ObjectReferenceKeyframe { time = 0f, value = action.enabledMaterial } });
            }
            return clip;
        }

        private static void AddMaterialCurves(AnimationClip clip, GameObject avatar, ShiryuMaterialAction action) {
            var path = AnimationUtility.CalculateTransformPath(action.renderer.transform, avatar.transform);
            var prefix = "material." + action.propertyName;
            if (action.valueType == ShiryuMaterialValueType.Color) {
                SetFloatCurve(clip, path, action.renderer.GetType(), prefix + ".r", action.enabledColor.r);
                SetFloatCurve(clip, path, action.renderer.GetType(), prefix + ".g", action.enabledColor.g);
                SetFloatCurve(clip, path, action.renderer.GetType(), prefix + ".b", action.enabledColor.b);
                SetFloatCurve(clip, path, action.renderer.GetType(), prefix + ".a", action.enabledColor.a);
            } else if (action.valueType == ShiryuMaterialValueType.Vector) {
                SetFloatCurve(clip, path, action.renderer.GetType(), prefix + ".x", action.enabledVector.x);
                SetFloatCurve(clip, path, action.renderer.GetType(), prefix + ".y", action.enabledVector.y);
                SetFloatCurve(clip, path, action.renderer.GetType(), prefix + ".z", action.enabledVector.z);
                SetFloatCurve(clip, path, action.renderer.GetType(), prefix + ".w", action.enabledVector.w);
            } else {
                SetFloatCurve(clip, path, action.renderer.GetType(), prefix, action.enabledFloat);
            }
        }

        private static void SetFloatCurve(AnimationClip clip, string path, Type type, string property, float value) {
            var binding = EditorCurveBinding.FloatCurve(path, type, property);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f / 60f, value));
        }

        private static void BuildPreset(ShiryuAvatarProfile profile, GameObject customRoot, AnimatorController controller, ShiryuPresetDefinition preset, ShiryuBuildResult result) {
            if (string.IsNullOrWhiteSpace(preset.parameter)) preset.parameter = "Shiryu_Preset_" + Sanitize(preset.displayName);
            if (string.IsNullOrWhiteSpace(preset.menuPath)) preset.menuPath = "PRESETS/" + preset.displayName;

            var holder = Holder(EnsureHierarchy(customRoot, "Presets"), preset.displayName);
            RemoveVrcFuryComponents(holder);
            var toggle = FuryComponents.CreateToggle(holder);
            toggle.SetMenuPath(preset.menuPath);
            toggle.SetGlobalParameter(preset.parameter);
            ShiryuVRCFuryBridge.SetHoldButton(holder, true);

            EnsureBoolParameter(controller, preset.parameter, false);
            controller.AddLayer("Shiryu Preset - " + preset.displayName);
            var layer = controller.layers[controller.layers.Length - 1];
            var sm = layer.stateMachine;
            var idle = sm.AddState("Idle");
            var apply = sm.AddState("Apply " + preset.displayName);
            sm.defaultState = idle;
            var toApply = idle.AddTransition(apply);
            toApply.hasExitTime = false;
            toApply.duration = 0f;
            toApply.AddCondition(AnimatorConditionMode.If, 0f, preset.parameter);
            var toIdle = apply.AddTransition(idle);
            toIdle.hasExitTime = false;
            toIdle.duration = 0f;
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, preset.parameter);

            var driver = apply.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            if (preset.values != null) foreach (var value in preset.values.Where(x => x != null)) {
                var control = profile.FindControl(value.controlId);
                if (control == null || string.IsNullOrWhiteSpace(control.parameter)) continue;
                AddDriverSet(driver, control.parameter, control.IsRadial ? Mathf.Clamp01(value.value) : (value.value >= 0.5f ? 1f : 0f));
            }
            result.Info("Preset: " + preset.displayName + " -> " + (preset.values != null ? preset.values.Count : 0) + " control values.");
        }

        private static void BuildRandomizer(ShiryuAvatarProfile profile, GameObject customRoot, AnimatorController controller, ShiryuBuildResult result) {
            var randomizeParam = string.IsNullOrWhiteSpace(profile.randomizeParameter) ? "Shiryu_Preset_Randomize" : profile.randomizeParameter;
            var menuPath = string.IsNullOrWhiteSpace(profile.randomizeMenuPath) ? "PRESETS/Randomize" : profile.randomizeMenuPath;
            const string rollSuffix = "__Roll";
            var rollParam = randomizeParam + rollSuffix;

            var holder = Holder(EnsureHierarchy(customRoot, "Presets"), "Randomize");
            RemoveVrcFuryComponents(holder);
            var toggle = FuryComponents.CreateToggle(holder);
            toggle.SetMenuPath(menuPath);
            toggle.SetGlobalParameter(randomizeParam);
            ShiryuVRCFuryBridge.SetHoldButton(holder, true);

            EnsureBoolParameter(controller, randomizeParam, false);
            EnsureFloatParameter(controller, rollParam, 0f);
            controller.AddLayer("Shiryu Preset Randomizer");
            var layer = controller.layers[controller.layers.Length - 1];
            var sm = layer.stateMachine;
            var idle = sm.AddState("Idle");
            var roll = sm.AddState("Roll Preset");
            var wait = sm.AddState("Wait For Release");
            sm.defaultState = idle;

            var toRoll = idle.AddTransition(roll);
            toRoll.hasExitTime = false;
            toRoll.duration = 0f;
            toRoll.AddCondition(AnimatorConditionMode.If, 0f, randomizeParam);

            var rollDriver = roll.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            rollDriver.localOnly = true;
            rollDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter {
                name = rollParam,
                type = VRC_AvatarParameterDriver.ChangeType.Random,
                valueMin = 0f,
                valueMax = 1f
            });

            var presets = profile.presets.Where(x => x != null).ToList();
            const float epsilon = 0.0001f;
            for (var i = 0; i < presets.Count; i++) {
                var preset = presets[i];
                var apply = sm.AddState("Random " + preset.displayName);
                var branch = roll.AddTransition(apply);
                branch.hasExitTime = false;
                branch.duration = 0f;
                var lower = i / (float)presets.Count;
                var upper = (i + 1) / (float)presets.Count;
                if (i > 0) branch.AddCondition(AnimatorConditionMode.Greater, lower - epsilon, rollParam);
                if (i < presets.Count - 1) branch.AddCondition(AnimatorConditionMode.Less, upper + epsilon, rollParam);

                var driver = apply.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                driver.localOnly = true;
                if (preset.values != null) foreach (var value in preset.values.Where(x => x != null)) {
                    var control = profile.FindControl(value.controlId);
                    if (control == null || string.IsNullOrWhiteSpace(control.parameter)) continue;
                    AddDriverSet(driver, control.parameter, control.IsRadial ? Mathf.Clamp01(value.value) : (value.value >= 0.5f ? 1f : 0f));
                }
                var toWait = apply.AddTransition(wait);
                toWait.hasExitTime = false;
                toWait.duration = 0f;
            }
            var release = wait.AddTransition(idle);
            release.hasExitTime = false;
            release.duration = 0f;
            release.AddCondition(AnimatorConditionMode.IfNot, 0f, randomizeParam);
            result.Info("Preset Randomize: uses the exact same values stored by the " + presets.Count + " migrated presets.");
        }

        private static void BuildEntryRule(ShiryuAvatarProfile profile, AnimatorController controller, ShiryuControlEntryRule rule, ShiryuBuildResult result) {
            var source = profile.FindControl(rule.sourceControlId);
            if (source == null || string.IsNullOrWhiteSpace(source.parameter) || rule.values == null || rule.values.Count == 0) {
                result.Warn("Entry rule '" + rule.displayName + "' has no valid source control or values.");
                return;
            }
            EnsureControllerParameter(controller, source);
            foreach (var value in rule.values.Where(x => x != null && !string.IsNullOrWhiteSpace(x.parameter))) {
                var target = profile.FindControlByParameter(value.parameter);
                if (target != null) EnsureControllerParameter(controller, target);
                else EnsureFloatParameter(controller, value.parameter, 0f);
            }

            controller.AddLayer("Shiryu Entry - " + rule.displayName);
            var layer = controller.layers[controller.layers.Length - 1];
            var sm = layer.stateMachine;
            var waiting = sm.AddState("Waiting");
            var active = sm.AddState("Active");
            sm.defaultState = waiting;
            var on = waiting.AddTransition(active);
            on.hasExitTime = false;
            on.duration = 0f;
            if (source.IsRadial) on.AddCondition(AnimatorConditionMode.Greater, 0.5f, source.parameter);
            else on.AddCondition(AnimatorConditionMode.If, 0f, source.parameter);
            var off = active.AddTransition(waiting);
            off.hasExitTime = false;
            off.duration = 0f;
            if (source.IsRadial) off.AddCondition(AnimatorConditionMode.Less, 0.5f, source.parameter);
            else off.AddCondition(AnimatorConditionMode.IfNot, 0f, source.parameter);
            var driver = active.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            foreach (var value in rule.values.Where(x => x != null && !string.IsNullOrWhiteSpace(x.parameter))) AddDriverSet(driver, value.parameter, value.value);
            result.Info("Entry rule: " + rule.displayName + " -> one-shot parameter reset on " + source.displayName + " entry.");
        }

        private static void AddDriverSet(VRCAvatarParameterDriver driver, string name, float value) {
            driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter {
                name = name,
                type = VRC_AvatarParameterDriver.ChangeType.Set,
                value = value
            });
        }

        private static void EnsureBoolParameter(AnimatorController controller, string name, bool defaultValue) {
            if (!controller.parameters.Any(p => p.name == name)) controller.AddParameter(name, AnimatorControllerParameterType.Bool);
            var p = controller.parameters.FirstOrDefault(x => x.name == name);
            if (p != null) p.defaultBool = defaultValue;
        }

        private static void EnsureFloatParameter(AnimatorController controller, string name, float defaultValue) {
            if (!controller.parameters.Any(p => p.name == name)) controller.AddParameter(name, AnimatorControllerParameterType.Float);
            var p = controller.parameters.FirstOrDefault(x => x.name == name);
            if (p != null) p.defaultFloat = defaultValue;
        }

        private static GameObject EnsureHierarchy(GameObject root, string path) {
            var current = root;
            foreach (var segment in (path ?? "").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)) current = Holder(current, segment.Trim());
            return current;
        }

        private static GameObject Holder(GameObject root, string name) {
            var existing = FindDirectChild(root, name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return go;
        }

        private static GameObject FindDirectChild(GameObject root, string name) {
            if (root == null || string.IsNullOrWhiteSpace(name)) return null;
            foreach (Transform child in root.transform) if (string.Equals(child.name.Trim(), name.Trim(), StringComparison.Ordinal)) return child.gameObject;
            return null;
        }

        private static void ClearChildren(GameObject root) {
            var children = new List<GameObject>();
            foreach (Transform child in root.transform) children.Add(child.gameObject);
            foreach (var child in children) UnityEngine.Object.DestroyImmediate(child);
        }

        private static void RemoveVrcFuryComponents(GameObject holder) {
            if (holder == null) return;
            foreach (var component in holder.GetComponents<Component>().Where(c => c != null && c.GetType().FullName == "VF.Model.VRCFury").ToArray())
                UnityEngine.Object.DestroyImmediate(component);
        }

        private static void EnsureAssetFolder(string path) {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static string Sanitize(string value) {
            if (string.IsNullOrWhiteSpace(value)) return "Avatar";
            foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Replace('/', '_').Replace(' ', '_');
        }
    }

    internal static class ShiryuVRCFuryBridge {
        private static Type TypeByName(string fullName) {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            throw new InvalidOperationException("VRCFury type not found: " + fullName);
        }

        private static object New(string fullName) {
            var type = TypeByName(fullName);
            var value = Activator.CreateInstance(type, true);
            var latest = type.GetMethod("GetLatestVersion", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var version = type.GetProperty("Version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (latest != null && version != null && version.CanWrite) {
                try { version.SetValue(value, Convert.ToInt32(latest.Invoke(value, null)), null); } catch { }
            }
            return value;
        }

        private static Component GetVrcFury(GameObject holder) {
            return holder.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().FullName == "VF.Model.VRCFury");
        }

        private static object GetContent(GameObject holder) {
            var component = GetVrcFury(holder);
            if (component == null) throw new InvalidOperationException("VRCFury component missing on " + holder.name);
            var field = component.GetType().GetField("content", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("VRCFury content field missing.");
            return field.GetValue(component);
        }

        private static void Set(object target, string fieldName, object value) {
            if (target == null) return;
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Field " + fieldName + " missing on " + target.GetType().FullName);
            if (value != null && field.FieldType.IsEnum && value is string) value = Enum.Parse(field.FieldType, (string)value);
            field.SetValue(target, value);
        }

        private static object Get(object target, string fieldName) {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Field " + fieldName + " missing on " + target.GetType().FullName);
            return field.GetValue(target);
        }

        private static object EnsureState(object content, string fieldName) {
            var state = Get(content, fieldName);
            if (state != null) return state;
            state = New("VF.Model.State");
            Set(content, fieldName, state);
            return state;
        }

        private static void AddAction(object state, object action) {
            var list = Get(state, "actions") as IList;
            if (list == null) throw new InvalidOperationException("VRCFury State.actions is unavailable.");
            list.Add(action);
        }

        public static void ConfigureExtendedToggle(GameObject holder, ShiryuControlDefinition control) {
            var content = GetContent(holder);
            if (control.IsRadial) {
                Set(content, "slider", true);
                Set(content, "sliderInactiveAtZero", control.passthroughAtZero);
                Set(content, "defaultSliderValue", Mathf.Clamp01(control.defaultValue));
            }
            if (control.kind == ShiryuControlKind.Button) Set(content, "holdButton", true);
        }

        public static void SetHoldButton(GameObject holder, bool value) {
            Set(GetContent(holder), "holdButton", value);
        }

        public static void SetMenuIcon(GameObject holder, Texture2D icon) {
            if (icon == null) return;
            var content = GetContent(holder);
            var wrapper = New("VF.Model.GuidTexture2d");
            Set(wrapper, "objRef", icon);
            string guid; long fileId;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(icon, out guid, out fileId)) Set(wrapper, "id", guid + ":" + fileId);
            Set(content, "enableIcon", true);
            Set(content, "icon", wrapper);
        }

        public static void AddObjectToggle(GameObject holder, GameObject target, bool on) {
            if (target == null) return;
            var action = New("VF.Model.StateAction.ObjectToggleAction");
            Set(action, "obj", target);
            Set(action, "mode", on ? "TurnOn" : "TurnOff");
            AddAction(EnsureState(GetContent(holder), "state"), action);
        }

        public static void AddMaterialProperty(GameObject holder, ShiryuMaterialAction action, bool transitionState) {
            if (action == null || action.renderer == null) return;
            var modelAction = New("VF.Model.StateAction.MaterialPropertyAction");
            Set(modelAction, "renderer2", action.renderer.gameObject);
            Set(modelAction, "affectAllMeshes", false);
            Set(modelAction, "propertyName", action.propertyName);
            if (action.valueType == ShiryuMaterialValueType.Color) {
                Set(modelAction, "propertyType", "Color");
                Set(modelAction, "valueColor", action.enabledColor);
            } else if (action.valueType == ShiryuMaterialValueType.Vector) {
                Set(modelAction, "propertyType", "Vector");
                Set(modelAction, "valueVector", action.enabledVector);
            } else {
                Set(modelAction, "propertyType", "Float");
                Set(modelAction, "value", action.enabledFloat);
            }
            var content = GetContent(holder);
            AddAction(EnsureState(content, transitionState ? "transitionStateIn" : "state"), modelAction);
        }

        public static void AddTransitionMaterialProperty(GameObject holder, Renderer renderer, string propertyName, float value) {
            if (renderer == null) return;
            var content = GetContent(holder);
            var inState = EnsureState(content, "transitionStateIn");
            var outState = EnsureState(content, "transitionStateOut");
            AddAction(inState, CreateFloatMaterialProperty(renderer, propertyName, value));
            AddAction(outState, CreateFloatMaterialProperty(renderer, propertyName, value));
        }

        private static object CreateFloatMaterialProperty(Renderer renderer, string propertyName, float value) {
            var modelAction = New("VF.Model.StateAction.MaterialPropertyAction");
            Set(modelAction, "renderer2", renderer.gameObject);
            Set(modelAction, "affectAllMeshes", false);
            Set(modelAction, "propertyName", propertyName);
            Set(modelAction, "propertyType", "Float");
            Set(modelAction, "value", value);
            return modelAction;
        }

        public static void AddMaterialSwap(GameObject holder, ShiryuMaterialSwapAction action) {
            if (action == null || action.renderer == null || action.enabledMaterial == null) return;
            var modelAction = New("VF.Model.StateAction.MaterialAction");
            Set(modelAction, "renderer", action.renderer);
            Set(modelAction, "materialIndex", action.materialSlot);
            var guidMaterial = New("VF.Model.GuidMaterial");
            Set(guidMaterial, "objRef", action.enabledMaterial);
            string guid; long fileId;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(action.enabledMaterial, out guid, out fileId)) Set(guidMaterial, "id", guid + ":" + fileId);
            Set(modelAction, "mat", guidMaterial);
            AddAction(EnsureState(GetContent(holder), "state"), modelAction);
        }

        public static void EnableTransition(GameObject holder, float inSeconds, float outSeconds) {
            var content = GetContent(holder);
            Set(content, "hasTransition", true);
            Set(content, "transitionTimeIn", Mathf.Max(0f, inSeconds));
            Set(content, "transitionTimeOut", Mathf.Max(0f, outSeconds));
            Set(content, "simpleOutTransition", true);
            Set(content, "expandIntoTransition", true);
            EnsureState(content, "transitionStateIn");
            EnsureState(content, "transitionStateOut");
        }
    }
}
