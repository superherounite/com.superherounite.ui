# Preview and Apply performance

English | [한국어](performance.ko.md)

The latest real-project check reduced median Apply time for a single font-size
edit across a 65-Recipe registry from 15,758.36 ms to 5,139.55 ms (67.39%). The
release candidate passed 99 Editor tests, including Recipe navigation and
serialization-callback policy checks. The performance implementation also
passed the 1,000-owner mixed case.
The native-digest and dependency-planning section below records this check;
the earlier measurements are retained separately.

In an isolated benchmark on 2026-09-11, changing one token among 100 owners
reduced median Preview time from 4,514.41 ms to 219.01 ms and Apply time from
12,824.67 ms to 530.55 ms. The incremental implementation inspected one owner
and one shared consumer for that change. These measurements describe the
fixture below; they do not predict a consuming project's exact performance.

## Workload and comparison

The benchmark ran in a temporary Windows project with Unity `6000.0.68f1` and
Unity Test Framework. It contained:

- 100 owner Prefabs and 100 Recipes in one registry;
- four existing `Image` components and four Image bindings per owner;
- one distinct `ColorToken` and `ImageStyle` per owner, shared by that owner's
  four bindings: 100 tokens, 100 styles, and 400 bindings in total;
- one screen Prefab containing one nested instance of each of the 100 owners,
  registered as a consumer in every Recipe.

The baseline used `StyleRecipeProcessor.cs` and `PrefabTargetResolver.cs` from
commit `7ede1ed6229c7b03b4d3adfddc42a283d6ef63b0`. Copies in the temporary
project changed only the class names to `LegacyStyleRecipeProcessor` and
`LegacyPrefabTargetResolver`, including references to those names. They shared
the package's authoring and review types with the incremental implementation.
The comparison did not add the legacy copies to the package.

Both implementations ran against the same fixture, paths, target identities,
and asset GUIDs. The baseline ran first. Before running the incremental
implementation, the benchmark restored the original owner Prefab bytes and
token values, imported the restored Prefabs, and cleared the incremental
Preview cache. Fixture creation, restoration, imports for restoration, and
assertions were outside the timed intervals.

Initially, all 400 image colors differed from their styles. After the initial
Preview and Apply reached `Ready`, the benchmark measured three unchanged
Previews. It then changed only the second owner's token to magenta, cyan, and
yellow in turn, measuring Preview and reviewed Apply separately for each edit.
Each edit changed four bindings in one owner. Token edits remained in memory
during these measurements.

## Recorded results

Times are milliseconds. Speedup is baseline time divided by incremental time.
The initial operations are single samples; the other rows are medians of three
samples. Tests check correctness and work counts; operation timings are diagnostic.
Fixture execution deadlines are described in the [validation guide](../Tools~/README.md).
The measured incremental
implementation includes SHA-256 serialized-state digests. These tables record
that intermediate implementation; the later native-digest measurements are
reported separately below.

| Operation | Samples | Baseline | Incremental | Speedup |
| --- | --- | ---: | ---: | ---: |
| Initial Preview, all owners stale | 1 | 4,580.32 | 483.89 | 9.47× |
| Initial Apply, all owners stale | 1 | 26,314.61 | 9,462.29 | 2.78× |
| Unchanged Preview at `Ready` | Median of 3 | 4,570.62 | 160.19 | 28.53× |
| Preview after one token edit | Median of 3 | 4,514.41 | 219.01 | 20.61× |
| Apply after one token edit | Median of 3 | 12,824.67 | 530.55 | 24.17× |

For the incremental implementation, unchanged Preview loaded no owner or
consumer Prefabs. Each one-token Preview loaded one owner and one consumer.
The corresponding Apply visited one owner in its write phase, and its final
Preview loaded one owner and one consumer before reporting `Ready`.

Initial Apply still writes all 100 owners and validates fresh registered
consumers immediately after each owner. This preserves checks for changes
introduced by asset callbacks and Prefab dependency propagation. It explains
why the initial Apply improvement is smaller than the improvement for a single
token edit.

Unchanged Preview still examines all registered inputs to calculate dependency
keys and the approval fingerprint. Separate three-sample measurements gave
medians of 65.45 ms for `GetDependencyFingerprint` and 74.02 ms for constructing a
`StyleInspectionSnapshot` and requesting every owner and consumer key. These
standalone measurements characterize the remaining input checks; they are not
an additive breakdown of Preview. A warm Preview therefore remains proportional
to the registered inputs even when it loads no Prefabs.

The cache contains plain Editor-session data. Loaded Prefab contents are
released after inspection; the package adds no runtime cache or runtime
components.

Custom scripts with `ExecuteAlways`, `ExecuteInEditMode`, or `OnValidate`, and
custom behaviours with `runInEditMode` enabled, require fresh inspection instead of
reusing cached results. This fallback applies to both owners and consumers.
Built-in uGUI and TMP components remain eligible for caching; this benchmark
used only built-in uGUI components.
The Play guard also excludes registries containing these custom Editor callbacks
from its Ready cache. The repository's runtime-component fixture in
`TestProject~/Assets/CallbackProbe` changes static state read by
`ExecuteAlways.OnEnable`, changing full inspection without changing the
dependency fingerprint.
The corrected owner and consumer paths rerun the callbacks, match full
inspection, and do not write a Play Ready cache file. These components belong
only to the isolated test host.

## Correctness checks

An earlier repository runner `Tests` run passed 72 cases: all 70 package Editor
cases and the two runtime-component callback cases retained in the test host.
The 100-owner baseline comparison also passed its selected case with the
implementation including serialized-state digests. The mixed-workload checks
below are separate runs.

The benchmark compared `State`, `Assets`, `Changes`, and `Errors` between the
baseline and incremental implementations for the initial review and every
single-token review and Apply result. It checked that:

- Preview preserved asset bytes and modification times;
- each Apply finished at `Ready`;
- single-token Apply changed only the affected owner file, preserving every
  other fixture file, including the shared consumer;
- nested images inherited the expected colors while unmanaged raycast settings
  and layout remained unchanged;
- repeating Apply at `Ready` wrote no files;
- the incremental operations met the Prefab load counts described above.

Approval fingerprints were not compared across the two implementations because
their compiled module identities differ. Each Apply still used the review
created by the same implementation.

## Reproducing the measurement

From the repository root, use PowerShell 7 and an installed Unity
`6000.0.68f1` Editor with [the validation runner](../Tools~/Validate-Package.ps1):

```powershell
$unityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.0.68f1/Editor/Unity.exe'
pwsh -File Tools~/Validate-Package.ps1 -EditorPath $unityEditor -Mode Tests
pwsh -File Tools~/Validate-Package.ps1 -EditorPath $unityEditor -Mode Comparison -OwnerCount 100
pwsh -File Tools~/Validate-Package.ps1 -EditorPath $unityEditor -Mode Scale -OwnerCount 300
pwsh -File Tools~/Validate-Package.ps1 -EditorPath $unityEditor -Mode Scale -OwnerCount 1000
```

The runner creates an isolated project outside the package from the repository's
`TestProject~` template, references the local package, enables its test assembly,
and prepares TMP Essential Resources. `Tests` excludes the opt-in `Scale` and
`PerformanceComparison` categories. The callback regression fixtures are part
of this ordinary test run. The runtime test assemblies are test-host assets;
they do not add a production runtime assembly to this Editor-only package.

`Comparison` obtains the two baseline files from the commit above and generates
the renamed copies in the isolated host. That commit must be available in local
Git history. The repository retains the comparison harness in
`TestProject~/Comparison`; it restores identical assets between implementations
and runs
`StyleRecipePerformanceComparisonTests.CompareLegacyAndCurrentOnIdenticalAssets(100)`.
It also accepts 200 or 300 owners. The 100-owner table above records the passed
run in `20260911-034830-bf8252-Comparison-100.xml` and its
`legacy-vs-current-100-owners.json` measurements.

`Scale` selects exactly the requested 300- or 1,000-owner mixed-workload case.
It does not run the legacy implementation. The runner checks the process exit
code and NUnit XML, rejects failed or empty test selections, and writes logs,
NUnit XML, and a JSON summary beneath the host's `Results` directory. The
comparison also writes `legacy-vs-current-100-owners.json` for its 100-owner
case. By default, hosts are under
`%TEMP%/SuperHeroUIValidation/<source-id>/<mode>`; the runner prints their exact
paths. `-ProjectDirectory` selects another isolated host and `-PrepareOnly`
creates the host without executing validation.

## Mixed-workload scale validation

Separate `Scale` runs on 2026-09-11 passed one case with 300 owners and one with
1,000 owners using the implementation with serialized-state digests. Each owner
has an Image binding, its own token, style, and Recipe.
Ten percent also have a TMP text binding, ten percent are Variants, ten percent
have a runtime-only custom script, and one percent have an `ExecuteAlways`
script: three callback owners in the smaller case and ten in the larger case.
The fixture has four shared screen consumers and explicitly registered Variant
consumers. All owners start at `Ready`; setup does not require a full Apply.

These are single-run wall-clock measurements in milliseconds, timed around
processor calls only. Fixture creation, file checks, and the separately executed
full comparisons are excluded. This workload differs from the 100-owner
Image-only benchmark above, so the tables must not be compared as speedups or
as a scaling ratio. These timings describe the run; the tests check correctness
and work counts.

| Operation | 300 owners: ms | Owner / consumer loads | 1,000 owners: ms | Owner / consumer loads |
| --- | ---: | ---: | ---: | ---: |
| Cold Preview at `Ready` | 1,815.3 | 300 / 34 | 5,446.7 | 1,000 / 104 |
| Warm Preview at `Ready` | 550.1 | 3 / 17 | 1,738.2 | 10 / 52 |
| Preview after one token edit | 576.7 | 4 / 20 | 1,924.7 | 11 / 55 |
| Apply after one token edit | 2,038.6 | 5 / 34 | 6,485.4 | 12 / 104 |
| Repeated Apply at `Ready` | 551.1 | 3 / 17 | 2,037.8 | 10 / 52 |

For Apply rows, the load counts describe its returned final Preview, not the
sum of all work inside Apply. The changed token belongs to a Variant's base
owner. The child initially matches its own proposal through inheritance and
has no pending change. Apply nevertheless includes both owners in dependency
order, writes exactly their two Prefab files, and preserves the child's own
color. Repeated Apply writes zero files.

Warm Preview reopens only the three or ten callback owners. Runtime-only custom
scripts remain eligible for reuse. Consumers containing callback scripts also
require fresh inspection. Because consumer results are cached per Recipe, a
Recipe with an unsafe consumer also rechecks its other registered consumers;
the expected load count is the union of those paths. This explains why warm
consumer loads exceed the number of shared screens. The extra fresh inspection
is a correctness fallback, not a claim that all large workloads load zero
Prefabs after warmup.

Each case compares `State`, `Assets`, `Changes`, `Errors`, and `Fingerprint`
against `PreviewFull` after the initial Preview, the token edit, and Apply.
It verifies exact owner and consumer work counts, asset bytes and modification
times, unchanged authoring and consumer files, unmanaged Image/layout/TMP and
runtime-script values, and idempotent repeated Apply. The recorded XML reports
were `20260911-034446-7da747-Scale-300.xml` and
`20260911-034550-b58bc0-Scale-1000.xml`; both contain one passed test case.

## Large shared ScriptableObject payloads

A separate profile on 2026-09-11 found another bottleneck in a real consuming
project with CJK font assets. Its registry contained 65 Recipes. A second run
measured the serialized-state digest change against identical input content:
the two runs had the same initial content hash across 1,736 input files, and
each preserved all those files. Both returned `Ready` with equivalent warmup
and warm reviews. Warm Preview inspected 13 owners and 20 consumers in both
runs, retaining the custom-callback fallback.

Each time below is one measurement in milliseconds. The 4.44× warm Preview
speedup is relative to the incremental implementation immediately before the
digest change, not the original release. This workload differs from both
synthetic tables above. The standalone fingerprint and snapshot measurements
are not an additive breakdown of Preview.

| Operation | Before digest | After digest | Speedup |
| --- | ---: | ---: | ---: |
| `GetDependencyFingerprint` | 8,616.004 | 1,523.148 | 5.66× |
| Snapshot construction and all owner / consumer keys | 8,113.960 | 1,562.267 | 5.19× |
| Warm Preview at `Ready` | 17,320.414 | 3,899.786 | 4.44× |

The existing operation-local cache avoided repeated JSON serialization but
retained each full JSON string. Each Recipe reference appended that string
again, so shared large TMP font assets repeatedly enlarged the buffers passed
to the final hash. The snapshot cache held about 17.3 million JSON characters
across 483 objects. A separate sample serialized all 549 distinct
ScriptableObjects in about 327 ms. Reference counts imply about 113.7 million
JSON characters appended in each of the registry fingerprint and snapshot
owner-key calculations. These payload estimates exclude paths, dependency
hashes, and separators; the serialization sample is not an additive timing
breakdown. The repeated copying and hashing of shared payloads explained the
large cost left after Prefab inspection caching.

At this stage, `StyleDependencyFingerprint` serialized each ScriptableObject's
live state and computed its SHA-256 digest once per helper instance. Repeated references
append the compact digest. `StyleInspectionSnapshot` uses that same helper for
Recipe state as well as authoring dependencies. Asset paths and recursive
dependency hashes remain part of the relevant keys; the owner key still omits
the Recipe's recursive dependency hash because that hash also follows
registered consumers.

The snapshot's cached string payload changed from 17,307,353 characters of raw
JSON for 483 objects to 30,688 digest characters for 548 objects. The new cache
also includes the 65 Recipes. These counts describe the stored string content,
not process memory, allocation totals, or bytes.

Digest caches last only for the current operation. The final approval
fingerprint uses a separate fresh helper, and subsequent operations serialize
the current in-memory state again. This preserves detection of unsaved edits,
including further changes to an already-dirty object. Serialization remains
cached by object, so separate subassets sharing a path retain their separate
state; pathless objects and missing-object markers are also retained. The
custom-callback inspection fallback and Play Ready cache restrictions remain
in place.

A subsequent read-only check covered all three registries in the same consuming
project, containing 92 Recipes in total. Cold, warm, and full inspections were
equivalent, all reported `Ready` without review errors, and all 1,972 checked
input files retained identical content.

## Native serialized-state digests and remaining Apply cost

The current implementation uses Unity's native `Hash128.Compute` for each
ScriptableObject's serialized JSON digest. This avoids the managed UTF-8 byte
copy and managed SHA-256 pass over large font payloads. Each operation still
serializes the current live state, and repeated references reuse only that
operation's digest. After Prefab inspection, the approval fingerprint uses a
separate fresh helper. The outer dependency and review hashes remain SHA-256.
These fingerprints detect changed inputs and review results; they are not
cryptographic signatures.

Full inspection does not construct an inspection snapshot. Cached Preview also
skips owner or consumer keys when that inspection path is already ineligible
for reuse. Dependency keys needed by another Recipe's direct specializations
remain available. The complete final fingerprint, dirty Prefab Stage checks,
custom-callback reinspection, and reviewed Apply checks remain in place.

The focused fingerprint suite passed all 11 cases after this change. Four new
cases cover a persistent TMP font's directly edited fallback list and three
large-JSON tail variants: Korean text with emoji, escaped Unicode, and ASCII.
The fallback-list case adds and removes a font reference without changing the
saved asset dependency hash or dirty count. A new fingerprint operation detects
the edit and returns to its original result after restoration. The tail cases
distinguish changes beyond a 1 MiB shared prefix and verify restoration too.

A separate before/after Apply profile used the same consuming project's
65-Recipe registry and changed the `Button 01 Label` TextStyle font size from
11 to 12. The baseline used SHA-256 serialized-state digests. The final version
included native JSON digests, unused-key skipping, and the dependency-planning
changes described below. Public `Apply` timings in milliseconds were:

| Sample | SHA-256 baseline | Final implementation |
| --- | ---: | ---: |
| 1 | 16,009.82 | 5,096.35 |
| 2 | 15,758.36 | 5,139.55 |
| 3 | 15,611.06 | 5,263.21 |
| Median | 15,758.36 | 5,139.55 |

The median fell by 67.39%, a 3.066× speedup. The measured 5.14 seconds still
represents a noticeable wait. Fresh serialization across the complete registry
and required custom-callback inspections remain part of each operation.

Separate instrumented Apply mirrors took 16,274.25 ms before and 5,819.91 ms
after, including small instrumentation overhead outside the listed phases.
Their phase times and the subsequent Ready cache updates are shown below.
These individual mirror runs do not break down the separately measured
public-call medians.

| Phase | SHA-256 baseline: ms | Final implementation: ms |
| --- | ---: | ---: |
| Approval revalidation Preview | 4,183.68 | 2,322.99 |
| Affected-owner dependency planning | 6,356.85 | 1,108.42 |
| Preview cache invalidation | 641.70 | 332.79 |
| Owner write and consumer inspection | 226.13 | 385.07 |
| Final Preview | 4,864.09 | 1,668.73 |
| Guard Ready cache update after Apply | 0.45 | 0.30 |

Both profiles passed all three public samples and the separate mirror sample.
Each Apply reached `Ready`, and its review matched that implementation's full
inspection, including `Fingerprint`. Apply visited two affected owners and
changed only `Period Button.prefab`: the owned `fontSize` and `fontSizeBase`
values plus Unity's CRLF-to-LF normalization. Repeated Apply wrote no files.
After each profile, all 1,736 checked input files were restored byte for byte.
The original consuming-project checkout remained unchanged.

Further dependency-query diagnostics on the same 65 owner paths produced the
following standalone measurements. These are query experiments, not an Apply
breakdown or timings of the final combined implementation.

| Dependency query | ms | Queries |
| --- | ---: | ---: |
| Native recursive lookup for each owner | 6,474.96 | 65 |
| Direct lookup for each owner | 97.51 | 65 |
| Shared direct traversal through every asset type | 486.55 | 327 distinct paths |

The direct traversal did not reproduce all native recursive results: 50 owner
closures omitted dependencies involving five distinct Editor icon assets. No
Prefab dependency was missing in this fixture, but this does not establish
general equivalence between direct traversal and Unity's recursive lookup.

The current `StyleAssetDependencySnapshot` therefore uses shared direct
dependency reads and their transitive closures only to group candidate paths.
A candidate whose direct closure reaches a changed owner is confirmed with a
native recursive lookup. The remaining candidates receive one batched native
recursive check; if that batch reveals a target absent from the direct hints,
each candidate is checked individually. The native recursive result remains
authoritative. `GetDependencies(path)`, used to order affected owners before
writing, also retains the native recursive result and caches it per path.
These caches exist only during planning before the first Prefab save.

Cache invalidation first clears affected owners and then queries only Recipes
that still have a cached consumer result. Entries with an empty consumer cache
already require inspection on the next Preview, so omitting their invalidation
queries does not skip their validation.

All five new dependency-snapshot tests passed, alongside the 11 fingerprint
cases above. They cover hidden recursive dependencies, false-positive direct
hints, shared paths across asset types, cycles and self-dependencies, and fresh
reads in subsequent snapshots. They also check that shared native and direct
queries are reused within one snapshot.

The final ordinary `Tests` run passed all 81 cases: 79 package Editor cases and
the two test-host runtime callback cases. It completed in 64.906 seconds and
recorded `20260911-092325-8a43ae-Tests-100.xml`. This validates the performance
implementation at that stage; later release checks are recorded below.

The final implementation also passed the 1,000-owner mixed-workload case in the
isolated test host in 126.438 seconds, recorded in
`native-dependency-scale-1000.xml`. The fixture follows the mixed-workload
structure described above. These are single-call measurements of the current
implementation, with no speedup inferred from the earlier separate run.

| Operation | ms | Owner / consumer loads |
| --- | ---: | ---: |
| Cold Preview at `Ready` | 4,084.5 | 1,000 / 104 |
| Warm Preview at `Ready` | 1,478.9 | 10 / 52 |
| Preview after one token edit | 1,390.9 | 11 / 55 |
| Apply after one token edit | 4,467.6 | 12 / 104 |
| Repeated Apply at `Ready` | 1,229.3 | 10 / 52 |

For Apply, these load counts describe the returned final Preview. The write
phase visited two affected owners; repeated Apply visited zero. The case passed
its full-inspection comparisons and checks for expected asset-byte changes,
owned and unmanaged values, and idempotent repeated Apply.

## Recipe navigation and release checks

The release candidate passed all 99 ordinary Editor tests in 74.093 seconds on
2026-09-11 (`release-full-graphics.xml`), with zero failures or skipped tests.
This run includes 13 Recipe-lookup cases, the separate-Inspector action test,
and four additional serialization-callback type-policy cases. Custom
`ISerializationCallbackReceiver` implementations, including explicit and
inherited implementations, bypass inspection and Play Ready caches; built-in
uGUI and TMP remain eligible for reuse.

In the isolated consuming-project snapshot, the actual `Zone Danger Button`
Prefab, its existing child and component, and preview-scene instances all
resolved the same direct owner Recipe and two inherited source Recipes.
Across five contexts, 20 warm queries per context had medians of 0.511–0.597 ms.
The first catalog lookup took 1,166.804 ms; these warm timings are not cold-start
or UI-click latency claims. All 1,972 checked input files retained identical
bytes, including an audit after the validation Editor exited.

## Consuming-project Player Build

An earlier Windows IL2CPP Build of the consuming-project snapshot ran only in a
separate worktree, leaving the original source checkout clean. The Build guard checked
all three registries. Unity's `BuildReport` reported `Succeeded` in 628.983
seconds and produced an executable, with zero errors and nine warnings from
existing consuming-project code and pipeline settings (obsolete APIs and
unused members among them).
This Build predates the native digest and dependency-planning changes and was
not rerun for those changes.

The strict file-byte check detected a CRLF-to-LF change in one TMP fallback
asset, so the raw validation report retains `Passed=false` despite the
successful Build. A Git audit also found two URP settings assets regenerated
by Unity. The Build worktree therefore changed; the 1,972 unchanged input files
reported above describe the preceding read-only Preview check.

GUI appearance, interactions, and Korean-language runtime behavior were not
tested.

## Measurement limits

The baseline comparison was a synthetic Image-only workload in one isolated
Editor process, with the baseline measured first rather than alternating
implementation order. The scale checks used a different synthetic mixture.
Hardware, filesystem and Unity caches, background activity, larger hierarchies,
Variant relationships, TMP assets, and project-specific callbacks can change
the timings. Measure the consuming project's own registries before setting
performance expectations.
