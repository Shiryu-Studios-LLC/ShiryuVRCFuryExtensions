using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShiryuStudios.VRCFuryExtensions.Editor {
    public sealed class ShiryuValidationMessage {
        public ShiryuIssueSeverity severity;
        public string message;
        public UnityEngine.Object context;

        public ShiryuValidationMessage(ShiryuIssueSeverity severity, string message, UnityEngine.Object context = null) {
            this.severity = severity;
            this.message = message;
            this.context = context;
        }
    }

    public static class ShiryuReferenceUtility {
        public static void Capture(ShiryuAvatarProfile profile, UnityEngine.Object target, ShiryuReferenceMetadata metadata) {
            if (metadata == null) return;
            if (target == null) {
                metadata.Set("", "", "");
                return;
            }

            var go = ToGameObject(target);
            var avatar = profile != null ? profile.AvatarRoot : null;
            var path = go != null ? GetHierarchyPath(go.transform, avatar != null ? avatar.transform : null) : "";
            var id = "";
            try {
                id = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
            } catch {
                // GlobalObjectId is a fallback only. A direct serialized reference remains primary.
            }
            metadata.Set(id, path, target.name);
        }

        public static string GetHierarchyPath(Transform transform, Transform root = null) {
            if (transform == null) return "";
            if (root != null && transform == root) return "";
            var names = new List<string>();
            var current = transform;
            while (current != null && current != root) {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        public static GameObject ToGameObject(UnityEngine.Object obj) {
            var go = obj as GameObject;
            if (go != null) return go;
            var component = obj as Component;
            return component != null ? component.gameObject : null;
        }

        public static void RefreshAllMetadata(ShiryuAvatarProfile profile) {
            if (profile == null || profile.controls == null) return;
            foreach (var control in profile.controls.Where(x => x != null)) {
                Capture(profile, control.targetObject, control.targetFallback);
                CaptureActions(profile, control.objectActions, control.blendshapeActions, control.materialActions, control.materialSwapActions);
                if (control.discreteOptions == null) continue;
                foreach (var option in control.discreteOptions.Where(x => x != null)) {
                    CaptureActions(profile, option.objectActions, option.blendshapeActions, option.materialActions, option.materialSwapActions);
                }
            }
            EditorUtility.SetDirty(profile);
        }

        private static void CaptureActions(
            ShiryuAvatarProfile profile,
            IEnumerable<ShiryuObjectAction> objectActions,
            IEnumerable<ShiryuBlendshapeAction> blendshapeActions,
            IEnumerable<ShiryuMaterialAction> materialActions,
            IEnumerable<ShiryuMaterialSwapAction> materialSwapActions
        ) {
            if (objectActions != null) foreach (var action in objectActions.Where(x => x != null)) Capture(profile, action.target, action.fallback);
            if (blendshapeActions != null) foreach (var action in blendshapeActions.Where(x => x != null)) Capture(profile, action.renderer, action.fallback);
            if (materialActions != null) foreach (var action in materialActions.Where(x => x != null)) Capture(profile, action.renderer, action.fallback);
            if (materialSwapActions != null) foreach (var action in materialSwapActions.Where(x => x != null)) Capture(profile, action.renderer, action.fallback);
        }

        public static string[] GetBlendshapeNames(SkinnedMeshRenderer renderer) {
            if (renderer == null || renderer.sharedMesh == null) return new string[0];
            var mesh = renderer.sharedMesh;
            var names = new string[mesh.blendShapeCount];
            for (var i = 0; i < names.Length; i++) names[i] = mesh.GetBlendShapeName(i);
            return names;
        }

        public static string[] GetShaderProperties(Renderer renderer, int materialSlot = -1) {
            if (renderer == null) return new string[0];
            var result = new HashSet<string>(StringComparer.Ordinal);
            var materials = renderer.sharedMaterials ?? new Material[0];
            for (var i = 0; i < materials.Length; i++) {
                if (materialSlot >= 0 && i != materialSlot) continue;
                var material = materials[i];
                if (material == null || material.shader == null) continue;
                var shader = material.shader;
                var count = ShaderUtil.GetPropertyCount(shader);
                for (var p = 0; p < count; p++) {
                    var type = ShaderUtil.GetPropertyType(shader, p);
                    if (type == ShaderUtil.ShaderPropertyType.Float ||
                        type == ShaderUtil.ShaderPropertyType.Range ||
                        type == ShaderUtil.ShaderPropertyType.Color ||
                        type == ShaderUtil.ShaderPropertyType.Vector) {
                        result.Add(ShaderUtil.GetPropertyName(shader, p));
                    }
                }
            }
            return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static ShiryuMaterialValueType GuessMaterialValueType(Renderer renderer, int materialSlot, string propertyName) {
            if (renderer == null || string.IsNullOrEmpty(propertyName)) return ShiryuMaterialValueType.Float;
            var materials = renderer.sharedMaterials ?? new Material[0];
            for (var i = 0; i < materials.Length; i++) {
                if (materialSlot >= 0 && i != materialSlot) continue;
                var material = materials[i];
                if (material == null || material.shader == null || !material.HasProperty(propertyName)) continue;
                var shader = material.shader;
                var count = ShaderUtil.GetPropertyCount(shader);
                for (var p = 0; p < count; p++) {
                    if (ShaderUtil.GetPropertyName(shader, p) != propertyName) continue;
                    var type = ShaderUtil.GetPropertyType(shader, p);
                    if (type == ShaderUtil.ShaderPropertyType.Color) return ShiryuMaterialValueType.Color;
                    if (type == ShaderUtil.ShaderPropertyType.Vector) return ShiryuMaterialValueType.Vector;
                    return ShiryuMaterialValueType.Float;
                }
            }
            return ShiryuMaterialValueType.Float;
        }

        public static List<ShiryuValidationMessage> Validate(ShiryuAvatarProfile profile) {
            var messages = new List<ShiryuValidationMessage>();
            if (profile == null) {
                messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, "No ShiryuAvatarProfile selected."));
                return messages;
            }
            if (profile.AvatarRoot == null) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, "Avatar root is missing.", profile));
            if (profile.controls == null || profile.controls.Count == 0) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Warning, "Profile has no controls.", profile));

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var parameters = new Dictionary<string, ShiryuControlDefinition>(StringComparer.Ordinal);
            foreach (var control in (profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null)) {
                var prefix = string.IsNullOrWhiteSpace(control.displayName) ? "Unnamed control" : control.displayName;
                if (string.IsNullOrWhiteSpace(control.id)) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " has no stable id.", profile));
                else if (!ids.Add(control.id)) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " duplicates control id " + control.id + ".", profile));
                if (string.IsNullOrWhiteSpace(control.menuPath)) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " has no menu path.", profile));
                if (string.IsNullOrWhiteSpace(control.parameter)) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " has no parameter.", profile));
                else if (parameters.ContainsKey(control.parameter)) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Warning, prefix + " shares parameter " + control.parameter + " with " + parameters[control.parameter].displayName + ".", profile));
                else parameters.Add(control.parameter, control);

                if (control.driveTargetObject && control.targetObject == null)
                    messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " drives its target object but no GameObject is assigned.", profile));

                ValidateActions(prefix, control.objectActions, control.blendshapeActions, control.materialActions, control.materialSwapActions, messages, profile);

                if (control.kind == ShiryuControlKind.DiscreteRadial) {
                    if (control.discreteOptions == null || control.discreteOptions.Count < 2) {
                        messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " is a Discrete Radial but has fewer than two steps.", profile));
                    } else {
                        foreach (var option in control.discreteOptions.Where(x => x != null)) {
                            ValidateActions(prefix + " / " + option.label, option.objectActions, option.blendshapeActions, option.materialActions, option.materialSwapActions, messages, profile);
                        }
                    }
                }
            }

            var controlIds = new HashSet<string>((profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null).Select(x => x.id), StringComparer.Ordinal);
            foreach (var preset in (profile.presets ?? new List<ShiryuPresetDefinition>()).Where(x => x != null)) {
                if (preset.values == null) continue;
                foreach (var value in preset.values.Where(x => x != null)) {
                    if (!controlIds.Contains(value.controlId)) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Warning, preset.displayName + " references missing control id " + value.controlId + ".", profile));
                }
            }

            if (profile.migrationIssues != null) {
                foreach (var issue in profile.migrationIssues.Where(x => x != null && x.severity != ShiryuIssueSeverity.Info)) {
                    messages.Add(new ShiryuValidationMessage(issue.severity, "Migration: " + issue.message, profile));
                }
            }
            return messages;
        }

        private static void ValidateActions(
            string prefix,
            IEnumerable<ShiryuObjectAction> objectActions,
            IEnumerable<ShiryuBlendshapeAction> blendshapeActions,
            IEnumerable<ShiryuMaterialAction> materialActions,
            IEnumerable<ShiryuMaterialSwapAction> materialSwapActions,
            List<ShiryuValidationMessage> messages,
            UnityEngine.Object context
        ) {
            if (objectActions != null) foreach (var action in objectActions.Where(x => x != null)) {
                if (action.target == null) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " has an object action with no target.", context));
            }
            if (blendshapeActions != null) foreach (var action in blendshapeActions.Where(x => x != null)) {
                if (action.renderer == null) {
                    messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " has a blendshape action with no SkinnedMeshRenderer.", context));
                    continue;
                }
                if (action.renderer.sharedMesh == null || action.renderer.sharedMesh.GetBlendShapeIndex(action.blendShape) < 0)
                    messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " references missing blendshape '" + action.blendShape + "' on " + action.renderer.name + ".", context));
            }
            if (materialActions != null) foreach (var action in materialActions.Where(x => x != null)) {
                if (action.renderer == null) {
                    messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " has a material action with no Renderer.", context));
                    continue;
                }
                var mats = action.renderer.sharedMaterials ?? new Material[0];
                if (action.materialSlot >= mats.Length) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " material slot " + action.materialSlot + " is outside " + action.renderer.name + "'s material array.", context));
                var candidates = mats.Select((m, i) => new { m, i }).Where(x => action.materialSlot < 0 || x.i == action.materialSlot).Select(x => x.m).Where(x => x != null).ToArray();
                if (string.IsNullOrWhiteSpace(action.propertyName)) {
                    messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " has an empty material property name.", context));
                } else if (!candidates.Any(m => m.HasProperty(action.propertyName))) {
                    messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Warning, prefix + " property '" + action.propertyName + "' is not exposed by the renderer's currently assigned materials. This can be valid when another control swaps in a material exposing that property.", context));
                }
            }
            if (materialSwapActions != null) foreach (var action in materialSwapActions.Where(x => x != null)) {
                if (action.renderer == null) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " has a material-swap action with no Renderer.", context));
                else if (action.materialSlot < 0 || action.materialSlot >= action.renderer.sharedMaterials.Length) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " material swap slot " + action.materialSlot + " is invalid on " + action.renderer.name + ".", context));
                if (action.enabledMaterial == null) messages.Add(new ShiryuValidationMessage(ShiryuIssueSeverity.Error, prefix + " has a material-swap action with no enabled material.", context));
            }
        }
    }
}
