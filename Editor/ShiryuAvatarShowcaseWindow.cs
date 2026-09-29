using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ShiryuStudios.VRCFuryExtensions.Editor {
    /// <summary>
    /// Profile-driven runtime showcase and QA. It works with any avatar using ShiryuAvatarProfile
    /// and intentionally contains no avatar names, control names, material names, or project paths.
    /// </summary>
    public sealed class ShiryuAvatarShowcaseWindow : EditorWindow {
        private const string AutoStartKey = "ShiryuStudios.VRCFuryExtensions.Showcase.AutoStart";
        private const string GestureManagerTypeName = "BlackStartX.GestureManager.GestureManager";
        private const string GestureManagerModuleTypeName = "BlackStartX.GestureManager.Editor.Modules.Vrc3.ModuleVrc3";
        private const string GestureManagerPrefabPath = "Packages/vrchat.blackstartx.gesture-manager/GestureManager.prefab";

        [Serializable]
        private sealed class ShowcaseStep {
            public string category;
            public string label;
            [NonSerialized] public Action apply;
            public ShowcaseStep(string category, string label, Action apply) {
                this.category = category;
                this.label = label;
                this.apply = apply;
            }
        }

        [SerializeField] private ShiryuAvatarProfile profile;
        [SerializeField] private bool loop;
        [SerializeField] private bool includeCrossControlQa = true;
        [SerializeField] private float secondsPerStep = 3f;
        private readonly List<ShowcaseStep> steps = new List<ShowcaseStep>();
        private int currentIndex = -1;
        private bool running;
        private double nextAdvanceAt;
        private double nextConnectAttemptAt;
        private Vector2 scroll;
        private string runtimeStatus = "Ready";
        private GUIStyle currentStyle;

        [MenuItem("Shiryu Studios/VRCFury Extensions/Runtime Showcase + QA")]
        public static void Open() {
            var window = GetWindow<ShiryuAvatarShowcaseWindow>("Shiryu Avatar Showcase");
            window.minSize = new Vector2(480f, 620f);
            window.EnsureProfile();
            window.BuildSteps();
            window.Show();
        }

        private void OnEnable() {
            EnsureProfile();
            if (profile != null && profile.showcaseSettings != null) {
                includeCrossControlQa = profile.showcaseSettings.includeCrossControlQa;
                secondsPerStep = profile.showcaseSettings.secondsPerStep;
            }
            BuildSteps();
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable() {
            running = false;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state) {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) {
                running = false;
                currentIndex = -1;
                runtimeStatus = "Ready";
            }
            Repaint();
        }

        private void Tick() {
            if (EditorApplication.isPlaying && SessionState.GetBool(AutoStartKey, false)) {
                if (EditorApplication.timeSinceStartup >= nextConnectAttemptAt) {
                    nextConnectAttemptAt = EditorApplication.timeSinceStartup + 0.5d;
                    string error;
                    if (EnsureGestureManagerConnected(FindShowcaseTarget(), out error)) {
                        SessionState.SetBool(AutoStartKey, false);
                        runtimeStatus = "Gesture Manager connected — runtime FX active";
                        StartShowcaseRuntime();
                    } else {
                        runtimeStatus = error;
                    }
                    Repaint();
                }
            }
            if (!running || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextAdvanceAt) return;
            Next();
        }

        private void OnGUI() {
            if (currentStyle == null) currentStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, wordWrap = true, alignment = TextAnchor.MiddleCenter };

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Shiryu Runtime Showcase + QA", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("This window is generated from the selected ShiryuAvatarProfile. It can exercise toggles, radials, discrete options, presets, exclusive groups, visibility features, and blendshape/clothing combinations through Gesture Manager.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            var nextProfile = (ShiryuAvatarProfile)EditorGUILayout.ObjectField("Avatar Profile", profile, typeof(ShiryuAvatarProfile), true);
            if (EditorGUI.EndChangeCheck()) {
                profile = nextProfile;
                currentIndex = -1;
                BuildSteps();
            }
            if (profile == null) {
                EditorGUILayout.HelpBox("Select an avatar containing a ShiryuAvatarProfile.", MessageType.Warning);
                if (GUILayout.Button("Use Selected Avatar")) { EnsureProfile(true); BuildSteps(); }
                return;
            }

            var target = FindShowcaseTarget();
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField("Avatar", GUILayout.Width(75f));
                EditorGUILayout.LabelField(profile.AvatarRoot != null ? profile.AvatarRoot.name : "Missing", EditorStyles.boldLabel);
                GUI.enabled = !EditorApplication.isPlaying;
                if (GUILayout.Button("Build / Refresh", GUILayout.Width(115f))) BuildOrRefreshTestCopy();
                GUI.enabled = true;
            }
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField("Test Copy", GUILayout.Width(75f));
                EditorGUILayout.LabelField(target != null ? target.name : "Not found", target != null ? EditorStyles.boldLabel : EditorStyles.miniLabel);
                if (GUILayout.Button("Frame", GUILayout.Width(70f)) && target != null) FrameAvatar(target);
            }
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField("Mode", GUILayout.Width(75f));
                EditorGUILayout.LabelField(EditorApplication.isPlaying ? "PLAY MODE — real runtime FX" : "Edit Mode", EditorApplication.isPlaying ? EditorStyles.boldLabel : EditorStyles.label);
                if (EditorApplication.isPlaying && GUILayout.Button("Exit Play Mode", GUILayout.Width(115f))) {
                    running = false;
                    SessionState.SetBool(AutoStartKey, false);
                    EditorApplication.ExitPlaymode();
                }
            }
            EditorGUILayout.LabelField("Status", runtimeStatus, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(6f);
            secondsPerStep = EditorGUILayout.Slider("Seconds per item", secondsPerStep, 1.25f, 12f);
            EditorGUI.BeginChangeCheck();
            includeCrossControlQa = EditorGUILayout.Toggle("Cross-control clipping QA", includeCrossControlQa);
            if (EditorGUI.EndChangeCheck()) BuildSteps();
            loop = EditorGUILayout.Toggle("Loop showcase", loop);

            if (!EditorApplication.isPlaying) {
                if (target == null) EditorGUILayout.HelpBox("Build the VRCFury Editor Test Copy before starting runtime QA.", MessageType.None);
                GUI.enabled = target != null;
                if (GUILayout.Button("▶ Enter Play Mode + Start Runtime Showcase", GUILayout.Height(38f))) EnterPlayModeAndStart();
                GUI.enabled = true;
            } else {
                using (new EditorGUILayout.HorizontalScope()) {
                    GUI.enabled = !running;
                    if (GUILayout.Button("▶ Start / Resume", GUILayout.Height(32f))) StartShowcaseRuntime();
                    GUI.enabled = running;
                    if (GUILayout.Button("Ⅱ Pause", GUILayout.Height(32f))) Pause();
                    GUI.enabled = true;
                    if (GUILayout.Button("■ Stop + Reset", GUILayout.Height(32f))) StopAndReset();
                }
            }

            using (new EditorGUILayout.HorizontalScope()) {
                GUI.enabled = EditorApplication.isPlaying;
                if (GUILayout.Button("◀ Previous")) Previous();
                if (GUILayout.Button("Next ▶")) Next();
                GUI.enabled = true;
            }

            EditorGUILayout.Space(10f);
            var currentText = currentIndex >= 0 && currentIndex < steps.Count
                ? (currentIndex + 1) + " / " + steps.Count + "\n" + steps[currentIndex].category + " — " + steps[currentIndex].label
                : "Ready — " + steps.Count + " generated QA step(s)";
            EditorGUILayout.LabelField(currentText, currentStyle, GUILayout.Height(50f));

            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField("Jump to an item", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            string lastCategory = null;
            for (var i = 0; i < steps.Count; i++) {
                var step = steps[i];
                if (!string.Equals(lastCategory, step.category, StringComparison.Ordinal)) {
                    if (lastCategory != null) EditorGUILayout.Space(5f);
                    EditorGUILayout.LabelField(step.category, EditorStyles.boldLabel);
                    lastCategory = step.category;
                }
                var index = i;
                GUI.enabled = EditorApplication.isPlaying;
                if (GUILayout.Button(step.label, currentIndex == index ? EditorStyles.miniButtonMid : EditorStyles.miniButton)) {
                    running = false;
                    ApplyStep(index);
                }
                GUI.enabled = true;
            }
            EditorGUILayout.EndScrollView();
        }

        private void EnsureProfile(bool forceSelected = false) {
            if (!forceSelected && profile != null) return;
            var selected = Selection.activeGameObject;
            if (selected != null) profile = selected.GetComponentInParent<ShiryuAvatarProfile>();
            if (profile == null) profile = UnityEngine.Object.FindObjectsOfType<ShiryuAvatarProfile>(true).FirstOrDefault();
        }

        private void BuildSteps() {
            steps.Clear();
            if (profile == null || profile.AvatarRoot == null) return;
            steps.Add(new ShowcaseStep("Overview", "Default Profile State", ResetRuntimeParameters));

            foreach (var control in (profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.parameter))) {
                var captured = control;
                var category = string.IsNullOrWhiteSpace(control.category) ? "Controls" : control.category;
                if (control.kind == ShiryuControlKind.Radial) {
                    steps.Add(new ShowcaseStep(category, control.displayName + " — 50%", () => ApplyControl(captured, 0.5f)));
                    steps.Add(new ShowcaseStep(category, control.displayName + " — 100%", () => ApplyControl(captured, 1f)));
                } else if (control.kind == ShiryuControlKind.DiscreteRadial && control.discreteOptions != null && control.discreteOptions.Count > 0) {
                    foreach (var option in control.discreteOptions.Where(x => x != null)) {
                        var capturedOption = option;
                        steps.Add(new ShowcaseStep(category, control.displayName + " — " + capturedOption.label, () => ApplyControl(captured, capturedOption.center)));
                    }
                } else {
                    var value = control.kind == ShiryuControlKind.Button ? 1f : (control.defaultOn ? 0f : 1f);
                    steps.Add(new ShowcaseStep(category, control.displayName + (control.defaultOn ? " — Off" : " — On"), () => ApplyControl(captured, value)));
                }
            }

            foreach (var preset in (profile.presets ?? new List<ShiryuPresetDefinition>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.parameter))) {
                var captured = preset;
                steps.Add(new ShowcaseStep("Presets", preset.displayName, () => {
                    ResetRuntimeParameters();
                    SetGestureParameter(captured.parameter, 1f);
                }));
            }

            var visibility = profile.buildSettings != null ? profile.buildSettings.visibility : null;
            if (visibility != null && visibility.enableInvisible) {
                steps.Add(new ShowcaseStep("Special", "Invisible — Remote Simulation", () => {
                    ResetRuntimeParameters();
                    SetGestureParameter("IsLocal", 0f);
                    SetGestureParameter(visibility.invisibleParameter, 1f);
                }));
                if (visibility.enableLocalGhost) {
                    steps.Add(new ShowcaseStep("Special", "Invisible + Local Ghost Self View", () => {
                        ResetRuntimeParameters();
                        SetGestureParameter("IsLocal", 1f);
                        SetGestureParameter(visibility.invisibleParameter, 1f);
                        SetGestureParameter(visibility.ghostParameter, 1f);
                    }));
                }
            }

            if (includeCrossControlQa) BuildCrossControlQaSteps();
        }

        private void BuildCrossControlQaSteps() {
            var controls = (profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null).ToList();
            var objectToggles = controls.Where(x => x.kind == ShiryuControlKind.Toggle && x.targetObject != null).ToArray();
            var morphControls = controls.Where(x => x.kind == ShiryuControlKind.Radial && x.blendshapeActions != null && x.blendshapeActions.Count > 0).ToArray();
            foreach (var toggle in objectToggles) {
                foreach (var morph in morphControls) {
                    if (!MorphTouchesTarget(toggle.targetObject, morph)) continue;
                    var capturedToggle = toggle;
                    var capturedMorph = morph;
                    steps.Add(new ShowcaseStep("QA / " + morph.displayName, toggle.displayName + " + " + morph.displayName + " 50%", () => ApplyCombination(capturedToggle, capturedMorph, 0.5f)));
                    steps.Add(new ShowcaseStep("QA / " + morph.displayName, toggle.displayName + " + " + morph.displayName + " 100%", () => ApplyCombination(capturedToggle, capturedMorph, 1f)));
                }
            }
        }

        private static bool MorphTouchesTarget(GameObject target, ShiryuControlDefinition morph) {
            if (target == null || morph == null || morph.blendshapeActions == null) return false;
            foreach (var action in morph.blendshapeActions.Where(x => x != null && x.renderer != null)) {
                var t = action.renderer.transform;
                if (t == target.transform || t.IsChildOf(target.transform)) return true;
            }
            return false;
        }

        private void ApplyCombination(ShiryuControlDefinition toggle, ShiryuControlDefinition morph, float morphValue) {
            ResetRuntimeParameters();
            ClearExclusivePeers(toggle);
            SetGestureParameter(toggle.parameter, 1f);
            SetGestureParameter(morph.parameter, morphValue);
        }

        private void ApplyControl(ShiryuControlDefinition control, float value) {
            ResetRuntimeParameters();
            ClearExclusivePeers(control);
            SetGestureParameter(control.parameter, value);
        }

        private void ClearExclusivePeers(ShiryuControlDefinition selected) {
            if (selected == null || string.IsNullOrWhiteSpace(selected.exclusiveGroup)) return;
            foreach (var peer in (profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null && x != selected && string.Equals(x.exclusiveGroup, selected.exclusiveGroup, StringComparison.Ordinal))) {
                if (!string.IsNullOrWhiteSpace(peer.parameter)) SetGestureParameter(peer.parameter, 0f);
            }
        }

        private void ResetRuntimeParameters() {
            if (!EditorApplication.isPlaying || profile == null) return;
            foreach (var control in (profile.controls ?? new List<ShiryuControlDefinition>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.parameter))) {
                var value = control.IsRadial ? control.defaultValue : (control.defaultOn ? 1f : 0f);
                SetGestureParameter(control.parameter, value);
            }
            foreach (var preset in (profile.presets ?? new List<ShiryuPresetDefinition>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.parameter))) SetGestureParameter(preset.parameter, 0f);
            if (profile.enableRandomizePresets && !string.IsNullOrWhiteSpace(profile.randomizeParameter)) SetGestureParameter(profile.randomizeParameter, 0f);
            var visibility = profile.buildSettings != null ? profile.buildSettings.visibility : null;
            if (visibility != null) {
                if (visibility.enableInvisible && !string.IsNullOrWhiteSpace(visibility.invisibleParameter)) SetGestureParameter(visibility.invisibleParameter, 0f);
                if (visibility.enableLocalGhost && !string.IsNullOrWhiteSpace(visibility.ghostParameter)) SetGestureParameter(visibility.ghostParameter, 0f);
            }
            SetGestureParameter("IsLocal", 1f);
        }

        private void BuildOrRefreshTestCopy() {
            if (profile == null || profile.AvatarRoot == null) return;
            var result = ShiryuAvatarBuildPipeline.Rebuild(profile, true, true);
            if (!result.Success) {
                runtimeStatus = "Build failed — check Console.";
                Debug.LogError("[Shiryu Showcase] " + string.Join("\n", result.messages.ToArray()), profile);
                return;
            }
            Selection.activeGameObject = profile.AvatarRoot;
            if (!EditorApplication.ExecuteMenuItem("Tools/VRCFury/Build an Editor Test Copy")) Debug.LogWarning("[Shiryu Showcase] VRCFury's Editor Test Copy menu item could not be executed.");
        }

        private void EnterPlayModeAndStart() {
            if (FindShowcaseTarget() == null) {
                runtimeStatus = "Build the VRCFury Editor Test Copy first.";
                return;
            }
            running = false;
            currentIndex = -1;
            runtimeStatus = "Entering Play Mode…";
            SessionState.SetBool(AutoStartKey, true);
            nextConnectAttemptAt = EditorApplication.timeSinceStartup + 0.5d;
            EditorApplication.EnterPlaymode();
        }

        private void StartShowcaseRuntime() {
            if (!EditorApplication.isPlaying) { EnterPlayModeAndStart(); return; }
            string error;
            if (!EnsureGestureManagerConnected(FindShowcaseTarget(), out error)) {
                runtimeStatus = error;
                running = false;
                return;
            }
            BuildSteps();
            runtimeStatus = "Gesture Manager connected — running generated profile QA";
            running = true;
            if (currentIndex < 0 || currentIndex >= steps.Count - 1) currentIndex = -1;
            Next();
        }

        private void Pause() { running = false; runtimeStatus = "Paused on current runtime state"; Repaint(); }

        private void StopAndReset() {
            running = false;
            currentIndex = -1;
            if (EditorApplication.isPlaying) ResetRuntimeParameters();
            runtimeStatus = EditorApplication.isPlaying ? "Stopped — runtime parameters reset" : "Ready";
            Repaint();
        }

        private void Next() {
            if (!EditorApplication.isPlaying || steps.Count == 0) { running = false; return; }
            var next = currentIndex + 1;
            if (next >= steps.Count) {
                if (!loop) {
                    running = false;
                    currentIndex = steps.Count - 1;
                    ResetRuntimeParameters();
                    runtimeStatus = "Showcase complete — parameters reset";
                    Repaint();
                    return;
                }
                next = 0;
            }
            ApplyStep(next);
            if (running) nextAdvanceAt = EditorApplication.timeSinceStartup + secondsPerStep;
        }

        private void Previous() {
            if (!EditorApplication.isPlaying || steps.Count == 0) return;
            running = false;
            ApplyStep(currentIndex <= 0 ? steps.Count - 1 : currentIndex - 1);
        }

        private void ApplyStep(int index) {
            if (!EditorApplication.isPlaying || index < 0 || index >= steps.Count) return;
            string error;
            if (!EnsureGestureManagerConnected(FindShowcaseTarget(), out error)) { runtimeStatus = error; running = false; return; }
            currentIndex = index;
            steps[index].apply();
            runtimeStatus = "Runtime: " + steps[index].category + " — " + steps[index].label;
            SceneView.RepaintAll();
            Repaint();
        }

        private GameObject FindShowcaseTarget() {
            if (profile == null || profile.AvatarRoot == null) return null;
            var avatarName = profile.AvatarRoot.name;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects()) if (string.Equals(root.name, "VRCF Test Copy for " + avatarName, StringComparison.OrdinalIgnoreCase)) return root;
            foreach (var root in scene.GetRootGameObjects()) if (string.Equals(root.name, avatarName + "(Clone)", StringComparison.OrdinalIgnoreCase)) return root;
            return null;
        }

        private static void FrameAvatar(GameObject avatar) {
            if (avatar == null) return;
            Selection.activeGameObject = avatar;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        }

        private static Type FindLoadedType(string fullName) {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static Component GetOrCreateGestureManager() {
            var type = FindLoadedType(GestureManagerTypeName);
            if (type == null) return null;
            var existing = UnityEngine.Object.FindObjectsOfType(type, true).OfType<Component>().FirstOrDefault();
            if (existing != null) return existing;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GestureManagerPrefabPath);
            if (prefab == null) return null;
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            return instance != null ? instance.GetComponent(type) as Component : null;
        }

        private bool EnsureGestureManagerConnected(GameObject avatar, out string error) {
            error = null;
            if (!EditorApplication.isPlaying) { error = "Runtime showcase requires Play Mode."; return false; }
            if (avatar == null) { error = "VRCFury Editor Test Copy was not found in Play Mode."; return false; }
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) { error = "The VRCFury test copy has no VRCAvatarDescriptor."; return false; }
            var manager = GetOrCreateGestureManager();
            if (manager == null) { error = "Gesture Manager is not available in this project."; return false; }

            var managerType = manager.GetType();
            var moduleField = managerType.GetField("Module", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var module = moduleField != null ? moduleField.GetValue(manager) : null;
            if (module != null) {
                var avatarField = module.GetType().GetField("Avatar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var controlled = avatarField != null ? avatarField.GetValue(module) as GameObject : null;
                if (controlled == avatar) return true;
            }

            var moduleType = FindLoadedType(GestureManagerModuleTypeName);
            if (moduleType == null) { error = "Gesture Manager's VRC3 runtime module has not loaded yet."; return false; }
            try {
                var constructor = moduleType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(c => { var p = c.GetParameters(); return p.Length == 2 && p[0].ParameterType.IsAssignableFrom(typeof(VRCAvatarDescriptor)); });
                if (constructor == null) { error = "Could not find Gesture Manager's VRC3 module constructor."; return false; }
                var newModule = constructor.Invoke(new object[] { descriptor, null });
                var setModule = managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "SetModule" && m.GetParameters().Length == 1);
                if (setModule == null) { error = "Gesture Manager SetModule API was not found."; return false; }
                setModule.Invoke(manager, new[] { newModule });
                return true;
            } catch (Exception e) {
                error = "Gesture Manager connection failed: " + (e.InnerException != null ? e.InnerException.Message : e.Message);
                return false;
            }
        }

        private bool SetGestureParameter(string parameterName, float value) {
            if (string.IsNullOrWhiteSpace(parameterName)) return false;
            var avatar = FindShowcaseTarget();
            string error;
            if (!EnsureGestureManagerConnected(avatar, out error)) { Debug.LogWarning("[Shiryu Showcase] " + error); return false; }
            var manager = GetOrCreateGestureManager();
            if (manager == null) return false;
            var moduleField = manager.GetType().GetField("Module", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var module = moduleField != null ? moduleField.GetValue(manager) : null;
            if (module == null) return false;
            try {
                var getParam = module.GetType().GetMethod("GetParam", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
                var param = getParam != null ? getParam.Invoke(module, new object[] { parameterName }) : null;
                if (param == null) return false;
                var set = param.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "Set" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(float));
                if (set == null) return false;
                set.Invoke(param, new object[] { value });
                return true;
            } catch (Exception e) {
                Debug.LogWarning("[Shiryu Showcase] Failed to set " + parameterName + ": " + e.Message);
                return false;
            }
        }
    }
}
