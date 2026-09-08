# Super Hero UI

[English](README.md) | 한국어

Super Hero UI는 Unity 6000.0용 Editor 전용 스타일 제작 패키지다. ScriptableObject 자산으로 시각 규칙을 공유하고, 적용 영향을 미리 보여준 뒤 승인된 값만 일반 uGUI Prefab에 굽는다. 패키지 ID는 `com.superherounite.ui`이며 현재 개발 버전은 `0.1.0-preview.1`이다.

이 패키지는 복합 컨트롤보다 낮은 계층까지만 책임진다. 입력 필드, 드롭다운, 탭, 테이블, 팝업, 배지는 보통 제품별 계층 구조, 상호작용, 접근성, 레이아웃, 데이터 동작을 포함한다. 각 프로젝트는 패키지가 정한 Prefab 계약을 상속하는 대신 재사용 가능한 시각 primitive로 이런 컨트롤을 구성한다.

## 제공 기능

- 코드를 다시 컴파일하지 않고 색상을 추가할 수 있는 enum 없는 `ColorToken` 자산
- 타입이 명확한 `ImageStyle`, `SurfaceStyle`, `TextStyle`, `SelectableStyle` 자산
- Prefab Mode에서 owner Prefab 컴포넌트를 안정적으로 캡처하는 기능
- 검토한 dependency fingerprint와 연결된 읽기 전용 Preview 및 명시적 Apply 단계
- 속성별 소유권 검사와 명시적으로 등록한 중첩 Prefab consumer 검증
- 선택적으로 사용할 수 있는 Play Mode 및 Player Build 준비 상태 guard
- runtime assembly가 없으며 스타일 대상 Prefab에 패키지 소유 Binding 컴포넌트를 추가하거나 요구하지 않는 구조

## AI Agent 지침

패키지에는 package source용 짧은 [AGENTS.md](AGENTS.md)와 상세
[영문](Documentation~/agent-integration.md) 및
[한국어](Documentation~/agent-integration.ko.md) 연동 가이드가 포함된다. 가이드는
package 검색, context 조사, Binding 선택, Prefab 작성, runtime 제약, 완료 검증을
정의한다.

Package scope의 지침은 소비 프로젝트의 sibling `Assets/` file에 자동으로
적용되지 않는다. 연동 가이드의 bootstrap을 해당 프로젝트에서 실제로 사용하는
root Agent 지침에 복사해 한 번 commit한다. Bootstrap은 clean checkout에서 Git
package가 아직 `Library/PackageCache`에 resolve되지 않은 경우에도 필요한 최소
안전 계약을 포함한다.

## 지원 binding

| Binding | Recipe가 관리하는 값 |
|---|---|
| Graphic Color | 기본 직렬화 color 필드를 사용하는 Graphic의 `Graphic.color`. TMP 대상은 Text binding을 사용한다. |
| Image | tint와 개별적으로 소유 여부를 정하는 sprite, `Image.Type`, `preserveAspect`, `fillCenter`, `pixelsPerUnitMultiplier` |
| Surface | 미리 작성된 `Image` target 하나에 적용하는 필수 fill `ImageStyle`과 두 번째 target에 적용하는 선택적 outline `ImageStyle` |
| Text | TMP font와 해당 shared material, color, size와 size base, style, auto-size 설정, character/word/line/paragraph spacing |
| Selectable | Color Tint transition, 다섯 가지 상태 color, color multiplier, fade duration |

콘텐츠, anchor, 크기, layout component, localization, UnityEvent, runtime 상태는 소비 프로젝트가 소유한다.

## 제작 절차

1. **Create > Super Hero UI > Styles**에서 color와 style 자산을 만든다.
2. **Prefab Style Recipe**를 만들고 owner Prefab을 지정한다.
3. Recipe에 typed binding을 추가한다.
4. owner를 Prefab Mode로 열어 저장하고, 각 대상을 선택한 뒤 binding의 **Capture** 버튼을 누른다.
5. **Style Recipe Registry**를 만들고 검증할 Recipe를 추가한다.
6. **Tools > Super Hero UI > Style Recipes**를 연다.
7. **Preview / Validate**를 실행해 제안된 속성 변경을 모두 검토한 뒤 **Apply Reviewed Changes**를 선택한다.
8. Preview를 다시 실행한다. 변경이나 오류가 없으면 `Ready`이며 같은 값을 다시 Apply해도 변경이 생기지 않는다.

변경 가능한 token, style, Recipe, registry는 `Assets/Editor/SuperHeroUI/` 같은 프로젝트 전용 제작 폴더에 보관한다. 대상 Prefab은 프로젝트의 일반 runtime 자산 계층에 둔다. 제작 자산을 runtime Resources, Addressables, AssetBundles에 넣지 않는다.

bake는 패키지 소유 컴포넌트나 Recipe, style, token 참조를 추가하지 않는다. 기존 Unity 및 TextMesh Pro 컴포넌트의 지원 속성만 기록한다. 각 소비 프로젝트의 build pipeline에서 Player Build 검증을 완료해야 한다.

registry를 만들면 패키지 검색 대상이 된다. 새 registry는 Play 및 Build 검증이 기본으로 활성화되며 기존 UI를 이전하는 동안 각각 비활성화할 수 있다. Consumer override 검사는 Recipe의 `Consumer Prefabs` 목록에 명시적으로 지정한 Prefab만 확인한다.

## 복합 컨트롤

가져올 수 있는 **Composite Control Recipes** sample에는 입력 필드, 드롭다운, 탭, 테이블, 팝업, 배지를 primitive로 구성하는 방법이 정리되어 있다. 제품 UI Prefab이나 runtime script를 설치하지 않는다.

## 설치

embedded 개발에서는 이 폴더를 `Packages/com.superherounite.ui`에 둔다. 독립 source 저장소는 [superherounite/com.superherounite.ui](https://github.com/superherounite/com.superherounite.ui)이며 `package.json`이 저장소 root에 있다. 이 저장소에서 변경되지 않는 Semantic Version tag를 발행한다.

저장소와 tag를 만든 뒤 소비 프로젝트의 `Packages/manifest.json`에 Git dependency를 추가한다.

```json
{
  "dependencies": {
    "com.superherounite.ui": "ssh://git@github.com/superherounite/com.superherounite.ui.git#v0.1.0-preview.1"
  }
}
```

소비 프로젝트와 같은 상위 폴더 아래에 독립 package 저장소를 함께 checkout했다면 `Packages/manifest.json` 기준 상대 경로를 사용할 수 있다.

```json
"com.superherounite.ui": "file:../../com.superherounite.ui"
```

monorepo의 package 하위 폴더를 임시로 참조할 때는 revision 앞에 package 경로를 쓴다.

```json
"com.superherounite.ui": "https://<git-host>/<organization>/<repository>.git?path=/Packages/com.superherounite.ui#<immutable-tag>"
```

소비 프로젝트에서는 `Packages/manifest.json`과 `Packages/packages-lock.json`을 함께 커밋한다. 운영 dependency에 변경되는 branch를 사용하거나 URL에 credential을 넣지 않는다. 패키지를 독립 저장소로 옮길 때 모든 `.meta` 파일을 보존한다.

Git으로 설치한 패키지의 테스트를 실행하려면 소비 또는 CI 프로젝트에 Unity Test Framework가 있어야 하고 manifest의 `testables`에 `"com.superherounite.ui"`를 추가해야 한다. Embedded package 테스트는 별도 설정 없이 검색된다.

업데이트를 배포할 때는 package version과 changelog를 함께 변경하고 기존 `.meta` GUID를 유지한 채 커밋한다. 새 immutable version tag를 만든 뒤 소비 프로젝트의 `#tag` 참조를 갱신하고 새 lock file을 함께 커밋한다. tag를 발행하기 전 작은 임시 Unity 프로젝트나 저장소의 `TestProject~`에서 package 테스트를 실행한다.

외부 배포 전 immutable release tag와 회사가 승인한 license가 필요하다. `Library/PackageCache` 아래의 사본은 직접 수정하지 않는다.

전체 계약은 [한국어 상세 가이드](Documentation~/index.ko.md), 구성 예시는 [한국어 Composite Control Recipes](Samples~/Composite%20Control%20Recipes/README.ko.md)에서 확인할 수 있다.
