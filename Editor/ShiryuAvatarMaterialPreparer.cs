using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ShiryuStudios.VRCFuryExtensions.Editor {
    /// <summary>
    /// Reusable material preparation for any avatar using ShiryuAvatarProfile. Nothing in this
    /// class depends on an avatar name, project folder, or hard-coded renderer hierarchy.
    /// </summary>
    public static class ShiryuAvatarMaterialPreparer {
        public static void Prepare(ShiryuAvatarProfile profile, ShiryuBuildResult result) {
            if (profile == null || profile.AvatarRoot == null || profile.buildSettings == null) return;
            var settings = profile.buildSettings.materials;
            if (settings == null || !settings.enabled) return;

            var materials = CollectMaterials(profile, settings).Where(IsPoiyomiMaterial).Distinct().ToArray();
            if (settings.backupMaterials) BackupMaterials(profile, materials, settings, result);

            var relock = materials.Where(IsPoiyomiMaterialLocked).Where(m => !IsMaterialVariant(m)).ToArray();
            SetPoiyomiMaterialsLocked(relock, false);

            foreach (var material in materials) {
                if (settings.configureTransClipping) ConfigureTransClipping(material, settings);
                if (settings.configureDissolve) ConfigureDissolve(material);
            }

            ApplyAliasRules(settings, result);
            if (settings.markProfilePropertiesAnimated) MarkProfilePropertiesAnimated(profile, result);
            if (settings.applyProfileMaterialDefaults) ApplyProfileDefaults(profile, result);
            ApplyShaderRepairs(profile.AvatarRoot, settings, result);

            SetPoiyomiMaterialsLocked(relock, true);
            result.Info("Material preparation: " + materials.Length + " Poiyomi material(s) prepared from profile settings.");
        }

        private static IEnumerable<Material> CollectMaterials(ShiryuAvatarProfile profile, ShiryuMaterialPreparationSettings settings) {
            var materials = new HashSet<Material>();
            if (settings.includeAllAvatarMaterials) {
                foreach (var renderer in profile.AvatarRoot.GetComponentsInChildren<Renderer>(true)) {
                    foreach (var material in renderer.sharedMaterials) if (material != null) materials.Add(material);
                }
            }

            foreach (var action in EnumerateMaterialActions(profile)) {
                if (action == null || action.renderer == null) continue;
                foreach (var material in MaterialsFor(action.renderer, action.materialSlot)) if (material != null) materials.Add(material);
            }
            foreach (var swap in EnumerateMaterialSwaps(profile)) {
                if (swap != null && swap.enabledMaterial != null) materials.Add(swap.enabledMaterial);
                if (swap == null || swap.renderer == null) continue;
                foreach (var material in MaterialsFor(swap.renderer, swap.materialSlot)) if (material != null) materials.Add(material);
            }
            foreach (var alias in settings.poiyomiAliases ?? new List<ShiryuPoiyomiAliasRule>()) {
                if (alias != null && alias.material != null) materials.Add(alias.material);
            }
            return materials;
        }

        private static IEnumerable<ShiryuMaterialAction> EnumerateMaterialActions(ShiryuAvatarProfile profile) {
            foreach (var control in profile.controls ?? new List<ShiryuControlDefinition>()) {
                if (control == null) continue;
                foreach (var action in control.materialActions ?? new List<ShiryuMaterialAction>()) yield return action;
                foreach (var option in control.discreteOptions ?? new List<ShiryuDiscreteOption>()) {
                    if (option == null) continue;
                    foreach (var action in option.materialActions ?? new List<ShiryuMaterialAction>()) yield return action;
                }
            }
        }

        private static IEnumerable<ShiryuMaterialSwapAction> EnumerateMaterialSwaps(ShiryuAvatarProfile profile) {
            foreach (var control in profile.controls ?? new List<ShiryuControlDefinition>()) {
                if (control == null) continue;
                foreach (var action in control.materialSwapActions ?? new List<ShiryuMaterialSwapAction>()) yield return action;
                foreach (var option in control.discreteOptions ?? new List<ShiryuDiscreteOption>()) {
                    if (option == null) continue;
                    foreach (var action in option.materialSwapActions ?? new List<ShiryuMaterialSwapAction>()) yield return action;
                }
            }
        }

        private static IEnumerable<Material> MaterialsFor(Renderer renderer, int slot) {
            if (renderer == null) yield break;
            var materials = renderer.sharedMaterials;
            if (slot >= 0) {
                if (slot < materials.Length && materials[slot] != null) yield return materials[slot];
                yield break;
            }
            foreach (var material in materials) if (material != null) yield return material;
        }

        private static void BackupMaterials(ShiryuAvatarProfile profile, IEnumerable<Material> materials, ShiryuMaterialPreparationSettings settings, ShiryuBuildResult result) {
            var root = string.IsNullOrWhiteSpace(settings.backupRoot) ? "Assets/ShiryuBackups/VRCFuryExtensions" : settings.backupRoot.TrimEnd('/');
            root += "/" + Sanitize(profile.AvatarRoot.name);
            EnsureFolder(root);
            var copied = 0;
            foreach (var material in materials.Where(m => m != null)) {
                var sourcePath = AssetDatabase.GetAssetPath(material).Replace('\\', '/');
                if (!sourcePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
                var guid = AssetDatabase.AssetPathToGUID(sourcePath);
                var suffix = string.IsNullOrEmpty(guid) ? "material" : guid.Substring(0, Math.Min(8, guid.Length));
                var destination = root + "/" + Sanitize(material.name) + "_" + suffix + ".mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(destination) != null) continue;
                if (AssetDatabase.CopyAsset(sourcePath, destination)) copied++;
            }
            if (copied > 0) result.Info("Material backups: " + copied + " new backup(s) written to " + root + ".");
        }

        private static void ConfigureTransClipping(Material material, ShiryuMaterialPreparationSettings settings) {
            if (!IsPoiyomiMaterial(material)) return;
            SetFloatIf(material, "_Mode", 9f);
            SetFloatIf(material, "_BlendOp", 0f);
            SetFloatIf(material, "_BlendOpAlpha", 4f);
            SetFloatIf(material, "_Cutoff", 0f);
            SetFloatIf(material, "_SrcBlend", 5f);
            SetFloatIf(material, "_DstBlend", 10f);
            SetFloatIf(material, "_SrcBlendAlpha", 1f);
            SetFloatIf(material, "_DstBlendAlpha", 1f);
            SetFloatIf(material, "_AddSrcBlend", 5f);
            SetFloatIf(material, "_AddDstBlend", 1f);
            SetFloatIf(material, "_AddSrcBlendAlpha", 0f);
            SetFloatIf(material, "_AddDstBlendAlpha", 1f);
            SetFloatIf(material, "_AlphaToCoverage", 0f);
            SetFloatIf(material, "_AlphaToMask", 0f);
            SetFloatIf(material, "_ZWrite", 1f);
            SetFloatIf(material, "_ZTest", 4f);
            SetFloatIf(material, "_AlphaPremultiply", 0f);
            SetFloatIf(material, "_AlphaForceOpaque", 0f);
            SetFloatIf(material, "_OutlineSrcBlend", 5f);
            SetFloatIf(material, "_OutlineDstBlend", 10f);
            SetFloatIf(material, "_OutlineSrcBlendAlpha", 1f);
            SetFloatIf(material, "_OutlineDstBlendAlpha", 1f);
            SetFloatIf(material, "_OutlineBlendOp", 0f);
            SetFloatIf(material, "_OutlineBlendOpAlpha", 4f);

            var queue = settings.defaultRenderQueue;
            var renderType = string.IsNullOrWhiteSpace(settings.defaultRenderType) ? "TransparentCutout" : settings.defaultRenderType;
            var path = AssetDatabase.GetAssetPath(material).Replace('\\', '/');
            foreach (var rule in settings.queueRules ?? new List<ShiryuMaterialQueueRule>()) {
                if (!Matches(rule, material, path)) continue;
                queue = rule.renderQueue;
                if (!string.IsNullOrWhiteSpace(rule.renderType)) renderType = rule.renderType;
                break;
            }
            material.renderQueue = queue;
            material.SetOverrideTag("RenderType", renderType);
            MaterialEditor.ApplyMaterialPropertyDrawers(material);
            EditorUtility.SetDirty(material);
        }

        private static bool Matches(ShiryuMaterialQueueRule rule, Material material, string path) {
            if (rule == null) return false;
            if (!string.IsNullOrWhiteSpace(rule.assetPathPrefix) && !path.StartsWith(rule.assetPathPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            if ((rule.excludedPathContains ?? new List<string>()).Any(x => !string.IsNullOrWhiteSpace(x) && path.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) return false;
            if ((rule.excludedMaterialNames ?? new List<string>()).Any(x => string.Equals(x, material.name, StringComparison.OrdinalIgnoreCase))) return false;
            return true;
        }

        private static void ConfigureDissolve(Material material) {
            if (!IsPoiyomiMaterial(material)) return;
            SetFloatIf(material, "_EnableDissolve", 1f);
            SetFloatIf(material, "_DissolveType", 1f);
            SetFloatIf(material, "_DissolveEdgeWidth", 0.055f);
            SetFloatIf(material, "_DissolveEdgeHardness", 0.494f);
            SetFloatIf(material, "_DissolveEdgeEmission", 2f);
            SetFloatIf(material, "_DissolveDetailStrength", 0.22f);
            SetFloatIf(material, "_DissolveDetailEdgeSmoothing", 0.25f);
            SetFloatIf(material, "_DissolveInvertNoise", 0f);
            SetFloatIf(material, "_DissolveInvertDetailNoise", 0f);
            SetFloatIf(material, "_DissolveNoiseTextureUV", 0f);
            SetFloatIf(material, "_DissolveDetailNoiseUV", 0f);
            SetFloatIf(material, "_DissolveAlpha", 0f);
            if (material.HasProperty("_DissolveTextureColor")) {
                var color = material.GetColor("_DissolveTextureColor");
                color.a = 0f;
                material.SetColor("_DissolveTextureColor", color);
            }

            var gradient = AssetDatabase.LoadAssetAtPath<Texture>("Assets/_PoiyomiShaders/Textures/Noise/T_Cloudy_Noise.png");
            var detail = AssetDatabase.LoadAssetAtPath<Texture>("Assets/_PoiyomiShaders/Textures/Noise/T_Voronoi_Noise.png");
            if (gradient != null && material.HasProperty("_DissolveNoiseTexture")) {
                material.SetTexture("_DissolveNoiseTexture", gradient);
                material.SetTextureScale("_DissolveNoiseTexture", new Vector2(2f, 2f));
            }
            if (detail != null && material.HasProperty("_DissolveDetailNoise")) {
                material.SetTexture("_DissolveDetailNoise", detail);
                material.SetTextureScale("_DissolveDetailNoise", new Vector2(4f, 4f));
            }
            MarkPoiyomiAnimated(material, "_DissolveAlpha");
            MaterialEditor.ApplyMaterialPropertyDrawers(material);
            EditorUtility.SetDirty(material);
        }

        private static void ApplyAliasRules(ShiryuMaterialPreparationSettings settings, ShiryuBuildResult result) {
            var ready = 0;
            foreach (var rule in settings.poiyomiAliases ?? new List<ShiryuPoiyomiAliasRule>()) {
                if (rule == null || rule.material == null || string.IsNullOrWhiteSpace(rule.renameSuffix)) continue;
                var material = rule.material;
                var wasLocked = IsPoiyomiMaterialLocked(material);
                if (wasLocked) SetPoiyomiMaterialsLocked(new[] { material }, false);
                material.SetOverrideTag("thry_rename_suffix", rule.renameSuffix.Trim());
                if (rule.enablePostProcessHueSaturation) {
                    SetFloatIf(material, "_PostProcess", 1f);
                    SetFloatIf(material, "_PPHue", rule.defaultHue);
                    SetFloatIf(material, "_PPSaturation", rule.defaultSaturation);
                    material.SetOverrideTag("_PPHueAnimated", "2");
                    material.SetOverrideTag("_PPSaturationAnimated", "2");
                }
                MaterialEditor.ApplyMaterialPropertyDrawers(material);
                EditorUtility.SetDirty(material);
                if (wasLocked) SetPoiyomiMaterialsLocked(new[] { material }, true);
                ready++;
            }
            if (ready > 0) result.Info("Poiyomi unique-property aliases prepared for " + ready + " material(s).");
        }

        private static void MarkProfilePropertiesAnimated(ShiryuAvatarProfile profile, ShiryuBuildResult result) {
            var marked = 0;
            foreach (var action in EnumerateMaterialActions(profile)) {
                if (action == null || action.renderer == null || string.IsNullOrWhiteSpace(action.propertyName)) continue;
                foreach (var material in MaterialsFor(action.renderer, action.materialSlot)) {
                    if (material == null) continue;
                    EnablePoiyomiDependency(material, action.propertyName);
                    if (MarkPoiyomiAnimated(material, action.propertyName)) marked++;
                }
            }
            foreach (var control in profile.controls ?? new List<ShiryuControlDefinition>()) {
                if (control == null || !control.useDissolve || control.targetObject == null) continue;
                foreach (var renderer in control.targetObject.GetComponentsInChildren<Renderer>(true)) {
                    foreach (var material in renderer.sharedMaterials) if (MarkPoiyomiAnimated(material, "_DissolveAlpha")) marked++;
                }
            }
            if (marked > 0) result.Info("Animated material properties: " + marked + " material/property binding(s) marked for Poiyomi optimization.");
        }

        private static void EnablePoiyomiDependency(Material material, string propertyName) {
            if (material == null || string.IsNullOrWhiteSpace(propertyName)) return;
            if (propertyName.StartsWith("_DecalHueShift", StringComparison.Ordinal)) {
                var suffix = propertyName.Substring("_DecalHueShift".Length);
                SetFloatIf(material, "_DecalEnabled" + suffix, 1f);
                SetFloatIf(material, "_DecalHueShiftEnabled" + suffix, 1f);
            } else if (propertyName.StartsWith("_DecalBlendAlpha", StringComparison.Ordinal)) {
                var suffix = propertyName.Substring("_DecalBlendAlpha".Length);
                SetFloatIf(material, "_DecalEnabled" + suffix, 1f);
            }
        }

        private static void ApplyProfileDefaults(ShiryuAvatarProfile profile, ShiryuBuildResult result) {
            var touched = new HashSet<Material>();
            foreach (var control in profile.controls ?? new List<ShiryuControlDefinition>()) {
                if (control == null) continue;
                if (control.kind == ShiryuControlKind.DiscreteRadial && control.discreteOptions != null && control.discreteOptions.Count > 0) {
                    var selected = control.discreteOptions.Where(x => x != null).OrderBy(x => Mathf.Abs(x.center - control.defaultValue)).FirstOrDefault();
                    if (selected != null) foreach (var action in selected.materialActions ?? new List<ShiryuMaterialAction>()) ApplyMaterialAction(action, 1f, touched);
                    continue;
                }
                var value = control.IsRadial ? Mathf.Clamp01(control.defaultValue) : (control.defaultOn ? 1f : 0f);
                foreach (var action in control.materialActions ?? new List<ShiryuMaterialAction>()) ApplyMaterialAction(action, value, touched);
            }
            foreach (var material in touched) {
                MaterialEditor.ApplyMaterialPropertyDrawers(material);
                EditorUtility.SetDirty(material);
            }
            if (touched.Count > 0) result.Info("Material defaults applied to " + touched.Count + " material(s) from the scene profile.");
        }

        private static void ApplyMaterialAction(ShiryuMaterialAction action, float t, HashSet<Material> touched) {
            if (action == null || action.renderer == null || string.IsNullOrWhiteSpace(action.propertyName)) return;
            foreach (var material in MaterialsFor(action.renderer, action.materialSlot)) {
                if (material == null || !material.HasProperty(action.propertyName)) continue;
                EnablePoiyomiDependency(material, action.propertyName);
                switch (action.valueType) {
                    case ShiryuMaterialValueType.Color:
                        material.SetColor(action.propertyName, Color.Lerp(action.disabledColor, action.enabledColor, t));
                        break;
                    case ShiryuMaterialValueType.Vector:
                        material.SetVector(action.propertyName, Vector4.Lerp(action.disabledVector, action.enabledVector, t));
                        break;
                    default:
                        material.SetFloat(action.propertyName, Mathf.Lerp(action.disabledFloat, action.enabledFloat, t));
                        break;
                }
                touched.Add(material);
            }
        }

        private static void ApplyShaderRepairs(GameObject avatar, ShiryuMaterialPreparationSettings settings, ShiryuBuildResult result) {
            var repaired = 0;
            foreach (var rule in settings.shaderRepairs ?? new List<ShiryuShaderRepairRule>()) {
                if (rule == null || string.IsNullOrWhiteSpace(rule.shaderName)) continue;
                var matches = avatar.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(x => x.sharedMaterials)
                    .Where(x => x != null && x.shader != null && string.Equals(x.shader.name, rule.shaderName, StringComparison.Ordinal))
                    .Distinct()
                    .ToArray();
                foreach (var material in matches) {
                    material.renderQueue = rule.renderQueue;
                    if (!string.IsNullOrWhiteSpace(rule.renderType)) material.SetOverrideTag("RenderType", rule.renderType);
                    EditorUtility.SetDirty(material);
                    repaired++;
                }
            }
            if (repaired > 0) result.Info("Shader repair rules updated " + repaired + " material(s).");
        }

        public static bool MarkPoiyomiAnimated(Material material, string propertyName) {
            if (material == null || !material.HasProperty(propertyName)) return false;
            try {
                var props = MaterialEditor.GetMaterialProperties(new UnityEngine.Object[] { material });
                var prop = props.FirstOrDefault(x => x != null && x.name == propertyName);
                if (prop == null) return false;
                var optimizer = FindPoiyomiOptimizerType();
                var method = optimizer != null ? optimizer.GetMethod("SetAnimatedTag", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(MaterialProperty), typeof(string) }, null) : null;
                if (method == null) return false;
                method.Invoke(null, new object[] { prop, "1" });
                EditorUtility.SetDirty(material);
                return true;
            } catch (Exception e) {
                Debug.LogWarning("[Shiryu VRCFury Extensions] Could not mark Poiyomi property animated: " + material.name + " / " + propertyName + " - " + e.Message);
                return false;
            }
        }

        private static Type FindPoiyomiOptimizerType() {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                var type = assembly.GetType("Thry.ShaderOptimizer", false);
                if (type != null) return type;
            }
            return null;
        }

        private static bool IsPoiyomiMaterialLocked(Material material) {
            if (material == null) return false;
            try {
                var optimizer = FindPoiyomiOptimizerType();
                var method = optimizer != null ? optimizer.GetMethod("IsMaterialLocked", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(Material) }, null) : null;
                if (method != null) return Convert.ToBoolean(method.Invoke(null, new object[] { material }));
            } catch { }
            return material.shader != null && material.shader.name.StartsWith("Hidden/Locked/", StringComparison.Ordinal);
        }

        private static bool SetPoiyomiMaterialsLocked(IEnumerable<Material> source, bool locked) {
            var materials = source == null ? new Material[0] : source.Where(x => x != null).Distinct().ToArray();
            if (materials.Length == 0) return true;
            try {
                var optimizer = FindPoiyomiOptimizerType();
                if (optimizer == null) return false;
                var method = optimizer.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(x => x.Name == "SetLockedForAllMaterials" && x.GetParameters().Length == 6);
                if (method == null) return false;
                var output = method.Invoke(null, new object[] { materials, locked ? 1 : 0, false, false, false, null });
                return output == null || Convert.ToBoolean(output);
            } catch (Exception e) {
                Debug.LogWarning("[Shiryu VRCFury Extensions] Could not " + (locked ? "lock" : "unlock") + " Poiyomi materials: " + e.Message);
                return false;
            }
        }

        private static bool IsMaterialVariant(Material material) {
            if (material == null) return false;
            var prop = typeof(Material).GetProperty("isVariant", BindingFlags.Instance | BindingFlags.Public);
            return prop != null && Convert.ToBoolean(prop.GetValue(material, null));
        }

        private static bool IsPoiyomiMaterial(Material material) {
            return material != null && material.shader != null && material.shader.name.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SetFloatIf(Material material, string propertyName, float value) {
            if (material != null && material.HasProperty(propertyName)) material.SetFloat(propertyName, value);
        }

        private static void EnsureFolder(string path) {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent)) return;
            parent = parent.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string Sanitize(string value) {
            value = string.IsNullOrWhiteSpace(value) ? "Avatar" : value;
            foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Replace(' ', '_');
        }
    }
}
