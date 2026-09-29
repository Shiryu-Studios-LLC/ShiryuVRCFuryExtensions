using System;
using System.Collections.Generic;
using System.Linq;

namespace ShiryuStudios.VRCFuryExtensions.Editor {
    /// <summary>
    /// Optional reusable extension points around the generic avatar build pipeline. Projects that
    /// truly need custom preparation can register a small hook without replacing the generic build.
    /// Most avatars should need no adapter at all; their behavior lives in ShiryuAvatarProfile.
    /// </summary>
    public static class ShiryuBuildExtensionRegistry {
        private sealed class Extension {
            public string id;
            public int priority;
            public Func<ShiryuAvatarProfile, bool> canHandle;
            public Action<ShiryuAvatarProfile, ShiryuBuildResult> beforeBuild;
            public Action<ShiryuAvatarProfile, ShiryuBuildResult> afterBuild;
        }

        private static readonly List<Extension> Extensions = new List<Extension>();

        public static void Register(
            string id,
            Func<ShiryuAvatarProfile, bool> canHandle,
            Action<ShiryuAvatarProfile, ShiryuBuildResult> beforeBuild = null,
            Action<ShiryuAvatarProfile, ShiryuBuildResult> afterBuild = null,
            int priority = 0
        ) {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Extension id is required.", "id");
            Extensions.RemoveAll(x => string.Equals(x.id, id, StringComparison.Ordinal));
            Extensions.Add(new Extension {
                id = id,
                priority = priority,
                canHandle = canHandle ?? (_ => false),
                beforeBuild = beforeBuild,
                afterBuild = afterBuild
            });
            Extensions.Sort((a, b) => b.priority.CompareTo(a.priority));
        }

        public static void Unregister(string id) {
            if (string.IsNullOrWhiteSpace(id)) return;
            Extensions.RemoveAll(x => string.Equals(x.id, id, StringComparison.Ordinal));
        }

        internal static void RunBeforeBuild(ShiryuAvatarProfile profile, ShiryuBuildResult result) {
            foreach (var extension in Matching(profile)) {
                if (extension.beforeBuild == null) continue;
                try { extension.beforeBuild(profile, result); }
                catch (Exception e) { result.Error("Build extension '" + extension.id + "' before-build failed: " + e.Message); }
            }
        }

        internal static void RunAfterBuild(ShiryuAvatarProfile profile, ShiryuBuildResult result) {
            foreach (var extension in Matching(profile)) {
                if (extension.afterBuild == null) continue;
                try { extension.afterBuild(profile, result); }
                catch (Exception e) { result.Error("Build extension '" + extension.id + "' after-build failed: " + e.Message); }
            }
        }

        private static IEnumerable<Extension> Matching(ShiryuAvatarProfile profile) {
            if (profile == null) return Enumerable.Empty<Extension>();
            return Extensions.Where(x => SafeCanHandle(x, profile)).ToArray();
        }

        private static bool SafeCanHandle(Extension extension, ShiryuAvatarProfile profile) {
            try { return extension.canHandle(profile); }
            catch { return false; }
        }
    }
}
