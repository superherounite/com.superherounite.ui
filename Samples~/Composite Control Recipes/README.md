# Composite Control Recipes

English | [한국어](README.ko.md)

These mappings show how product-owned Prefabs can share visual primitives without adopting a package-owned hierarchy or runtime component.

| Control | Suggested bindings | Keep in the consuming project |
|---|---|---|
| Input field | Surface for the field shell, Text for value and placeholder, Selectable on `TMP_InputField` | validation, focus/error messaging, character limits, labels, layout |
| Dropdown | Surface for closed field and list shell, Text for caption and items, Image for arrow/checkmark, Selectable on `TMP_Dropdown` and item controls | option data, list sizing, virtualization, open direction, selection behavior |
| Tab | Surface and Text for each tab, Selectable on `Toggle` or `Button`, Graphic Color for a simple indicator | selected-state source of truth, content switching, keyboard navigation, tab count |
| Table | Surface for header/rows, Text for headers and cells, Image for sort/status icons | schema, dynamic row Prefab, sorting, resizing, scrolling, empty/loading states |
| Popup | Surface for shell and optional outline, Text for title/body/actions, Image for icon, Selectable for action buttons | modal stack, focus trap, dismissal policy, animation, persistent events |
| Badge | Surface for pill or marker, Text for label/count, Graphic Color for a status mark | semantic status mapping, count formatting, visibility rules, placement |

## Recipe pattern

1. Keep the complete control as a normal project Prefab.
2. Create one Recipe for the Prefab that owns the styled targets.
3. Use the narrowest binding that owns the intended fields. Prefer Graphic Color when only tint is shared; use Image when image presentation is shared; use Surface when fill and outline must change together.
4. Capture targets from the saved owner in Prefab Mode.
5. If another Prefab nests this owner, add that outer Prefab to `Consumer Prefabs` so managed overrides are detected.
6. Preview, review, and Apply through **Tools > Super Hero UI > Style Recipes**.

Do not bind text content, anchors, sizes, layout components, UnityEvents, or feature state merely because two controls currently look alike. Those values usually belong to the Prefab or its product code.

## Dynamic lists

Style the authored item Prefab and instantiate that finished Prefab at runtime. Runtime code should bind data and state only. The style Recipe never runs in the Player and does not need a Binding component on each item.

## Selected and semantic states

Selectable styling covers Unity's Color Tint states. If a control has a separate semantic state such as success, warning, active tab, unread, or critical alert, model that state in project code and choose an authored Prefab variant or project-owned presentation component. Do not overload hover or pressed colors with domain state.
