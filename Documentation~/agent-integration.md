# Super Hero UI AI-agent integration guide

English | [한국어](agent-integration.ko.md)

Use this guide to start `com.superherounite.ui` work without prior conversation
context. It explains how an Agent finds the installed revision, loads only the
documents relevant to the task, applies the package contract, and reports
verification. The consuming repository's instructions and the developer's
explicit requirements take precedence.

## One-time host-project bootstrap

For Codex, `AGENTS.md` is loaded at task startup from the project root through
the initial working directory. The package's own `AGENTS.md` therefore loads
automatically only when the task starts in the package root or below it. Opening
the package later from a host-root task does not add that file to the instruction
chain. Other AI tools can have different discovery rules.

Add this compact section to the consuming repository's active root Agent
instruction file:

```md
## Super Hero UI

For any task involving `com.superherounite.ui`, and for every Unity uGUI
implementation or refactor, first check for the embedded
`Packages/com.superherounite.ui/package.json`, then check
`Packages/manifest.json` and `Packages/packages-lock.json` for a local, Git,
registry, or transitive installation. When the package is installed:

- Locate the installed package revision and read
  `Documentation~/agent-integration.md` before editing UI. Embedded packages are
  normally under `Packages/com.superherounite.ui/`. Resolve a local `file:` path
  relative to `Packages/manifest.json`. For Git or registry packages, match the
  version and source/hash in `Packages/packages-lock.json` to the exact
  `Library/PackageCache/com.superherounite.ui@*/` folder; never pick an arbitrary
  or merely newest cache folder.
- Never edit `Library/PackageCache`. Embed the package or work in its source
  repository when package code must change.
- Keep mutable tokens, Styles, Recipes, and registries in project-owned
  `Assets/Editor/...`; keep runtime Prefabs in the normal `Assets/...` hierarchy.
- Do not add package Binding MonoBehaviours or runtime Style traversal. Use
  `Preview / Validate` -> review -> `Apply Reviewed Changes`, then require a fresh
  `Ready` result.
- Preserve project-owned hierarchy, layout, content, state, persistent
  UnityEvents, and unmanaged Prefab overrides. Register nested consumers
  explicitly.
```

Commit the bootstrap with the consuming project. If the repository uses another
active instruction filename or an override file, put it in the file the Agent
actually loads. Start a new Agent task after changing startup instructions.

The block includes a minimum safe contract because a clean checkout may not have
restored its cache yet, and a local dependency can be outside the Agent's
accessible workspace. When the exact installed guide cannot be read, do not
guess version-specific APIs or select another cached revision. Retain the minimum
contract above and resolve access or package restoration before authoring package
assets.

## Resolve and route package context

1. Check `Packages/com.superherounite.ui/package.json` first, then inspect
   `Packages/manifest.json` and `Packages/packages-lock.json` for the active
   local, Git, registry, or transitive `com.superherounite.ui` dependency.
2. Resolve the embedded, local, Git, or registry root exactly as described in the
   bootstrap. Treat resolved PackageCache content as read-only.
3. Read the installed `package.json`; it is authoritative for version, Unity
   compatibility, and dependencies.
4. For consuming-project UI authoring, read [the authoring guide](index.md). It is
   the canonical contract for binding ownership, target capture, Preview/Apply,
   consumer validation, and guards.
5. When the task involves an input field, dropdown, tab, table, popup, badge, or
   dynamic list, also read
   [Composite Control Recipes](../Samples~/Composite%20Control%20Recipes/README.md).
6. Read the package-root [AGENTS.md](../AGENTS.md) only when changing package
   source. Do not load package tests or implementation files for routine UI
   authoring unless the installed documentation is insufficient or behavior must
   be diagnosed.

This routing keeps ordinary tasks small: the integration guide supplies Agent
workflow, the authoring guide supplies the product contract, and the optional
sample supplies composite mappings.

## Inspect the consuming project

Before proposing or editing UI, inspect:

1. the closest repository instructions and current worktree changes;
2. the requested design source and every required visual or interaction state;
3. the target Prefab, Scene placement, behavior code, and serialized event
   contract;
4. compatible project Common Prefabs and nearby controls;
5. existing project-owned `ColorToken`, typed Style, `PrefabStyleRecipe`, and
   `StyleRecipeRegistry` assets.

Record the following task contract:

- owner Prefab for each styled component;
- outer Prefabs that intentionally nest the owner;
- serialized component properties managed by the Recipe;
- hierarchy, layout, content, events, and state that stay locally managed;
- initial, editing, error, apply, cancel, reopen, and data-refresh behavior;
- visual, interaction, serialization, and build checks required for completion.

Do not infer missing product UX from Style primitives. Resolve consequentially
different behavior with the developer.

## Apply the ownership model

Super Hero UI is an Editor-only authoring and Prefab-bake package. It has no
runtime assembly, runtime Style service, theme traversal, or package Binding
component. Input fields, dropdowns, tabs, tables, popups, and badges remain
project-owned composites.

The project owns the source token, Style, Sprite, TMP font, Recipe, registry, and
Prefab assets. A configured Recipe owns only the documented serialized values on
its target components, including a Sprite or font reference when its typed
binding declares that reference. Layout, content, localization, interaction,
accessibility, data, semantic state, and persistent UnityEvents remain local.

Keep mutable authoring assets in a project Editor folder such as
`Assets/Editor/SuperHeroUI/`. Keep styled Prefabs in normal runtime asset paths.
Do not put authoring assets in Resources, Addressables, or AssetBundles.

Use the narrowest binding defined in the authoring guide. In particular:

- Image tint is required even when optional Image fields are selected.
- Surface requires fill; outline target and outline Style are assigned as a pair.
- Text owns the selected TMP font's shared material as well as its documented
  typography fields.
- Selectable requires an existing, project-owned `targetGraphic` and all five
  state tokens; the Recipe owns transition, colors, multiplier, and fade, not the
  `targetGraphic` reference itself.
- Different registries do not cross-check ownership, so do not split one owner
  Prefab's managed properties across registries.

## Execute the authored workflow

1. Author the complete project Prefab and all required components first. Keep
   compatible shared Prefabs nested, and use serialized references plus
   persistent UnityEvents for fixed relationships.
2. Reuse semantically matching tokens and Styles. Create clearly named project
   assets only when meaning or the complete owned value set differs.
3. Create one Recipe for each saved owner Prefab and add at least one typed
   binding. Capture targets from that exact, saved owner in Prefab Mode.
4. A Recipe cannot capture a component owned by a nested Prefab. Put that binding
   in the nested source Prefab's Recipe.
5. A Recipe owner may itself be a Prefab Variant. Managed overrides at that owner
   boundary are its valid baseline. For every explicitly registered outer
   consumer, managed overrides in direct and intermediate Variant links above the
   owner are rejected.
6. Add intended outer consumers to `Consumer Prefabs`; there is no project-wide
   automatic consumer scan.
7. Add the Recipe to a registry under `Assets/`. New registries default both Play
   and Build validation toggles to enabled; verify their actual Inspector values.
8. In **Tools > Super Hero UI > Style Recipes**, run **Preview / Validate**. Check
   owner and `Consumer Prefabs` assignments in the Recipe Inspector; the window
   lists change/error details and a total asset count rather than every asset
   path.
9. `Stale` normally means valid reviewed changes are available. Review them and
   run **Apply Reviewed Changes**. A dependency change after Preview invalidates
   the approval fingerprint and requires a new Preview.
10. Run Preview again and require `Ready`. Applying unchanged values again must
    produce no write.

## Tune an existing UI in Play Mode

For color or image PPUM adjustments during Play Mode, use
**Tools > Super Hero UI > Play Mode Tuning** and the
[authoring guide's tuning workflow](index.md#play-mode-tuning).

- Choose an existing **Recipe Target** and explicitly assign the intended
  **Live Component**, or use **Use Selected Object**. Do not infer runtime
  targets from hierarchy names or add package
  components to discover them. PPUM is available only when its ImageStyle owns
  it; it is not a Figma radius conversion.
- Keep experiments in the tuning drafts. Do not edit source ColorTokens,
  Styles, Recipes, or Prefabs to try a runtime value. Drafts are retained for
  the current Editor session; they are not a replacement for saved assets.
- Check the selected instance visually and exercise its relevant states.
  Runtime scripts and animations may overwrite a draft, and new instances do
  not inherit it automatically.
- After Play exit, review all **Saved Drafts** in Edit Mode, including drafts
  from other Recipes. A shared token or Style affects its other references too.
  Resolve conflicting drafts or source changes before **Write Reviewed Style
  Changes**, which writes the supported source fields and saves those assets.
  Source files with existing unsaved edits, including sub-assets, block review;
  resolve those edits separately without using tuning to save or discard them.
- Source writes do not bake Prefabs. Continue with the existing registry
  Preview, reviewed Apply, and fresh `Ready` checks. Check every affected
  registry; the tuning window does not automatically bake other registries.

## Keep authoring safe and deterministic

- Do not add runtime Style managers, package Binding MonoBehaviours, reflection
  scans, or hierarchy traversal. Baked Unity component values are the runtime
  result.
- Avoid runtime `AddComponent`, `GetComponent`, `transform.Find`, and fixed
  `AddListener` wiring for authored fixed UI.
- Instantiate only fully authored, Inspector-assigned item Prefabs for genuine
  dynamic collections, then bind runtime data and state.
- Recipe and Style public values are getter-only. Prefer Inspector authoring and
  the package's `Capture`, `Select`, and `Clear` controls.
- Do not manufacture `GlobalObjectId` strings or hand-edit Prefab or
  ScriptableObject YAML. If approved Editor automation is necessary, keep it in
  an Editor assembly, use `SerializedObject` against the installed version, and
  verify the saved assets in Unity.
- Never call `SuperHeroUnite.UI.Editor` APIs from runtime code. Public
  `StyleRecipeProcessor.Preview` and `Apply` are Editor-tooling APIs.
- Preserve GUIDs, nested Prefab links, persistent events, and every property
  outside a binding's declared ownership.

## Verify and report completion

Verify the Recipe Inspector assignments, then confirm that Preview is read-only,
Apply changes only reviewed managed properties, and a fresh Preview is `Ready`.
Check missing scripts and references, exact captured component types, Variant
source chains, consumer overrides, fixed references, and persistent event targets
and counts.

Exercise the consuming feature's specified state transitions and data timing.
Check required resolutions, localized text, expanded menus, scrolling, clipping,
and hit areas where applicable. Confirm the actual Play and Build guard settings
and run a consuming-project Player Build before release.

Package tests validate the bake contract. They do not prove the consuming UI's
visual fidelity, behavior, localization, or Player build. The completion report
must separate automated results from checks still required in the developer's
Unity Editor.

## Keep the integration current

Pin production consumers to an immutable package version or Git tag. After a
dependency update, re-read the installed guide, update any version-specific host
summary, Preview affected registries, and commit `Packages/manifest.json` with
`Packages/packages-lock.json`.

Keep only the compact bootstrap in the host root. Use the installed package
revision as the canonical detailed contract instead of copying these guides into
every consuming repository.
