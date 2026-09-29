using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShiryuStudios.VRCFuryExtensions.Editor {
    /// <summary>
    /// Keeps an avatar's editable source hidden in Scene view while a VRCFury test copy exists.
    /// This works for every ShiryuAvatarProfile and never depends on an avatar-specific name.
    /// </summary>
    [InitializeOnLoad]
    internal static class ShiryuTestCopyVisibilityWatcher {
        private const string SessionPrefix = "ShiryuStudios.VRCFuryExtensions.TestCopyHidden.";
        private static bool applying;

        static ShiryuTestCopyVisibilityWatcher() {
            EditorApplication.hierarchyChanged -= Refresh;
            EditorApplication.hierarchyChanged += Refresh;
            EditorApplication.delayCall += Refresh;
        }

        private static void Refresh() {
            if (applying || EditorApplication.isPlayingOrWillChangePlaymode) return;
            applying = true;
            try {
                var scene = EditorSceneManager.GetActiveScene();
                if (!scene.IsValid()) return;
                var roots = scene.GetRootGameObjects();
                var profiles = UnityEngine.Object.FindObjectsOfType<ShiryuAvatarProfile>(true)
                    .Where(x => x != null && x.gameObject.scene == scene && x.AvatarRoot != null)
                    .ToArray();
                var visibility = SceneVisibilityManager.instance;

                foreach (var profile in profiles) {
                    if (profile.buildSettings == null || !profile.buildSettings.hideSourceWhenTestCopyExists) continue;
                    var source = profile.AvatarRoot;
                    var name = source.name;
                    var hasTestCopy = roots.Any(root => root != source && (
                        string.Equals(root.name, "VRCF Test Copy for " + name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(root.name, name + "(Clone)", StringComparison.OrdinalIgnoreCase)));
                    var key = SessionPrefix + source.GetInstanceID();
                    var hiddenByUs = SessionState.GetBool(key, false);

                    if (hasTestCopy) {
                        if (!visibility.IsHidden(source)) {
                            visibility.Hide(source, true);
                            SessionState.SetBool(key, true);
                        }
                    } else if (hiddenByUs) {
                        visibility.Show(source, true);
                        SessionState.SetBool(key, false);
                    }
                }
            } finally {
                applying = false;
            }
        }
    }
}
