# 변경 이력

[English](CHANGELOG.md) | 한국어

Super Hero UI의 주요 변경 사항을 기록한다. 이 패키지는 Semantic Versioning을 따른다.

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
