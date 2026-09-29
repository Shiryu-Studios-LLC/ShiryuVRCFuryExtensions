using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ShiryuStudios.VRCFuryExtensions.Editor {
    /// <summary>
    /// Reusable special features that are useful across avatars but are more complex than a normal
    /// toggle. Currently provides full local/remote invisibility and a local-only ghost self view.
    /// </summary>
    public static class ShiryuSpecialFeatureBuilder {
        public static void Build(ShiryuAvatarProfile profile, GameObject customRoot, ShiryuBuildResult result) {
            if (profile == null || customRoot == null || profile.buildSettings == null) return;
            var settings = profile.buildSettings.visibility;
            if (settings == null || (!settings.enableInvisible && !settings.enableLocalGhost)) return;
            var specialRoot = EnsureHierarchy(customRoot, "Special");
            if (settings.enableInvisible) BuildInvisible(profile.AvatarRoot, specialRoot, settings, result);
            if (settings.enableLocalGhost) BuildLocalGhost(profile.AvatarRoot, specialRoot, settings, result);
        }

        private static void BuildInvisible(GameObject avatar, GameObject root, ShiryuVisibilityFeatureSettings settings, ShiryuBuildResult result) {
            var menu = string.IsNullOrWhiteSpace(settings.invisibleMenuPath) ? "SPECIAL/Invisible" : settings.invisibleMenuPath;
            var parameter = string.IsNullOrWhiteSpace(settings.invisibleParameter) ? "Shiryu_Invisible" : settings.invisibleParameter;
            var dissolveProperty = string.IsNullOrWhiteSpace(settings.dissolveProperty) ? "_DissolveAlpha" : settings.dissolveProperty;
            var transition = Mathf.Max(0f, settings.transitionSeconds);

            var toggle = NewToggle(menu, parameter, false, true, transition > 0f, transition);
            Set(toggle, "separateLocal", true);
            var remote = State();
            var local = State();
            var remoteTransition = State();
            var localTransition = State();
            var dissolveCount = 0;
            var hardHideCount = 0;

            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true)) {
                var materials = renderer.sharedMaterials.Where(x => x != null).ToArray();
                if (materials.Length == 0) continue;
                var hasDissolve = materials.Any(x => x.HasProperty(dissolveProperty));
                if (hasDissolve) {
                    AddAction(remote, MaterialProperty(renderer.gameObject, dissolveProperty, 1f));
                    AddAction(local, MaterialProperty(renderer.gameObject, dissolveProperty, 1f));
                    AddAction(remoteTransition, MaterialProperty(renderer.gameObject, dissolveProperty, 0f));
                    AddAction(localTransition, MaterialProperty(renderer.gameObject, dissolveProperty, 0f));
                    dissolveCount++;
                }

                // If any material slot cannot dissolve, hide the complete renderer at the finished
                // invisible state so mixed renderers never leave a visible slot behind.
                if (materials.Any(x => !x.HasProperty(dissolveProperty))) {
                    AddAction(remote, ObjectToggle(renderer.gameObject, false));
                    AddAction(local, ObjectToggle(renderer.gameObject, false));
                    hardHideCount++;
                }
            }

            Set(toggle, "state", remote);
            Set(toggle, "localState", local);
            Set(toggle, "transitionStateIn", remoteTransition);
            Set(toggle, "transitionStateOut", remoteTransition);
            Set(toggle, "localTransitionStateIn", localTransition);
            Set(toggle, "localTransitionStateOut", localTransition);
            SetIfPresent(toggle, "localTransitionTimeIn", transition);
            SetIfPresent(toggle, "localTransitionTimeOut", transition);
            PutFeature(Holder(root, "Invisible"), toggle);
            result.Info("Visibility: Invisible affects " + dissolveCount + " dissolvable renderer(s); " + hardHideCount + " mixed/non-dissolve renderer(s) hard-hide at the completed state.");
        }

        private static void BuildLocalGhost(GameObject avatar, GameObject root, ShiryuVisibilityFeatureSettings settings, ShiryuBuildResult result) {
            var menu = string.IsNullOrWhiteSpace(settings.ghostMenuPath) ? "SPECIAL/Ghost Self View" : settings.ghostMenuPath;
            var parameter = string.IsNullOrWhiteSpace(settings.ghostParameter) ? "Shiryu_GhostSelfView" : settings.ghostParameter;
            var dissolveProperty = string.IsNullOrWhiteSpace(settings.dissolveProperty) ? "_DissolveAlpha" : settings.dissolveProperty;
            var alphaProperty = string.IsNullOrWhiteSpace(settings.ghostAlphaProperty) ? "_AlphaMod" : settings.ghostAlphaProperty;

            var toggle = NewToggle(menu, parameter, false, true, false, 0f);
            Set(toggle, "separateLocal", true);
            var remote = State();
            var local = State();
            var compatible = 0;
            var hidden = new List<string>();

            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true)) {
                var materials = renderer.sharedMaterials.Where(x => x != null).ToArray();
                if (materials.Length == 0) continue;
                if (materials.All(x => x.HasProperty(dissolveProperty) && x.HasProperty(alphaProperty))) {
                    AddAction(local, MaterialProperty(renderer.gameObject, dissolveProperty, 0f));
                    AddAction(local, MaterialProperty(renderer.gameObject, alphaProperty, settings.ghostAlphaModifier));
                    compatible++;
                } else {
                    AddAction(local, ObjectToggle(renderer.gameObject, false));
                    hidden.Add(renderer.name);
                }
            }

            Set(toggle, "state", remote);
            Set(toggle, "localState", local);
            PutFeature(Holder(root, "Ghost Self View"), toggle);
            result.Info("Visibility: Ghost Self View reveals " + compatible + " compatible renderer(s) locally and hides " + hidden.Distinct().Count() + " unsupported renderer(s) locally.");
        }

        private static GameObject EnsureHierarchy(GameObject root, string path) {
            var current = root;
            foreach (var segment in (path ?? string.Empty).Split('/').Where(x => !string.IsNullOrWhiteSpace(x))) current = Holder(current, segment.Trim());
            return current;
        }

        private static GameObject Holder(GameObject root, string name) {
            foreach (Transform child in root.transform) if (string.Equals(child.name, name, StringComparison.Ordinal)) return child.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return go;
        }

        private static Type FindType(string fullName) {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            throw new InvalidOperationException("VRCFury type not found: " + fullName);
        }

        private static object New(string fullName) {
            var type = FindType(fullName);
            var value = Activator.CreateInstance(type, true);
            var latest = type.GetMethod("GetLatestVersion", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var version = type.GetProperty("Version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (latest != null && version != null && version.CanWrite) {
                try { version.SetValue(value, Convert.ToInt32(latest.Invoke(value, null)), null); } catch { }
            }
            return value;
        }

        private static void Set(object target, string fieldName, object value) {
            if (target == null) return;
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Field " + fieldName + " missing on " + target.GetType().FullName);
            if (value != null && field.FieldType.IsEnum && value is string) value = Enum.Parse(field.FieldType, (string)value);
            field.SetValue(target, value);
        }

        private static void SetIfPresent(object target, string fieldName, object value) {
            if (target == null) return;
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) field.SetValue(target, value);
        }

        private static object State() {
            return New("VF.Model.State");
        }

        private static void AddAction(object state, object action) {
            var field = state.GetType().GetField("actions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var list = field != null ? field.GetValue(state) as IList : null;
            if (list == null) throw new InvalidOperationException("VRCFury State.actions is unavailable.");
            list.Add(action);
        }

        private static object ObjectToggle(GameObject target, bool enabled) {
            var action = New("VF.Model.StateAction.ObjectToggleAction");
            Set(action, "obj", target);
            Set(action, "mode", enabled ? "TurnOn" : "TurnOff");
            return action;
        }

        private static object MaterialProperty(GameObject target, string property, float value) {
            var action = New("VF.Model.StateAction.MaterialPropertyAction");
            Set(action, "renderer2", target);
            Set(action, "affectAllMeshes", false);
            Set(action, "propertyName", property);
            Set(action, "propertyType", "Float");
            Set(action, "value", value);
            return action;
        }

        private static object NewToggle(string menu, string parameter, bool defaultOn, bool saved, bool transition, float transitionSeconds) {
            var toggle = New("VF.Model.Feature.Toggle");
            Set(toggle, "name", menu);
            Set(toggle, "saved", saved);
            Set(toggle, "defaultOn", defaultOn);
            Set(toggle, "useGlobalParam", true);
            Set(toggle, "globalParam", parameter);
            if (transition) {
                Set(toggle, "hasTransition", true);
                Set(toggle, "transitionTimeIn", transitionSeconds);
                Set(toggle, "transitionTimeOut", transitionSeconds);
                Set(toggle, "simpleOutTransition", true);
                Set(toggle, "expandIntoTransition", true);
            }
            return toggle;
        }

        private static Component PutFeature(GameObject holder, object feature) {
            var componentType = FindType("VF.Model.VRCFury");
            var components = holder.GetComponents(componentType);
            var component = components.Length > 0 ? components[0] as Component : holder.AddComponent(componentType);
            for (var i = 1; i < components.Length; i++) UnityEngine.Object.DestroyImmediate(components[i], true);
            var field = componentType.GetField("content", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("VRCFury component content field is unavailable.");
            field.SetValue(component, feature);
            EditorUtility.SetDirty(component);
            return component;
        }
    }
}
