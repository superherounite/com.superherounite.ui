using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor
{
    internal sealed class PlayModeTuningWindow : EditorWindow
    {
        [SerializeField] private PrefabStyleRecipe _recipe;
        [SerializeField] private string _targetId;
        [SerializeField] private Vector2 _scroll;

        private Component _runtimeTarget;
        private IReadOnlyList<PlayModeTuningBinding> _bindings = Array.Empty<PlayModeTuningBinding>();
        private PlayModeTuningBinding[] _targets = Array.Empty<PlayModeTuningBinding>();
        private string[] _targetLabels = Array.Empty<string>();
        private PlayModeTuningReview _review;
        private bool _refreshBindings = true;
        private string _error;
        private string _notice;

        private static PlayModeTuningSession Session => PlayModeTuningState.Session;

        [MenuItem("Tools/Super Hero UI/Play Mode Tuning")]
        internal static void Open()
        {
            PlayModeTuningWindow window = GetWindow<PlayModeTuningWindow>("Play Mode Tuning");
            window.minSize = new Vector2(420f, 420f);
            if (Selection.activeObject is PrefabStyleRecipe recipe)
            {
                window.SelectRecipe(recipe);
            }
        }

        [MenuItem("GameObject/Super Hero UI/Play Mode Tuning", false, 11)]
        private static void OpenForSelection()
        {
            Open();
            GetWindow<PlayModeTuningWindow>().FindRecipesForSelection();
        }

        private void OnEnable()
        {
            Session.Changed += OnSessionChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            _refreshBindings = true;
        }

        private void OnDisable()
        {
            Session.Changed -= OnSessionChanged;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Session.RestoreLiveValues();
            _runtimeTarget = null;
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            bool busy = EditorApplication.isCompiling || EditorApplication.isUpdating;
            if (_refreshBindings && !busy && Event.current.type == EventType.Layout)
            {
                RefreshBindings();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox(
                EditorApplication.isPlaying
                    ? "Tune the selected live component. Drafts stay here when Play Mode ends; source assets are unchanged."
                    : "Choose a Recipe, enter Play Mode, and select its live UI component. After tuning, review and write the saved drafts here.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(busy))
            {
                DrawTargetSelection();
                DrawLiveControls();
                DrawDrafts();
                DrawReview();
            }

            if (!string.IsNullOrEmpty(_error))
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
            }

            if (!string.IsNullOrEmpty(_notice))
            {
                EditorGUILayout.HelpBox(_notice, MessageType.Info);
            }

            if (GUILayout.Button("Open Style Recipes"))
            {
                StyleRecipeWindow.Open();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTargetSelection()
        {
            EditorGUI.BeginChangeCheck();
            var recipe = (PrefabStyleRecipe)EditorGUILayout.ObjectField("Recipe", _recipe, typeof(PrefabStyleRecipe), false);
            if (EditorGUI.EndChangeCheck())
            {
                SelectRecipe(recipe);
            }

            if (GUILayout.Button("Find Recipes for Selection"))
            {
                Run(FindRecipesForSelection);
            }

            if (_targets.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "Choose a saved Recipe with captured color or owned PPUM bindings. Capture targets in Edit Mode first.",
                    MessageType.Info);
                return;
            }

            int index = Array.FindIndex(_targets, binding => binding.TargetReference.GlobalObjectId == _targetId);
            index = Mathf.Max(0, index);
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup("Recipe Target", index, _targetLabels);
            if (EditorGUI.EndChangeCheck())
            {
                _targetId = _targets[next].TargetReference.GlobalObjectId;
                _runtimeTarget = null;
                _error = null;
            }

            PlayModeTuningBinding selected = _targets[next];
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                EditorGUI.BeginChangeCheck();
                Component target = (Component)EditorGUILayout.ObjectField(
                    "Live Component", _runtimeTarget,
                    PrefabTargetResolver.GetExpectedType(selected.TargetReference.ExpectedKind), true);
                if (EditorGUI.EndChangeCheck())
                {
                    _runtimeTarget = target;
                    _error = null;
                }

                if (GUILayout.Button("Use Selected Object"))
                {
                    Run(() => UseSelection(selected));
                }
            }

            if (_runtimeTarget != null && !PrefabUtility.IsPartOfPrefabInstance(_runtimeTarget.gameObject))
            {
                EditorGUILayout.HelpBox(
                    "This object has no Prefab link. Match it to the Recipe target above; only its component type can be checked automatically.",
                    MessageType.Info);
            }
        }

        private void DrawLiveControls()
        {
            if (_targets.Length == 0)
            {
                return;
            }

            bool playing = EditorApplication.isPlaying;
            foreach (PlayModeTuningBinding binding in _bindings.Where(binding => binding.TargetReference.GlobalObjectId == _targetId))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(GetPropertyLabel(binding.Property), EditorStyles.boldLabel);
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField("Source", binding.SourceAsset, typeof(Object), false);
                    }

                    PlayModeTuningDraft draft = Session.FindDraft(binding);
                    bool valid = binding.ValidateTarget(_runtimeTarget, out string error);
                    using (new EditorGUI.DisabledScope(!playing || !valid))
                    {
                        EditorGUI.BeginChangeCheck();
                        if (binding.IsColor)
                        {
                            Color value = draft != null ? draft.ColorValue
                                : valid ? binding.ReadColor(_runtimeTarget) : binding.SourceColor;
                            value = EditorGUILayout.ColorField(new GUIContent("Color"), value, true, true, true);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Run(() => Session.SetColor(binding, _runtimeTarget, value));
                            }
                        }
                        else
                        {
                            float value = draft != null ? draft.FloatValue
                                : valid ? binding.ReadFloat(_runtimeTarget) : binding.SourceFloat;
                            value = EditorGUILayout.FloatField(new GUIContent("PPUM", "Pixels Per Unit Multiplier; minimum 0.01."), value);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Run(() => Session.SetFloat(binding, _runtimeTarget, value));
                            }

                            if (_runtimeTarget is Image image && image.type != Image.Type.Sliced && image.type != Image.Type.Tiled)
                            {
                                EditorGUILayout.HelpBox("PPUM controls sliced/tiled images. This live Image uses another type.", MessageType.Info);
                            }
                        }

                        using (new EditorGUI.DisabledScope(draft == null))
                        {
                            if (GUILayout.Button("Reapply Draft"))
                            {
                                Run(() => Reapply(binding, draft));
                            }
                        }
                    }

                    if (playing && valid)
                    {
                        string value = binding.IsColor
                            ? PlayModeTuningSession.FormatColor(binding.ReadColor(_runtimeTarget))
                            : PlayModeTuningSession.FormatFloat(binding.ReadFloat(_runtimeTarget));
                        EditorGUILayout.LabelField("Live value", value);
                    }
                    else if (playing && !string.IsNullOrEmpty(error))
                    {
                        EditorGUILayout.HelpBox(error, MessageType.Info);
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!Session.HasLiveValues))
            {
                if (GUILayout.Button("Restore Live Values"))
                {
                    Run(Session.RestoreLiveValues);
                }
            }

            EditorGUILayout.LabelField(
                "Only explicitly tuned instances change. Animation or game code can replace these values; use Reapply Draft when needed.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawDrafts()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Saved Drafts ({Session.Drafts.Count})", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Drafts survive Play Mode, window closing, and script reloads within this Editor session. Write them before quitting the Editor.",
                EditorStyles.wordWrappedMiniLabel);

            foreach (PlayModeTuningDraft draft in Session.Drafts.ToArray())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(draft.Label, EditorStyles.wordWrappedLabel);
                    string value = draft.IsColor ? PlayModeTuningSession.FormatColor(draft.ColorValue)
                        : "PPUM " + PlayModeTuningSession.FormatFloat(draft.FloatValue);
                    EditorGUILayout.LabelField(value, EditorStyles.wordWrappedLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Select Draft"))
                        {
                            Run(() => SelectDraft(draft));
                        }

                        if (GUILayout.Button("Discard"))
                        {
                            Run(() => Session.RemoveDraft(draft));
                        }
                    }
                }
            }

            using (new EditorGUI.DisabledScope(Session.Drafts.Count == 0))
            {
                if (GUILayout.Button("Discard All Drafts"))
                {
                    Run(Session.Clear);
                }
            }
        }

        private void DrawReview()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Write to Style Assets", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Writing a shared Token or ImageStyle affects every Recipe that uses it. After writing, review and bake affected registries in Style Recipes.",
                EditorStyles.wordWrappedLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || Session.Drafts.Count == 0))
            {
                if (GUILayout.Button("Review Style Changes"))
                {
                    Run(() => _review = Session.Review());
                }

                if (_review != null)
                {
                    EditorGUILayout.LabelField($"{_review.Changes.Count} source change(s), {_review.Errors.Count} error(s)");
                    foreach (string error in _review.Errors)
                    {
                        EditorGUILayout.HelpBox(error, MessageType.Error);
                    }

                    foreach (string change in _review.Changes)
                    {
                        EditorGUILayout.HelpBox(change, MessageType.None);
                    }
                }

                using (new EditorGUI.DisabledScope(_review == null || _review.Errors.Count > 0))
                {
                    if (GUILayout.Button("Write Reviewed Style Changes"))
                    {
                        Run(() =>
                        {
                            Session.WriteReviewedChanges(_review);
                            _notice = "Style assets saved. Run Preview / Validate, review the Prefab changes, then Apply Reviewed Changes in Style Recipes.";
                        });
                    }
                }
            }
        }

        private void Reapply(PlayModeTuningBinding binding, PlayModeTuningDraft draft)
        {
            if (draft.IsColor)
            {
                Session.SetColor(binding, _runtimeTarget, draft.ColorValue);
            }
            else
            {
                Session.SetFloat(binding, _runtimeTarget, draft.FloatValue);
            }
        }

        private void SelectDraft(PlayModeTuningDraft draft)
        {
            if (!draft.TryResolve(out PlayModeTuningBinding binding, out string error))
            {
                throw new InvalidOperationException(error);
            }

            SelectRecipe(binding.Recipe);
            _targetId = binding.TargetReference.GlobalObjectId;
        }

        private void SelectRecipe(PrefabStyleRecipe recipe)
        {
            _recipe = recipe;
            _targetId = null;
            _runtimeTarget = null;
            _error = null;
            RefreshBindings();
        }

        private void RefreshBindings()
        {
            _bindings = PlayModeTuningBinding.GetBindings(_recipe);
            _targets = _bindings.GroupBy(binding => binding.TargetReference.GlobalObjectId)
                .Select(group => group.First()).ToArray();
            _targetLabels = _targets.Select(binding =>
                binding.TargetReference.DisplayPath + " (" + binding.TargetReference.ExpectedKind + ")").ToArray();
            if (!_targets.Any(binding => binding.TargetReference.GlobalObjectId == _targetId))
            {
                _targetId = _targets.FirstOrDefault()?.TargetReference.GlobalObjectId;
            }

            _refreshBindings = false;
        }

        private void FindRecipesForSelection()
        {
            var lookup = new StyleRecipeLookup();
            IReadOnlyList<StyleRecipeMatch> matches = lookup.Find(Selection.activeObject);
            if (matches.Count == 0)
            {
                _notice = "No linked Recipe found. For runtime clones, choose the Recipe explicitly.";
                return;
            }

            if (matches.Count == 1)
            {
                SelectRecipe(matches[0].Recipe);
                return;
            }

            var menu = new GenericMenu();
            foreach (StyleRecipeMatch match in matches)
            {
                PrefabStyleRecipe recipe = match.Recipe;
                menu.AddItem(new GUIContent($"{recipe.name} ({match.Relationship})"), false, () => SelectRecipe(recipe));
            }

            menu.ShowAsContext();
        }

        private void UseSelection(PlayModeTuningBinding binding)
        {
            Component selected = Selection.activeObject as Component;
            if (selected == null && Selection.activeGameObject != null)
            {
                Component[] candidates = Selection.activeGameObject.GetComponents<Component>()
                    .Where(component => component != null && component.GetType().FullName == binding.TargetReference.ComponentType).ToArray();
                if (candidates.Length == 1)
                {
                    selected = candidates[0];
                }
            }

            if (!binding.ValidateTarget(selected, out string error))
            {
                throw new InvalidOperationException(error + " Drag the exact component into Live Component if necessary.");
            }

            _runtimeTarget = selected;
        }

        private void OnSessionChanged()
        {
            _review = null;
            Repaint();
        }

        private void OnProjectChanged()
        {
            _refreshBindings = true;
            _review = null;
            Repaint();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            _review = null;
            _runtimeTarget = null;
            _refreshBindings = true;
            Repaint();
        }

        private void Run(Action action)
        {
            _error = null;
            _notice = null;
            try
            {
                action();
            }
            catch (InvalidOperationException exception)
            {
                _error = exception.Message;
            }
        }

        private static string GetPropertyLabel(PlayModeTuningProperty property)
        {
            return property switch
            {
                PlayModeTuningProperty.GraphicColor => "Color",
                PlayModeTuningProperty.PixelsPerUnitMultiplier => "Rounding (PPUM)",
                PlayModeTuningProperty.NormalColor => "Normal Color",
                PlayModeTuningProperty.HighlightedColor => "Highlighted Color",
                PlayModeTuningProperty.PressedColor => "Pressed Color",
                PlayModeTuningProperty.SelectedColor => "Selected Color",
                PlayModeTuningProperty.DisabledColor => "Disabled Color",
                _ => property.ToString(),
            };
        }
    }
}
