# Super Hero UI 제작 가이드

[English](index.md) | 한국어

Super Hero UI `0.1.0-preview.4`은 Unity 6000.0용 Editor 전용 제작 패키지다. Production code는 `SuperHeroUnite.UI.Editor`, package test는 `SuperHeroUnite.UI.Editor.Tests`에 속한다. 이 패키지에는 runtime assembly가 없다.

## 패키지 경계

패키지는 범용 제작 방식을 책임진다.

- ScriptableObject color 및 typed style 정의
- 안정적인 Prefab component capture와 resolve
- 읽기 전용 변경 Preview
- 검토를 거쳐 같은 입력에 같은 결과를 내는 Prefab bake
- 속성 소유권과 명시적으로 등록한 consumer 검증
- `StyleRecipeRegistry` 생성으로 활성화되는 자동 검색과 설정 가능한 Play Mode 및 Player Build guard

소비 프로젝트는 변경 가능한 값과 제품 동작을 책임진다.

- token 및 style 자산
- Recipe 및 registry
- 공유 Prefab과 기능별 Prefab
- 디자인별 계층 구조와 layout
- sprite, font, 콘텐츠, localization, 입력 검증, 상태 logic, persistent UnityEvent

권장 프로젝트 구조는 다음과 같다.

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

제작 자산을 runtime Resources, Addressables, AssetBundles에 넣지 않는다. 대상 Prefab은 일반 runtime 계층에 유지하며 bake된 component 값만 받는다.

## AI Agent 연동

Package root에는 package source용 짧은 [AGENTS.md](../AGENTS.md)가 있다. 상세
[AI Agent 연동 가이드](agent-integration.ko.md)는 바로 복사할 수 있는 소비
프로젝트 bootstrap, package resolve 규칙, context 조사 순서, 제작 절차, 완료
검증을 제공한다. 해당 bootstrap을 소비 저장소에서 실제로 사용하는 root 지침에
추가하면 `Assets/` 아래에서 작업하는 Agent도 이전 대화 맥락 없이 package 계약을
찾을 수 있다.

## 복합 컨트롤을 프로젝트가 소유하는 이유

이름이 같은 컨트롤이라도 프로젝트마다 안정적으로 공유할 수 있는 계약은 드물다. 입력 필드는 validation과 오류 UX가 다르고, dropdown은 data와 virtualization이 다르다. Tab은 선택 상태 소유권, table은 schema와 sorting, popup은 navigation과 focus, badge는 의미가 서로 다르다. 이를 범용 Prefab으로 배포하면 제품 가정이 낮은 계층의 package에 들어가고 이후 migration이 어려워진다.

Super Hero UI는 이런 컨트롤 사이에서도 안정적으로 공유되는 요소를 제공한다. color, image 표현, fill과 outline으로 이루어진 surface, typography, Selectable color 상태, target identity, Preview, bake, validation이 여기에 해당한다. 각 프로젝트는 자체 Prefab 안에서 이 primitive들을 조합한다.

## Typed primitive

### Color Token

`ColorToken`은 color 하나를 담는 이름 있는 자산이다. Recipe가 이 자산을 직접 참조하므로 token을 추가하거나 이름을 바꿀 때 enum member를 추가하거나 script를 다시 compile하지 않는다.

### Graphic Color

Graphic Color binding은 기본 직렬화 `Graphic.color`만 소유한다. 전체 `ImageStyle`이 지나치게 많은 값을 소유할 때 사용한다. TMP text처럼 color를 별도로 구현한 component에는 해당 typed binding을 사용해야 한다.

### Image

Image binding은 항상 tint를 소유한다. Sprite, `Image.Type`, `preserveAspect`, `fillCenter`, `pixelsPerUnitMultiplier`는 각각 소유 여부를 선택할 수 있다. Material, raycast 동작, fill method와 amount, mask, layout은 변경하지 않는다.

### Surface

Surface는 필수 fill `ImageStyle`과 선택적 outline `ImageStyle`을 조합한다. Prefab에는 대응하는 한두 개의 `Image` component가 미리 있어야 한다. 패키지는 해당 target의 style만 지정하며 계층이나 component를 만들지 않는다.

### TextMesh Pro text

Text binding은 TMP font와 해당 shared material, color, font size와 직렬화된 size base, font style, auto-size 상태와 범위, character, word, line, paragraph spacing을 소유한다. Text content, localization hook, alignment, wrapping, overflow, margin, RectTransform layout은 보존한다.

### Selectable

Selectable binding은 transition을 Color Tint로 지정하고 normal, highlighted, pressed, selected, disabled color와 color multiplier, fade duration을 소유한다. 기존 `targetGraphic`이 필요하다. Navigation, interactability, animation trigger, sprite state, persistent event는 변경하지 않는다.

## Target capture

각 binding은 Unity가 직렬화한 `GlobalObjectId`와 사람이 읽을 수 있는 target metadata를 저장한다. Capture 순서는 다음과 같다.

1. Recipe의 owner Prefab을 지정한다.
2. 해당 owner를 Prefab Mode로 열고 미저장 변경을 저장한다.
3. 예상 component가 있는 GameObject를 선택한다.
4. Recipe Inspector의 binding에서 **Capture**를 선택한다.

도구는 identity를 기록하기 전에 Prefab Mode 선택 대상을 저장된 asset으로 다시 연결한다. 이후 **Select**를 누르면 owner를 다시 열고 저장된 target을 선택한다. Unity가 직렬화 object identity를 유지하는 일반적인 rename과 reparent는 계속 동작한다. Component를 삭제하고 새로 만들었다면 다시 Capture해야 한다.

Recipe는 nested Prefab instance가 소유한 component를 style 대상으로 삼을 수 없다. 해당 binding은 nested source Prefab의 Recipe에 두고, 검사가 필요한 외부 Prefab을 consumer로 등록한다.

## Prefab에서 Recipe 찾기

Project 창에서 Prefab을 선택하거나 Hierarchy·Prefab Mode에서 instance나
자식을 선택하면 Inspector 헤더에 **Style Recipes**가 표시된다. Recipe 이름을
누르면 선택한 UI 오브젝트와 Prefab Mode를 유지한 채 별도 Inspector로 연다.
기존 Inspector가 잠겨 있어도 사용할 수 있으며, 단순 탐색을 위해 미저장
Prefab 편집을 저장할 필요는 없다.

실제 Prefab 참조를 사용하므로 Recipe 이름이나 폴더 규칙에 의존하지 않는다.
**Owner**는 선택한 Prefab의 Recipe, **Base Prefab**은 Variant가 상속하는
원본 Prefab의 Recipe, **Consumer**는 선택한 Prefab을 consumer로 명시적으로
등록한 Recipe다. Nested Prefab 안의 오브젝트는 가장 가까운 nested source를
기준으로 찾는다. 아직 registry에 추가하지 않은 Recipe도 탐색할 수 있다.

링크 세 개까지 바로 표시하며, 더 많으면 **Show all … recipes**에서 고른다.
링크에 마우스를 올리면 Recipe와 Prefab 경로를 확인할 수 있다. Project와
Hierarchy 우클릭 메뉴의 **Super Hero UI > Find Style Recipes**도 사용할 수
있다. 결과가 하나면 바로 열고, 여러 개면 경로가 표시된 목록에서 고른다.
오브젝트는 하나씩 선택한다. 외부 도구나 스크립트로 바꾼 연결이 아직 보이지
않으면 **Refresh**로 다시 찾는다.

Recipe 찾기와 열기는 읽기 전용이다. Component 추가, Recipe 생성, target
Capture, Preview 실행이나 Apply를 수행하지 않는다. Recipe 목록은 캐시하며,
변경 없는 Inspector repaint에서는 자산을 검색하거나 Prefab contents를
로드하지 않는다.

## Preview와 Apply

**Tools > Super Hero UI > Style Recipes**를 열고 registry를 선택한다.

- `Preview / Validate`는 저장된 Prefab 내용을 읽어 모든 typed target을 resolve하고, 자산을 저장하지 않은 채 영향을 받는 owner, target, property, proposed value를 표시한다.
- `Apply Reviewed Changes`는 Preview와 정확히 같은 fingerprint일 때만 실행된다. Recipe, style, token, owner, consumer 또는 package 구현이 바뀌면 Preview는 무효가 된다.
- Apply는 선언된 property만 쓰고 차이가 있는 owner만 저장한다.
- 새 Preview에서 차이나 validation error가 없으면 `Ready`다.
- `Ready` review를 다시 Apply해도 쓰기가 발생하지 않는다.

`Stale`은 Prefab 값 하나 이상이 Recipe와 다르다는 뜻이다. Preview 이후 입력이 바뀌어 기존 승인 fingerprint가 무효가 된 상태도 별도로 검사하며, 이 경우 다시 Preview해야 한다. `Error`는 Recipe를 안전하게 평가하거나 적용할 수 없다는 뜻이다.

## 검증 규칙

### Target과 소유권

- owner와 consumer는 저장된 Prefab asset이어야 한다.
- 모든 target은 예상 component type으로 resolve되어야 한다.
- Missing Script가 있으면 검증에 실패한다.
- 하나의 registry 안에서 owner Prefab 하나는 Recipe 하나만 소유한다.
- 하나의 Recipe 안에서 managed property 하나는 binding 하나만 소유한다. 제안 값이 같아도 중복 소유권은 오류다.
- 서로 다른 registry는 교차 검사하지 않는다. 하나의 Prefab 소유권을 여러 registry로 나누지 않는다.

### Variant specialization

시각 의도가 의도적으로 다른 Prefab Variant는 완전한 typed Recipe를 따로 만들고 source Recipe를 `Base Recipe`로 명시한다. 두 Recipe는 같은 registry에 있어야 한다. Variant는 typed binding으로 같은 capture target과 property를 소유해야 하며, literal 값이 같거나 raw serialized override가 있다는 사실만으로는 충분하지 않다. 이 계약은 해당 Variant 경계에서만 base property 전파를 대체하고 다른 consumer에는 base Recipe를 유지한다.

### 명시적 consumer 검사

`PrefabStyleRecipe.ConsumerPrefabs`에 지정한 Prefab만 검사한다. 각 consumer에는 managed owner가 nested Prefab instance로 포함되어야 한다. Consumer와 owner 사이에서 Recipe 소유 property를 override하면 예측 가능한 전파를 막으므로 오류다. 명시적인 Variant specialization만 예외이며, `Base Recipe`와 정확한 target/property의 typed binding 소유가 필요하다. Owner 자체가 Prefab Variant라면 owner에 작성된 override는 Recipe의 baseline이므로 허용한다. Managed 대상이 아닌 layout, content, event, 기능 값의 override는 허용한다.

복합 owner는 자체 중첩 Prefab instance를 포함할 수 있다. Consumer 검증은 해당
owner와 일치하는 가장 바깥 instance만 선택하고 중첩 source root를 별도 owner
instance로 오인하지 않는다. Target 식별과 typed property 소유권은 그대로 유지된다.

명시적 등록 방식은 Preview, Play 전환, Build 때마다 프로젝트 전체 Prefab dependency를 검색하는 비용을 피한다. Managed owner를 의도적으로 중첩하고 bake된 style을 상속해야 하는 Prefab은 consumer로 추가한다.

### 미저장 Prefab Mode

현재 Prefab Stage가 tracked owner 또는 consumer이고 저장하지 않은 변경이 있으면 검증을 중단한다. 패키지는 개발자가 Prefab Mode에서 작업 중인 내용을 저장하거나 닫거나 버리지 않는다.

## Editor와 build 동작

`StyleRecipeRegistry`를 만들면 package 검색 대상이 된다. 새 registry는 두 guard가 기본으로 활성화되며 migration 중에는 각각 끌 수 있다.

Play guard는 정상 dependency fingerprint를 `Library/SuperHeroUI`에 cache하고, 모든 dependency가 저장된 채 변경되지 않았다면 전체 검사를 반복하지 않는다. Custom Editor callback이 있는 Registry는 이 Ready cache도 우회한다. Build guard는 항상 새 전체 Preview를 실행한다. 두 guard 모두 style을 자동 Apply하지 않는다. Registry가 `Ready`가 아니면 실행을 막고 Editor window에서 검토하도록 안내한다.

패키지는 Prefab에 component를 추가하지 않는다. Runtime lookup, runtime component 생성, runtime style 순회도 수행하지 않는다. Recipe 탐색, Preview/Apply와 설정한 guard는 Editor에서만 실행된다.

### 검사 비용

[성능 측정](performance.ko.md)에서 실제 비교 결과와 재현 조건을 확인할 수 있다.

Preview는 owner 검사와 consumer 검증 결과를 일반 데이터로 분리해 Editor 세션의
메모리에 cache한다. Cache를 사용할 수 있는 Prefab은 변경되지 않은 결과를 재사용한다.
`ExecuteAlways`, `ExecuteInEditMode`, `OnValidate`, `ISerializationCallbackReceiver`가 있는 custom script나
`runInEditMode`가 활성화된 custom behaviour는 owner와 consumer를 새로 검사해야 한다.
Built-in uGUI와 TMP component는 cache를 사용할 수 있다. 그 외 owner와 consumer는
입력이 바뀔 때만 다시 로드한다. 로드한 Prefab 객체는 검사 사이에 보관하지 않는다.
최초 Preview, domain reload 이후 Preview, **Full Preview / Validate**는 모든 Recipe를
검사한다. Build guard는 항상 결과 cache를 우회해 전체 검사를 실행한다.

모든 Preview는 등록된 전체 dependency, 현재 ScriptableObject JSON, registry
구성, tracked Prefab Stage의 미저장 상태를 계속 확인한다. Specialization
Recipe에서 Base Recipe로 이어지는 관계도 매번 갱신하므로, specialization 변경은
영향받는 base consumer 검사도 무효화한다. 검증 오류가 있는 결과는 다음 Preview에서
다시 검사한다. Prefab을 다시 로드할 필요가 없어도 dependency key와 승인 fingerprint
계산 비용은 등록된 입력의 수에 따라 증가한다.

Consumer 검증이 필요하면 Preview는 공유 consumer를 한 번 로드하고, 그 상태에서
영향받는 등록 owner 관계를 검사한다. Missing Script 검사와 owner instance 탐색은
한 번의 hierarchy 순회를 공유한다. Owner target 탐색은 로드한 owner마다
component ID index를 한 번 구성하고, 각 dependency fingerprint 계산은 공유
asset의 hash와 ScriptableObject JSON을 한 번씩 읽는다. Recipe와 consumer 진단은
중복 등록을 포함해 기존 등록 순서를 유지한다.

Apply는 검토한 변경이 있는 owner와 해당 Prefab에 의존하는 등록 owner를 선택한다.
여기에는 중첩 Prefab으로 포함하거나 Variant로 상속하는 owner도 포함된다.
상위 source 저장으로 상속 값이 바뀔 수
있으므로 이런 의존 owner는 처음에 `Ready`였어도 다시 평가한다. Apply는 쓰기 전에
영향받는 cache를 무효화하고 Prefab 의존 순서대로 저장한 뒤, 각 owner 직후 최신
consumer를 즉시 검사한다. Apply 전후 Preview는 증분 검사를 사용한다. 승인은 계속
전체 registry의 현재 fingerprint를 요구하며, cache를 사용해도 오래된 승인으로
일부 변경만 적용할 수는 없다.

## Composite recipe 참고 자료

**Composite Control Recipes** sample은 입력 필드, 드롭다운, 탭, 테이블, 팝업, 배지를 위 primitive에 연결하는 방법을 보여준다. 완성된 복합 컨트롤 Prefab이 아닌 설계 참고 자료이므로 프로젝트는 자체 hierarchy와 동작 계약을 유지한다.

## Git 및 UPM 배포

독립 source 저장소는 [superherounite/com.superherounite.ui](https://github.com/superherounite/com.superherounite.ui)이며 `package.json`이 저장소 root에 있다. `.meta` 파일을 보존하고 이 저장소에서 변경되지 않는 Semantic Version tag를 발행한다.

```json
"com.superherounite.ui": "https://github.com/superherounite/com.superherounite.ui.git#v0.1.0-preview.4"
```

소비 프로젝트와 같은 상위 폴더 아래에 local checkout을 함께 두었다면 `Packages/manifest.json` 기준 상대 경로를 사용한다.

```json
"com.superherounite.ui": "file:../../com.superherounite.ui"
```

Monorepo에서는 embedded 하위 폴더를 임시로 노출할 수 있다.

```json
"com.superherounite.ui": "https://<git-host>/<organization>/<repository>.git?path=/Packages/com.superherounite.ui#<immutable-tag>"
```

`?path=` query는 `#revision`보다 앞에 둔다. 같은 ID의 embedded package가 있으면 Git 또는 `file:` dependency보다 우선하므로 설치 검증 전에 제거한다. 소비 프로젝트의 manifest와 lock file은 함께 커밋한다. Git 설치 package test에는 소비 또는 CI manifest의 `testables`에 `"com.superherounite.ui"`를 추가하고 Unity Test Framework를 설치해야 한다.

업데이트할 때마다 `package.json`과 package changelog를 함께 바꾸고 기존 `.meta` GUID를 유지한 채 커밋한 다음 새 immutable version tag를 만든다. 작은 임시 Unity 프로젝트나 저장소의 `TestProject~`에서 그 tag를 검증한다. 소비 프로젝트는 dependency의 `#tag`를 갱신하고 재생성된 lock file을 manifest와 함께 커밋한다.

Dependency URL에 credential을 넣지 않는다. Public 저장소에서는 익명 HTTPS로 설치하며, 패키지는 MIT License로 배포한다.

## 현재 검증 범위

소스 package와 두 Editor assembly는 Unity `6000.0.68f1`에서 import 및 compile됐다. 자동 테스트는 Preview 무변경, Sprite 소유권을 포함한 다섯 primitive category, Apply 멱등성, stale approval 거부, 중복 property 소유권, 직접 및 중간 Variant consumer override, Variant owner baseline, Variant 추가 child target 해석, 중첩 Prefab을 포함한 복합 owner, 누락 target, 잘못된 image parameter, unmanaged 값 보존을 확인한다.

추가 회귀 테스트는 owner 2개, 12개, 100개가 공유하는 consumer, 반복 조회에서
target index 한 번 구성, 공유 fingerprint 읽기 재사용을 확인한다. 증분 테스트는
전체 검사와 결과를 대조하고, cache 재사용, 미저장 및 반복 token 수정, Undo/Redo,
공유 dependency, 저장된 owner와 consumer 변경, registry 구성 변경, 참조 교체와
삭제, 선택적인 Apply, 반복 Apply 시 무변경을 확인한다. 장비별 실행 시간 한계값 대신
작업 횟수와 asset 내용의 무변경을 검증한다. Text style fixture는 TMP 기본 font를
사용하므로, 최소 구성 테스트 프로젝트에서는 전체 테스트 실행 전에
**TMP Essential Resources**를 import해야 한다.

전체 테스트는 [검증 스크립트](../Tools~/Validate-Package.ps1)로 실행한다.
사용 중단을 권고한 `StyleRecipeProcessorBatchRunner.Run` 진입점은 최초 smoke test
12개만 실행하므로, 성공하더라도 package 전체를 검증한 것은 아니다.

Recipe 탐색 테스트는 asset·instance의 자식, 추가 override, nested Prefab,
Variant 원본과 Prefab Mode, 명시적 consumer, sub-asset, cache 무효화와 파일
무변경을 확인한다. 별도 Inspector 열기 테스트에는 그래픽 장치가 필요하다.
스크립트의 `-EnableGraphics` 옵션으로 함께 검증할 수 있으며, 그래픽이 없는
headless 실행에서는 이 테스트를 건너뛴다.

외부 release 전에는 immutable tag, 해당 Git URL로 설치한 결과, 프로젝트별 asset 및 build 설정을 사용하는 소비 프로젝트 Player Build 검증이 필요하다.
