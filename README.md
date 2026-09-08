# Super Hero UI

English | [한국어](README.ko.md)

Super Hero UI is an Editor-only style authoring package for Unity 6000.0. It shares visual decisions through ScriptableObject assets, previews their impact, and bakes approved values into ordinary uGUI Prefabs. The package ID is `com.superherounite.ui` and this development release is `0.1.0-preview.1`.

The package deliberately stops below the composite-control boundary. An input field, dropdown, tab, table, popup, or badge usually carries product-specific hierarchy, interaction, accessibility, layout, and data behavior. Projects build those controls from reusable visual primitives instead of inheriting a package-owned Prefab contract.

## What it provides

- enum-free `ColorToken` assets, so adding a color does not recompile scripts;
- typed `ImageStyle`, `SurfaceStyle`, `TextStyle`, and `SelectableStyle` assets;
- stable component capture from the owner Prefab in Prefab Mode;
- a read-only Preview and an explicit Apply step tied to the reviewed dependency fingerprint;
- per-property ownership checks and validation of explicitly tracked nested-Prefab consumers;
- optional Play Mode and Player Build readiness guards;
- no runtime assembly and no package-owned Binding component added to or required by styled Prefabs.

## AI agent instructions

The package includes a short source-level [AGENTS.md](AGENTS.md) and complete
[English](Documentation~/agent-integration.md) and
[Korean](Documentation~/agent-integration.ko.md) integration guides. The guides
define package discovery, context loading, binding choices, Prefab authoring,
runtime constraints, and completion checks.

A package-scoped instruction file cannot automatically govern sibling files in
the consuming project's `Assets/` directory. Copy the bootstrap from the
integration guide into that project's active root Agent instructions and commit
it once. The bootstrap includes a minimum safe contract for clean checkouts in
which a Git package has not yet resolved into `Library/PackageCache`.

## Supported bindings

| Binding | Values owned by the Recipe |
|---|---|
| Graphic Color | `Graphic.color` for Graphics that use the base serialized color field; use Text for TMP targets |
| Image | tint, plus individually enabled ownership of sprite, `Image.Type`, `preserveAspect`, `fillCenter`, and `pixelsPerUnitMultiplier` |
| Surface | a required fill `ImageStyle` on one authored `Image` target and an optional outline `ImageStyle` on a second target |
| Text | TMP font and its shared material, color, size and size base, style, auto-size settings, character/word/line/paragraph spacing |
| Selectable | Color Tint transition, five state colors, color multiplier, and fade duration |

Content, anchors, dimensions, layout components, localization, UnityEvents, and runtime state remain owned by the consuming project.

## Authoring workflow

1. Create color and style assets from **Create > Super Hero UI > Styles**.
2. Create a **Prefab Style Recipe** and assign its owner Prefab.
3. Add typed bindings to the Recipe.
4. Open and save the owner in Prefab Mode, select each target, then use the binding's **Capture** button.
5. Create a **Style Recipe Registry** and add the Recipes it should validate.
6. Open **Tools > Super Hero UI > Style Recipes**.
7. Run **Preview / Validate**, review every proposed property change, then choose **Apply Reviewed Changes**.
8. Run Preview again; a clean result is `Ready`. Applying the same values again produces no changes.

Keep mutable tokens, styles, Recipes, and registries in a dedicated project authoring folder such as `Assets/Editor/SuperHeroUI/`. Keep target Prefabs in the project's normal runtime asset hierarchy. Do not place authoring assets in runtime Resources, Addressables, or AssetBundles.

The bake adds no package-owned component or Recipe, style, or token reference. It writes supported values to existing Unity and TextMesh Pro components. Player-build verification remains a release gate for each consuming project's build pipeline.

Creating a registry opts it into package discovery. Play and Build validation are enabled by default on a new registry and can be disabled independently while a project migrates existing UI. Consumer override checks cover only the Prefabs explicitly assigned to a Recipe's `Consumer Prefabs` list.

## Composite controls

The importable **Composite Control Recipes** sample documents practical primitive mappings for input fields, dropdowns, tabs, tables, popups, and badges. It does not install product UI Prefabs or runtime scripts.

## Installation

For embedded development, keep this folder at `Packages/com.superherounite.ui`. A standalone Git repository should place `package.json` at its root and publish immutable Semantic Version tags.

After that repository and tag exist, add a Git dependency to the consuming project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.superherounite.ui": "https://github.com/<organization>/super-hero-ui.git#v0.1.0-preview.1"
  }
}
```

For a local standalone checkout kept beside the consuming project under the same parent folder, the path is relative to the consuming project's `Packages/manifest.json`:

```json
"com.superherounite.ui": "file:../../super-hero-ui"
```

A temporary monorepo dependency uses the package subfolder before the revision:

```json
"com.superherounite.ui": "https://<git-host>/<organization>/<repository>.git?path=/Packages/com.superherounite.ui#<immutable-tag>"
```

Commit both `Packages/manifest.json` and `Packages/packages-lock.json` in consuming projects. Do not use a moving branch for production dependencies or put credentials in the URL. Preserve every `.meta` file when extracting the package to its standalone repository.

Git-installed package tests require the Unity Test Framework plus `"testables": ["com.superherounite.ui"]` in the consuming or CI project's manifest. Embedded package tests are discovered directly.

To publish an update, change the package version and changelog together, commit them without changing existing `.meta` GUIDs, create a new immutable version tag, then update the consuming project's `#tag` reference and commit its refreshed lock file. Run package tests from a small temporary Unity project or a repository-owned `TestProject~` before publishing the tag.

The standalone repository address, release tag, and company-approved license must exist before external distribution. Never edit the copy under `Library/PackageCache`.

See [Documentation~/index.md](Documentation~/index.md) for the complete contract and [Samples~/Composite Control Recipes/README.md](Samples~/Composite%20Control%20Recipes/README.md) for composition guidance.
