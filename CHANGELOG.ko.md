# 변경 이력

[English](CHANGELOG.md) | 한국어

Super Hero UI의 주요 변경 사항을 기록한다. 이 패키지는 Semantic Versioning을 따른다.

## [Unreleased]

### 추가

- 명시적으로 등록한 Prefab의 의도하지 않은 Recipe 소유 consumer override를
  위한 별도 읽기 전용 복구 Preview와 검토 후 Apply. Unmanaged 값과 유효한 typed
  Variant specialization을 보존하고 일반 style Preview/Apply 절차로 돌아간다.

### 변경

- Unity artifact dependency version이 같으면 저장된 asset별 dependency hash를
  재사용하고, 같은 Editor 세션의 domain reload 뒤에도 version이 일치할 때
  이어서 사용. 정확한 현재 제작 상태와 callback 검사는 유지하고, 완료된 review의
  fingerprint를 중복 계산 없이 기록. Build guard의 전체 Preview는 계속 유지.
- 로드된 consumer별 검사에서 반복되는 Recipe, specialization chain, target
  source chain 조회를 재사용. Unload 이후에는 결과를 보관하지 않으며 callback
  검사와 Build 전체 검증은 유지.

### 수정

- 누락된 `Base Recipe` 관계를 실제 Variant 상위 Prefab 중 가장 가까운 등록
  Recipe로 자동 해석하며 여러 단계의 specialization도 지원. 명시적 지정과
  정확한 typed 소유권 검사를 유지하고 제작 asset은 수정하지 않음.
- 명시적으로 등록한 consumer에서 지정 owner가 사라지면 그 consumer에 실제로
  존재하는 등록 owner instance를 모두 검사. 하나도 없으면 수정 가능한 오류를
  유지. Preview, Apply, Play, Build가 같은 현재 관계를 따르며 이름 추측,
  미등록 consumer 검색, asset 기록은 하지 않음. 현재 dependency로 Variant 상속
  관계를 갱신하고 대체 consumer owner는 매번 다시 검사.
- 검토 상세와 Registered Recipes에 높이가 제한된 독립 스크롤 영역을 제공하고,
  작게 dock한 창에서도 조작부에 접근할 수 있도록 창 전체 스크롤 추가.

## [0.1.0-preview.5] - 2026-09-12

### 추가

- 명시적으로 선택한 실행 중 UI component용 Editor 전용 Play Mode Tuning:
  Graphic, Image, Surface, TMP text, Selectable 상태 color 및 Style이 소유한
  Image·Surface pixels-per-unit multiplier 조절.
- Play 종료, domain reload, 창 닫기 이후에도 Editor 세션 동안 유지되는 튜닝
  draft. 임시 실행 값 복구와 Edit Mode 검토를 거친 ColorToken·ImageStyle 원본
  기록 및 Undo 지원.
- 공유 원본 사용처 검토, 상충 draft와 원본 변경 검사 및 기존 검토 기반 Prefab
  bake로 이어지는 별도 적용 절차.
- 제작 가이드와 AI Agent 가이드의 영문·한국어 튜닝 절차.

### 검증

- Unity `6000.0.68f1`에서 graphics를 활성화한 Editor 테스트 121개 전체 통과.
  튜닝 22개 case는 실행 값 변경, reload, 복구, 원본 기록 검토와 Undo,
  Prefab identity 및 기존 bake 계약을 검증한다.
- 실제 소비 프로젝트 테스트를 위한 prerelease다. 프로젝트별 화면, 상호작용,
  localization 및 Player Build 검증은 별도로 수행한다.

## [0.1.0-preview.4] - 2026-09-11

### 추가

- Prefab과 자식 오브젝트 Inspector의 Recipe 바로가기 및 Project·Hierarchy
  우클릭 메뉴. 실제 Prefab 참조를 사용하고 owner, Variant 원본, 등록 consumer를
  구분하며, 선택한 UI 오브젝트를 유지한 채 별도 Inspector로 연다.
- 격리된 Editor 테스트, owner 1,000개까지의 혼합 작업, 기준 구현과의 성능 비교를
  재현하는 도구 및 영문·한국어 검증 가이드.

### 변경

- Preview가 변경 없는 owner·consumer 검사 결과를 재사용한다. Apply는 변경된
  owner와 이에 의존하는 등록 Prefab을 의존 순서대로 처리한다. 전체 검증은 계속
  사용할 수 있으며 Build guard는 항상 새 전체 Preview를 실행한다.
- 공유 target index, 작업 단위 dependency 조회와 native 직렬화 상태 digest로
  대규모 registry와 TMP font 자산의 반복 처리를 줄였다. Recipe 65개인 소비 프로젝트의
  폰트 크기 Apply 중앙값이 15.76초에서 5.14초로 줄었다. 측정 조건은 성능 가이드를 참고한다.

### 수정

- Cache를 사용하면서도 전체 registry 승인 검사, 미저장 상태 감지, 등록 consumer
  검증, unmanaged 값 보존과 Apply 멱등성을 유지한다.
- 사용자 정의 Editor callback과 직렬화 callback은 새로 검사하여 Preview·Play
  Ready 결과 재사용으로 callback의 변경이 가려지지 않도록 한다.

## [0.1.0-preview.3] - 미출시

### 추가

- Public 배포를 위한 MIT License

### 수정

- 관리 owner의 source chain을 단순히 통과하는 중첩 Prefab instance root를
  consumer 검증 대상에서 제외했다. 복합 owner는 가장 바깥 instance에서 한 번만
  검증되므로 owner 자체 target이 잘못된 중복 instance 오류 없이 해석된다.

## [0.1.0-preview.2] - 미출시

### 추가

- Prefab Variant Recipe가 base Recipe를 명시하는 `Base Recipe` 계약. Variant는 같은 registry에 base와 함께 등록되고, 동일하게 capture한 target 및 property를 typed binding으로 소유할 때만 base의 관리 property를 전문화할 수 있다.

### 변경

- consumer 검사는 관리되지 않거나 선언되지 않은 override를 계속 오류로 처리한다. 위의 명시적 typed specialization만 허용한다.

## [0.1.0-preview.1] - 미출시

### 추가

- runtime assembly가 없는 Unity 6000.0용 Editor 전용 `SuperHeroUnite.UI.Editor` assembly
- 프로젝트가 소유하는 token과 style 자산을 만들기 위한 ScriptableObject type 및 Graphic Color, Image, Surface fill/outline, TextMesh Pro text, Selectable Color Tint 상태용 typed binding
- Unity `GlobalObjectId` metadata를 사용하는 안정적인 Prefab Mode target capture
- 읽기 전용 Preview, dependency fingerprint 승인, 명시적 Apply, 멱등적인 Ready 판정
- 누락된 target과 script, registry 내부 중복 소유권, nested owner 오용, Prefab Variant chain을 포함해 명시적으로 등록한 consumer override, 미저장 tracked Prefab Stage 검증
- Editor 전용 Ready cache를 사용하는 설정 가능한 Play Mode 및 Player Build 준비 상태 guard
- 입력 필드, 드롭다운, 탭, 테이블, 팝업, 배지, 동적 목록을 위한 복합 컨트롤 구성 참고 자료
- README, 제작 가이드, 변경 이력, 복합 컨트롤 참고 자료의 영문판과 한국어판
- 별도 맥락 없이 package 규칙을 찾을 수 있는 host-project bootstrap과 영문 및 한국어 AI Agent 지침
- Preview 무변경, Sprite 소유권을 포함한 다섯 primitive category, unmanaged 값 보존, Apply 멱등성, stale approval 거부, 중복 소유권, 직접 및 중간 Variant consumer override, Variant owner baseline, 잘못된 image parameter, 누락 target을 검증하는 Editor 테스트

### 배포 전 조건

- 독립 Git 저장소에서 immutable version tag 생성 및 검증
- 깨끗한 소비 프로젝트에서 release Git URL로 설치
- 실제 자산과 build 설정을 사용하는 소비 프로젝트 Player Build 검증
