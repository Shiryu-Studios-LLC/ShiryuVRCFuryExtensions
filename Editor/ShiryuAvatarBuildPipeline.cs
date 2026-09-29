using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShiryuStudios.VRCFuryExtensions.Editor {
    /// <summary>
    /// General end-to-end builder for any ShiryuAvatarProfile. Avatar-specific behavior is data in
    /// the profile, with optional tiny extension hooks only for genuinely project-specific needs.
    /// </summary>
    public static class ShiryuAvatarBuildPipeline {
        private const string GeneratedRoot = "Assets/ShiryuGenerated/VRCFuryExtensions";

        [MenuItem("Shiryu Studios/VRCFury Extensions/Rebuild Selected Avatar")]
        private static void RebuildSelectedMenu() {
            var profile = FindSelectedProfile();
            if (profile == null) {
                EditorUtility.DisplayDialog("Shiryu VRCFury Extensions", "Select an avatar containing a ShiryuAvatarProfile first.", "OK");
                return;
            }
            var result = Rebuild(profile, true, true);
            Debug.Log("[Shiryu VRCFury Extensions] Build " + (result.Success ? "complete" : "failed") + "\n" + string.Join("\n", result.messages.ToArray()), profile);
        }

        [MenuItem("Shiryu Studios/VRCFury Extensions/Build VRCFury Test Copy")]
        private static void BuildTestCopySelectedMenu() {
            var profile = FindSelectedProfile();
            if (profile == null) {
                EditorUtility.DisplayDialog("Shiryu VRCFury Extensions", "Select an avatar containing a ShiryuAvatarProfile first.", "OK");
                return;
            }
            var result = BuildTestCopy(profile);
            Debug.Log("[Shiryu VRCFury Extensions] Test-copy build " + (result.Success ? "requested" : "failed") + "\n" + string.Join("\n", result.messages.ToArray()), profile);
        }

        public static ShiryuBuildResult BuildTestCopy(ShiryuAvatarProfile profile) {
            var result = Rebuild(profile, true, true);
            if (!result.Success || profile == null || profile.AvatarRoot == null) return result;

            Selection.activeGameObject = profile.AvatarRoot;
            if (!EditorApplication.ExecuteMenuItem("Tools/VRCFury/Build an Editor Test Copy")) {
                result.Error("VRCFury Editor Test Copy menu item could not be executed.");
                return result;
            }

            // VRCFury may build the test copy asynchronously (especially while shaders recompile),
            // so do not inspect for the clone in this same call. The Runtime Showcase discovers and
            // validates the generated copy once VRCFury has finished creating it.
            result.Info("VRCFury Editor Test Copy build requested for '" + profile.AvatarRoot.name + "'.");
            return result;
        }

        public static ShiryuBuildResult Rebuild(ShiryuAvatarProfile profile, bool clearGeneratedRoot = true, bool saveScene = true) {
            var result = new ShiryuBuildResult();
            if (profile == null) {
                result.Error("Profile is null.");
                return result;
            }
            if (profile.AvatarRoot == null) {
                result.Error("Profile has no avatar root.");
                return result;
            }

            // Fail fast on broken references before touching materials. Warnings are reported by the
            // low-level generic builder once, so the pipeline does not duplicate them in reports.
            var validation = ShiryuReferenceUtility.Validate(profile);
            foreach (var message in validation.Where(x => x.severity == ShiryuIssueSeverity.Error)) result.Error(message.message);
            if (!result.Success) return result;

            EnsureAlwaysActive(profile, result);
            ShiryuBuildExtensionRegistry.RunBeforeBuild(profile, result);
            if (!result.Success) return result;

            try { ShiryuAvatarMaterialPreparer.Prepare(profile, result); }
            catch (Exception e) { result.Error("Material preparation failed: " + e.Message); }
            if (!result.Success) return result;

            var generic = ShiryuVRCFuryBuilder.Rebuild(profile, clearGeneratedRoot);
            Merge(result, generic);
            if (!result.Success) return result;

            var customRoot = FindDirectChild(profile.AvatarRoot, string.IsNullOrWhiteSpace(profile.generatedRootName) ? "Shiryu Customization" : profile.generatedRootName.Trim());
            if (customRoot == null) {
                result.Error("Generated customization root was not created.");
                return result;
            }

            try { ShiryuSpecialFeatureBuilder.Build(profile, customRoot, result); }
            catch (Exception e) { result.Error("Special feature build failed: " + e.Message); }

            ShiryuBuildExtensionRegistry.RunAfterBuild(profile, result);
            EnsureAlwaysActive(profile, result);

            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(profile.AvatarRoot);
            EditorSceneManager.MarkSceneDirty(profile.gameObject.scene);
            AssetDatabase.SaveAssets();

            if (profile.buildSettings != null && profile.buildSettings.generateBuildReport) {
                WriteReport(profile, result);
            }

            if (saveScene && profile.gameObject.scene.IsValid() && !string.IsNullOrWhiteSpace(profile.gameObject.scene.path)) {
                EditorSceneManager.SaveScene(profile.gameObject.scene);
            }
            return result;
        }

        private static void EnsureAlwaysActive(ShiryuAvatarProfile profile, ShiryuBuildResult result) {
            if (profile.buildSettings == null || profile.buildSettings.alwaysActiveObjects == null) return;
            var changed = 0;
            foreach (var target in profile.buildSettings.alwaysActiveObjects.Where(x => x != null)) {
                if (target.activeSelf) continue;
                target.SetActive(true);
                EditorUtility.SetDirty(target);
                changed++;
            }
            if (changed > 0) result.Info("Always-active objects restored: " + changed + ".");
        }

        private static void WriteReport(ShiryuAvatarProfile profile, ShiryuBuildResult result) {
            var folder = GeneratedRoot + "/" + Sanitize(profile.AvatarRoot.name);
            EnsureFolder(folder);
            var path = folder + "/BuildReport.txt";
            var lines = new[] {
                "Shiryu VRCFury Extensions Build Report",
                "Avatar: " + profile.AvatarRoot.name,
                "Profile: " + profile.profileName,
                "Schema: " + profile.schemaVersion,
                "Warnings: " + result.warnings,
                "Errors: " + result.errors,
                ""
            }.Concat(result.messages ?? new System.Collections.Generic.List<string>()).ToArray();
            File.WriteAllLines(Path.GetFullPath(path), lines);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static void Merge(ShiryuBuildResult target, ShiryuBuildResult source) {
            if (source == null) return;
            target.messages.AddRange(source.messages);
            target.warnings += source.warnings;
            target.errors += source.errors;
        }

        private static ShiryuAvatarProfile FindSelectedProfile() {
            var selected = Selection.activeGameObject;
            if (selected != null) {
                var profile = selected.GetComponentInParent<ShiryuAvatarProfile>();
                if (profile != null) return profile;
            }
            return UnityEngine.Object.FindObjectsOfType<ShiryuAvatarProfile>(true).FirstOrDefault();
        }

        private static GameObject FindDirectChild(GameObject root, string name) {
            if (root == null) return null;
            foreach (Transform child in root.transform) if (string.Equals(child.name, name, StringComparison.Ordinal)) return child.gameObject;
            return null;
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
