# Preview와 Apply 성능

[English](performance.md) | 한국어

최신 실제 프로젝트 검증에서 Recipe 65개의 registry에 속한 폰트 크기 하나를
변경했을 때 Apply 중앙값은 15,758.36 ms에서 5,139.55 ms로 67.39% 줄었다.
릴리스 후보는 Recipe 탐색과 직렬화 callback 정책 검사를 포함한 Editor 테스트
99개를 통과했다. 성능 구현은 owner 1,000개 혼합 case도 통과했다.
아래 native digest와 의존성 계획 절에 이 검증을 기록했으며, 이전 측정은
별도로 보존했다.

2026-09-11에 실행한 격리 벤치마크에서 owner 100개 중 token 하나를 변경했을 때
Preview 중앙값은 4,514.41 ms에서 219.01 ms로, Apply 중앙값은 12,824.67 ms에서
530.55 ms로 줄었다. 증분 구현은 이 변경에 대해 owner 하나와 공유 consumer 하나를
검사했다. 아래 수치는 명시한 fixture의 측정 결과이며, 소비 프로젝트의 정확한
처리 시간을 보장하지 않는다.

## 작업 구성과 비교 방식

Unity `6000.0.68f1`과 Unity Test Framework를 사용하는 Windows 임시 프로젝트에서
다음 구성을 측정했다.

- registry 하나에 owner Prefab 100개와 Recipe 100개
- owner마다 기존 `Image` component 네 개와 Image binding 네 개
- owner마다 서로 다른 `ColorToken` 하나와 `ImageStyle` 하나를 네 binding이 공유:
  총 token 100개, style 100개, binding 400개
- owner 100개를 각각 한 번씩 중첩한 화면 Prefab 하나를 모든 Recipe의 consumer로 등록

기준 구현은 commit `7ede1ed6229c7b03b4d3adfddc42a283d6ef63b0`의
`StyleRecipeProcessor.cs`와 `PrefabTargetResolver.cs`를 사용했다. 임시 프로젝트의
복사본에서는 class 이름과 해당 이름의 참조만 `LegacyStyleRecipeProcessor`와
`LegacyPrefabTargetResolver`로 바꿨다. 제작 자산과 review type은 증분 구현과
동일한 package type을 사용했다. 이 legacy 복사본은 패키지에 추가하지 않았다.

두 구현은 동일한 fixture, 경로, target identity, asset GUID를 사용했다. 기준
구현을 먼저 실행했다. 증분 구현을 실행하기 전에는 원래 owner Prefab bytes와
token 값을 복원하고, 복원한 Prefab을 import한 뒤 증분 Preview cache를 비웠다.
Fixture 생성, 복원, 복원을 위한 import, assertion은 측정 시간에서 제외했다.

최초 상태에서는 image color 400개 모두 style과 달랐다. 최초 Preview와 Apply가
`Ready`에 도달한 뒤 변경 없는 Preview를 세 번 측정했다. 이후 두 번째 owner의
token만 magenta, cyan, yellow로 차례로 바꾸며 각 변경의 Preview와 reviewed
Apply를 따로 측정했다. 매번 owner 하나의 binding 네 개가 바뀌었다. 이 측정 중
token 수정은 메모리에만 유지했다.

## 측정 결과

단위는 밀리초다. 배율은 기준 구현 시간을 증분 구현 시간으로 나눈 값이다.
최초 작업은 단일 측정이며, 나머지는 세 번 측정한 중앙값이다. 테스트는 정확성과
작업 횟수를 검증하며, 작업별 시간은 진단 자료로 사용한다. 별도의 테스트 실행
시간 제한은 [검증 가이드](../Tools~/README.ko.md)에 설명되어 있다.
측정한 증분 구현에는 SHA-256 직렬화 상태 digest가
포함되어 있다. 이 표는 해당 중간 구현의 기록이며, 이후 native digest 측정은
아래에서 별도로 설명한다.

| 작업 | 표본 | 기준 구현 | 증분 구현 | 개선 배율 |
| --- | --- | ---: | ---: | ---: |
| 최초 Preview, 모든 owner가 Stale | 1회 | 4,580.32 | 483.89 | 9.47× |
| 최초 Apply, 모든 owner가 Stale | 1회 | 26,314.61 | 9,462.29 | 2.78× |
| `Ready`에서 변경 없는 Preview | 3회 중앙값 | 4,570.62 | 160.19 | 28.53× |
| token 하나 변경 후 Preview | 3회 중앙값 | 4,514.41 | 219.01 | 20.61× |
| token 하나 변경 후 Apply | 3회 중앙값 | 12,824.67 | 530.55 | 24.17× |

증분 구현의 변경 없는 Preview는 owner나 consumer Prefab을 로드하지 않았다.
token 하나를 수정한 Preview는 owner 하나와 consumer 하나를 로드했다. 해당
Apply는 쓰기 단계에서 owner 하나를 처리했고, 마지막 Preview는 owner 하나와
consumer 하나를 로드한 뒤 `Ready`를 반환했다.

최초 Apply는 여전히 owner 100개 모두를 쓰고, 각 owner 직후 최신 등록 consumer를
즉시 검증한다. 이는 asset callback과 Prefab 의존 관계의 전파로 생긴 변경을
검사하기 위한 동작이다. 최초 Apply의 개선 폭이 token 하나를 수정했을 때보다
작은 이유다.

변경 없는 Preview도 dependency key와 승인 fingerprint를 계산하기 위해 등록된
전체 입력을 확인한다. 별도로 세 번 측정한 중앙값은 `GetDependencyFingerprint`가
65.45 ms, `StyleInspectionSnapshot`을 구성하고 모든 owner와 consumer key를
요청하는 작업이 74.02 ms였다. 이 개별 측정은 남아 있는 입력 확인 비용을 보여주며,
합산해서 Preview의 단계별 시간으로 해석할 수는 없다. 따라서 Prefab을 로드하지
않는 warm Preview도 등록된 입력 수에 비례하는 비용이 남는다.

Cache에는 Editor 세션의 일반 데이터만 들어간다. 로드한 Prefab 내용은 검사가
끝나면 해제하며, 패키지는 runtime cache나 runtime component를 추가하지 않는다.

`ExecuteAlways`, `ExecuteInEditMode`, `OnValidate`가 있는 custom script나
`runInEditMode`가 활성화된 custom behaviour는 cache 결과를 재사용하지 않고 새로
검사해야 한다. 이 처리는 owner와 consumer에 모두 적용한다. Built-in uGUI와 TMP
component는 cache를 사용할 수 있으며, 이 벤치마크에는 built-in uGUI component만
사용했다.
Play guard도 이런 custom Editor callback이 있는 Registry를 Ready cache에서
제외한다. 저장소의 `TestProject~/Assets/CallbackProbe` runtime component fixture는
`ExecuteAlways.OnEnable`이 읽는 static 상태를 바꿔 dependency fingerprint 변경
없이 전체 검사 결과가 달라지는 상황을 재현한다. 수정된 owner·consumer 검사에서는
callback을 다시 실행하고 전체 검사와 결과가 일치하며, Play Ready cache 파일도
기록하지 않는다. 이 component들은 격리된 테스트 host에서만 사용한다.

## 정확성 검증

이전 저장소 runner의 `Tests` 실행에서 package Editor test 70개와 테스트 host에
보존한 runtime component callback test 2개, 총 72개가 통과했다. Owner 100개
기준 구현 비교도 직렬화 상태 digest를 포함한 구현으로 선택한 case가 통과했다.
아래 혼합 작업 검증은 별도 실행이다.

벤치마크는 최초 review와 각 token 변경의 review·Apply 결과에서 기준 구현과
증분 구현의 `State`, `Assets`, `Changes`, `Errors`를 비교했다. 다음 항목도
확인했다.

- Preview가 asset bytes와 수정 시각을 보존함
- 각 Apply가 `Ready`로 끝남
- token 하나의 Apply가 해당 owner 파일만 변경하고 공유 consumer를 포함한 다른
  모든 fixture 파일을 보존함
- 중첩 image가 예상 색상을 상속하면서 unmanaged raycast 설정과 layout을 유지함
- `Ready`에서 Apply를 반복해도 파일을 쓰지 않음
- 증분 작업이 위에 기록한 Prefab 로드 횟수를 만족함

컴파일된 module identity가 다르므로 두 구현 사이의 승인 fingerprint는 비교하지
않았다. 각 Apply에는 동일 구현에서 만든 review를 사용했다.

## 측정 재현 방법

저장소 root에서 PowerShell 7과 설치된 Unity `6000.0.68f1` Editor를 사용해
[검증 runner](../Tools~/Validate-Package.ps1)를 실행한다.

```powershell
$unityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.0.68f1/Editor/Unity.exe'
pwsh -File Tools~/Validate-Package.ps1 -EditorPath $unityEditor -Mode Tests
pwsh -File Tools~/Validate-Package.ps1 -EditorPath $unityEditor -Mode Comparison -OwnerCount 100
pwsh -File Tools~/Validate-Package.ps1 -EditorPath $unityEditor -Mode Scale -OwnerCount 300
pwsh -File Tools~/Validate-Package.ps1 -EditorPath $unityEditor -Mode Scale -OwnerCount 1000
```

Runner는 저장소의 `TestProject~` template으로 패키지 바깥에 격리 프로젝트를
만들고, 로컬 패키지를 참조하며, test assembly를 활성화하고 TMP Essential
Resources를 준비한다. `Tests`는 별도 선택이 필요한 `Scale`과
`PerformanceComparison` category를 제외한다. Callback 회귀 fixture는 이 일반
테스트 실행에 포함된다. Runtime test assembly는 테스트 host의 asset이며,
Editor-only 패키지에 production runtime assembly를 추가하지 않는다.

`Comparison`은 위 commit에서 기준 source 두 개를 읽고 이름을 바꾼 복사본을
격리 host에 만든다. 해당 commit이 로컬 Git history에 있어야 한다. 비교 harness는
저장소의 `TestProject~/Comparison`에 보존되어 있으며, 두 구현 사이에 동일한
asset을 복원하고
`StyleRecipePerformanceComparisonTests.CompareLegacyAndCurrentOnIdenticalAssets(100)`을
실행한다. Owner 200개나 300개도 선택할 수 있다. 위 owner 100개 표는 통과한
`20260911-034830-bf8252-Comparison-100.xml` 실행과 해당 실행의
`legacy-vs-current-100-owners.json` 측정값을 기록한 것이다.

`Scale`은 요청한 owner 300개 또는 1,000개 혼합 작업 case 하나만 선택하며
legacy 구현은 실행하지 않는다. Runner는 process 종료 코드와 NUnit XML을
확인해 실패하거나 선택된 테스트가 없는 실행을 거부한다. Host의 `Results`
폴더에 log, NUnit XML, JSON summary를 기록한다. Owner 100개 비교에는
`legacy-vs-current-100-owners.json`도 기록한다. 기본 host 위치는
`%TEMP%/SuperHeroUIValidation/<source-id>/<mode>`이며 runner가 정확한 경로를
출력한다. `-ProjectDirectory`로 다른 격리 host를 지정할 수 있고,
`-PrepareOnly`는 검증을 실행하지 않고 host만 준비한다.

## 혼합 작업의 대규모 검증

2026-09-11의 별도 `Scale` 실행에서 owner 300개 case 하나와 1,000개 case 하나가
직렬화 상태 digest를 적용한 구현으로 각각 통과했다. 모든 owner에는 Image
binding과 개별 token, style, Recipe가 있다.
10%에는 TMP text binding도 있고, 10%는 Variant이며, 10%에는 runtime-only custom
script, 1%에는 `ExecuteAlways` script가 있다. Callback owner는 작은 case에
3개, 큰 case에 10개다. 공유 화면 consumer 네 개와 명시적으로 등록한 Variant
consumer를 포함한다. 모든 owner를 처음부터 `Ready`로 만들어 setup에서 전체
Apply를 실행하지 않는다.

아래 시간은 processor 호출만 한 번씩 측정한 wall-clock 밀리초다. Fixture 생성,
파일 검사, 따로 실행한 전체 비교 검사는 제외했다. 앞의 owner 100개 Image-only
벤치마크와 작업 구성이 다르므로 두 표를 개선 배율이나 규모 증가 비율로 직접
비교하면 안 된다. 시간은 해당 실행의 관측값이며, 테스트는 정확성과 작업 횟수를
검증한다.

| 작업 | owner 300개: ms | owner / consumer 로드 | owner 1,000개: ms | owner / consumer 로드 |
| --- | ---: | ---: | ---: | ---: |
| `Ready`에서 cold Preview | 1,815.3 | 300 / 34 | 5,446.7 | 1,000 / 104 |
| `Ready`에서 warm Preview | 550.1 | 3 / 17 | 1,738.2 | 10 / 52 |
| token 하나 변경 후 Preview | 576.7 | 4 / 20 | 1,924.7 | 11 / 55 |
| token 하나 변경 후 Apply | 2,038.6 | 5 / 34 | 6,485.4 | 12 / 104 |
| `Ready`에서 반복 Apply | 551.1 | 3 / 17 | 2,037.8 | 10 / 52 |

Apply 행의 로드 횟수는 반환된 마지막 Preview의 값이며, Apply 내부의 전체 작업을
합산한 값이 아니다. 수정하는 token은 Variant의 base owner에 속한다. 처음에는
child가 상속을 통해 자신의 제안 값과 일치하여 적용할 변경이 없다. 그래도 Apply는
의존 순서에 따라 두 owner를 포함하고, 정확히 해당 Prefab 파일 두 개만 쓰며,
child 고유 색상을 보존한다. 반복 Apply는 파일을 하나도 쓰지 않는다.

Warm Preview는 callback owner 3개 또는 10개만 다시 연다. Runtime-only custom
script는 cache를 재사용할 수 있다. Callback script를 포함한 consumer도 새로
검사해야 한다. Consumer 결과를 Recipe 단위로 cache하므로, 재사용할 수 없는
consumer가 있는 Recipe는 그 밖의 등록 consumer도 다시 검사한다. 예상 로드
횟수는 해당 경로들의 합집합으로 계산한다. 따라서 warm consumer 로드 수가 공유
화면 개수보다 많다. 이는 정확성을 위한 재검사이며, 모든 대규모 작업이 warmup
뒤에 Prefab을 전혀 로드하지 않는다는 의미는 아니다.

각 case는 최초 Preview, token 수정, Apply 직후의 `State`, `Assets`, `Changes`,
`Errors`, `Fingerprint`를 `PreviewFull`과 비교한다. 정확한 owner·consumer 작업
횟수, asset bytes와 수정 시각, 제작 자산·consumer 파일 보존, unmanaged
Image/layout/TMP와 runtime script 값, 반복 Apply의 멱등성도 검증한다. 기록된 XML은
`20260911-034446-7da747-Scale-300.xml`과
`20260911-034550-b58bc0-Scale-1000.xml`이며 각각 테스트 case 하나가 통과했다.

## 공유 ScriptableObject의 대용량 JSON

2026-09-11에 CJK font asset이 있는 실제 소비 프로젝트를 별도로 프로파일링하여
다른 병목을 확인했다. 해당 registry에는 Recipe 65개가 있었다. 두 번째 실행은
동일한 입력 내용에서 직렬화 상태 digest 변경을 측정했다. 두 실행의 입력 파일
1,736개는 최초 내용 해시가 같았고, 각 실행은 모든 파일을 보존했다. 두 실행
모두 `Ready`를 반환하고 warmup과 warm review 결과가 일치했다. Warm Preview는
두 실행에서 모두 owner 13개와 consumer 20개를 검사하여 custom callback에 따른
재검사를 유지했다.

아래 시간은 각각 한 번 측정한 밀리초 값이다. Warm Preview의 4.44배 개선은
최초 release가 아니라 digest 변경 직전의 증분 구현 대비다. 앞의 두 합성 작업
표와는 작업 구성이 다르다. 따로 측정한 fingerprint와 snapshot 시간을 합산해
Preview의 단계별 시간으로 해석할 수는 없다.

| 작업 | digest 적용 전 | digest 적용 후 | 개선 배율 |
| --- | ---: | ---: | ---: |
| `GetDependencyFingerprint` | 8,616.004 | 1,523.148 | 5.66× |
| Snapshot 생성 및 전체 owner / consumer key | 8,113.960 | 1,562.267 | 5.19× |
| `Ready`에서 warm Preview | 17,320.414 | 3,899.786 | 4.44× |

기존 operation 내부 cache는 JSON 직렬화 중복을 막았지만 JSON 원문을 저장했다.
각 Recipe가 같은 자산을 참조할 때마다 원문을 다시 붙였기 때문에 공유하는
대용량 TMP font asset이 최종 해시에 전달되는 buffer를 반복해서 키웠다.
Snapshot cache에는 객체 483개의 JSON 약 1,730만 글자가 들어 있었다. 별도로
서로 다른 ScriptableObject 549개를 한 번씩 직렬화한 시간은 약 327 ms였다.
참조 횟수로 계산한 JSON 추가량은 registry fingerprint와 snapshot owner key
계산에서 각각 약 1억 1,370만 글자였다. 이 추정치는 경로, dependency hash,
구분자를 제외하며, 별도 직렬화 측정은 합산 가능한 단계별 시간이 아니다.
공유 JSON을 반복 복사하고 해시하는 작업이 Prefab 검사 cache 적용 후에도
남은 큰 비용의 원인이었다.

이 단계의 `StyleDependencyFingerprint`는 각 ScriptableObject의 현재 메모리 상태를
직렬화하고 helper instance마다 SHA-256 digest를 한 번 계산했다. 반복 참조는
짧은 digest를 추가한다. `StyleInspectionSnapshot`도 제작 의존 자산과 Recipe
상태에 같은 helper를 사용한다. 해당 key의 asset 경로와 재귀 dependency hash는
유지한다. Recipe의 재귀 dependency hash는 등록 consumer까지 따라가므로
owner key에서는 계속 제외한다.

Snapshot에 cache한 문자열은 객체 483개의 JSON 원문 17,307,353글자에서
객체 548개의 digest 30,688글자로 바뀌었다. 새 cache에는 Recipe 65개도
포함한다. 이 수치는 저장된 문자열 내용의 글자 수이며, process 메모리나
전체 할당량, byte 수를 뜻하지 않는다.

Digest cache는 현재 operation 동안만 유지한다. 최종 승인 fingerprint는 별도의
새 helper를 사용하며, 다음 operation은 현재 메모리 상태를 다시 직렬화한다.
따라서 이미 dirty인 객체를 다시 수정한 경우를 포함해 미저장 변경을 계속
감지한다. 직렬화 cache는 객체 단위이므로 같은 경로를 공유하는 서로 다른
subasset의 상태도 구분한다. 경로 없는 객체와 누락 객체 marker도 유지한다.
Custom callback에 따른 재검사와 Play Ready cache 제한도 그대로 적용한다.

이후 같은 소비 프로젝트의 registry 세 개, 총 Recipe 92개를 읽기 전용으로
검증했다. Cold·warm·full 검사 결과가 일치했고, 모두 review 오류 없이 `Ready`를
반환했으며 확인한 입력 파일 1,972개의 내용이 모두 보존됐다.

## Native 직렬화 상태 digest와 남은 Apply 비용

현재 구현은 각 ScriptableObject의 직렬화 JSON digest에 Unity의 native
`Hash128.Compute`를 사용한다. 대용량 font payload를 managed UTF-8 byte 배열로
복사하고 managed SHA-256으로 처리하는 비용을 줄인다. 각 operation은 현재
메모리 상태를 다시 직렬화하며, 반복 참조는 해당 operation의 digest만 재사용한다.
Prefab 검사 후 승인 fingerprint는 별도의 새 helper로 계산한다. 바깥쪽 dependency
hash와 review hash는 SHA-256을 유지한다. 이 fingerprint는 입력과 검사 결과의
변경을 감지하기 위한 값이며 암호학적 서명이 아니다.

Full 검사는 inspection snapshot을 만들지 않는다. Cache를 사용하는 Preview도
해당 검사 경로가 이미 재사용 불가 상태라면 owner 또는 consumer key를 생략한다.
다른 Recipe의 직접 specialization 의존성에 필요한 key는 계속 계산할 수 있다.
전체 입력의 최종 fingerprint, dirty Prefab Stage 검사, custom callback 재검사,
승인된 Apply 검증은 유지한다.

이 변경 후 fingerprint 집중 테스트 11개가 모두 통과했다. 새 case 네 개는
persistent TMP font의 fallback 목록 직접 수정과 대용량 JSON 끝부분의 세 변형인
한글·emoji, escaped Unicode, ASCII를 검사한다. Fallback 목록 case는 저장된
asset dependency hash와 dirty count가 변하지 않은 상태에서 font 참조를 추가하고
제거한다. 새 fingerprint operation은 수정을 감지하고, 복원하면 원래 결과로
돌아온다. JSON case도 1 MiB의 공통 앞부분 뒤에 있는 변경을 구분하고 복원을
검증한다.

별도의 Apply 전후 비교는 같은 소비 프로젝트의 Recipe 65개 registry에서
`Button 01 Label` TextStyle의 font size를 11에서 12로 변경했다. 기준 구현은
직렬화 상태에 SHA-256 digest를 사용했다. 최종 구현에는 native JSON digest,
불필요한 key 생략, 아래에 설명하는 의존성 계획 변경이 포함됐다. Public `Apply`
호출 시간은 다음과 같으며 단위는 밀리초다.

| Sample | SHA-256 기준 구현 | 최종 구현 |
| --- | ---: | ---: |
| 1 | 16,009.82 | 5,096.35 |
| 2 | 15,758.36 | 5,139.55 |
| 3 | 15,611.06 | 5,263.21 |
| 중앙값 | 15,758.36 | 5,139.55 |

중앙값은 67.39% 줄어 3.066배 빨라졌다. 측정된 5.14초도 사용자가 체감하는
대기 시간이다. 전체 registry의 현재 상태를 다시 직렬화하는 작업과 필요한
custom callback 검사는 각 operation에서 계속 수행한다.

별도로 계측한 Apply mirror는 변경 전 16,274.25 ms, 변경 후 5,819.91 ms였다.
아래 단계 외의 작은 계측 오버헤드도 포함한다. 단계별 시간과 뒤이어 수행한
Ready cache 갱신 시간은 아래와 같다. 각각 한 번 실행한 mirror 값이며,
별도로 측정한 public 호출 중앙값을 분해한 수치가 아니다.

| 단계 | SHA-256 기준 구현: ms | 최종 구현: ms |
| --- | ---: | ---: |
| 승인 재검증 Preview | 4,183.68 | 2,322.99 |
| 영향받는 owner의 의존성 계획 | 6,356.85 | 1,108.42 |
| Preview cache 무효화 | 641.70 | 332.79 |
| Owner 쓰기와 consumer 검사 | 226.13 | 385.07 |
| 최종 Preview | 4,864.09 | 1,668.73 |
| Apply 후 Guard Ready cache 갱신 | 0.45 | 0.30 |

전후 profile 모두 public sample 세 개와 별도 mirror sample이 통과했다.
각 Apply는 `Ready`에 도달하고 `Fingerprint`를 포함한 review가 해당 구현의
full 검사와 일치했다. 영향받는 owner 두 개를 방문했으며 실제 변경 파일은
`Period Button.prefab` 하나였다. 변경 내용은 소유한 `fontSize`와 `fontSizeBase`
값, Unity의 CRLF→LF 정규화뿐이었다. 반복 Apply는 파일을 쓰지 않았다.
각 profile 종료 후 확인한 입력 파일 1,736개를 모두 byte 단위로 복원했다.
원본 소비 프로젝트 checkout은 변경하지 않았다.

같은 owner 경로 65개의 의존성 조회를 추가로 진단한 개별 측정값은 다음과 같다.
이는 조회 방식의 실험 결과이며 Apply 단계별 시간이나 최종 통합 구현의 시간이
아니다.

| 의존성 조회 | ms | 조회 수 |
| --- | ---: | ---: |
| Owner별 native 재귀 조회 | 6,474.96 | 65 |
| Owner별 직접 조회 | 97.51 | 65 |
| 모든 asset type을 따라가는 공유 직접 의존성 순회 | 486.55 | 서로 다른 경로 327개 |

직접 의존성 순회는 native 재귀 결과를 모두 재현하지 못했다. Owner 50개의
closure에서 서로 다른 Editor icon asset 다섯 개에 해당하는 의존성이 누락됐다.
이 fixture에서는 누락된 Prefab 의존성이 없었지만, 직접 순회가 Unity의 재귀
조회와 일반적으로 동등하다는 근거가 되지는 않는다.

따라서 현재 `StyleAssetDependencySnapshot`은 공유 직접 의존성 조회와 그
전이 closure를 후보 경로의 분류에만 사용한다. 직접 closure가 변경된 owner에
도달하는 후보는 native 재귀 조회로 확인한다. 나머지 후보는 한 번의 batch
native 재귀 조회로 검사하며, 직접 조회의 예상과 달리 target이 발견되면 각
후보를 개별 조회한다. 최종 판단은 native 재귀 결과를 따른다. 쓰기 전 영향받는
owner의 순서를 정하는 `GetDependencies(path)`도 native 재귀 결과를 그대로
사용하고 경로별로 cache한다. 이 cache는 첫 Prefab 저장 전의 계획 단계에서만
유지한다.

Cache 무효화는 영향받는 owner를 먼저 지우고, consumer 검사 결과가 아직
cache에 남아 있는 Recipe만 조회한다. Consumer cache가 비어 있는 entry는
다음 Preview에서 이미 검사가 필요하므로, 무효화용 조회를 생략해도 검증을
생략하지 않는다.

앞의 fingerprint 11개와 함께 신규 dependency snapshot 테스트 다섯 개가
모두 통과했다. 직접 조회에 없는 재귀 의존성, 직접 조회의 잘못된 양성 예측,
asset type을 가로지르는 공유 경로, 순환과 자기 의존성, 다음 snapshot의 새
조회를 검사한다. 같은 snapshot 안에서 공유 native·직접 조회를 재사용하는지도
확인한다.

최종 일반 `Tests` 실행에서는 package Editor case 79개와 테스트 host의 runtime
callback case 두 개, 총 81개가 모두 통과했다. 64.906초가 걸렸으며 결과는
`20260911-092325-8a43ae-Tests-100.xml`에 기록됐다. 해당 시점의 성능 구현 검증이며,
이후 릴리스 검증은 아래에 기록했다.

최종 구현은 격리된 테스트 host에서 owner 1,000개 혼합 작업 case도
126.438초에 통과했으며, 결과는 `native-dependency-scale-1000.xml`에 기록됐다.
Fixture는 앞에 설명한 혼합 작업 구성을 따른다. 아래 값은 현재 구현의 개별
호출 시간이며, 이전 별도 실행과 비교해 개선 배율을 계산하지 않는다.

| 작업 | ms | owner / consumer 로드 |
| --- | ---: | ---: |
| `Ready`에서 cold Preview | 4,084.5 | 1,000 / 104 |
| `Ready`에서 warm Preview | 1,478.9 | 10 / 52 |
| token 하나 변경 후 Preview | 1,390.9 | 11 / 55 |
| token 하나 변경 후 Apply | 4,467.6 | 12 / 104 |
| `Ready`에서 반복 Apply | 1,229.3 | 10 / 52 |

Apply의 로드 수는 반환된 최종 Preview 기준이다. 쓰기 단계는 영향받는 owner
두 개를 방문했고, 반복 Apply는 하나도 방문하지 않았다. Full 검사와의 비교,
예상한 asset byte 변경, 소유한 값과 unmanaged 값, 반복 Apply의 멱등성 검사가
모두 통과했다.

## Recipe 탐색과 릴리스 검증

릴리스 후보는 2026-09-11에 일반 Editor 테스트 99개를 모두 74.093초에 통과했다
(`release-full-graphics.xml`). 실패하거나 건너뛴 테스트는 없었다. Recipe 탐색
13개, 별도 Inspector 열기 동작, 추가된 직렬화 callback 타입 정책 4개를 포함한다.
직접·명시적·상속 구현을 포함한 사용자 정의 `ISerializationCallbackReceiver`는
검사 및 Play Ready cache를 우회한다. Built-in uGUI와 TMP는 계속 재사용할 수 있다.

별도로 새로 만든 Unity 프로젝트에서 Unity Package Manager로 공개 Git URL의
후보 commit `24b99fbcba5fcc33790d07fc29f10f19db0d13d0`을 설치했다. 해석된
lock file의 hash와 해당 commit이 일치하고 Editor·테스트 소스가 checkout과
같음을 확인했으며, graphics를 켠 Editor 테스트 99개도 다시 모두 통과했다
(`git-candidate-full.xml`).

격리된 소비 프로젝트 snapshot의 실제 `Zone Danger Button` Prefab, 기존 자식과
component, preview scene의 instance에서 동일한 owner Recipe와 상속한 원본
Recipe 두 개를 찾았다. 다섯 context에서 각각 warm 조회 20회를 실행했으며 중앙값은
0.511~0.597 ms였다. 최초 catalog 조회는 1,166.804 ms였으므로 warm 조회 시간을
최초 실행이나 UI 클릭 지연으로 해석해서는 안 된다. 검증 Editor 종료 후 audit을
포함해 검사한 입력 파일 1,972개의 byte는 모두 그대로였다.

## 소비 프로젝트의 Player Build

앞서 실행한 소비 프로젝트 snapshot의 Windows IL2CPP Build는 별도 worktree에서만
실행하여 원본 source checkout을 clean 상태로 유지했다. Build guard는 registry 세 개를
모두 검사했다. Unity `BuildReport`는 628.983초에 `Succeeded`를 보고하고 실행
파일을 생성했다. 오류는 0개, 경고는 9개였으며 기존 소비 프로젝트 코드의
obsolete API·미사용 member와 pipeline 설정에서 발생했다.
이전 Build는 native digest와 의존성 계획 변경 이전의 결과이며, 아래의 릴리스
Build에서 갱신된 구현을 검증했다.

엄격한 파일 byte 검사에서는 TMP fallback asset 하나의 CRLF→LF 변경이
검출됐다. 따라서 Build 성공과 별개로 원본 검증 report의 `Passed=false`를
유지했다. Git audit에서도 Unity가 재생성한 URP 설정 asset 두 개를 확인했다.
Build worktree에는 변경이 있었으며, 앞의 입력 파일 1,972개 보존 결과는
Build 이전의 읽기 전용 Preview 검사에 해당한다.

이후 `0.1.0-preview.4` 릴리스 후보를 2026-09-11에 격리된 소비 프로젝트에서
다시 빌드했다. 기존에 활성화된 scene 세 개와 Windows x64 IL2CPP 설정을 사용했다.
`BuildReport`는 449.350초에 `Succeeded`를 보고했으며 오류 0개, 기존 프로젝트
경고 9개였고 실행 파일을 생성했다. 빌드 전후 세 registry의 새 전체 검사가 모두
변경·오류 없이 통과했으며 Build guard 세 개도 계속 활성화돼 있었다. Player
컴파일 graph와 IL2CPP stripped assembly 모두 package Editor assembly와 참조를
포함하지 않았다.

이번 릴리스 실행에서는 `Assets`, `ProjectSettings`, `Packages` 아래 기존 파일
5,289개를 확인했다. Byte가 모두 그대로여서 복구가 필요하지 않았다. 앞서 기록한
정규화 변경은 이전 Build의 이력이며 이번 릴리스 실행 결과와 구분한다.
Editor 종료 후 외부 감사에서도 입력 5,289개와 package production C#/assembly
definition 23개의 hash가 모두 일치했다. 경고 메시지 집합도 이전 성공 Build와 같았다.

이 Build 검사에서는 GUI 외관, 상호작용, 한국어 runtime 동작을 검증하지 않았다.

## 측정의 한계

기준 구현 비교는 격리된 Editor process 하나에서 Image만 사용하는 합성 작업이었다.
구현 순서를 교대로 바꾸지 않고 기준 구현을 먼저 측정했다. 대규모 검증은 다른
합성 혼합 작업을 사용했다. Hardware,
filesystem과 Unity cache, 백그라운드 작업, 더 큰 계층, Variant 관계, TMP asset,
프로젝트별 callback에 따라 시간은 달라질 수 있다. 성능 목표를 정하기 전에 소비
프로젝트의 실제 registry를 측정해야 한다.
