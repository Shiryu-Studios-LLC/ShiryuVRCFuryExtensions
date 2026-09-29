# Shiryu VRCFury Extensions

Reusable VRChat avatar authoring, build and runtime QA extensions by Shiryu Studios LLC.

## Architecture

- `ShiryuAvatarProfile` is a **scene-bound component** attached to or beneath an avatar root.
- Authored targets use serialized `GameObject`, `Renderer`, `SkinnedMeshRenderer`, and `Material` references as the source of truth.
- Hierarchy paths and `GlobalObjectId` strings are stored only as fallback/debug metadata.
- The reusable package contains no avatar-specific object names, avatar names, or required project paths.
- Avatar behavior is primarily data-driven through the profile. Reusable build features can be enabled per avatar without writing a new builder.
- Truly project-specific work can register small pre/post hooks through `ShiryuBuildExtensionRegistry` instead of replacing the build pipeline.
- Legacy import/migration adapters can still register through `ShiryuProjectAdapterRegistry`, but normal builds should use `ShiryuAvatarBuildPipeline`.

## Manager

Open **Shiryu Studios → VRCFury Extensions → Manager**.

The manager provides a VRCFury-inspired workflow for:

- Toggles and buttons
- Continuous radials
- Discrete radials
- Presets that store control values rather than duplicating action logic
- Object-state actions
- Blendshape actions selected from the referenced mesh
- Material properties selected from referenced renderers/materials
- Material swaps
- Exclusive groups
- Build/reference validation
- Reusable material preparation
- Invisible and local ghost/self-view features
- Always-active avatar objects
- VRCFury Editor Test Copy generation for runtime inspection
- Runtime showcase / QA

## General build pipeline

`ShiryuAvatarBuildPipeline` is the normal entry point for any avatar. It:

1. Validates serialized scene references.
2. Restores configured always-active objects.
3. Runs optional reusable build-extension hooks.
4. Prepares configured materials.
5. Builds controls/presets/discrete radials/entry rules through `ShiryuVRCFuryBuilder`.
6. Builds reusable visibility features such as Invisible and Ghost Self View.
7. Writes an avatar-scoped report under `Assets/ShiryuGenerated/VRCFuryExtensions/<avatar>/`.
8. Saves the scene when requested.

Generated controllers and reports are avatar-scoped. The package itself is never used as a generated-output directory.

## Material preparation

`ShiryuAvatarMaterialPreparer` is profile-driven and reusable across avatars. Depending on profile settings it can:

- Prepare all avatar Poiyomi materials or only materials referenced by controls.
- Configure TransClipping and noisy dissolve.
- Force Dissolved Color alpha to zero.
- Apply configurable render-queue rules.
- Mark material properties referenced by controls as animated for Poiyomi optimization.
- Apply profile defaults for material actions and discrete radial options.
- Configure unique Poiyomi rename suffixes for multi-material animated-property aliases.
- Apply generic shader repair rules by shader name.
- Create avatar-scoped material backups.

No material folder, body name, hair name, skin name, or Demon-specific material is hard-coded in the package.

## Visibility features

`ShiryuSpecialFeatureBuilder` provides reusable profile-configured features:

- Full local + remote Invisible state using a configurable dissolve property.
- Hard-hide fallback for mixed/non-dissolve renderers so unsupported material slots cannot remain visible.
- Separate local-only Ghost Self View with a configurable transparency property/value.

These are available to any avatar by enabling them in the profile's Build Features section.

## Runtime showcase + QA

Open **Shiryu Studios → VRCFury Extensions → Runtime Showcase + QA**.

`ShiryuAvatarShowcaseWindow` generates its test plan from the selected profile rather than from avatar-specific parameter names. It can exercise:

- Toggle/button controls
- Continuous radials at representative values
- Every discrete radial option
- Presets
- Exclusive sets
- Invisible / local ghost behavior
- Cross-control blendshape QA for clothing/body-morph combinations

The showcase drives a VRCFury Editor Test Copy through Gesture Manager, so generated FX behavior is tested rather than manually previewing the editable source avatar.

`ShiryuTestCopyVisibilityWatcher` also works across profiles and can hide an editable source avatar in Scene view while its VRCFury test copy exists.

## VRCFury integration

The normal control builder prefers VRCFury's public API (`FuryComponents`, `FuryToggle`, `FuryActionSet`, and `FuryFullController`). A small compatibility bridge is used only for VRCFury features that are not currently exposed through that public API, such as some material-property and local/remote transition-state configuration.

## Build extensions

Most avatars should not need custom C# builders. When a project has a genuinely unique requirement, register a narrowly scoped hook through `ShiryuBuildExtensionRegistry`:

- `beforeBuild` for preparation that cannot be represented by profile data.
- `afterBuild` for project-specific generated additions.
- `canHandle` to restrict the hook to the intended profile/avatar.

This keeps the central builder reusable and prevents avatar-specific code from becoming a second source of truth.

## Migration

Migration adapters should resolve legacy names once, store the resulting Unity references directly on `ShiryuAvatarProfile`, and surface ambiguous matches instead of guessing. After migration is verified, the scene-bound profile becomes the only customization source of truth and legacy authoring assets/code can be retired.
