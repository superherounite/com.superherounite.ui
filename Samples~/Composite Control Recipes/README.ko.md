# Composite Control Recipes

[English](README.md) | 한국어

다음 표는 프로젝트가 관리하는 Prefab이 패키지 소유 hierarchy나 runtime component를 사용하지 않고 시각 primitive를 공유하는 방법을 보여준다.

| 컨트롤 | 권장 binding | 소비 프로젝트가 소유할 내용 |
|---|---|---|
| 입력 필드 | field shell용 Surface, value와 placeholder용 Text, `TMP_InputField`용 Selectable | validation, focus 및 오류 message, 글자 수 제한, label, layout |
| 드롭다운 | 닫힌 field와 list shell용 Surface, caption과 item용 Text, arrow/checkmark용 Image, `TMP_Dropdown`과 item control용 Selectable | option data, list 크기, virtualization, 열리는 방향, 선택 동작 |
| 탭 | 각 tab용 Surface와 Text, `Toggle` 또는 `Button`용 Selectable, 단순 indicator용 Graphic Color | selected state의 source of truth, content 전환, keyboard navigation, tab 수 |
| 테이블 | header/row용 Surface, header/cell용 Text, sort/status icon용 Image | schema, dynamic row Prefab, sorting, resizing, scrolling, empty/loading 상태 |
| 팝업 | shell과 선택적 outline용 Surface, title/body/action용 Text, icon용 Image, action button용 Selectable | modal stack, focus trap, 닫기 정책, animation, persistent event |
| 배지 | pill 또는 marker용 Surface, label/count용 Text, 상태 표시용 Graphic Color | semantic status mapping, count format, 표시 규칙, 배치 |

## Recipe 구성 방식

1. 완성된 컨트롤은 일반 project Prefab으로 유지한다.
2. Style target을 소유하는 Prefab마다 Recipe 하나를 만든다.
3. 필요한 field만 소유하는 가장 좁은 binding을 사용한다. Tint만 공유하면 Graphic Color, image 표현을 공유하면 Image, fill과 outline을 함께 변경해야 하면 Surface를 사용한다.
4. 저장된 owner를 Prefab Mode로 열어 target을 Capture한다.
5. 다른 Prefab이 이 owner를 중첩한다면 managed override를 검사할 수 있도록 외부 Prefab을 `Consumer Prefabs`에 추가한다.
6. **Tools > Super Hero UI > Style Recipes**에서 Preview, 검토, Apply를 진행한다.

두 컨트롤의 현재 모습이 비슷하다는 이유만으로 text content, anchor, size, layout component, UnityEvent, feature state를 binding하지 않는다. 이런 값은 보통 Prefab이나 제품 code가 소유한다.

## 동적 목록

미리 작성한 item Prefab에 style을 bake한 뒤 runtime에서 완성된 Prefab을 instantiate한다. Runtime code는 data와 state만 binding한다. Style Recipe는 Player에서 실행되지 않으며 각 item에 Binding component를 붙일 필요가 없다.

## 선택 상태와 의미 상태

Selectable style은 Unity Color Tint 상태를 처리한다. 성공, 경고, 활성 tab, 읽지 않음, 심각 경보처럼 별도의 의미 상태가 있다면 project code가 상태를 소유하고 작성된 Prefab Variant나 project 소유 presentation component를 선택하게 한다. Hover 또는 pressed color를 domain state 용도로 사용하지 않는다.
