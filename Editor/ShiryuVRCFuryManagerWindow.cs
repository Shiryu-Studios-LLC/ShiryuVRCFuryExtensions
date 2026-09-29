using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShiryuStudios.VRCFuryExtensions.Editor {
    public sealed class ShiryuVRCFuryManagerWindow : EditorWindow {
        private static readonly string[] Tabs = { "Overview", "Toggles", "Radials", "Presets", "Blendshapes", "Materials", "Groups", "Build / Validation" };
        private ShiryuAvatarProfile profile;
        private int tab;
        private Vector2 contentScroll;
        private Vector2 sidebarScroll;
        private readonly Dictionary<string, bool> controlFoldouts = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> optionFoldouts = new Dictionary<string, bool>();
        private List<ShiryuValidationMessage> lastValidation = new List<ShiryuValidationMessage>();

        [MenuItem("Shiryu Studios/VRCFury Extensions/Manager")]
        public static void OpenWindow() {
            var window = GetWindow<ShiryuVRCFuryManagerWindow>("Shiryu VRCFury Extensions");
            window.minSize = new Vector2(820f, 520f);
            window.TryResolveFromSelection();
            window.Show();
        }

        public static void Open(ShiryuAvatarProfile selectedProfile) {
            var window = GetWindow<ShiryuVRCFuryManagerWindow>("Shiryu VRCFury Extensions");
            window.minSize = new Vector2(820f, 520f);
            window.profile = selectedProfile;
            window.lastValidation = ShiryuReferenceUtility.Validate(selectedProfile);
            window.Show();
        }

        private void OnSelectionChange() {
            if (Selection.activeGameObject == null) return;
            var selected = Selection.activeGameObject.GetComponentInParent<ShiryuAvatarProfile>();
            if (selected == null) selected = Selection.activeGameObject.GetComponentInChildren<ShiryuAvatarProfile>(true);
            if (selected != null && selected != profile) {
                profile = selected;
                lastValidation = ShiryuReferenceUtility.Validate(profile);
                Repaint();
            }
        }

        private void TryResolveFromSelection() {
            if (profile != null) return;
            if (Selection.activeGameObject != null) {
                profile = Selection.activeGameObject.GetComponentInParent<ShiryuAvatarProfile>();
                if (profile == null) profile = Selection.activeGameObject.GetComponentInChildren<ShiryuAvatarProfile>(true);
            }
            if (profile == null) profile = UnityEngine.Object.FindObjectOfType<ShiryuAvatarProfile>();
            lastValidation = ShiryuReferenceUtility.Validate(profile);
        }

        private void OnGUI() {
            DrawHeader();
            DrawProfileRow();
            if (profile == null) {
                DrawNoProfile();
                return;
            }

            EditorGUILayout.Space(4f);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            DrawSidebar();
            EditorGUILayout.BeginVertical();
            contentScroll = EditorGUILayout.BeginScrollView(contentScroll);
            switch (tab) {
                case 0: DrawOverview(); break;
                case 1: DrawControls(false); break;
                case 2: DrawControls(true); break;
                case 3: DrawPresets(); break;
                case 4: DrawBlendshapes(); break;
                case 5: DrawMaterials(); break;
                case 6: DrawGroups(); break;
                default: DrawBuildValidation(); break;
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck()) MarkDirty();
        }

        private void DrawHeader() {
            var bg = EditorGUIUtility.isProSkin ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.86f, 0.86f, 0.86f);
            var rect = GUILayoutUtility.GetRect(10f, 43f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, bg);
            var tag = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1f, 0.47f, 0.08f) }, fontSize = 13 };
            GUI.Label(new Rect(rect.x + 12f, rect.y + 5f, 270f, 20f), "SHIRYU × VRCFURY", tag);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 23f, rect.width - 24f, 17f), "Reusable avatar controls — Shiryu Studios extension", EditorStyles.miniLabel);
        }

        private void DrawProfileRow() {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Avatar Profile", GUILayout.Width(82f));
            var next = (ShiryuAvatarProfile)EditorGUILayout.ObjectField(profile, typeof(ShiryuAvatarProfile), true, GUILayout.MinWidth(220f));
            if (next != profile) {
                profile = next;
                lastValidation = ShiryuReferenceUtility.Validate(profile);
            }
            if (profile != null) {
                GUILayout.Label(profile.AvatarRoot != null ? profile.AvatarRoot.name : "Missing avatar", EditorStyles.miniLabel, GUILayout.Width(150f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Select", EditorStyles.toolbarButton, GUILayout.Width(54f))) Selection.activeObject = profile;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawNoProfile() {
            EditorGUILayout.HelpBox("No scene-bound ShiryuAvatarProfile is selected. The profile belongs on the avatar scene object so it can serialize real GameObject and Renderer references.", MessageType.Info);
            if (Selection.activeGameObject != null && GUILayout.Button("Create Profile on Selected Avatar", GUILayout.Height(28f))) {
                var avatar = Selection.activeGameObject;
                Undo.AddComponent<ShiryuAvatarProfile>(avatar);
                profile = avatar.GetComponent<ShiryuAvatarProfile>();
                profile.avatarRoot = avatar;
                profile.profileName = avatar.name;
                EditorUtility.SetDirty(profile);
                lastValidation = ShiryuReferenceUtility.Validate(profile);
            }
        }

        private void DrawSidebar() {
            EditorGUILayout.BeginVertical(GUILayout.Width(150f));
            sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            for (var i = 0; i < Tabs.Length; i++) {
                var style = i == tab ? new GUIStyle(EditorStyles.miniButton) { fontStyle = FontStyle.Bold } : EditorStyles.miniButton;
                if (GUILayout.Button(Tabs[i], style, GUILayout.Height(27f))) tab = i;
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawOverview() {
            SectionTitle("Overview");
            EditorGUI.BeginChangeCheck();
            profile.profileName = EditorGUILayout.TextField("Profile Name", profile.profileName);
            profile.avatarRoot = (GameObject)EditorGUILayout.ObjectField("Avatar", profile.avatarRoot, typeof(GameObject), true);
            profile.generatedRootName = EditorGUILayout.TextField("Generated Root", profile.generatedRootName);
            if (EditorGUI.EndChangeCheck()) MarkDirty();

            EditorGUILayout.Space(8f);
            var controls = profile.controls ?? new List<ShiryuControlDefinition>();
            var presets = profile.presets ?? new List<ShiryuPresetDefinition>();
            EditorGUILayout.BeginHorizontal();
            StatBox("Controls", controls.Count.ToString());
            StatBox("Toggles", controls.Count(x => x != null && (x.kind == ShiryuControlKind.Toggle || x.kind == ShiryuControlKind.Button)).ToString());
            StatBox("Radials", controls.Count(x => x != null && x.IsRadial).ToString());
            StatBox("Presets", presets.Count.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            SectionTitle("Reference health");
            DrawValidationSummary(lastValidation);
            if (GUILayout.Button("Refresh Reference Metadata + Validate", GUILayout.Height(24f))) {
                ShiryuReferenceUtility.RefreshAllMetadata(profile);
                lastValidation = ShiryuReferenceUtility.Validate(profile);
            }

            if (profile.migrationIssues != null && profile.migrationIssues.Count > 0) {
                EditorGUILayout.Space(8f);
                SectionTitle("Migration review");
                foreach (var issue in profile.migrationIssues) {
                    if (issue == null) continue;
                    var type = issue.severity == ShiryuIssueSeverity.Error ? MessageType.Error : issue.severity == ShiryuIssueSeverity.Warning ? MessageType.Warning : MessageType.Info;
                    EditorGUILayout.HelpBox(issue.message + (issue.candidatePaths != null && issue.candidatePaths.Count > 0 ? "\nCandidates:\n• " + string.Join("\n• ", issue.candidatePaths.ToArray()) : ""), type);
                }
            }
        }

        private void DrawControls(bool radialTab) {
            SectionTitle(radialTab ? "Radials" : "Toggles & Buttons");
            var controls = profile.controls ?? (profile.controls = new List<ShiryuControlDefinition>());
            var filtered = controls.Where(x => x != null && (radialTab ? x.IsRadial : !x.IsRadial)).ToList();
            foreach (var control in filtered) DrawControlCard(control);

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(radialTab ? "Add Radial" : "Add Toggle", GUILayout.Height(26f))) AddControl(radialTab ? ShiryuControlKind.Radial : ShiryuControlKind.Toggle);
            if (radialTab && GUILayout.Button("Add Discrete Radial", GUILayout.Height(26f))) AddControl(ShiryuControlKind.DiscreteRadial);
            if (!radialTab && GUILayout.Button("Add Button", GUILayout.Height(26f))) AddControl(ShiryuControlKind.Button);
            EditorGUILayout.EndHorizontal();
        }

        private void AddControl(ShiryuControlKind kind) {
            Undo.RecordObject(profile, "Add Shiryu control");
            var name = kind == ShiryuControlKind.DiscreteRadial ? "New Discrete Radial" : kind == ShiryuControlKind.Radial ? "New Radial" : kind == ShiryuControlKind.Button ? "New Button" : "New Toggle";
            var control = new ShiryuControlDefinition {
                displayName = name,
                kind = kind,
                driveTargetObject = kind == ShiryuControlKind.Toggle,
                category = kind == ShiryuControlKind.Radial || kind == ShiryuControlKind.DiscreteRadial ? "Appearance/Controls" : "Toggles/Other",
                menuPath = "Shiryu/" + name,
                parameter = "Shiryu_" + name.Replace(" ", "")
            };
            if (kind == ShiryuControlKind.DiscreteRadial) {
                control.discreteOptions.Add(new ShiryuDiscreteOption { label = "Option 0", center = 0.25f });
                control.discreteOptions.Add(new ShiryuDiscreteOption { label = "Option 1", center = 0.75f });
            }
            profile.controls.Add(control);
            MarkDirty();
        }

        private void DrawControlCard(ShiryuControlDefinition control) {
            if (control == null) return;
            if (!controlFoldouts.ContainsKey(control.id)) controlFoldouts[control.id] = false;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            controlFoldouts[control.id] = EditorGUILayout.Foldout(controlFoldouts[control.id], control.displayName + "  —  " + ControlKindLabel(control.kind), true, EditorStyles.foldoutHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Options", GUILayout.Width(68f))) ShowControlOptions(control);
            if (GUILayout.Button("×", GUILayout.Width(24f))) {
                if (EditorUtility.DisplayDialog("Delete Control", "Delete " + control.displayName + " from this profile?", "Delete", "Cancel")) {
                    Undo.RecordObject(profile, "Delete Shiryu control");
                    profile.controls.Remove(control);
                    MarkDirty();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    return;
                }
            }
            EditorGUILayout.EndHorizontal();

            DrawChips(control);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            control.menuPath = EditorGUILayout.TextField("Menu Path", control.menuPath);
            EditorGUILayout.EndHorizontal();
            control.parameter = EditorGUILayout.TextField("Parameter", control.parameter);
            control.category = EditorGUILayout.TextField("Group / Folder", control.category);
            control.displayName = EditorGUILayout.TextField("Display Name", control.displayName);
            control.generatedObjectName = EditorGUILayout.TextField("Generated Object", control.generatedObjectName);
            if (EditorGUI.EndChangeCheck()) MarkDirty();

            if (controlFoldouts[control.id]) {
                EditorGUILayout.Space(3f);
                EditorGUI.BeginChangeCheck();
                control.driveTargetObject = EditorGUILayout.Toggle("Drive Target Object", control.driveTargetObject);
                if (control.driveTargetObject) control.targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", control.targetObject, typeof(GameObject), true);
                if (control.IsRadial) {
                    control.defaultValue = EditorGUILayout.Slider("Default %", control.defaultValue, 0f, 1f);
                    control.passthroughAtZero = EditorGUILayout.Toggle("Passthrough at 0%", control.passthroughAtZero);
                } else {
                    control.defaultOn = EditorGUILayout.Toggle("Default On", control.defaultOn);
                }
                control.menuIcon = (Texture2D)EditorGUILayout.ObjectField("Menu Icon", control.menuIcon, typeof(Texture2D), false);
                control.exclusiveGroup = EditorGUILayout.TextField("Exclusive Group", control.exclusiveGroup);
                if (!string.IsNullOrWhiteSpace(control.exclusiveGroup)) control.exclusiveOffState = EditorGUILayout.Toggle("Exclusive Off State", control.exclusiveOffState);
                control.useDissolve = EditorGUILayout.Toggle("Dissolve Transition", control.useDissolve);
                if (control.useDissolve) {
                    control.transitionInSeconds = EditorGUILayout.FloatField("Dissolve In", control.transitionInSeconds);
                    control.transitionOutSeconds = EditorGUILayout.FloatField("Dissolve Out", control.transitionOutSeconds);
                }
                if (EditorGUI.EndChangeCheck()) {
                    ShiryuReferenceUtility.Capture(profile, control.targetObject, control.targetFallback);
                    MarkDirty();
                }

                if (control.kind == ShiryuControlKind.DiscreteRadial) DrawDiscreteOptions(control);
                else DrawActionSet("When Enabled / 100%", control.objectActions, control.blendshapeActions, control.materialActions, control.materialSwapActions);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3f);
        }

        private void ShowControlOptions(ShiryuControlDefinition control) {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Saved Between Worlds"), control.saved, () => ToggleBool(control, "saved"));
            menu.AddItem(new GUIContent("Include In Presets"), control.includeInPresets, () => ToggleBool(control, "preset"));
            if (!control.IsRadial) menu.AddItem(new GUIContent("Default On"), control.defaultOn, () => ToggleBool(control, "default"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Enable Exclusive Group"), !string.IsNullOrWhiteSpace(control.exclusiveGroup), () => {
                Undo.RecordObject(profile, "Toggle exclusive group");
                control.exclusiveGroup = string.IsNullOrWhiteSpace(control.exclusiveGroup) ? "ShiryuExclusive" : "";
                MarkDirty();
            });
            menu.AddItem(new GUIContent("This Is Exclusive Off State"), control.exclusiveOffState, () => ToggleBool(control, "exclusiveOff"));
            menu.ShowAsContext();
        }

        private void ToggleBool(ShiryuControlDefinition control, string field) {
            Undo.RecordObject(profile, "Edit Shiryu control option");
            if (field == "saved") control.saved = !control.saved;
            else if (field == "preset") control.includeInPresets = !control.includeInPresets;
            else if (field == "default") control.defaultOn = !control.defaultOn;
            else if (field == "exclusiveOff") control.exclusiveOffState = !control.exclusiveOffState;
            MarkDirty();
        }

        private void DrawChips(ShiryuControlDefinition control) {
            EditorGUILayout.BeginHorizontal();
            if (control.saved) Chip("Saved");
            if (control.defaultOn && !control.IsRadial) Chip("Default On");
            if (control.IsRadial) Chip(control.kind == ShiryuControlKind.DiscreteRadial ? "Discrete Radial" : "Radial");
            if (!string.IsNullOrWhiteSpace(control.exclusiveGroup)) Chip("Exclusive: " + control.exclusiveGroup);
            if (control.includeInPresets) Chip("Preset");
            if (control.useDissolve) Chip("Dissolve");
            EditorGUILayout.EndHorizontal();
        }

        private void Chip(string text) {
            var style = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleCenter, fontSize = 9, fixedHeight = 18f };
            GUILayout.Label(text, style, GUILayout.ExpandWidth(false));
        }

        private void DrawDiscreteOptions(ShiryuControlDefinition control) {
            SectionTitle("Discrete Steps");
            if (control.discreteOptions == null) control.discreteOptions = new List<ShiryuDiscreteOption>();
            for (var i = 0; i < control.discreteOptions.Count; i++) {
                var option = control.discreteOptions[i];
                if (option == null) continue;
                var key = control.id + ":" + i;
                if (!optionFoldouts.ContainsKey(key)) optionFoldouts[key] = false;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                optionFoldouts[key] = EditorGUILayout.Foldout(optionFoldouts[key], (i + 1) + ". " + option.label, true);
                option.center = EditorGUILayout.Slider(option.center, 0f, 1f, GUILayout.Width(180f));
                GUI.enabled = i > 0;
                if (GUILayout.Button("↑", GUILayout.Width(24f))) { Move(control.discreteOptions, i, -1); MarkDirty(); }
                GUI.enabled = i < control.discreteOptions.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(24f))) { Move(control.discreteOptions, i, 1); MarkDirty(); }
                GUI.enabled = true;
                if (GUILayout.Button("×", GUILayout.Width(24f))) {
                    Undo.RecordObject(profile, "Delete discrete step");
                    control.discreteOptions.RemoveAt(i--);
                    MarkDirty();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    continue;
                }
                EditorGUILayout.EndHorizontal();
                if (optionFoldouts[key]) {
                    option.label = EditorGUILayout.TextField("Display Name", option.label);
                    DrawActionSet("Step Actions", option.objectActions, option.blendshapeActions, option.materialActions, option.materialSwapActions);
                }
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("Add Step")) {
                Undo.RecordObject(profile, "Add discrete step");
                var count = control.discreteOptions.Count;
                control.discreteOptions.Add(new ShiryuDiscreteOption { label = "Option " + count, center = (count + 0.5f) / (count + 1f) });
                MarkDirty();
            }
        }

        private void DrawActionSet(string title, List<ShiryuObjectAction> objects, List<ShiryuBlendshapeAction> blends, List<ShiryuMaterialAction> materials, List<ShiryuMaterialSwapAction> swaps) {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            DrawObjectActions(objects);
            DrawBlendshapeActions(blends);
            DrawMaterialActions(materials);
            DrawMaterialSwapActions(swaps);
            EditorGUI.indentLevel--;
        }

        private void DrawObjectActions(List<ShiryuObjectAction> actions) {
            if (actions == null) return;
            EditorGUILayout.LabelField("Object State", EditorStyles.miniBoldLabel);
            for (var i = 0; i < actions.Count; i++) {
                var action = actions[i];
                if (action == null) continue;
                EditorGUILayout.BeginHorizontal();
                action.target = (GameObject)EditorGUILayout.ObjectField(action.target, typeof(GameObject), true);
                action.activeWhenEnabled = EditorGUILayout.ToggleLeft(action.activeWhenEnabled ? "ON" : "OFF", action.activeWhenEnabled, GUILayout.Width(50f));
                GUI.enabled = i > 0;
                if (GUILayout.Button("↑", GUILayout.Width(22f))) { Move(actions, i, -1); MarkDirty(); }
                GUI.enabled = i < actions.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(22f))) { Move(actions, i, 1); MarkDirty(); }
                GUI.enabled = true;
                if (GUILayout.Button("×", GUILayout.Width(22f))) { actions.RemoveAt(i--); MarkDirty(); }
                else ShiryuReferenceUtility.Capture(profile, action.target, action.fallback);
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Object State", EditorStyles.miniButton)) { actions.Add(new ShiryuObjectAction()); MarkDirty(); }
        }

        private void DrawBlendshapeActions(List<ShiryuBlendshapeAction> actions) {
            if (actions == null) return;
            EditorGUILayout.LabelField("Blend Shapes", EditorStyles.miniBoldLabel);
            for (var i = 0; i < actions.Count; i++) {
                var action = actions[i];
                if (action == null) continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                action.renderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(action.renderer, typeof(SkinnedMeshRenderer), true);
                GUI.enabled = i > 0;
                if (GUILayout.Button("↑", GUILayout.Width(22f))) { Move(actions, i, -1); MarkDirty(); }
                GUI.enabled = i < actions.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(22f))) { Move(actions, i, 1); MarkDirty(); }
                GUI.enabled = true;
                if (GUILayout.Button("×", GUILayout.Width(22f))) { actions.RemoveAt(i--); MarkDirty(); EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); continue; }
                EditorGUILayout.EndHorizontal();
                var names = ShiryuReferenceUtility.GetBlendshapeNames(action.renderer);
                var index = Array.IndexOf(names, action.blendShape);
                if (names.Length > 0 && index >= 0) {
                    index = EditorGUILayout.Popup("Blendshape", index, names);
                    action.blendShape = names[index];
                } else if (names.Length > 0) {
                    if (!string.IsNullOrWhiteSpace(action.blendShape)) EditorGUILayout.HelpBox("Configured blendshape '" + action.blendShape + "' is not present on this mesh. It will not be replaced automatically.", MessageType.Warning);
                    var choices = new[] { "<Choose blendshape>" }.Concat(names).ToArray();
                    var choice = EditorGUILayout.Popup("Blendshape", 0, choices);
                    if (choice > 0) action.blendShape = names[choice - 1];
                } else action.blendShape = EditorGUILayout.TextField("Blendshape (Advanced)", action.blendShape);
                EditorGUILayout.BeginHorizontal();
                action.disabledValue = EditorGUILayout.FloatField("0% / OFF", action.disabledValue);
                action.enabledValue = EditorGUILayout.FloatField("100% / ON", action.enabledValue);
                EditorGUILayout.EndHorizontal();
                ShiryuReferenceUtility.Capture(profile, action.renderer, action.fallback);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ Blend Shape", EditorStyles.miniButton)) { actions.Add(new ShiryuBlendshapeAction()); MarkDirty(); }
        }

        private void DrawMaterialActions(List<ShiryuMaterialAction> actions) {
            if (actions == null) return;
            EditorGUILayout.LabelField("Material Properties", EditorStyles.miniBoldLabel);
            for (var i = 0; i < actions.Count; i++) {
                var action = actions[i];
                if (action == null) continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                action.renderer = (Renderer)EditorGUILayout.ObjectField(action.renderer, typeof(Renderer), true);
                GUI.enabled = i > 0;
                if (GUILayout.Button("↑", GUILayout.Width(22f))) { Move(actions, i, -1); MarkDirty(); }
                GUI.enabled = i < actions.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(22f))) { Move(actions, i, 1); MarkDirty(); }
                GUI.enabled = true;
                if (GUILayout.Button("×", GUILayout.Width(22f))) { actions.RemoveAt(i--); MarkDirty(); EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); continue; }
                EditorGUILayout.EndHorizontal();
                var maxSlot = action.renderer != null ? action.renderer.sharedMaterials.Length - 1 : -1;
                action.materialSlot = EditorGUILayout.IntSlider("Material Slot (-1 = all)", action.materialSlot, -1, Mathf.Max(-1, maxSlot));
                var properties = ShiryuReferenceUtility.GetShaderProperties(action.renderer, action.materialSlot);
                var selected = Array.IndexOf(properties, action.propertyName);
                if (properties.Length > 0 && selected >= 0) {
                    selected = EditorGUILayout.Popup("Shader Property", selected, properties);
                    action.propertyName = properties[selected];
                    action.valueType = ShiryuReferenceUtility.GuessMaterialValueType(action.renderer, action.materialSlot, action.propertyName);
                } else if (properties.Length > 0) {
                    if (!string.IsNullOrWhiteSpace(action.propertyName)) EditorGUILayout.HelpBox("Configured shader property '" + action.propertyName + "' is not exposed by the selected material(s). It will not be replaced automatically.", MessageType.Warning);
                    var choices = new[] { "<Choose shader property>" }.Concat(properties).ToArray();
                    var choice = EditorGUILayout.Popup("Shader Property", 0, choices);
                    if (choice > 0) {
                        action.propertyName = properties[choice - 1];
                        action.valueType = ShiryuReferenceUtility.GuessMaterialValueType(action.renderer, action.materialSlot, action.propertyName);
                    }
                } else action.propertyName = EditorGUILayout.TextField("Property (Advanced)", action.propertyName);
                action.valueType = (ShiryuMaterialValueType)EditorGUILayout.EnumPopup("Value Type", action.valueType);
                if (action.valueType == ShiryuMaterialValueType.Color) {
                    action.disabledColor = EditorGUILayout.ColorField("OFF / 0%", action.disabledColor);
                    action.enabledColor = EditorGUILayout.ColorField("ON / 100%", action.enabledColor);
                } else if (action.valueType == ShiryuMaterialValueType.Vector) {
                    action.disabledVector = EditorGUILayout.Vector4Field("OFF / 0%", action.disabledVector);
                    action.enabledVector = EditorGUILayout.Vector4Field("ON / 100%", action.enabledVector);
                } else {
                    action.disabledFloat = EditorGUILayout.FloatField("OFF / 0%", action.disabledFloat);
                    action.enabledFloat = EditorGUILayout.FloatField("ON / 100%", action.enabledFloat);
                }
                ShiryuReferenceUtility.Capture(profile, action.renderer, action.fallback);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ Material Property", EditorStyles.miniButton)) { actions.Add(new ShiryuMaterialAction()); MarkDirty(); }
        }

        private void DrawMaterialSwapActions(List<ShiryuMaterialSwapAction> actions) {
            if (actions == null) return;
            EditorGUILayout.LabelField("Material Swaps", EditorStyles.miniBoldLabel);
            for (var i = 0; i < actions.Count; i++) {
                var action = actions[i];
                if (action == null) continue;
                EditorGUILayout.BeginHorizontal();
                action.renderer = (Renderer)EditorGUILayout.ObjectField(action.renderer, typeof(Renderer), true);
                action.materialSlot = EditorGUILayout.IntField(action.materialSlot, GUILayout.Width(38f));
                action.enabledMaterial = (Material)EditorGUILayout.ObjectField(action.enabledMaterial, typeof(Material), false);
                GUI.enabled = i > 0;
                if (GUILayout.Button("↑", GUILayout.Width(22f))) { Move(actions, i, -1); MarkDirty(); }
                GUI.enabled = i < actions.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(22f))) { Move(actions, i, 1); MarkDirty(); }
                GUI.enabled = true;
                if (GUILayout.Button("×", GUILayout.Width(22f))) { actions.RemoveAt(i--); MarkDirty(); }
                else ShiryuReferenceUtility.Capture(profile, action.renderer, action.fallback);
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Material Swap", EditorStyles.miniButton)) { actions.Add(new ShiryuMaterialSwapAction()); MarkDirty(); }
        }

        private void DrawPresets() {
            SectionTitle("Presets");
            EditorGUILayout.HelpBox("Presets store control values only. The control definition owns the object, blendshape, material and transition behavior.", MessageType.Info);
            if (profile.presets == null) profile.presets = new List<ShiryuPresetDefinition>();
            var controls = (profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null && x.includeInPresets).ToList();
            foreach (var preset in profile.presets.Where(x => x != null)) {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                preset.displayName = EditorGUILayout.TextField("Name", preset.displayName);
                preset.menuPath = EditorGUILayout.TextField("Menu Path", preset.menuPath);
                preset.parameter = EditorGUILayout.TextField("Button Parameter", preset.parameter);
                EditorGUILayout.Space(2f);
                foreach (var control in controls) {
                    var current = preset.GetValue(control.id, control.IsRadial ? control.defaultValue : (control.defaultOn ? 1f : 0f));
                    float next;
                    if (control.IsRadial) next = EditorGUILayout.Slider(control.displayName, current, 0f, 1f);
                    else next = EditorGUILayout.Toggle(control.displayName, current >= 0.5f) ? 1f : 0f;
                    if (!Mathf.Approximately(current, next)) { preset.SetValue(control.id, next); MarkDirty(); }
                }
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Delete Preset", GUILayout.Width(105f))) {
                    profile.presets.Remove(preset);
                    MarkDirty();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("Add Preset", GUILayout.Height(25f))) {
                var n = profile.presets.Count + 1;
                var preset = new ShiryuPresetDefinition { displayName = "Preset " + n, menuPath = "PRESETS/Preset " + n, parameter = "Shiryu_Preset_" + n };
                foreach (var control in controls) preset.SetValue(control.id, control.IsRadial ? control.defaultValue : (control.defaultOn ? 1f : 0f));
                profile.presets.Add(preset);
                MarkDirty();
            }
            EditorGUILayout.Space(6f);
            profile.enableRandomizePresets = EditorGUILayout.Toggle("Enable Randomize", profile.enableRandomizePresets);
            if (profile.enableRandomizePresets) {
                profile.randomizeMenuPath = EditorGUILayout.TextField("Randomize Menu", profile.randomizeMenuPath);
                profile.randomizeParameter = EditorGUILayout.TextField("Randomize Parameter", profile.randomizeParameter);
            }
        }

        private void DrawBlendshapes() {
            SectionTitle("Blendshape Actions");
            foreach (var control in (profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null)) {
                if ((control.blendshapeActions == null || control.blendshapeActions.Count == 0) &&
                    (control.discreteOptions == null || !control.discreteOptions.Any(o => o != null && o.blendshapeActions != null && o.blendshapeActions.Count > 0))) continue;
                EditorGUILayout.LabelField(control.displayName, EditorStyles.boldLabel);
                DrawBlendshapeActions(control.blendshapeActions);
                if (control.discreteOptions != null) foreach (var option in control.discreteOptions.Where(x => x != null && x.blendshapeActions != null && x.blendshapeActions.Count > 0)) {
                    EditorGUILayout.LabelField("  " + option.label, EditorStyles.miniBoldLabel);
                    DrawBlendshapeActions(option.blendshapeActions);
                }
            }
        }

        private void DrawMaterials() {
            SectionTitle("Material Actions");
            foreach (var control in (profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null)) {
                var has = control.materialActions != null && control.materialActions.Count > 0;
                var hasSwap = control.materialSwapActions != null && control.materialSwapActions.Count > 0;
                var hasDiscrete = control.discreteOptions != null && control.discreteOptions.Any(o => o != null && ((o.materialActions != null && o.materialActions.Count > 0) || (o.materialSwapActions != null && o.materialSwapActions.Count > 0)));
                if (!has && !hasSwap && !hasDiscrete) continue;
                EditorGUILayout.LabelField(control.displayName, EditorStyles.boldLabel);
                DrawMaterialActions(control.materialActions);
                DrawMaterialSwapActions(control.materialSwapActions);
                if (control.discreteOptions != null) foreach (var option in control.discreteOptions.Where(x => x != null)) {
                    if ((option.materialActions == null || option.materialActions.Count == 0) && (option.materialSwapActions == null || option.materialSwapActions.Count == 0)) continue;
                    EditorGUILayout.LabelField("  " + option.label, EditorStyles.miniBoldLabel);
                    DrawMaterialActions(option.materialActions);
                    DrawMaterialSwapActions(option.materialSwapActions);
                }
            }
        }

        private void DrawGroups() {
            SectionTitle("Groups");
            var controls = (profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null).ToList();
            foreach (var grouping in controls.GroupBy(x => string.IsNullOrWhiteSpace(x.category) ? "Ungrouped" : x.category).OrderBy(x => x.Key)) {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(grouping.Key, EditorStyles.boldLabel);
                foreach (var control in grouping) EditorGUILayout.LabelField("• " + control.displayName + "  →  " + control.menuPath, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
            SectionTitle("Exclusive Sets");
            foreach (var group in controls.Where(x => !string.IsNullOrWhiteSpace(x.exclusiveGroup)).GroupBy(x => x.exclusiveGroup).OrderBy(x => x.Key)) {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(group.Key, EditorStyles.boldLabel);
                foreach (var control in group) EditorGUILayout.LabelField("• " + control.displayName + (control.exclusiveOffState ? "  [off state]" : ""));
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawBuildValidation() {
            SectionTitle("Reusable Build Features");
            if (profile.buildSettings == null) profile.buildSettings = new ShiryuAvatarBuildSettings();
            if (profile.buildSettings.materials == null) profile.buildSettings.materials = new ShiryuMaterialPreparationSettings();
            if (profile.buildSettings.visibility == null) profile.buildSettings.visibility = new ShiryuVisibilityFeatureSettings();
            if (profile.buildSettings.alwaysActiveObjects == null) profile.buildSettings.alwaysActiveObjects = new List<GameObject>();
            if (profile.showcaseSettings == null) profile.showcaseSettings = new ShiryuShowcaseSettings();

            var materials = profile.buildSettings.materials;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Material Preparation", EditorStyles.boldLabel);
            materials.enabled = EditorGUILayout.Toggle("Enabled", materials.enabled);
            if (materials.enabled) {
                materials.includeAllAvatarMaterials = EditorGUILayout.Toggle("All Avatar Materials", materials.includeAllAvatarMaterials);
                materials.configureTransClipping = EditorGUILayout.Toggle("Poiyomi TransClipping", materials.configureTransClipping);
                materials.configureDissolve = EditorGUILayout.Toggle("Poiyomi Dissolve", materials.configureDissolve);
                materials.markProfilePropertiesAnimated = EditorGUILayout.Toggle("Mark Animated Properties", materials.markProfilePropertiesAnimated);
                materials.applyProfileMaterialDefaults = EditorGUILayout.Toggle("Apply Profile Defaults", materials.applyProfileMaterialDefaults);
                materials.backupMaterials = EditorGUILayout.Toggle("Backup Materials", materials.backupMaterials);
                if (materials.backupMaterials) materials.backupRoot = EditorGUILayout.TextField("Backup Root", materials.backupRoot);
                materials.defaultRenderQueue = EditorGUILayout.IntField("Default Render Queue", materials.defaultRenderQueue);
            }
            EditorGUILayout.EndVertical();

            var visibility = profile.buildSettings.visibility;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Visibility Features", EditorStyles.boldLabel);
            visibility.enableInvisible = EditorGUILayout.Toggle("Invisible", visibility.enableInvisible);
            if (visibility.enableInvisible) {
                visibility.invisibleMenuPath = EditorGUILayout.TextField("Invisible Menu", visibility.invisibleMenuPath);
                visibility.invisibleParameter = EditorGUILayout.TextField("Invisible Parameter", visibility.invisibleParameter);
                visibility.transitionSeconds = EditorGUILayout.FloatField("Transition Seconds", visibility.transitionSeconds);
                visibility.enableLocalGhost = EditorGUILayout.Toggle("Local Ghost Self View", visibility.enableLocalGhost);
                if (visibility.enableLocalGhost) {
                    visibility.ghostMenuPath = EditorGUILayout.TextField("Ghost Menu", visibility.ghostMenuPath);
                    visibility.ghostParameter = EditorGUILayout.TextField("Ghost Parameter", visibility.ghostParameter);
                    visibility.ghostAlphaModifier = EditorGUILayout.Slider("Ghost Alpha Modifier", visibility.ghostAlphaModifier, -1f, 0f);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Build / QA Behavior", EditorStyles.boldLabel);
            profile.buildSettings.hideSourceWhenTestCopyExists = EditorGUILayout.Toggle("Hide Source With Test Copy", profile.buildSettings.hideSourceWhenTestCopyExists);
            profile.buildSettings.generateBuildReport = EditorGUILayout.Toggle("Generate Build Report", profile.buildSettings.generateBuildReport);
            profile.showcaseSettings.enabled = EditorGUILayout.Toggle("Runtime Showcase", profile.showcaseSettings.enabled);
            profile.showcaseSettings.includeCrossControlQa = EditorGUILayout.Toggle("Cross-Control QA", profile.showcaseSettings.includeCrossControlQa);
            profile.showcaseSettings.secondsPerStep = EditorGUILayout.Slider("Showcase Seconds", profile.showcaseSettings.secondsPerStep, 1.25f, 12f);
            EditorGUILayout.LabelField("Always Active Objects", EditorStyles.miniBoldLabel);
            for (var i = 0; i < profile.buildSettings.alwaysActiveObjects.Count; i++) {
                EditorGUILayout.BeginHorizontal();
                profile.buildSettings.alwaysActiveObjects[i] = (GameObject)EditorGUILayout.ObjectField(profile.buildSettings.alwaysActiveObjects[i], typeof(GameObject), true);
                if (GUILayout.Button("−", GUILayout.Width(24f))) { profile.buildSettings.alwaysActiveObjects.RemoveAt(i); i--; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Add Always Active Object", GUILayout.Height(22f))) profile.buildSettings.alwaysActiveObjects.Add(null);
            EditorGUILayout.EndVertical();

            SectionTitle("Build / Validation");
            DrawValidationSummary(lastValidation);
            EditorGUILayout.Space(5f);
            if (GUILayout.Button("Validate Scene References", GUILayout.Height(27f))) {
                ShiryuReferenceUtility.RefreshAllMetadata(profile);
                lastValidation = ShiryuReferenceUtility.Validate(profile);
            }
            if (ShiryuProjectAdapterRegistry.HasMigrationAdapter(profile) && GUILayout.Button("Run Project Import / Migration", GUILayout.Height(27f))) {
                ShiryuProjectAdapterRegistry.TryMigrate(profile);
                lastValidation = ShiryuReferenceUtility.Validate(profile);
            }
            if (GUILayout.Button("Rebuild Avatar from Shiryu Profile", GUILayout.Height(31f))) {
                var result = ShiryuAvatarBuildPipeline.Rebuild(profile, true, true);
                Debug.Log("[Shiryu VRCFury Extensions] " + string.Join("\n", result.messages.ToArray()), profile);
                lastValidation = ShiryuReferenceUtility.Validate(profile);
            }
            if (GUILayout.Button("Build VRCFury Test Copy", GUILayout.Height(27f))) {
                var result = ShiryuAvatarBuildPipeline.BuildTestCopy(profile);
                Debug.Log("[Shiryu VRCFury Extensions] " + string.Join("\n", result.messages.ToArray()), profile);
                lastValidation = ShiryuReferenceUtility.Validate(profile);
            }
            if (profile.showcaseSettings.enabled && GUILayout.Button("Open Runtime Showcase + QA", GUILayout.Height(27f))) {
                Selection.activeGameObject = profile.gameObject;
                ShiryuAvatarShowcaseWindow.Open();
            }
            if (GUILayout.Button("Save Scene", GUILayout.Height(25f))) {
                EditorSceneManager.MarkSceneDirty(profile.gameObject.scene);
                EditorSceneManager.SaveScene(profile.gameObject.scene);
                AssetDatabase.SaveAssets();
            }
        }

        private void DrawValidationSummary(List<ShiryuValidationMessage> messages) {
            messages = messages ?? new List<ShiryuValidationMessage>();
            var errors = messages.Count(x => x.severity == ShiryuIssueSeverity.Error);
            var warnings = messages.Count(x => x.severity == ShiryuIssueSeverity.Warning);
            if (errors == 0 && warnings == 0) EditorGUILayout.HelpBox("All serialized references and configured actions validate.", MessageType.Info);
            else EditorGUILayout.HelpBox(errors + " error(s), " + warnings + " warning(s). Expand the items below before building.", errors > 0 ? MessageType.Error : MessageType.Warning);
            foreach (var message in messages.Take(30)) {
                var type = message.severity == ShiryuIssueSeverity.Error ? MessageType.Error : message.severity == ShiryuIssueSeverity.Warning ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(message.message, type);
            }
            if (messages.Count > 30) EditorGUILayout.LabelField("… and " + (messages.Count - 30) + " more", EditorStyles.miniLabel);
        }

        private void SectionTitle(string title) {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(1f, 0.47f, 0.08f, 0.65f));
            EditorGUILayout.Space(3f);
        }

        private void StatBox(string label, string value) {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(95f));
            var valueStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            var labelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            GUILayout.Label(value, valueStyle);
            GUILayout.Label(label, labelStyle);
            EditorGUILayout.EndVertical();
        }

        private string ControlKindLabel(ShiryuControlKind kind) {
            switch (kind) {
                case ShiryuControlKind.Radial: return "Radial";
                case ShiryuControlKind.DiscreteRadial: return "Discrete Radial";
                case ShiryuControlKind.Button: return "Button";
                default: return "Toggle";
            }
        }

        private static void Move<T>(List<T> list, int index, int delta) {
            if (list == null) return;
            var next = index + delta;
            if (index < 0 || index >= list.Count || next < 0 || next >= list.Count) return;
            var value = list[index];
            list[index] = list[next];
            list[next] = value;
        }

        private void MarkDirty() {
            if (profile == null) return;
            EditorUtility.SetDirty(profile);
            EditorSceneManager.MarkSceneDirty(profile.gameObject.scene);
        }
    }

    [CustomEditor(typeof(ShiryuAvatarProfile))]
    public sealed class ShiryuAvatarProfileInspector : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            var profile = (ShiryuAvatarProfile)target;
            EditorGUILayout.Space(3f);
            var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1f, 0.47f, 0.08f) }, fontSize = 13 };
            EditorGUILayout.LabelField("Shiryu VRCFury Extension", style);
            EditorGUILayout.LabelField("Scene-bound profile • real Unity object references", EditorStyles.miniLabel);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Avatar", profile.AvatarRoot != null ? profile.AvatarRoot.name : "Missing");
            EditorGUILayout.LabelField("Controls", (profile.controls != null ? profile.controls.Count : 0).ToString());
            EditorGUILayout.LabelField("Presets", (profile.presets != null ? profile.presets.Count : 0).ToString());
            if (GUILayout.Button("Open Shiryu VRCFury Manager", GUILayout.Height(28f))) ShiryuVRCFuryManagerWindow.Open(profile);
            if (GUILayout.Button("Validate References")) {
                ShiryuReferenceUtility.RefreshAllMetadata(profile);
                var messages = ShiryuReferenceUtility.Validate(profile);
                var errors = messages.Count(x => x.severity == ShiryuIssueSeverity.Error);
                var warnings = messages.Count(x => x.severity == ShiryuIssueSeverity.Warning);
                Debug.Log("[Shiryu VRCFury Extensions] Validation: " + errors + " errors, " + warnings + " warnings.\n" + string.Join("\n", messages.Select(x => x.message).ToArray()), profile);
            }
        }
    }
}
