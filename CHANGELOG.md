# Changelog

English | [한국어](CHANGELOG.ko.md)

All notable changes to Super Hero UI are documented in this file. The package follows Semantic Versioning.

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

- Add a company-approved license.
- Create and verify an immutable version tag from the standalone Git repository.
- Install from the released Git URL in a clean consuming project.
- Verify a consuming-project Player Build with its real asset and build configuration.
