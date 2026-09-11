# Changelog

English | [한국어](CHANGELOG.ko.md)

All notable changes to Super Hero UI are documented in this file. The package follows Semantic Versioning.

## [0.1.0-preview.6] - 2026-09-12

### Added

- Separate read-only override repair Preview and reviewed Apply for unintended
  Recipe-owned consumer overrides in explicitly registered Prefabs. Repairs
  preserve unmanaged values and valid typed Variant specializations, then return
  to the ordinary style Preview/Apply workflow.

### Changed

- Reuse direct saved-asset dependency hashes while Unity's artifact dependency
  version is unchanged, including matching domain reloads in the same Editor
  session. Keep exact live authoring state and callback checks, and record the
  completed review's fingerprint without repeating that work. Build guards
  continue to perform full Preview.
- Reuse repeated Recipe, specialization-chain, and target source-chain lookups
  within each loaded consumer inspection, without retaining those results
  across unloads or weakening callback checks and full Build validation.

### Fixed

- Resolve missing `Base Recipe` links from the nearest registered actual Variant
  ancestor, including multi-level specializations. Preserve explicit assignments
  and exact typed ownership checks without modifying authoring assets.
- When an explicitly listed consumer no longer contains its declared owner,
  validate all registered owner instances actually present in that consumer.
  Keep an actionable error when none exists. Preview, Apply, Play, and Build
  follow the same current relationships without guessing names, discovering
  unregistered consumers, or writing assets. Refresh inferred Variant ancestry
  from current dependencies and recheck replacement consumer owners each time.
- Independent bounded scroll views for review details and Registered Recipes,
  with window scrolling to keep controls reachable in short docked windows.

### Validation

- Unity `6000.0.68f1`, graphics enabled: all 54 focused regression cases and
  all 172 Editor tests passed, with no failed or skipped cases.
- Consuming-project visual, interaction, localization, and Player Build checks
  remain separate from package validation.

## [0.1.0-preview.5] - 2026-09-12

### Added

- Editor-only Play Mode Tuning for explicitly selected live UI components:
  Graphic, Image, Surface, TMP text, and Selectable state colors, plus owned
  Image and Surface pixels-per-unit multiplier values.
- Editor-session tuning drafts that survive Play exit, domain reload, and
  closing the window, with temporary live-value restoration and an Edit Mode
  review before writing ColorToken or ImageStyle source values with Undo.
- Shared-source usage review, conflicting-draft and changed-source checks,
  followed by the existing separate reviewed Prefab bake workflow.
- English and Korean tuning workflows in the authoring and AI-agent guides.

### Validation

- Unity `6000.0.68f1`: all 121 Editor tests passed with graphics enabled,
  including 22 tuning cases covering live changes, reloads, restoration,
  source-write review and Undo, Prefab identity, and the existing bake contract.
- This prerelease is for consuming-project testing. Its project-specific
  visual, interaction, localization, and Player Build checks remain separate.

## [0.1.0-preview.4] - 2026-09-11

### Added

- Direct Recipe links in Prefab and child-object Inspector headers, plus Project
  and Hierarchy context menus. Links follow actual Prefab references, distinguish
  owners, Variant sources, and registered consumers, and open a separate
  Inspector without changing the selected UI object.
- Reproducible isolated Editor tests, mixed workloads up to 1,000 owners, and
  baseline performance comparisons with English and Korean validation guides.

### Changed

- Preview reuses unchanged owner and consumer inspections. Apply visits changed
  owners and their registered Prefab dependents in dependency order, while full
  validation remains available and Build guards always run a fresh full Preview.
- Shared target indexes, operation-local dependency reads and native serialized
  state digests reduce repeated work across large registries and TMP font assets.
  The measured 65-Recipe consumer workload reduced median font-size Apply time
  from 15.76 seconds to 5.14 seconds; see the performance guide for conditions.

### Fixed

- Preserve full-registry approval checks, unsaved-state detection, registered
  consumer validation, unmanaged values, and idempotent Apply while using caches.
- Reinspect custom Editor callbacks and serialization callbacks instead of
  reusing Preview or Play Ready results that could conceal callback changes.

## [0.1.0-preview.3] - Unreleased

### Added

- MIT License for public distribution.

### Fixed

- Consumer validation now ignores nested Prefab instance roots that merely pass
  through the managed owner's source chain. A composite owner is validated once
  at its outermost instance, so its own targets resolve without false errors.

## [0.1.0-preview.2] - Unreleased

### Added

- An explicit `Base Recipe` contract for a Prefab Variant Recipe. A Variant may
  specialize a base Recipe's managed property only when it declares that base,
  owns the same captured target and property through a typed binding, and is
  registered beside the base in the same registry.

### Changed

- Consumer validation continues to reject unmanaged or undeclared overrides.
  It now accepts only the explicit typed specialization described above.

## [0.1.0-preview.1] - Unreleased

### Added

- Editor-only `SuperHeroUnite.UI.Editor` assembly for Unity 6000.0 with no runtime assembly.
- ScriptableObject types for project-owned tokens and styles, with typed bindings for Graphic Color, Image, Surface fill and outline, TextMesh Pro text, and Selectable Color Tint states.
- Stable Prefab Mode target capture backed by Unity `GlobalObjectId` metadata.
- Read-only Preview, dependency fingerprint approval, explicit Apply, and idempotent Ready evaluation.
- Validation for missing targets and scripts, duplicate ownership within a registry, nested-owner misuse, explicitly registered consumer overrides through Prefab Variant chains, and dirty tracked Prefab Stages.
- Configurable Play Mode and Player Build readiness guards with an Editor-only Ready cache.
- Composite-control reference mappings for input fields, dropdowns, tabs, tables, popups, badges, and dynamic lists.
- English and Korean versions of the README, authoring guide, changelog, and composite-control reference.
- Self-contained English and Korean AI-agent instructions, including a host-project bootstrap for zero-context package discovery.
- Editor tests for Preview immutability, all five primitive categories including sprite ownership, unmanaged-value preservation, Apply idempotence, stale approval rejection, duplicate ownership, direct and intermediate-Variant consumer overrides, a Variant owner baseline, invalid image parameters, and missing targets.

### Release gates

- Create and verify an immutable version tag from the standalone Git repository.
- Install from the released Git URL in a clean consuming project.
- Verify a consuming-project Player Build with its real asset and build configuration.
