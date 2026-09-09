# Super Hero UI 제작 가이드

[English](index.md) | 한국어

Super Hero UI `0.1.0-preview.3`은 Unity 6000.0용 Editor 전용 제작 패키지다. Production code는 `SuperHeroUnite.UI.Editor`, package test는 `SuperHeroUnite.UI.Editor.Tests`에 속한다. 이 패키지에는 runtime assembly가 없다.

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

Play guard는 정상 dependency fingerprint를 `Library/SuperHeroUI`에 cache하고, 모든 dependency가 저장된 채 변경되지 않았다면 전체 검사를 반복하지 않는다. Build guard는 항상 새 Preview를 실행한다. 두 guard 모두 style을 자동 Apply하지 않는다. Registry가 `Ready`가 아니면 실행을 막고 Editor window에서 검토하도록 안내한다.

패키지는 Prefab에 component를 추가하지 않는다. Runtime lookup, runtime component 생성, runtime style 순회도 수행하지 않는다. Editor 비용은 명시적 Preview/Apply와 설정한 guard 경계에서만 발생한다.

## Composite recipe 참고 자료

**Composite Control Recipes** sample은 입력 필드, 드롭다운, 탭, 테이블, 팝업, 배지를 위 primitive에 연결하는 방법을 보여준다. 완성된 복합 컨트롤 Prefab이 아닌 설계 참고 자료이므로 프로젝트는 자체 hierarchy와 동작 계약을 유지한다.

## Git 및 UPM 배포

독립 source 저장소는 [superherounite/com.superherounite.ui](https://github.com/superherounite/com.superherounite.ui)이며 `package.json`이 저장소 root에 있다. `.meta` 파일을 보존하고 이 저장소에서 변경되지 않는 Semantic Version tag를 발행한다.

```json
"com.superherounite.ui": "ssh://git@github.com/superherounite/com.superherounite.ui.git#v0.1.0-preview.3"
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

Dependency URL에 credential을 넣지 않는다. 이 private 저장소는 host의 Git credential manager 또는 SSH agent를 사용한다. Release tag를 발행하기 전에 회사가 승인한 license를 추가한다.

## 현재 검증 범위

소스 package와 두 Editor assembly는 Unity `6000.0.68f1`에서 import 및 compile됐다. 자동 테스트는 Preview 무변경, Sprite 소유권을 포함한 다섯 primitive category, Apply 멱등성, stale approval 거부, 중복 property 소유권, 직접 및 중간 Variant consumer override, Variant owner baseline, Variant 추가 child target 해석, 중첩 Prefab을 포함한 복합 owner, 누락 target, 잘못된 image parameter, unmanaged 값 보존을 확인한다.

외부 release 전에는 회사가 승인한 license, immutable tag, 해당 Git URL로 설치한 결과, 프로젝트별 asset 및 build 설정을 사용하는 소비 프로젝트 Player Build 검증이 필요하다.
