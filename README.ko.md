# Super Hero UI

[English](README.md) | 한국어

Super Hero UI는 Unity 6000.0용 Editor 전용 스타일 제작 패키지다. ScriptableObject 자산으로 시각 규칙을 공유하고, 적용 영향을 미리 보여준 뒤 승인된 값만 일반 uGUI Prefab에 굽는다. 패키지 ID는 `com.superherounite.ui`이며 현재 개발 버전은 `0.1.0-preview.5`이다.

이 패키지는 복합 컨트롤보다 낮은 계층까지만 책임진다. 입력 필드, 드롭다운, 탭, 테이블, 팝업, 배지는 보통 제품별 계층 구조, 상호작용, 접근성, 레이아웃, 데이터 동작을 포함한다. 각 프로젝트는 패키지가 정한 Prefab 계약을 상속하는 대신 재사용 가능한 시각 primitive로 이런 컨트롤을 구성한다.

## 제공 기능

- 코드를 다시 컴파일하지 않고 색상을 추가할 수 있는 enum 없는 `ColorToken` 자산
- 타입이 명확한 `ImageStyle`, `SurfaceStyle`, `TextStyle`, `SelectableStyle` 자산
- Prefab Mode에서 owner Prefab 컴포넌트를 안정적으로 캡처하는 기능
- 자산 이름에 의존하지 않고 선택한 Prefab이나 자식의 Inspector에서 Recipe로 바로 이동하는 링크
- 검토한 dependency fingerprint와 연결된 읽기 전용 Preview 및 명시적 Apply 단계
- 선택한 실행 중 component의 color·소유 PPUM 튜닝, draft 보관 및 검토 후 원본 asset 기록
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

기존 UI를 다시 편집할 때는 Prefab이나 자식을 선택하고 Inspector 헤더의
**Style Recipes**에서 Recipe를 누른다. UI 선택을 유지한 채 별도 Inspector로
열린다. 자세한 내용은 [Prefab에서 Recipe 찾기](Documentation~/index.ko.md#prefab에서-recipe-찾기)를 참고한다.

실행 중 시각 값을 조절하려면 **Tools > Super Hero UI > Play Mode Tuning**을 연다.
Recipe target과 실행 중 component를 선택하고 color 또는 PPUM을 조절한 뒤 Play를
종료한다. 보관된 draft를 검토해 원본 asset에 기록하고 기존 Preview/Apply로
Prefab을 bake한다. Draft는 현재 Editor 세션 동안 보관된다.
자세한 내용은 [튜닝 가이드](Documentation~/index.ko.md#play-mode-튜닝)를 참고한다.

변경 가능한 token, style, Recipe, registry는 `Assets/Editor/SuperHeroUI/` 같은 프로젝트 전용 제작 폴더에 보관한다. 대상 Prefab은 프로젝트의 일반 runtime 자산 계층에 둔다. 제작 자산을 runtime Resources, Addressables, AssetBundles에 넣지 않는다.

bake는 패키지 소유 컴포넌트나 Recipe, style, token 참조를 추가하지 않는다. 기존 Unity 및 TextMesh Pro 컴포넌트의 지원 속성만 기록한다. 각 소비 프로젝트의 build pipeline에서 Player Build 검증을 완료해야 한다.

registry를 만들면 패키지 검색 대상이 된다. 새 registry는 Play 및 Build 검증이 기본으로 활성화되며 기존 UI를 이전하는 동안 각각 비활성화할 수 있다. Consumer override 검사는 Recipe의 `Consumer Prefabs` 목록에 명시적으로 지정한 Prefab만 확인한다.

## 복합 컨트롤

가져올 수 있는 **Composite Control Recipes** sample에는 입력 필드, 드롭다운, 탭, 테이블, 팝업, 배지를 primitive로 구성하는 방법이 정리되어 있다. 제품 UI Prefab이나 runtime script를 설치하지 않는다.

## 설치

Unity Package Manager에서 버전을 선택하고 업데이트하려면 OpenUPM scoped
registry를 사용한다. `0.1.0-preview.5`는
[OpenUPM](https://openupm.com/packages/com.superherounite.ui/)에서 설치할 수 있다.

1. **Edit > Project Settings > Package Manager**에서 scoped registry를 추가한다.
   **Name**은 `OpenUPM`, **URL**은 `https://package.openupm.com`,
   **Scope(s)**는 `com.superherounite.ui`로 입력하고 적용한다.
2. 이 preview release를 표시하도록 **Show Pre-release Package Versions**를 켠다.
3. **Window > Package Manager**에서 **+ > Add package by name**을 선택한다.
   이름 `com.superherounite.ui`, 버전 `0.1.0-preview.5`를 입력하고 설치한다.
   기존 Git 설치에서는 Git dependency가 registry 버전으로 교체된다.
   이 설치 source 전환은 한 번만 하면 된다.
4. 이후에는 **In Project** 또는 **My Registries**에서 **Super Hero UI**를
   선택하고 **Version History**에서 원하는 버전의 **Update**를 누른다.
   Git URL을 수정할 필요가 없다.

이에 해당하는 `Packages/manifest.json` 설정은 다음과 같다. 기존 dependency와
registry를 유지하면서 해당 항목을 합친다.

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

Unity가 package를 resolve하고 `Packages/packages-lock.json`을 갱신하면
manifest와 함께 커밋한다. `Packages/com.superherounite.ui`에 embedded package가
있으면 우선 적용되므로, 로컬 변경을 보존한 뒤 해당 사본을 제거하고 registry
package로 전환한다. [OpenUPM 설정](https://openupm.com/docs/getting-started.html)과
[Unity 버전 업데이트 절차](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-ui-update.html)를 참고한다.

대체 방법으로 독립 [source 저장소](https://github.com/superherounite/com.superherounite.ui)에서
Git으로 설치할 수 있다. `package.json`은 저장소 root에 있다.

```json
"com.superherounite.ui": "https://github.com/superherounite/com.superherounite.ui.git#v0.1.0-preview.5"
```

이 Git URL은 immutable tag에 고정된다. 다른 release로 업데이트하려면 tag를
변경해야 하며, Git의 **Update**는 새로운 버전 tag를 선택하지 않는다.
Embedded 개발에서는 이 폴더를 `Packages/com.superherounite.ui`에 둔다.

소비 프로젝트와 같은 상위 폴더 아래에 독립 package 저장소를 함께 checkout했다면 `Packages/manifest.json` 기준 상대 경로를 사용할 수 있다.

```json
"com.superherounite.ui": "file:../../com.superherounite.ui"
```

monorepo의 package 하위 폴더를 임시로 참조할 때는 revision 앞에 package 경로를 쓴다.

```json
"com.superherounite.ui": "https://<git-host>/<organization>/<repository>.git?path=/Packages/com.superherounite.ui#<immutable-tag>"
```

소비 프로젝트에서는 `Packages/manifest.json`과 `Packages/packages-lock.json`을 함께 커밋한다. 운영 dependency에 변경되는 branch를 사용하거나 URL에 credential을 넣지 않는다. 패키지를 독립 저장소로 옮길 때 모든 `.meta` 파일을 보존한다.

Registry 또는 Git으로 설치한 패키지의 테스트를 실행하려면 소비 또는 CI 프로젝트에 Unity Test Framework가 있어야 하고 manifest의 `testables`에 `"com.superherounite.ui"`를 추가해야 한다. Embedded package 테스트는 별도 설정 없이 검색된다.

재현 가능한 Editor 테스트, 혼합 작업, 기준 성능 비교 방법은 [검증 도구 가이드](Tools~/README.ko.md)를 참고한다.

업데이트를 배포할 때는 package version과 changelog를 함께 변경하고 기존 `.meta` GUID를 유지한 채 커밋한 다음 새 immutable version tag를 만든다. OpenUPM이 이 저장소의 version tag를 빌드해 게시하므로 새 registry 버전의 설치 가능 여부를 확인한 뒤 배포를 알린다. 소비 프로젝트는 Package Manager의 **Update**를 사용하고, Git 설치를 유지하는 프로젝트는 `#tag` 참조를 갱신한다. 갱신된 manifest와 lock file을 함께 커밋한다. tag를 발행하기 전 작은 임시 Unity 프로젝트나 저장소의 `TestProject~`에서 package 테스트를 실행한다.

이 패키지는 [MIT License](LICENSE)로 배포한다. 외부 배포 전 immutable release tag를 생성한다. `Library/PackageCache` 아래의 사본은 직접 수정하지 않는다.

전체 계약은 [한국어 상세 가이드](Documentation~/index.ko.md), 구성 예시는 [한국어 Composite Control Recipes](Samples~/Composite%20Control%20Recipes/README.ko.md)에서 확인할 수 있다. 스크린샷과 함께 실제 소비 프로젝트 화면으로 따라 하는 실습 가이드는 [한국어 사용 가이드](Documentation~/walkthrough.ko.md)에 있다.
