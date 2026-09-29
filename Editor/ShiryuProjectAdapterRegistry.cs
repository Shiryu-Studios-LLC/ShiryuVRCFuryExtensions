using System;
using System.Collections.Generic;
using System.Linq;

namespace ShiryuStudios.VRCFuryExtensions.Editor {
    /// <summary>
    /// Optional bridge for project-specific compatibility layers. The reusable package never needs
    /// to know an avatar name or project path; an adapter living in Assets/ may register itself and
    /// wrap the generic build with any project-only preparation/special controls it still requires.
    /// </summary>
    public static class ShiryuProjectAdapterRegistry {
        private sealed class Adapter {
            public string id;
            public int priority;
            public Func<ShiryuAvatarProfile, bool> canHandle;
            public Func<ShiryuAvatarProfile, ShiryuBuildResult> build;
            public Action<ShiryuAvatarProfile> migrate;
        }

        private static readonly List<Adapter> Adapters = new List<Adapter>();

        public static void Register(
            string id,
            Func<ShiryuAvatarProfile, bool> canHandle,
            Func<ShiryuAvatarProfile, ShiryuBuildResult> build = null,
            Action<ShiryuAvatarProfile> migrate = null,
            int priority = 0
        ) {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Adapter id is required.", "id");
            Adapters.RemoveAll(x => string.Equals(x.id, id, StringComparison.Ordinal));
            Adapters.Add(new Adapter {
                id = id,
                priority = priority,
                canHandle = canHandle ?? (_ => false),
                build = build,
                migrate = migrate
            });
            Adapters.Sort((a, b) => b.priority.CompareTo(a.priority));
        }

        public static void Unregister(string id) {
            if (string.IsNullOrWhiteSpace(id)) return;
            Adapters.RemoveAll(x => string.Equals(x.id, id, StringComparison.Ordinal));
        }

        public static bool HasBuildAdapter(ShiryuAvatarProfile profile) {
            return Find(profile, x => x.build != null) != null;
        }

        public static bool HasMigrationAdapter(ShiryuAvatarProfile profile) {
            return Find(profile, x => x.migrate != null) != null;
        }

        public static bool TryBuild(ShiryuAvatarProfile profile, out ShiryuBuildResult result) {
            var adapter = Find(profile, x => x.build != null);
            if (adapter == null) {
                result = null;
                return false;
            }
            result = adapter.build(profile) ?? new ShiryuBuildResult();
            return true;
        }

        public static bool TryMigrate(ShiryuAvatarProfile profile) {
            var adapter = Find(profile, x => x.migrate != null);
            if (adapter == null) return false;
            adapter.migrate(profile);
            return true;
        }

        private static Adapter Find(ShiryuAvatarProfile profile, Func<Adapter, bool> filter) {
            if (profile == null) return null;
            return Adapters.FirstOrDefault(x => filter(x) && SafeCanHandle(x, profile));
        }

        private static bool SafeCanHandle(Adapter adapter, ShiryuAvatarProfile profile) {
            try {
                return adapter.canHandle(profile);
            } catch {
                return false;
            }
        }
    }
}
