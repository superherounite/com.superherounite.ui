# Super Hero UI authoring guide

English | [한국어](index.ko.md)

Super Hero UI `0.1.0-preview.5` is an Editor-only authoring package for Unity 6000.0. Production code belongs to `SuperHeroUnite.UI.Editor`; package tests belong to `SuperHeroUnite.UI.Editor.Tests`. The package has no runtime assembly.

## Package boundary

The package owns generic authoring mechanics:

- ScriptableObject color and typed style definitions;
- stable Prefab component capture and resolution;
- read-only change previews;
- reviewed, deterministic Prefab baking;
- temporary Play Mode color and image PPUM tuning with reviewed source changes;
- property ownership and explicitly registered consumer validation;
- opt-in discovery with configurable Play Mode and Player Build guards.

The consuming project owns mutable values and product behavior:

- token and style assets;
- Recipes and registries;
- shared and feature-owned Prefabs;
- design-specific hierarchy and layout;
- sprites, fonts, content, localization, input validation, state logic, and persistent UnityEvents.

A practical project layout is:

```text
Assets/
  Editor/SuperHeroUI/
    Tokens/
    Styles/
    Recipes/
  Prefabs/UI/
    Shared/
    Features/
```

Keep authoring assets out of runtime Resources, Addressables, and AssetBundles. Target Prefabs remain in the normal runtime hierarchy and receive only baked component values.

## AI agent integration

The package root includes short source-level [AGENTS.md](../AGENTS.md)
instructions. The complete [AI-agent integration guide](agent-integration.md)
provides a copy-ready host-project bootstrap, package resolution rules, context
loading order, authoring workflow, and completion checks. Add its bootstrap to
the consuming repository's active root instructions so an Agent working under
`Assets/` finds the package contract without prior conversation context.

## Why composite controls stay project-owned

Controls with the same label rarely share a reliable cross-project contract. Input fields differ in validation and error UX; dropdowns differ in data and virtualization; tabs differ in selected-state ownership; tables differ in schema and sorting; popups differ in navigation and focus; badges differ in semantic meaning. Shipping these as universal Prefabs would move product assumptions into a low-level package and make migrations harder.

Super Hero UI instead shares the parts that remain stable across those controls: color, image presentation, paired fill and outline surfaces, typography, selectable color states, target identity, preview, baking, and validation. Projects compose these primitives inside their own Prefabs.

## Typed primitives

### Color Token

A `ColorToken` is a named asset containing one color. Recipes reference the asset directly, so adding or renaming a token does not add an enum member or trigger script compilation.

### Graphic Color

A Graphic Color binding owns only the base serialized `Graphic.color`. Use it when a full `ImageStyle` would own too much. Components such as TMP text that specialize color must use their typed binding.

### Image

An Image binding always owns tint. It can independently opt into ownership of sprite, `Image.Type`, `preserveAspect`, `fillCenter`, and `pixelsPerUnitMultiplier`. Material, raycast behavior, fill method and amount, masks, and layout remain untouched.

### Surface

A Surface combines a required fill `ImageStyle` with an optional outline `ImageStyle`. The Prefab must already contain the corresponding one or two `Image` components. The package styles those targets; it does not create hierarchy or components.

### TextMesh Pro text

A Text binding owns the TMP font and its shared material, color, font size and serialized size base, font style, auto-size state and limits, plus character, word, line, and paragraph spacing. It preserves text content, localization hooks, alignment, wrapping, overflow, margins, and RectTransform layout.

### Selectable

A Selectable binding sets the transition to Color Tint and owns normal, highlighted, pressed, selected, and disabled colors, the color multiplier, and fade duration. It requires an existing `targetGraphic`. Navigation, interactability, animation triggers, sprite states, and persistent events remain unchanged.

## Target capture

Each binding stores Unity's serialized `GlobalObjectId` plus readable target metadata. Capture follows this sequence:

1. Assign the Recipe's owner Prefab.
2. Open that exact owner in Prefab Mode and save pending changes.
3. Select a GameObject containing the expected component.
4. Choose **Capture** on the binding in the Recipe Inspector.

The tool maps the Prefab Mode selection back to the saved asset before recording identity. **Select** later reopens the owner and selects the saved target. Ordinary rename or reparent operations keep working when Unity preserves the serialized object identity; deleting and recreating the component requires a new capture.

A Recipe cannot style a component owned by a nested Prefab instance. Put that binding in the nested source Prefab's Recipe and register any outer Prefabs that must be checked as consumers.

## Find Recipes from a Prefab

Select a Prefab in the Project window, or select an instance or child in the
Hierarchy or Prefab Mode. The Inspector header shows **Style Recipes**. Click
a Recipe name to open it in a separate Inspector while keeping the selected
UI object and Prefab Mode open. This also works when the original Inspector is
locked. No Prefab edits need to be saved just to navigate.

The links use actual Prefab references, so Recipe names and folder conventions
do not affect discovery. **Owner** identifies the selected Prefab's Recipe;
**Base Prefab** identifies a source Recipe inherited by a Variant; **Consumer**
identifies a Recipe that explicitly lists this Prefab as a consumer. Selecting
an object inside a nested Prefab uses that nearest nested source. Navigation
also finds Recipes that have not yet been added to a registry.

Up to three links appear directly, with **Show all … recipes** for larger
results. Hover a link to see the Recipe and Prefab paths. The Project and
Hierarchy context menus also provide **Super Hero UI > Find Style Recipes**;
a single result opens directly, while multiple results offer a choice with
paths. Select one object at a time. Use **Refresh** if a link was changed by
an external tool or script and has not appeared yet.

Finding and opening Recipes is read-only. It does not add components, create
Recipes, capture targets, run Preview, or Apply changes. The Recipe catalog is
cached, and unchanged Inspector repaints do not scan assets or load Prefab
contents.

## Preview and Apply

Open **Tools > Super Hero UI > Style Recipes** and select a registry.

- `Preview / Validate` loads clean Prefab contents, resolves every typed target, and reports the affected owner, target, property, and proposed value without saving.
- `Apply Reviewed Changes` accepts only the exact preview fingerprint. A change to a Recipe, style, token, owner, consumer, or package implementation makes the preview invalid.
- Apply writes only declared properties and saves only owners with differences.
- A fresh preview reports `Ready` when no differences or validation errors remain.
- Reapplying a `Ready` review produces no write.

`Stale` means at least one Prefab value differs from its Recipe. A stale approval is a separate condition: Apply rejects it and requires another Preview. `Error` means the Recipe cannot be safely evaluated or applied.

## Play Mode tuning

Open **Tools > Super Hero UI > Play Mode Tuning** to try colors and
`Image.pixelsPerUnitMultiplier` (PPUM) on a running UI. This is an Editor tool:
it adds no runtime component, Style reference, or Player code. The existing
Prefab **Preview / Validate** remains read-only and Prefab Apply remains an Edit
Mode operation. **Style Recipes** also has an **Open Play Mode Tuning** button;
the Hierarchy context menu provides **Super Hero UI > Play Mode Tuning**.

1. Select a saved project Recipe and its **Recipe Target**. The window shows the
   supported properties together for that component: Graphic Color, Image tint,
   Surface fill or outline tint, TMP text color, or the five Selectable Color
   Tint states. **Rounding (PPUM)** appears for Image and Surface targets only
   when the corresponding `ImageStyle` owns PPUM. Recipe and source assets must
   be saved under `Assets/`. **Find Recipes for Selection** can find a Recipe
   from an object's Prefab link; choose the Recipe explicitly for unlinked clones.
2. In Play Mode, drag the actual running component into **Live Component**, or
   select its GameObject and click **Use Selected Object**. The tool requires
   the captured concrete component type. For linked Prefab instances it also
   checks correspondence with the captured target; for unlinked generated
   objects you must verify the mapping yourself. An unlinked clone can be
   assigned even under a linked parent. Names and hierarchy paths are not used
   to guess the target. Prefab assets, Prefab Mode, and preview scenes are excluded.
3. Adjust the draft value to preview it on that instance. Experiments do not
   write ColorToken, Style, Recipe, or Prefab assets. Other instances, including
   newly spawned ones, do not receive the draft automatically.
4. Leave Play Mode when the result is useful. Drafts survive Play exit, domain
   reload, and closing the window in the same Editor session. They are not a
   durable save across Editor restarts. Temporary component changes are restored
   when Play ends, the window closes, or scripts reload, as described below.
5. In Edit Mode, choose **Review Style Changes**. This reviews every entry in
   **Saved Drafts**, including other Recipes, rather than just the currently
   selected target. Inspect source paths, proposed values, and shared usage.
   Discard conflicting drafts that propose different values for one source.
   If review reports a changed Recipe, binding, or conflicting source value,
   discard the affected draft and tune again. Changes after review require another review.
   **Write Reviewed Style Changes** modifies only ColorToken color and
   ImageStyle PPUM, saves those source assets with Undo support, and clears the
   drafts. It does not modify Recipe assignments or bake Prefabs. Sources must
   be editable project assets under `Assets/`. Save or revert existing unsaved
   changes in a source asset file before reviewing, including its sub-assets.
   The tool rejects these dirty files and does not save or discard those edits.
6. Click **Open Style Recipes**, run **Preview / Validate**, inspect the affected
   Prefabs and registered consumers, use **Apply Reviewed Changes**, and require
   a fresh `Ready` result. Repeat for other affected registries; source writes
   do not automatically bake any registry.

Use **Restore Live Values** to restore all tracked temporary values while
keeping the drafts, then **Reapply Draft** beside a property to compare that
draft on the assigned component. Restoration changes a value only while it
still matches the tool's last write; later changes made by game code or an
animation are preserved. **Select Draft** returns to its Recipe target without
guessing a live instance. **Discard** removes one draft and restores its tracked
live values; **Discard All Drafts** clears the session's drafts and live previews.
Each Recipe property keeps one draft, updated by its latest tuning adjustment.

Writing a shared ColorToken changes every Style or Recipe that references it;
writing ImageStyle PPUM affects every binding using that Style. Tuning a single
instance does not limit that source change to the instance. Use a separate
project-owned token or Style when the visual intent should differ, then tune
the updated binding. The shared-use list counts supported tuning bindings in
Recipes under `Assets/`.

PPUM must be finite and at least `0.01`, and retains Unity's image units. It is
useful for adjusting sliced or tiled sprite borders, but it is not a Figma
corner-radius value and the tool does not
convert between them. The sprite, border, Image type, and layout stay as
authored. Animator, Selectable transitions, or project scripts can overwrite a
temporary color or PPUM; use **Reapply Draft** to compare again. Selectable
state colors need a live Color Tint transition and the relevant interaction
state to be visible. The tuning window does not change the transition or take
ownership of runtime behavior.

## Validation rules

### Target and ownership

- owners and consumers must be saved Prefab assets;
- every target must resolve to the expected component type;
- missing scripts fail validation;
- within one registry, one Recipe owns an owner Prefab;
- within one Recipe, one binding owns each managed property regardless of whether duplicate bindings propose the same value;
- separate registries are not cross-validated, so do not split ownership of one Prefab across registries.

### Variant specialization

A Prefab Variant with a deliberately different visual intent uses its own complete typed Recipe and explicitly assigns the source Recipe as `Base Recipe`. Both Recipes must be in the same registry. The Variant must own the same captured target and property through its typed binding; a matching literal or a raw serialized override is not sufficient. This replaces that base property's propagation at the Variant boundary while preserving the base Recipe for other consumers.

### Explicit consumer checks

Only Prefabs listed in `PrefabStyleRecipe.ConsumerPrefabs` are inspected. Each listed Prefab must contain the managed owner as a nested Prefab instance. Overrides of Recipe-owned properties between the consumer and owner are errors because they block predictable propagation. An explicit Variant specialization is the only exception: it must use a `Base Recipe` and own the exact target/property through a typed binding. When the owner is itself a Prefab Variant, its own authored overrides form the Recipe's baseline and are allowed. Overrides of unmanaged layout, content, events, and feature values remain allowed.

A composite owner may contain its own nested Prefab instances. Consumer
validation selects only the outermost instance matching that owner and does not
mistake the nested source roots for additional owner instances. Target identity
and typed property ownership remain unchanged.

Explicit registration avoids a full-project Prefab dependency scan during every Preview, Play transition, and Build. Add a consumer whenever it intentionally nests a managed owner and should inherit the baked style.

### Dirty Prefab Mode

If the current Prefab Stage is a tracked owner or consumer and has unsaved changes, validation stops. The package never saves, closes, or discards the developer's current Prefab Mode edits.

## Editor and build behavior

Creating a `StyleRecipeRegistry` opts it into package discovery. New registries enable both guard flags by default; either flag can be disabled during migration.

The Play guard caches a clean dependency fingerprint under `Library/SuperHeroUI` and avoids repeating the full check while all dependencies remain saved and unchanged. Registries containing custom Editor callbacks also bypass this Ready cache. The Build guard always performs a fresh full Preview. Neither guard applies styles implicitly: a non-Ready registry blocks the action and requires review in the Editor window.

The package adds no component to a Prefab and supplies no runtime Style system.
Recipe navigation, Preview/Apply, Play Mode tuning, and the configured guards
run only in the Editor. Play Mode tuning changes explicitly assigned live
components temporarily; Player builds use the baked component values.

### Inspection cost

See [performance measurements](performance.md) for the recorded comparison and reproduction conditions.

Preview caches owner inspection and consumer validation results separately as
plain data in Editor-session memory. It reuses unchanged results when the Prefab
is eligible for caching. Custom scripts with `ExecuteAlways`, `ExecuteInEditMode`,
`OnValidate`, or `ISerializationCallbackReceiver`, and custom behaviours with `runInEditMode` enabled, require fresh owner
and consumer inspection; built-in uGUI and TMP components remain eligible.
Other owners and consumers reload only when their inputs change. Loaded Prefab
objects are never kept between inspections. The first Preview, a Preview after domain reload, and
**Full Preview / Validate** inspect every Recipe. The Build guard always bypasses
the result caches and performs a full inspection.

Every Preview still checks the complete registered dependency set, current
ScriptableObject JSON, registry membership, and unsaved tracked Prefab Stage
state. It refreshes the relationship from specialization Recipes back to their
Base Recipes so a specialization change also invalidates the affected base
consumer checks. Results containing validation errors are retried on the next
Preview. Dependency-key and approval-fingerprint work still grows with the
registered inputs, even when no Prefab needs reloading.

When consumer validation is needed, Preview loads each shared consumer once and
checks its affected registered owner relationships against that snapshot.
Missing-script and owner-instance discovery share one hierarchy traversal. Owner
target lookup builds one component-ID index per loaded owner, and each dependency
fingerprint reads a shared asset's hash and ScriptableObject JSON once. Recipe
and consumer diagnostics retain their registration order, including duplicate
registrations.

Apply selects owners with reviewed changes plus registered Prefab dependents,
including owners that contain them as nested Prefabs or inherit them as Variants.
Such dependent owners are
re-evaluated even when initially `Ready`, because an upstream save can change
their inherited values. Apply invalidates affected caches before writing, saves
in Prefab dependency order, and immediately validates fresh consumers after each
owner. Its preflight and final Preview use incremental inspection. Approval
continues to require the current fingerprint of the whole registry; caching does
not permit applying a subset under an outdated approval.

## Composite recipe reference

The **Composite Control Recipes** sample maps input fields, dropdowns, tabs, tables, popups, and badges to the primitives above. It is a design reference rather than a set of prebuilt composite-control Prefabs, so projects keep their hierarchy and behavior contracts.

## Git and UPM distribution

The standalone source repository is [superherounite/com.superherounite.ui](https://github.com/superherounite/com.superherounite.ui), with `package.json` at its root. Preserve `.meta` files and publish immutable Semantic Version tags from that repository.

```json
"com.superherounite.ui": "https://github.com/superherounite/com.superherounite.ui.git#v0.1.0-preview.5"
```

For a local checkout kept beside the consuming project under the same parent folder, use a path relative to the consuming project's `Packages/manifest.json`:

```json
"com.superherounite.ui": "file:../../com.superherounite.ui"
```

A monorepo can expose the embedded subfolder temporarily:

```json
"com.superherounite.ui": "https://<git-host>/<organization>/<repository>.git?path=/Packages/com.superherounite.ui#<immutable-tag>"
```

The `?path=` query precedes `#revision`. Remove any embedded package with the same ID before testing a Git or `file:` dependency because the embedded package takes precedence. Commit the consuming project's manifest and lock file together. Git-installed tests require the Unity Test Framework and `"testables": ["com.superherounite.ui"]` in the consuming or CI manifest.

For each update, change `package.json` and this package's changelog together, commit without replacing existing `.meta` GUIDs, and create a new immutable version tag. Validate that tag from a small temporary Unity project or a repository-owned `TestProject~`. Consumers then update the dependency's `#tag` value and commit the regenerated lock file with the manifest.

Do not put credentials in dependency URLs. The public repository supports anonymous HTTPS installation. The package is distributed under the MIT License.

## Current verification boundary

The source package imports and both Editor assemblies compile in Unity `6000.0.68f1`. Automated tests cover Preview immutability, all five primitive categories including sprite ownership, Apply idempotence, stale approval rejection, duplicate property ownership, explicit direct and intermediate-Variant consumer overrides, Variant-added child target resolution, composite owners with nested Prefabs, missing targets, invalid image parameters, and preservation of unmanaged values.

Additional regression tests cover shared consumers with 2, 12, and 100 owners,
one target-index build across repeated lookups, and shared fingerprint reads.
Incremental tests compare results with full inspection and cover cache reuse,
unsaved and repeated token edits, Undo/Redo, shared dependencies, saved owner and
consumer changes, registry edits, replaced or deleted references, selective
Apply, and repeated Apply without writes. They assert operation counts and
unchanged asset contents rather than machine-specific time limits. A minimal
test project must import **TMP Essential Resources** before running the complete
suite because the text-style fixtures use TMP's default font.

Use [the validation script](../Tools~/Validate-Package.ps1) for the complete suite.
The deprecated `StyleRecipeProcessorBatchRunner.Run` entry point runs only the
original 12 smoke tests; its success does not validate the complete package.

Recipe-navigation tests cover asset and instance children, added overrides,
nested Prefabs, Variant sources and Prefab Mode, explicit consumers, sub-assets,
cache invalidation, and unchanged files. The separate-Inspector action test
requires a graphics device; use the script's `-EnableGraphics` option to include
it, as headless runs without graphics skip that test.

External release still requires an immutable tag, installation from that exact Git URL, and a consuming-project Player Build that confirms its project-specific asset and build configuration.
