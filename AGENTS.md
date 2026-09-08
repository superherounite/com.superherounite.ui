# Super Hero UI package instructions

Read [Documentation~/agent-integration.md](Documentation~/agent-integration.md)
before using this package to author UI in a consuming project. A Korean version
is available at
[Documentation~/agent-integration.ko.md](Documentation~/agent-integration.ko.md).

For Codex, these instructions load automatically only when a task starts with
its initial working directory at this package root or below it. Opening a package
file later from a host-root task does not add this file to that task's startup
instruction chain. Other AI tools may use different discovery rules. A consuming
project's active root Agent instructions must therefore include the bootstrap
from the integration guide.

## Package contract

- `com.superherounite.ui` is an Editor-only uGUI style-authoring and Prefab-bake
  package. It has no runtime assembly or runtime style system.
- The package owns typed Style definitions, Prefab target capture, read-only
  Preview, explicit reviewed Apply, property ownership checks, registered
  consumer checks, and optional Play and Build guards.
- The consuming project owns tokens, Styles, Recipes, registries, Prefabs,
  hierarchy, layout, content, localization, behavior, and UnityEvents.
- Input fields, dropdowns, tabs, tables, popups, and badges are project-owned
  composites. The package supplies composition guidance, not product Prefabs or
  runtime behavior.
- Baking writes supported values to existing Unity UI and TextMesh Pro
  components. It must not add package components or authoring-asset references
  to runtime Prefabs.

## Source changes

- Treat the installed `package.json` and current code as authoritative. Do not
  assume APIs from another version.
- Keep production code in the Editor assembly and namespace. Runtime assemblies
  must not reference `SuperHeroUnite.UI.Editor`.
- Preserve public API behavior, ScriptableObject field serialization, `.meta`
  GUIDs, target identity, and deterministic/idempotent Apply behavior.
- Do not expose arbitrary serialized-property paths or runtime bindings as an
  extension mechanism. Add a typed primitive only for a stable cross-project
  visual concern.
- Never edit a resolved copy under `Library/PackageCache`; embed the package or
  edit its source repository.
- Keep English and Korean versions of user-facing package documentation in sync.
- Update `package.json` and `CHANGELOG.md` together for a release. Do not reuse or
  move an immutable release tag.

## Validation

- Run the narrowest Editor tests for the changed contract, then the package's
  complete Editor test suite when shared processing or validation changes.
- Verify Preview does not write, reviewed Apply changes only owned properties, a
  fresh Preview reaches `Ready`, and repeated Apply is idempotent.
- Preserve unmanaged Prefab values and explicitly registered nested consumers.
- Package tests do not replace the consuming project's visual, interaction,
  localization, target-resolution, or Player Build checks.
