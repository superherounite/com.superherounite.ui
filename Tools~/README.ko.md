# 패키지 검증 도구

[English](README.md) | 한국어

[`Validate-Package.ps1`](Validate-Package.ps1)은
[`TestProject~`](../TestProject~)에서 격리된 Unity host를 준비하고 공식 EditMode
Test Runner를 실행한다. Host의 callback 회귀를 포함한 package Editor test를
자동 검색한다. 선택형 mode로 더 큰 혼합 작업이나 원래 처리기와의 비교를 실행할
수 있다.

아래 명령은 사용 예시이며 검증 실행 결과를 뜻하지 않는다. Package 저장소 root의
PowerShell 7 이상에서 실행한다. 설치되어 정상적으로 사용할 수 있는 Unity
`6000.0.68f1` Editor가 필요하다.

## Editor 테스트 실행

```powershell
pwsh -NoProfile -File ./Tools~/Validate-Package.ps1 -EditorPath 'C:/Program Files/Unity/Hub/Editor/6000.0.68f1/Editor/Unity.exe'
```

기본 mode는 `Tests`다. `SuperHeroUnite.UI.Editor.Tests` assembly에서 `Scale`과
`PerformanceComparison` category를 제외한 테스트를 실행한다. 일반 scaling 회귀
테스트도 포함한다. 최초 smoke test 12개만 실행하는 기존
`StyleRecipeProcessorBatchRunner.Run`은 사용하지 않는다.

기본적으로 Unity를 `-batchmode -nographics`로 실행한다. Inspector 창 생성을
검증하는 `EditorGraphics` category 테스트는 Unity가 graphics device 없음으로
보고하면 ignored로 기록한다. 따라서 headless 실행의 성공은 창 열기 동작까지
검증했다는 뜻이 아니다. 사용할 수 있는 graphics device가 있는 환경에서는 다음과
같이 graphics를 명시적으로 켜서 해당 검증을 포함한다.

```powershell
pwsh -NoProfile -File ./Tools~/Validate-Package.ps1 -EditorPath 'C:/Program Files/Unity/Hub/Editor/6000.0.68f1/Editor/Unity.exe' -EnableGraphics
```

`-EnableGraphics`는 두 Editor 실행에서 `-nographics`를 생략하며 batch mode와
process 창 숨김은 유지한다. 그래도 Unity가 null graphics device를 보고하면
graphics 테스트는 ignored로 남으므로 NUnit XML에서 건너뛴 테스트를 확인한다.
이 동작 테스트는 Inspector의 화면 확인을 대신하지 않는다.

Host는 현재 checkout을 가리키는 local `file:` package dependency를 사용하며
`testables`에 `com.superherounite.ui`를 등록한다. 별도의 Editor 실행으로 package를
resolve하고 host를 compile한다. TMP 기본 font가 없으면 **TMP Essential
Resources**도 import한다. Bootstrap과 Test Runner가 각각 Editor 종료를
처리하므로 비동기 작업이 끝나기 전에 종료될 수 있는 `-quit`은 추가하지 않는다.

## 선택형 작업

| Mode | Owner 수 | 기본값 | 목적 |
| --- | --- | --- | --- |
| `Tests` | Owner 수로 테스트를 제한하지 않음 | 해당 없음 | 일반 Editor 회귀 테스트 전체 |
| `Scale` | `300`, `1000` | `300` | 공유 consumer가 있는 uGUI/TMP, Variant, runtime-only script, Editor callback 혼합 작업 |
| `Comparison` | `100`, `200`, `300` | `100` | Owner마다 Image binding 네 개가 있는 동일 에셋으로 기준 구현과 비교 |

```powershell
pwsh -NoProfile -File ./Tools~/Validate-Package.ps1 -EditorPath $env:UNITY_EDITOR_PATH -Mode Scale -OwnerCount 300
pwsh -NoProfile -File ./Tools~/Validate-Package.ps1 -EditorPath $env:UNITY_EDITOR_PATH -Mode Scale -OwnerCount 1000
pwsh -NoProfile -File ./Tools~/Validate-Package.ps1 -EditorPath $env:UNITY_EDITOR_PATH -Mode Comparison -OwnerCount 100
```

위 예시의 `UNITY_EDITOR_PATH`에는 Editor 실행 파일 경로가 미리 들어 있어야 한다.
`Scale`은 증분·전체 결과의 동등성, 의존 관계 전파, callback 처리, 파일 보존, 작업
횟수를 검증하면서 경과 시간을 기록한다. 소비 프로젝트의 UI를 복사한 것이 아닌
합성 작업이다.

`Comparison`은 `TestProject~/Comparison`에서 harness를 복사한 뒤 commit
`7ede1ed6229c7b03b4d3adfddc42a283d6ef63b0`의 `git show` 결과로 legacy processor와
resolver를 host 안에 생성한다. Git과 해당 commit을 로컬에서 사용할 수 있어야
한다. Class identifier와 그 참조만 이름을 바꾼다. Legacy source는 package에
보관하지 않으며 `Tests`나 `Scale`에서는 생성하지 않는다. 측정 조건과 해석은
[성능 보고서](../Documentation~/performance.ko.md)를 참고한다.

## Parameter와 host 재사용

| Parameter | 동작 |
| --- | --- |
| `-EditorPath` | Editor 실행 파일 경로. `-PrepareOnly`에서도 필수 |
| `-Mode` | 기본값 `Tests`. `Scale`, `Comparison`도 지원 |
| `-OwnerCount` | `0`이면 mode 기본값 사용. 지원 개수는 위 표 참고. `Tests`는 이 값으로 테스트 범위를 제한하지 않음 |
| `-ProjectDirectory` | Package source 밖에 둘 전용 host directory. 선택 사항 |
| `-TimeoutSeconds` | 기본값 `1800`, 허용 범위 `60`–`14400`. 각 Editor 실행에 따로 적용 |
| `-EnableGraphics` | `-nographics`를 생략하여 사용 가능한 graphics device로 `EditorGraphics` 동작 테스트를 실행. 기본값은 꺼짐 |
| `-PrepareOnly` | Host 파일과 manifest를 작성하고 project 경로만 반환. Unity 실행과 TMP resource import는 하지 않음 |

NUnit은 별도로 `Scale` 테스트 하나에 10분, `Comparison` 테스트 하나에 20분의
제한을 둔다. 이는 시작과 import를 포함한 각 Editor 실행 전체에 적용하는
`-TimeoutSeconds`의 기본 30분과 독립적이다. `-TimeoutSeconds`를 늘려도 NUnit의
개별 테스트 제한은 바뀌지 않는다.

기본적으로 checkout과 mode별로 시스템 임시 directory의
`SuperHeroUIValidation/<source-path-hash>/<Mode>`에 재사용 가능한 host를 만든다.
재사용하면 `Library`와 결과가 유지된다. 사용자 지정 directory는 비어 있거나 같은
package source와 mode의 `.superhero-ui-validation.json` marker가 있어야 한다.
Marker 없이 비어 있지 않은 directory, package source 안의 host, Unity에서 이미
열고 있는 host는 거부한다. 별도의 run lock으로 동시에 여러 스크립트가 같은
host를 준비하는 것도 막는다.

수정은 저장소의 `TestProject~` template에서 관리한다. 재사용 시 현재 template
파일을 복사하고 상대 경로를 `.superhero-ui-template-files.json`에 기록한다.
Template에서 삭제한 파일은 이전에 관리 대상으로 기록된 경우에만 다음 host 준비
때 확인된 host 경계 안에서 오래된 metadata와 함께 제거한다. Import한 TMP
resource와 `Library`는 유지한다. 완전히 새로 import한 상태가 필요하면 다른 빈
host directory를 지정한다.

```powershell
pwsh -NoProfile -File ./Tools~/Validate-Package.ps1 -EditorPath $env:UNITY_EDITOR_PATH -Mode Scale -OwnerCount 1000 -ProjectDirectory 'C:/Validation/SuperHeroUI-Scale' -TimeoutSeconds 3600
pwsh -NoProfile -File ./Tools~/Validate-Package.ps1 -EditorPath $env:UNITY_EDITOR_PATH -Mode Tests -ProjectDirectory 'C:/Validation/SuperHeroUI-Tests' -PrepareOnly
```

Package에는 여전히 runtime assembly가 없다.
`TestProject~/Assets/CallbackProbe`와 `TestProject~/Assets/ScaleTests`의 runtime
script는 실제 Prefab callback을 검증하기 위한 host 전용 파일이다. 이름이 tilde로
끝나는 template 폴더는 package runtime code로 import되지 않는다.

## 결과와 CI

Host의 `Results`에 UTC 시각과 고유한 실행 suffix를 붙여 기록한다.

- `<run>-bootstrap.log`: package, compile, TMP 준비 출력
- `<run>-tests.log`: Editor와 Test Runner 출력
- `<run>-<Mode>-<OwnerCount>.xml`: NUnit 결과와 fixture 출력
- `<run>-summary.json`: 테스트 report가 생성된 이후 mode, host, 결과, 개수,
  종료 code, 시간, report와 log 경로 기록

`Comparison`은 측정값을 담은 `legacy-vs-current-<OwnerCount>-owners.json`도
기록한다. 같은 개수로 다시 실행하면 갱신되므로 과거 결과를 보관할 때는 해당
시각의 report와 함께 보존한다. Scale의 시간과 작업 횟수는 테스트 출력과 XML에
들어간다.

Unity의 비정상 종료, timeout, 테스트 report 누락, 테스트 실패, 선택한 테스트
0개 또는 성공한 테스트 0개는 스크립트 실패로 처리한다. 준비나 compile에서
실패하면 XML과 summary JSON이 생기기 전에 종료될 수 있으므로 bootstrap log를
확인한다. 스크립트의 process timeout에서는 시작한 Editor와 그 자식 process만
종료한다. `-PrepareOnly`는 파일만 준비하며 테스트 결과를 보고하지 않는다.

PowerShell, Editor, 필요한 실행 환경이 준비된 기존 CI job에서도 같은 명령을
사용하고 반환된 결과 directory를 보관할 수 있다.

```powershell
pwsh -NoProfile -File ./Tools~/Validate-Package.ps1 -EditorPath $env:UNITY_EDITOR_PATH -Mode Tests -ProjectDirectory 'C:/Validation/SuperHeroUI-CI'
```

Package 검증은 소비 프로젝트의 화면, interaction, localization, Player Build
검사를 대신하지 않는다.
