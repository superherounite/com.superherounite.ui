# Super Hero UI authoring guide

Super Hero UI `0.1.0-preview.1` is an Editor-only authoring package for Unity 6000.0. Production code belongs to `SuperHeroUnite.UI.Editor`; package tests belong to `SuperHeroUnite.UI.Editor.Tests`. The package has no runtime assembly.

## Package boundary

The package owns generic authoring mechanics:

- ScriptableObject color and typed style definitions;
- stable Prefab component capture and resolution;
- read-only change previews;
- reviewed, deterministic Prefab baking;
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

## Preview and Apply

Open **Tools > Super Hero UI > Style Recipes** and select a registry.

- `Preview / Validate` loads clean Prefab contents, resolves every typed target, and reports the affected owner, target, property, and proposed value without saving.
- `Apply Reviewed Changes` accepts only the exact preview fingerprint. A change to a Recipe, style, token, owner, consumer, or package implementation makes the preview invalid.
- Apply writes only declared properties and saves only owners with differences.
- A fresh preview reports `Ready` when no differences or validation errors remain.
- Reapplying a `Ready` review produces no write.

`Stale` means at least one Prefab value differs from its Recipe. A stale approval is a separate condition: Apply rejects it and requires another Preview. `Error` means the Recipe cannot be safely evaluated or applied.

## Validation rules

### Target and ownership

- owners and consumers must be saved Prefab assets;
- every target must resolve to the expected component type;
- missing scripts fail validation;
- within one registry, one Recipe owns an owner Prefab;
- within one Recipe, one binding owns each managed property regardless of whether duplicate bindings propose the same value;
- separate registries are not cross-validated, so do not split ownership of one Prefab across registries.

### Explicit consumer checks

Only Prefabs listed in `PrefabStyleRecipe.ConsumerPrefabs` are inspected. Each listed Prefab must contain the managed owner as a nested Prefab instance. Overrides of Recipe-owned properties between the consumer and owner are errors because they block predictable propagation. When the owner is itself a Prefab Variant, its own authored overrides form the Recipe's baseline and are allowed. Overrides of unmanaged layout, content, events, and feature values remain allowed.

Explicit registration avoids a full-project Prefab dependency scan during every Preview, Play transition, and Build. Add a consumer whenever it intentionally nests a managed owner and should inherit the baked style.

### Dirty Prefab Mode

If the current Prefab Stage is a tracked owner or consumer and has unsaved changes, validation stops. The package never saves, closes, or discards the developer's current Prefab Mode edits.

## Editor and build behavior

Creating a `StyleRecipeRegistry` opts it into package discovery. New registries enable both guard flags by default; either flag can be disabled during migration.

The Play guard caches a clean dependency fingerprint under `Library/SuperHeroUI` and avoids repeating the full check while all dependencies remain saved and unchanged. The Build guard always performs a fresh Preview. Neither guard applies styles implicitly: a non-Ready registry blocks the action and requires review in the Editor window.

The package adds no component to a Prefab. It performs no runtime lookup, runtime component creation, or runtime style traversal. This keeps Editor cost at explicit Preview/Apply operations and configured guard boundaries.

## Composite recipe reference

The **Composite Control Recipes** sample maps input fields, dropdowns, tabs, tables, popups, and badges to the primitives above. It is a design reference rather than a set of prebuilt composite-control Prefabs, so projects keep their hierarchy and behavior contracts.

## Git and UPM distribution

For normal distribution, extract the contents of `Packages/com.superherounite.ui` to a repository whose root contains `package.json`. Preserve `.meta` files and publish immutable Semantic Version tags.

```json
"com.superherounite.ui": "https://github.com/<organization>/super-hero-ui.git#v0.1.0-preview.1"
```

For a local checkout kept beside the consuming project under the same parent folder, use a path relative to the consuming project's `Packages/manifest.json`:

```json
"com.superherounite.ui": "file:../../super-hero-ui"
```

A monorepo can expose the embedded subfolder temporarily:

```json
"com.superherounite.ui": "https://<git-host>/<organization>/<repository>.git?path=/Packages/com.superherounite.ui#<immutable-tag>"
```

The `?path=` query precedes `#revision`. Remove any embedded package with the same ID before testing a Git or `file:` dependency because the embedded package takes precedence. Commit the consuming project's manifest and lock file together. Git-installed tests require the Unity Test Framework and `"testables": ["com.superherounite.ui"]` in the consuming or CI manifest.

For each update, change `package.json` and this package's changelog together, commit without replacing existing `.meta` GUIDs, and create a new immutable version tag. Validate that tag from a small temporary Unity project or a repository-owned `TestProject~`. Consumers then update the dependency's `#tag` value and commit the regenerated lock file with the manifest.

Do not put credentials in dependency URLs. Use the host's Git credential manager or SSH agent for private repositories. Add the company-approved license before publishing the standalone repository or release tag.

## Current verification boundary

The source package imports and both Editor assemblies compile in Unity `6000.0.68f1`. Automated tests cover Preview immutability, all five primitive categories including sprite ownership, Apply idempotence, stale approval rejection, duplicate property ownership, explicit direct and intermediate-Variant consumer overrides, missing targets, invalid image parameters, and preservation of unmanaged values.

External release still requires a company-approved license, standalone repository and tag, installation from that exact Git URL, and a consuming-project Player Build that confirms its project-specific asset and build configuration.
