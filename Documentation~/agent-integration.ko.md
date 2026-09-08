# Super Hero UI AI Agent 연동 가이드

[English](agent-integration.md) | 한국어

이 가이드는 이전 대화 맥락 없이 `com.superherounite.ui` 작업을 시작할 때
사용한다. Agent가 설치된 revision을 찾고, 작업에 필요한 문서만 읽고, package
계약을 적용하고, 검증 결과를 보고하는 방법을 설명한다. 소비 저장소의 지침과
개발자의 명시적 요구 사항이 우선한다.

## 소비 프로젝트에 한 번 등록할 Bootstrap

Codex는 task 시작 시 project root부터 최초 working directory까지의
`AGENTS.md`를 읽는다. 따라서 package 자체의 `AGENTS.md`는 task가 package root
또는 그 하위에서 시작할 때만 자동으로 읽힌다. Host root에서 시작한 task가
나중에 package file을 열어도 instruction chain에 추가되지 않는다. 다른 AI
도구는 다른 검색 규칙을 사용할 수 있다.

소비 저장소에서 실제로 사용하는 root Agent 지침 파일에 다음 내용을 추가한다.

```md
## Super Hero UI

`com.superherounite.ui`와 관련된 모든 작업 및 Unity uGUI 구현·리팩터링에서는
먼저 embedded `Packages/com.superherounite.ui/package.json`을 확인한다. 그다음
`Packages/manifest.json`과 `Packages/packages-lock.json`에서 local, Git,
registry 또는 transitive 설치 여부를 확인한다. Package가 설치되어 있으면
다음을 따른다.

- 설치된 package revision을 찾고 UI를 수정하기 전에
  `Documentation~/agent-integration.ko.md`를 읽는다. Embedded package는
  일반적으로 `Packages/com.superherounite.ui/`에 있다. Local `file:` 경로는
  `Packages/manifest.json`을 기준으로 해석한다. Git 또는 registry package는
  `Packages/packages-lock.json`의 version과 source/hash를 정확히 일치하는
  `Library/PackageCache/com.superherounite.ui@*/` folder와 대조한다. 임의의
  cache folder나 단순히 가장 최신인 folder를 선택하지 않는다.
- `Library/PackageCache`를 수정하지 않는다. Package code 변경이 필요하면
  package를 embed하거나 원본 저장소에서 작업한다.
- 변경 가능한 token, Style, Recipe, registry는 프로젝트 소유
  `Assets/Editor/...`에 두고 runtime Prefab은 일반 `Assets/...` hierarchy에 둔다.
- Package Binding MonoBehaviour나 runtime Style traversal을 추가하지 않는다.
  `Preview / Validate` -> 검토 -> `Apply Reviewed Changes` 순서로 적용하고 새
  Preview가 `Ready`인지 확인한다.
- 프로젝트 소유 hierarchy, layout, content, state, persistent UnityEvent,
  unmanaged Prefab override를 보존하고 nested consumer를 명시적으로 등록한다.
```

이 bootstrap을 소비 프로젝트와 함께 commit한다. 저장소가 다른 활성 지침
파일명이나 override file을 사용한다면 Agent가 실제로 읽는 파일에 넣는다. 시작
지침을 바꾼 뒤에는 새 Agent task를 시작한다.

Clean checkout에서는 아직 cache가 restore되지 않았을 수 있고, local dependency가
Agent가 접근할 수 있는 workspace 밖에 있을 수 있다. 그래서 bootstrap 자체에
최소 안전 계약을 포함한다. 정확한 설치 guide를 읽을 수 없으면 version별 API를
추측하거나 다른 cache revision을 선택하지 않는다. 위의 최소 계약을 유지하고
package restore 또는 접근 문제를 해결한 뒤 package asset 제작을 진행한다.

## Package Context 찾기 및 분기

1. 먼저 `Packages/com.superherounite.ui/package.json`을 확인한다. 그다음
   `Packages/manifest.json`과 `Packages/packages-lock.json`에서 활성 local,
   Git, registry 또는 transitive `com.superherounite.ui` dependency를 확인한다.
2. Bootstrap에 설명된 방식으로 embedded, local, Git 또는 registry root를
   정확히 찾는다. Resolve된 PackageCache는 읽기 전용으로 취급한다.
3. 설치된 `package.json`을 읽는다. 이 파일이 version, Unity compatibility,
   dependency의 기준이다.
4. 소비 프로젝트 UI 제작에서는 [제작 가이드](index.ko.md)를 읽는다. 이 문서가
   Binding ownership, target capture, Preview/Apply, consumer validation, guard의
   기준 계약이다.
5. Input Field, Dropdown, Tab, Table, Popup, Badge 또는 dynamic list 작업이면
   [Composite Control Recipes](../Samples~/Composite%20Control%20Recipes/README.ko.md)도
   읽는다.
6. Package source를 바꿀 때만 package root의 [AGENTS.md](../AGENTS.md)를 읽는다.
   일반 UI 제작에서는 설치 문서가 부족하거나 동작을 진단해야 할 때만 package
   test나 implementation file을 읽는다.

이 분기 방식은 일반 작업의 context를 줄인다. Agent 연동 guide는 작업 흐름,
제작 guide는 product contract, 선택적 sample은 복합 control mapping을 제공한다.

## 소비 프로젝트 조사

UI를 제안하거나 수정하기 전에 다음을 조사한다.

1. 가장 가까운 저장소 지침과 현재 worktree 변경
2. 요청된 design source와 필요한 모든 시각 및 상호작용 상태
3. target Prefab, Scene 배치, 동작 code, serialized event 계약
4. 호환되는 project Common Prefab과 인접 control
5. 기존 project-owned `ColorToken`, typed Style, `PrefabStyleRecipe`,
   `StyleRecipeRegistry` asset

다음 작업 계약을 기록한다.

- 각 styled component의 owner Prefab
- 해당 owner를 의도적으로 중첩하는 외부 Prefab
- Recipe가 관리할 serialized component property
- 로컬에서 관리할 hierarchy, layout, content, event, state
- 초기, 편집, 오류, 적용, 취소, 재진입, data refresh 동작
- 완료에 필요한 시각, 상호작용, serialization, build 검사

Style primitive만 보고 누락된 제품 UX를 추론하지 않는다. 동작에 유의미한
차이가 생기는 항목은 개발자와 확정한다.

## Ownership Model 적용

Super Hero UI는 Editor 전용 제작 및 Prefab bake package다. Runtime assembly,
runtime Style service, theme traversal, package Binding component가 없다. Input
Field, Dropdown, Tab, Table, Popup, Badge는 project-owned composite로 유지한다.

프로젝트는 원본 token, Style, Sprite, TMP font, Recipe, registry, Prefab asset을
소유한다. 구성된 Recipe는 typed Binding이 선언한 경우 Sprite 또는 font reference를
포함하여 target component의 문서화된 serialized value만 관리한다. Layout,
content, localization, interaction, accessibility, data, semantic state,
persistent UnityEvent는 로컬에서 관리한다.

변경 가능한 authoring asset은 `Assets/Editor/SuperHeroUI/` 같은 project Editor
folder에 둔다. Styled Prefab은 일반 runtime asset path에 둔다. Authoring asset을
Resources, Addressables, AssetBundles에 넣지 않는다.

제작 가이드에 정의된 가장 좁은 Binding을 선택한다. 특히 다음을 지킨다.

- Image tint는 optional Image field 사용 여부와 관계없이 필수다.
- Surface에는 fill이 필요하고 outline target과 outline Style은 한 쌍으로 지정한다.
- Text는 문서화된 typography field와 선택된 TMP font의 shared material도 소유한다.
- Selectable에는 project-owned `targetGraphic`과 다섯 state token이 필요하다.
  Recipe는 transition, color, multiplier, fade를 소유하며 `targetGraphic`
  reference 자체는 소유하지 않는다.
- 서로 다른 registry는 ownership을 교차 검사하지 않으므로 한 owner Prefab의
  managed property를 여러 registry로 나누지 않는다.

## 작성 절차 실행

1. 완성된 project Prefab과 필요한 component를 먼저 작성한다. 호환되는 shared
   Prefab은 nested 상태로 유지하고 고정 관계는 serialized reference와 persistent
   UnityEvent로 연결한다.
2. 의미가 일치하는 token과 Style을 재사용한다. 의미 또는 전체 owned value
   집합이 다를 때만 명확한 이름의 project asset을 만든다.
3. 저장된 owner Prefab마다 Recipe 하나를 만들고 typed Binding을 하나 이상
   추가한다. 정확한 owner를 Prefab Mode로 열어 저장한 뒤 target을 Capture한다.
4. Recipe는 nested Prefab이 소유한 component를 Capture할 수 없다. 해당 Binding은
   nested source Prefab의 Recipe에 둔다.
5. Recipe owner 자체가 Prefab Variant일 수 있다. 해당 owner boundary의 managed
   override는 유효한 baseline이다. 명시적으로 등록한 각 outer consumer에서는
   owner 위의 direct 및 intermediate Variant link에 있는 managed override를
   허용하지 않는다.
6. 검사할 외부 Prefab을 `Consumer Prefabs`에 추가한다. 프로젝트 전체 consumer를
   자동 검색하지 않는다.
7. Recipe를 `Assets/` 아래 registry에 추가한다. 새 registry의 Play 및 Build
   validation toggle은 기본으로 켜지지만 실제 Inspector 값을 확인한다.
8. **Tools > Super Hero UI > Style Recipes**에서 **Preview / Validate**를 실행한다.
   Recipe Inspector에서 owner와 `Consumer Prefabs` 지정을 확인한다. Window에는
   모든 asset path 대신 change/error 상세와 전체 asset 수가 표시된다.
9. `Stale`은 일반적으로 적용 가능한 유효한 변경이 있다는 뜻이다. 검토 후
   **Apply Reviewed Changes**를 실행한다. Preview 후 dependency가 바뀌면 승인
   fingerprint가 무효가 되므로 새 Preview가 필요하다.
10. Preview를 다시 실행해 `Ready`인지 확인한다. 같은 값을 다시 Apply해도 쓰기가
    발생하지 않아야 한다.

## 안전하고 결정적인 제작 유지

- Runtime Style manager, package Binding MonoBehaviour, reflection scan, hierarchy
  traversal을 추가하지 않는다. Bake된 Unity component 값이 runtime 결과다.
- 작성된 고정 UI에서는 runtime `AddComponent`, `GetComponent`,
  `transform.Find`, 고정 `AddListener` 연결을 피한다.
- 실제 dynamic collection에서는 Inspector로 지정한 완성형 item Prefab만 생성하고
  runtime data와 state를 bind한다.
- Recipe와 Style의 public 값은 getter-only다. Inspector와 package의 `Capture`,
  `Select`, `Clear` control을 우선 사용한다.
- `GlobalObjectId` 문자열을 임의로 만들거나 Prefab 또는 ScriptableObject YAML을
  손으로 수정하지 않는다. 승인된 Editor 자동화가 필요하면 Editor assembly에
  두고 설치된 version에 맞는 `SerializedObject`를 사용한 뒤 Unity에서 저장된
  asset을 검증한다.
- Runtime code에서 `SuperHeroUnite.UI.Editor` API를 호출하지 않는다. 공개
  `StyleRecipeProcessor.Preview`와 `Apply`는 Editor tooling용이다.
- GUID, nested Prefab link, persistent event, Binding이 선언한 ownership 밖의
  모든 property를 보존한다.

## 검증 및 완료 보고

Recipe Inspector 지정을 확인한 뒤 Preview가 asset을 쓰지 않고, Apply가 검토한
managed property만 변경하며, 새 Preview가 `Ready`인지 확인한다. Missing Script와
reference, Capture한 concrete component type, Variant source chain, consumer
override, 고정 reference, persistent event target과 count를 검사한다.

소비 feature에 지정된 state transition과 data timing을 실행한다. 해당하는 target
resolution, localized text, 펼친 menu, scroll, clipping, hit area를 확인한다. 실제
Play 및 Build guard 설정을 확인하고 release 전에 소비 프로젝트 Player Build를
실행한다.

Package test는 bake 계약을 검증한다. 소비 UI의 시각 일치, 동작, localization,
Player build를 보장하지 않는다. 완료 보고에서는 자동 검사 결과와 개발자의
Unity Editor에서 남은 검사를 구분한다.

## 연동 지침 최신 상태 유지

운영 소비 프로젝트는 변경되지 않는 package version 또는 Git tag에 고정한다.
Dependency를 갱신하면 설치된 guide를 다시 읽고 version별 host 요약을 갱신하며,
영향받는 registry를 Preview한다. `Packages/manifest.json`과
`Packages/packages-lock.json`을 함께 commit한다.

Host root에는 짧은 bootstrap만 둔다. 전체 guide를 각 소비 저장소에 복사하지
말고 설치된 package revision을 상세 계약의 기준으로 사용한다.
