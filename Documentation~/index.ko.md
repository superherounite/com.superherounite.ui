# Super Hero UI 제작 가이드

[English](index.md) | 한국어

Super Hero UI `0.1.0-preview.5`은 Unity 6000.0용 Editor 전용 제작 패키지다. Production code는 `SuperHeroUnite.UI.Editor`, package test는 `SuperHeroUnite.UI.Editor.Tests`에 속한다. 이 패키지에는 runtime assembly가 없다.

## 패키지 경계

패키지는 범용 제작 방식을 책임진다.

- ScriptableObject color 및 typed style 정의
- 안정적인 Prefab component capture와 resolve
- 읽기 전용 변경 Preview
- 검토를 거쳐 같은 입력에 같은 결과를 내는 Prefab bake
- Play Mode에서 color와 Image PPUM 임시 튜닝 및 검토 후 원본 반영
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

검토 상세와 **Registered Recipes** 목록은 각각 독립적으로 스크롤된다. 각 영역의
높이는 제한되며 창을 작게 dock해 모든 조작부가 보이지 않으면 창 전체도 스크롤된다.

### Consumer override 복구

누락된 `Base Recipe` 관계와 명시적 consumer 등록의 오래된 owner 연결은 현재
Prefab 관계로 자동 해석한다. 복구 Apply가 필요하지 않으며
[Variant specialization](#variant-specialization)과
[consumer 검사](#명시적-consumer-검사)를 참고한다.

등록 consumer에 Recipe 소유 property의 override가 있으면 일반 Preview는 계속
`Error`를 표시하고 **Apply Reviewed Changes**를 비활성화한다. 의도하지 않은
override는 다음 별도 검토 절차로 복구한다.

1. **Preview Override Repairs**를 선택한다. 현재 Prefab을 읽기만 하며 되돌릴
   정확한 asset, target, property를 표시한다.
2. 목록을 검토한 뒤 **Apply Reviewed Repairs**를 선택한다. 이 registry에 owner나
   consumer로 명시적으로 등록된 Prefab에서 검토한 Recipe 소유 property만 source
   Prefab 값으로 되돌린다. Unmanaged 값, owner baseline, 유효한 typed Variant
   specialization은 보존한다. 다른 validation error가 있으면 먼저 해결해야 한다.
3. 창은 일반 Preview로 돌아간다. 결과가 `Stale`이면 남은 style 차이를 검토하고
   **Apply Reviewed Changes**로 bake한 뒤 새 Preview가 `Ready`인지 확인한다.

복구 Preview 이후 입력이 바뀌면 승인이 무효가 되므로 다시 검토해야 한다.
등록되지 않은 중간 source Prefab의 override는 outer consumer를 통해 복구할 수
없으므로 해당 source를 먼저 등록한다. 의도적인 시각 차이라면 되돌리는 대신
Variant에 자체 typed Recipe를 만든다. 복구 과정에서
asset을 자동 등록하거나 override를 specialization으로 자동 변환하지 않는다.

잘못된 명시적 `Base Recipe` 지정이나 유효한 Variant 관계가 없는 소유권 충돌은
수정이 필요한 오류로 남는다. 복구가 등록 owner의 작성된 style을 지워 이런
충돌을 해소하지 않는다.

## Play Mode 튜닝

**Tools > Super Hero UI > Play Mode Tuning**에서 실행 중인 UI의 color와
`Image.pixelsPerUnitMultiplier`(PPUM)를 조절할 수 있다. Editor 도구이므로
runtime component, Style 참조, Player code를 추가하지 않는다. 기존 Prefab
**Preview / Validate**는 계속 읽기 전용이며 Prefab Apply는 Edit Mode에서
실행한다. **Style Recipes**의 **Open Play Mode Tuning** 버튼과 Hierarchy
우클릭 메뉴의 **Super Hero UI > Play Mode Tuning**에서도 열 수 있다.

1. 저장된 프로젝트 Recipe와 **Recipe Target**을 선택한다. 해당 component의
   지원 property가 함께 표시된다. Graphic Color, Image tint, Surface fill 또는
   outline tint, TMP text color, Selectable Color Tint의 다섯 상태를 지원한다.
   Image·Surface target의 **Rounding (PPUM)**은 해당 `ImageStyle`이 PPUM을
   소유할 때만 표시된다. Recipe와 원본 asset은 `Assets/` 아래에 저장되어
   있어야 한다. **Find Recipes for Selection**으로 선택한 오브젝트의 Prefab
   연결에서 Recipe를 찾을 수 있다. 연결이 없는 clone은 Recipe를 직접 선택한다.
2. Play Mode에서 실제 실행 중 component를 **Live Component**에 끌어 놓거나
   해당 GameObject를 선택한 뒤 **Use Selected Object**를 누른다. Capture한
   구체적인 component type이 일치해야 한다. Prefab 연결이 있으면 capture한
   target과의 대응도 검사한다. 연결이 없는 생성 오브젝트는 직접 대응을 확인해야
   하며, Prefab에 연결된 parent 아래의 연결 없는 clone도 지정할 수 있다.
   이름이나 hierarchy 경로로 target을 추측하지 않는다. Prefab asset,
   Prefab Mode, preview scene은 대상에서 제외한다.
3. Draft 값을 조절해 해당 instance에서 확인한다. 실험 중에는 ColorToken,
   Style, Recipe, Prefab asset을 쓰지 않는다. 다른 instance와 나중에 생성된
   instance에는 draft가 자동으로 적용되지 않는다.
4. 결과가 마음에 들면 Play Mode를 종료한다. Draft는 같은 Editor 세션에서
   Play 종료, domain reload, 창 닫기 이후에도 남는다. Editor 재시작까지
   보존하는 영구 저장은 아니다. 임시 component 변경은 Play 종료, 창 닫기,
   script reload 때 아래 복구 규칙에 따라 복구한다.
5. Edit Mode에서 **Review Style Changes**를 누른다. 현재 target뿐 아니라
   다른 Recipe를 포함한 **Saved Drafts** 전체를 검토한다. 원본 경로, 변경값,
   공유 사용처를 확인하고 같은 원본에 서로 다른 값을 쓰려는 draft는 폐기한다.
   검토에서 Recipe·binding 변경이나 원본 값 충돌을 보고하면 해당 draft를 폐기하고
   다시 튜닝한다. 검토 이후 변경이 생기면 다시 Review해야 한다.
   **Write Reviewed Style Changes**는 ColorToken color와 ImageStyle PPUM만
   수정하고 해당 원본 asset을 Undo 가능한 변경으로 저장한 뒤 draft를 비운다.
   Recipe 참조나 Prefab bake는 변경하지 않는다. 원본은 `Assets/` 아래의 편집
   가능한 프로젝트 asset이어야 한다. 원본 asset 파일에 기존 미저장 변경이 있으면
   같은 파일의 sub-asset을 포함해 먼저 저장하거나 되돌린 뒤 검토한다. 도구는
   이런 dirty 파일을 거부하며 해당 편집을 대신 저장하거나 폐기하지 않는다.
6. **Open Style Recipes**를 누르고 **Preview / Validate**로 영향을 받는 Prefab과
   등록 consumer를 검토하고 **Apply Reviewed Changes** 후 새 Preview가
   `Ready`인지 확인한다. 영향을 받는 다른 registry도 같은 절차로 확인한다.
   원본 기록은 registry를 자동으로 bake하지 않는다.

**Restore Live Values**는 draft를 보관한 채 추적 중인 임시 값을 모두 복구한다.
각 property의 **Reapply Draft**로 지정한 component에 draft를 다시 적용해 비교할
수 있다. 복구는 현재 값이 도구가 마지막으로 기록한 값과 같을 때만 수행한다.
그 이후 game code나 animation이 변경한 값은 유지한다. **Select Draft**는 해당
Recipe target으로 돌아가며 실행 중 instance를 추측해 지정하지 않는다.
**Discard**는 draft 하나를 지우고 해당 draft가 추적하는 실행 값을 복구한다.
**Discard All Drafts**는 세션의 모든 draft와 임시 적용을 정리한다. Recipe의
각 property에는 draft 하나가 있으며 마지막으로 튜닝한 값으로 갱신된다.

공유 ColorToken을 쓰면 이를 참조하는 모든 Style과 Recipe에 영향을 준다.
ImageStyle PPUM 변경도 같은 Style을 사용하는 모든 binding에 영향을 준다.
한 instance에서 튜닝했다고 원본 변경의 범위가 해당 instance로 제한되지는
않는다. 시각 의도가 달라야 한다면 별도 project-owned token이나 Style을 만들고
변경한 binding을 튜닝한다. 공유 사용처 목록은 `Assets/` 아래 Recipe의 지원
튜닝 binding 수를 기준으로 한다.

PPUM은 `0.01` 이상의 유한한 값이어야 하며 Unity의 image 단위를 유지한다.
Sliced·Tiled sprite의 border 크기를 맞추는 데 사용할 수 있지만 Figma corner
radius 값과 같지 않으며 단위를 자동 변환하지 않는다. Sprite, border, Image
type, layout은 작성된 상태를 유지한다.
Animator, Selectable transition, project script가 임시 color나 PPUM을 덮어쓸 수
있다. 다시 비교하려면 **Reapply Draft**를 누른다. Selectable 상태 color를 보려면
실행 중 transition이 Color Tint여야 하고 해당 상호작용 상태가 되어야 한다.
튜닝 창이 transition을 바꾸거나 runtime 동작의 소유권을 가져가지는 않는다.

## 검증 규칙

### Target과 소유권

- owner와 consumer는 저장된 Prefab asset이어야 한다.
- 모든 target은 예상 component type으로 resolve되어야 한다.
- Missing Script가 있으면 검증에 실패한다.
- 하나의 registry 안에서 owner Prefab 하나는 Recipe 하나만 소유한다.
- 하나의 Recipe 안에서 managed property 하나는 binding 하나만 소유한다. 제안 값이 같아도 중복 소유권은 오류다.
- 서로 다른 registry는 교차 검사하지 않는다. 하나의 Prefab 소유권을 여러 registry로 나누지 않는다.

### Variant specialization

시각 의도가 의도적으로 다른 Prefab Variant는 완전한 typed Recipe를 따로 만든다. `Base Recipe`가 비어 있으면 실제 Variant 상위 Prefab 중 가장 가까운 등록 Recipe로 자동 해석한다. 명시적으로 지정한 `Base Recipe`는 우선하며 잘못된 명시적 지정은 계속 오류다. 두 Recipe는 같은 registry에 있어야 한다. Variant는 typed binding으로 같은 capture target과 property를 소유해야 하며, literal 값이 같거나 raw serialized override가 있다는 사실만으로는 충분하지 않다. 이 계약은 해당 Variant 경계에서만 base property 전파를 대체하고 다른 consumer에는 base Recipe를 유지한다.

여러 단계로 이어지는 `Base Recipe` chain도 인식한다. Chain의 모든 Recipe를
등록해야 하며, 각 specialization에는 유효한 Variant source 관계와 정확한
target/property 소유권이 계속 필요하다. 관계 해석은 읽기 전용이며 serialized
field를 채우거나 Recipe와 Prefab asset을 수정하지 않는다.

### 명시적 consumer 검사

`PrefabStyleRecipe.ConsumerPrefabs`에 지정한 Prefab만 검사한다. 보통 각 consumer에 지정한 managed owner를 따라 검사한다. UI 구조 변경으로 그 owner가 사라지면 같은 consumer에 실제로 존재하는 등록 owner instance를 모두 따라 검사한다. 이름이 아닌 실제 Prefab 관계를 사용하며 등록 owner가 하나도 없으면 수정 가능한 오류를 유지한다. Consumer를 추가하거나 Recipe 목록과 Prefab을 수정하지 않는다.

Consumer와 owner 사이에서 Recipe 소유 property를 override하면 예측 가능한 전파를 막으므로 오류다. 유효한 Variant specialization만 예외이며, 자동 해석하거나 명시한 base 관계와 정확한 target/property의 typed binding 소유가 필요하다. Owner 자체가 Prefab Variant라면 owner에 작성된 override는 Recipe의 baseline이므로 허용한다. Managed 대상이 아닌 layout, content, event, 기능 값의 override는 허용한다.

Preview, Apply, Play, Build가 이 관계 해석을 공유한다. 자동 해석한 Variant
관계는 현재 상속 관계와 제작 상태를 반영한다. 대체 owner를 따라 검사하는
consumer는 이전 연결의 검증 cache를 재사용하지 않고 매번 새로 검사한다.

복합 owner는 자체 중첩 Prefab instance를 포함할 수 있다. Consumer 검증은 해당
owner와 일치하는 가장 바깥 instance만 선택하고 중첩 source root를 별도 owner
instance로 오인하지 않는다. Target 식별과 typed property 소유권은 그대로 유지된다.

명시적 등록 방식은 Preview, Play 전환, Build 때마다 프로젝트 전체 Prefab dependency를 검색하는 비용을 피한다. Managed owner를 의도적으로 중첩하고 bake된 style을 상속해야 하는 Prefab은 consumer로 추가한다.

### 미저장 Prefab Mode

현재 Prefab Stage가 tracked owner 또는 consumer이고 저장하지 않은 변경이 있으면 검증을 중단한다. 패키지는 개발자가 Prefab Mode에서 작업 중인 내용을 저장하거나 닫거나 버리지 않는다.

## Editor와 build 동작

`StyleRecipeRegistry`를 만들면 package 검색 대상이 된다. 새 registry는 두 guard가 기본으로 활성화되며 migration 중에는 각각 끌 수 있다.

Play guard는 정상 dependency fingerprint를 `Library/SuperHeroUI`에 cache하고, 모든 dependency가 저장된 채 변경되지 않았다면 전체 검사를 반복하지 않는다. Custom Editor callback이 있는 Registry는 이 Ready cache도 우회한다. Build guard는 항상 새 전체 Preview를 실행한다. 두 guard 모두 style을 자동 Apply하지 않는다. Registry가 `Ready`가 아니면 실행을 막고 Editor window에서 검토하도록 안내한다.

반복 Play 검사에서는 Unity의 artifact dependency version이 같을 때 저장된
asset의 dependency hash를 재사용한다. 같은 Editor 세션에서 domain reload가
발생해도 version이 일치할 때만 이 hash cache를 이어서 사용하고, dependency가
바뀌면 hash를 새로 읽는다. 각 검사는 TMP font 데이터를 포함한 현재 ScriptableObject
상태, tracked 미저장 상태, 현재 callback의 cache 사용 가능 여부를 계속 확인한다.
`Ready` 기록에는 완료된 review의 fingerprint를 사용하므로 이전 review로 이후
편집을 승인하지 않는다. 큰 제작 데이터나 custom Editor callback은 여전히 Play
진입 시간에 영향을 줄 수 있다.

패키지는 Prefab에 component를 추가하지 않으며 runtime Style system을 제공하지
않는다. Recipe 탐색, Preview/Apply, Play Mode 튜닝, 설정한 guard는 Editor에서만
실행된다. Play Mode 튜닝은 명시적으로 지정한 실행 중 component를 임시로
변경하며 Player build는 bake된 component 값을 사용한다.

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

로드된 consumer 하나를 검사하는 동안 반복되는 Recipe owner, `Base Recipe`
chain, target source chain 조회는 해당 검사 안에서 결과를 공유한다. Consumer를
unload하거나 owner를 저장한 뒤에는 이 결과를 재사용하지 않는다. Custom Editor
callback이 있는 consumer는 계속 새로 검사하며, Build의 전체 검증도 유지한다.

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

## OpenUPM 설치와 업데이트

Package Manager 안에서 버전을 선택하고 업데이트하려면 OpenUPM scoped registry를
사용한다. `0.1.0-preview.5`는
[OpenUPM](https://openupm.com/packages/com.superherounite.ui/)에서 설치할 수 있다.

1. **Edit > Project Settings > Package Manager**에서 **Name** `OpenUPM`,
   **URL** `https://package.openupm.com`, **Scope(s)** `com.superherounite.ui`로
   scoped registry를 추가하고 적용한다.
2. Preview release를 표시하도록 **Show Pre-release Package Versions**를 켠다.
3. **Window > Package Manager**에서 **+ > Add package by name**을 선택하고
   `com.superherounite.ui`의 `0.1.0-preview.5` 버전을 설치한다.
4. 이후에는 **In Project** 또는 **My Registries**에서 **Super Hero UI**를
   선택한 뒤 **Version History**에서 원하는 버전의 **Update**를 누른다.

### 기존 Git 설치에서 한 번 전환하기

Registry를 추가한 뒤 같은 **Add package by name**을 사용하면 기존 Git
dependency가 registry 버전으로 교체된다. 또는 아래 registry 설정을
`Packages/manifest.json`에 합치고 이 패키지의 dependency 값만
`0.1.0-preview.5`로 교체한다. 나머지 manifest 항목은 유지한다.

```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": ["com.superherounite.ui"]
    }
  ],
  "dependencies": {
    "com.superherounite.ui": "0.1.0-preview.5"
  }
}
```

Unity가 새 source를 resolve하고 `Packages/packages-lock.json`을 갱신하도록
기다린다. Cache package나 lock 항목을 직접 수정하지 않는다. Manifest와 lock
file을 함께 커밋한다. 이후 registry 업데이트는 Package Manager에서 수행하며
Git URL을 고칠 필요가 없다. `Packages/com.superherounite.ui`의 embedded package는
registry dependency보다 우선하므로 로컬 변경을 보존한 뒤 제거하고 설치 source를
전환한다. [OpenUPM 설정](https://openupm.com/docs/getting-started.html)과
[Unity 업데이트 절차](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-ui-update.html)를 참고한다.

### Git과 로컬 대체 설치

독립 source 저장소는 [superherounite/com.superherounite.ui](https://github.com/superherounite/com.superherounite.ui)이며 `package.json`이 저장소 root에 있다. `.meta` 파일을 보존하고 이 저장소에서 변경되지 않는 Semantic Version tag를 발행한다. Git dependency로 release를 직접 고정할 수도 있다.

```json
"com.superherounite.ui": "https://github.com/superherounite/com.superherounite.ui.git#v0.1.0-preview.5"
```

이 tag는 고정된다. Git의 **Update**는 다음 release tag를 선택하지 않으므로
Git 설치를 유지하는 프로젝트는 다른 release로 이동할 때 `#tag`를 변경해야 한다.

소비 프로젝트와 같은 상위 폴더 아래에 local checkout을 함께 두었다면 `Packages/manifest.json` 기준 상대 경로를 사용한다.

```json
"com.superherounite.ui": "file:../../com.superherounite.ui"
```

Monorepo에서는 embedded 하위 폴더를 임시로 노출할 수 있다.

```json
"com.superherounite.ui": "https://<git-host>/<organization>/<repository>.git?path=/Packages/com.superherounite.ui#<immutable-tag>"
```

`?path=` query는 `#revision`보다 앞에 둔다. 같은 ID의 embedded package가 있으면 Git 또는 `file:` dependency보다 우선하므로 설치 검증 전에 제거한다. 소비 프로젝트의 manifest와 lock file은 함께 커밋한다. Registry 또는 Git 설치 package test에는 소비 또는 CI manifest의 `testables`에 `"com.superherounite.ui"`를 추가하고 Unity Test Framework를 설치해야 한다.

Package를 release할 때마다 `package.json`과 package changelog를 함께 바꾸고 기존 `.meta` GUID를 유지한 채 커밋한 다음 새 immutable version tag를 만든다. 작은 임시 Unity 프로젝트나 저장소의 `TestProject~`에서 그 tag를 검증한다. OpenUPM이 이 저장소의 version tag를 빌드해 registry에 게시한다. 새 registry 버전의 설치 가능 여부를 확인한 뒤 배포를 알린다. Registry 소비 프로젝트는 **Update**를 사용하고 Git 소비 프로젝트는 `#tag`를 갱신한다. 모두 재생성된 lock file을 manifest와 함께 커밋한다.

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
