using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShiryuStudios.VRCFuryExtensions {
    public enum ShiryuControlKind {
        Toggle = 0,
        Radial = 1,
        DiscreteRadial = 2,
        Button = 3
    }

    public enum ShiryuMaterialValueType {
        Float = 0,
        Color = 1,
        Vector = 2
    }

    public enum ShiryuIssueSeverity {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    [Serializable]
    public sealed class ShiryuReferenceMetadata {
        [SerializeField] private string globalObjectId = "";
        [SerializeField] private string hierarchyPath = "";
        [SerializeField] private string cachedName = "";

        public string GlobalObjectIdString { get { return globalObjectId ?? ""; } }
        public string HierarchyPath { get { return hierarchyPath ?? ""; } }
        public string CachedName { get { return cachedName ?? ""; } }

        public void Set(string objectId, string path, string name) {
            globalObjectId = objectId ?? "";
            hierarchyPath = path ?? "";
            cachedName = name ?? "";
        }
    }

    [Serializable]
    public sealed class ShiryuObjectAction {
        public GameObject target;
        public bool activeWhenEnabled = true;
        public ShiryuReferenceMetadata fallback = new ShiryuReferenceMetadata();
    }

    [Serializable]
    public sealed class ShiryuBlendshapeAction {
        public SkinnedMeshRenderer renderer;
        public string blendShape = "";
        [Range(0f, 100f)] public float disabledValue = 0f;
        [Range(0f, 100f)] public float enabledValue = 100f;
        public ShiryuReferenceMetadata fallback = new ShiryuReferenceMetadata();
    }

    [Serializable]
    public sealed class ShiryuMaterialAction {
        public Renderer renderer;
        [Tooltip("-1 means all material slots on the selected renderer that expose the property.")]
        public int materialSlot = -1;
        public string propertyName = "";
        public ShiryuMaterialValueType valueType = ShiryuMaterialValueType.Float;
        public float disabledFloat = 0f;
        public float enabledFloat = 1f;
        public Color disabledColor = Color.white;
        public Color enabledColor = Color.white;
        public Vector4 disabledVector = Vector4.zero;
        public Vector4 enabledVector = Vector4.one;
        public ShiryuReferenceMetadata fallback = new ShiryuReferenceMetadata();
    }

    [Serializable]
    public sealed class ShiryuMaterialSwapAction {
        public Renderer renderer;
        public int materialSlot = 0;
        public Material enabledMaterial;
        public ShiryuReferenceMetadata fallback = new ShiryuReferenceMetadata();
    }

    [Serializable]
    public sealed class ShiryuDiscreteOption {
        public string label = "Option";
        [Range(0f, 1f)] public float center = 0.5f;
        public List<ShiryuObjectAction> objectActions = new List<ShiryuObjectAction>();
        public List<ShiryuBlendshapeAction> blendshapeActions = new List<ShiryuBlendshapeAction>();
        public List<ShiryuMaterialAction> materialActions = new List<ShiryuMaterialAction>();
        public List<ShiryuMaterialSwapAction> materialSwapActions = new List<ShiryuMaterialSwapAction>();
    }

    [Serializable]
    public sealed class ShiryuControlDefinition {
        public string id = Guid.NewGuid().ToString("N");
        public string displayName = "New Toggle";
        [Tooltip("Editor/build grouping path, for example Toggles/Accessories or Appearance/Hair.")]
        public string category = "Toggles/Other";
        public string generatedObjectName = "";
        public string menuPath = "Shiryu/New Toggle";
        public string parameter = "Shiryu_NewToggle";
        public ShiryuControlKind kind = ShiryuControlKind.Toggle;
        public bool saved = true;
        public bool includeInPresets = true;
        public bool defaultOn = false;
        [Range(0f, 1f)] public float defaultValue = 0f;
        public bool passthroughAtZero = false;
        public string exclusiveGroup = "";
        public bool exclusiveOffState = false;
        public Texture2D menuIcon;

        [Header("Primary target")]
        public bool driveTargetObject = true;
        public GameObject targetObject;
        public ShiryuReferenceMetadata targetFallback = new ShiryuReferenceMetadata();

        [Header("Transition")]
        public bool useDissolve = false;
        [Min(0f)] public float transitionInSeconds = 0.65f;
        [Min(0f)] public float transitionOutSeconds = 0.65f;

        [Header("Actions")]
        public List<ShiryuObjectAction> objectActions = new List<ShiryuObjectAction>();
        public List<ShiryuBlendshapeAction> blendshapeActions = new List<ShiryuBlendshapeAction>();
        public List<ShiryuMaterialAction> materialActions = new List<ShiryuMaterialAction>();
        public List<ShiryuMaterialSwapAction> materialSwapActions = new List<ShiryuMaterialSwapAction>();
        public List<ShiryuDiscreteOption> discreteOptions = new List<ShiryuDiscreteOption>();

        [TextArea] public string notes = "";
        [HideInInspector] public string legacySourceKey = "";

        public bool IsRadial { get { return kind == ShiryuControlKind.Radial || kind == ShiryuControlKind.DiscreteRadial; } }
    }

    [Serializable]
    public sealed class ShiryuPresetControlValue {
        public string controlId = "";
        [Range(0f, 1f)] public float value = 0f;
    }

    [Serializable]
    public sealed class ShiryuPresetDefinition {
        public string id = Guid.NewGuid().ToString("N");
        public string displayName = "Preset";
        public string menuPath = "PRESETS/Preset";
        public string parameter = "Shiryu_Preset";
        public List<ShiryuPresetControlValue> values = new List<ShiryuPresetControlValue>();

        public float GetValue(string controlId, float fallback = 0f) {
            if (values == null) return fallback;
            var value = values.FirstOrDefault(x => x != null && string.Equals(x.controlId, controlId, StringComparison.Ordinal));
            return value == null ? fallback : Mathf.Clamp01(value.value);
        }

        public void SetValue(string controlId, float value) {
            if (values == null) values = new List<ShiryuPresetControlValue>();
            var existing = values.FirstOrDefault(x => x != null && string.Equals(x.controlId, controlId, StringComparison.Ordinal));
            if (existing == null) {
                values.Add(new ShiryuPresetControlValue { controlId = controlId, value = Mathf.Clamp01(value) });
            } else {
                existing.value = Mathf.Clamp01(value);
            }
        }
    }

    [Serializable]
    public sealed class ShiryuParameterValue {
        public string parameter = "";
        public float value = 0f;
    }

    [Serializable]
    public sealed class ShiryuControlEntryRule {
        [Tooltip("When this toggle control becomes active, write these parameters once on state entry.")]
        public string sourceControlId = "";
        public string displayName = "Entry Rule";
        public List<ShiryuParameterValue> values = new List<ShiryuParameterValue>();
    }

    [Serializable]
    public sealed class ShiryuMigrationIssue {
        public ShiryuIssueSeverity severity = ShiryuIssueSeverity.Warning;
        public string controlId = "";
        public string source = "";
        public string message = "";
        public List<string> candidatePaths = new List<string>();
    }

    [Serializable]
    public sealed class ShiryuMaterialQueueRule {
        [Tooltip("Optional material asset-path prefix. Leave empty to match any path.")]
        public string assetPathPrefix = "";
        public List<string> excludedPathContains = new List<string>();
        public List<string> excludedMaterialNames = new List<string>();
        public int renderQueue = 2450;
        public string renderType = "TransparentCutout";
    }

    [Serializable]
    public sealed class ShiryuShaderRepairRule {
        public string shaderName = "";
        public int renderQueue = 3000;
        public string renderType = "Transparent";
    }

    [Serializable]
    public sealed class ShiryuPoiyomiAliasRule {
        public Material material;
        [Tooltip("Value written to Poiyomi's thry_rename_suffix tag so animated properties are unique per material slot.")]
        public string renameSuffix = "";
        public bool enablePostProcessHueSaturation = false;
        public float defaultHue = 0f;
        public float defaultSaturation = 1f;
    }

    [Serializable]
    public sealed class ShiryuMaterialPreparationSettings {
        public bool enabled = false;
        [Tooltip("When enabled, prepares all Poiyomi materials used anywhere under the avatar, not only materials referenced directly by controls.")]
        public bool includeAllAvatarMaterials = true;
        public bool configureTransClipping = true;
        public bool configureDissolve = true;
        public bool markProfilePropertiesAnimated = true;
        public bool applyProfileMaterialDefaults = true;
        public bool backupMaterials = true;
        public string backupRoot = "Assets/ShiryuBackups/VRCFuryExtensions";
        public int defaultRenderQueue = 2450;
        public string defaultRenderType = "TransparentCutout";
        public List<ShiryuMaterialQueueRule> queueRules = new List<ShiryuMaterialQueueRule>();
        public List<ShiryuShaderRepairRule> shaderRepairs = new List<ShiryuShaderRepairRule>();
        public List<ShiryuPoiyomiAliasRule> poiyomiAliases = new List<ShiryuPoiyomiAliasRule>();
    }

    [Serializable]
    public sealed class ShiryuVisibilityFeatureSettings {
        public bool enableInvisible = false;
        public string invisibleMenuPath = "SPECIAL/Invisible";
        public string invisibleParameter = "Shiryu_Invisible";
        public float transitionSeconds = 0.65f;
        public string dissolveProperty = "_DissolveAlpha";
        public bool enableLocalGhost = false;
        public string ghostMenuPath = "SPECIAL/Ghost Self View";
        public string ghostParameter = "Shiryu_GhostSelfView";
        public string ghostAlphaProperty = "_AlphaMod";
        [Range(-1f, 0f)] public float ghostAlphaModifier = -0.75f;
    }

    [Serializable]
    public sealed class ShiryuAvatarBuildSettings {
        public ShiryuMaterialPreparationSettings materials = new ShiryuMaterialPreparationSettings();
        public ShiryuVisibilityFeatureSettings visibility = new ShiryuVisibilityFeatureSettings();
        public List<GameObject> alwaysActiveObjects = new List<GameObject>();
        public bool hideSourceWhenTestCopyExists = true;
        public bool generateBuildReport = true;
    }

    [Serializable]
    public sealed class ShiryuShowcaseSettings {
        public bool enabled = true;
        public bool includeCrossControlQa = true;
        [Range(1.25f, 12f)] public float secondsPerStep = 3f;
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Shiryu Studios/VRCFury Extensions/Avatar Profile")]
    public sealed class ShiryuAvatarProfile : MonoBehaviour {
        public const int CurrentSchemaVersion = 3;

        public int schemaVersion = CurrentSchemaVersion;
        public string profileName = "Avatar Profile";
        [Tooltip("Usually the VRCAvatarDescriptor root. If unset, this component's GameObject is used.")]
        public GameObject avatarRoot;
        public string generatedRootName = "Shiryu Customization";
        public List<ShiryuControlDefinition> controls = new List<ShiryuControlDefinition>();
        public List<ShiryuPresetDefinition> presets = new List<ShiryuPresetDefinition>();
        public List<ShiryuControlEntryRule> entryRules = new List<ShiryuControlEntryRule>();

        [Header("Preset Randomize")]
        public bool enableRandomizePresets = true;
        public string randomizeMenuPath = "PRESETS/Randomize";
        public string randomizeParameter = "Shiryu_Preset_Randomize";

        [Header("Build Features")]
        public ShiryuAvatarBuildSettings buildSettings = new ShiryuAvatarBuildSettings();

        [Header("Showcase / QA")]
        public ShiryuShowcaseSettings showcaseSettings = new ShiryuShowcaseSettings();

        [Header("Migration")]
        public string migrationSource = "";
        public string migrationTimestampUtc = "";
        public List<ShiryuMigrationIssue> migrationIssues = new List<ShiryuMigrationIssue>();

        public GameObject AvatarRoot { get { return avatarRoot != null ? avatarRoot : gameObject; } }

        public ShiryuControlDefinition FindControl(string id) {
            if (controls == null) return null;
            return controls.FirstOrDefault(x => x != null && string.Equals(x.id, id, StringComparison.Ordinal));
        }

        public ShiryuControlDefinition FindControlByParameter(string parameter) {
            if (controls == null) return null;
            return controls.FirstOrDefault(x => x != null && string.Equals(x.parameter, parameter, StringComparison.Ordinal));
        }

        private void Reset() {
            if (avatarRoot == null) avatarRoot = gameObject;
            if (string.IsNullOrWhiteSpace(profileName)) profileName = gameObject.name;
        }

        private void OnValidate() {
            schemaVersion = CurrentSchemaVersion;
            if (controls == null) controls = new List<ShiryuControlDefinition>();
            if (presets == null) presets = new List<ShiryuPresetDefinition>();
            if (entryRules == null) entryRules = new List<ShiryuControlEntryRule>();
            if (migrationIssues == null) migrationIssues = new List<ShiryuMigrationIssue>();
            if (buildSettings == null) buildSettings = new ShiryuAvatarBuildSettings();
            if (buildSettings.materials == null) buildSettings.materials = new ShiryuMaterialPreparationSettings();
            if (buildSettings.visibility == null) buildSettings.visibility = new ShiryuVisibilityFeatureSettings();
            if (buildSettings.alwaysActiveObjects == null) buildSettings.alwaysActiveObjects = new List<GameObject>();
            if (showcaseSettings == null) showcaseSettings = new ShiryuShowcaseSettings();
        }
    }

    // Kept only so projects that briefly created a 0.1 ScriptableObject profile do not get a
    // missing-script asset. New authoring must use the scene-bound ShiryuAvatarProfile above.
    [Obsolete("Use the scene-bound ShiryuAvatarProfile component instead.")]
    public sealed class ShiryuVRCFuryProfile : ScriptableObject { }
}
