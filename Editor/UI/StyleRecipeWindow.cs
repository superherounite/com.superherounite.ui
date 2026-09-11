using System;

using UnityEditor;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    public sealed class StyleRecipeWindow : EditorWindow
    {
        [SerializeField] private StyleRecipeRegistry _registry;

        private StyleReview _review;
        private Vector2 _windowScroll;
        private Vector2 _reviewScroll;
        private Vector2 _recipesScroll;

        [MenuItem("Tools/Super Hero UI/Style Recipes")]
        public static void Open()
        {
            StyleRecipeWindow window = GetWindow<StyleRecipeWindow>("Super Hero UI");
            if (window._registry == null)
            {
                window._registry = FindFirstRegistry();
            }

            window.Repaint();
        }

        internal static void Open(StyleRecipeRegistry registry, StyleReview review)
        {
            StyleRecipeWindow window = GetWindow<StyleRecipeWindow>("Super Hero UI");
            window._registry = registry;
            window._review = review;
            window.Repaint();
        }

        private void OnGUI()
        {
            _windowScroll = EditorGUILayout.BeginScrollView(_windowScroll);

            if (GUILayout.Button("Open Play Mode Tuning"))
            {
                PlayModeTuningWindow.Open();
            }

            EditorGUI.BeginChangeCheck();
            _registry = (StyleRecipeRegistry)EditorGUILayout.ObjectField(
                "Registry",
                _registry,
                typeof(StyleRecipeRegistry),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                _review = null;
                _reviewScroll = Vector2.zero;
                _recipesScroll = Vector2.zero;
            }

            using (new EditorGUI.DisabledScope(_registry == null))
            {
                if (GUILayout.Button("Select Registry Asset"))
                {
                    Selection.activeObject = _registry;
                    EditorGUIUtility.PingObject(_registry);
                }
            }

            bool editorBusy = EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating;
            using (new EditorGUI.DisabledScope(_registry == null || editorBusy))
            {
                if (GUILayout.Button("Preview / Validate"))
                {
                    Preview();
                }

                if (GUILayout.Button("Full Preview / Validate"))
                {
                    Preview(true);
                }

                if (_review != null && _review.CanPreviewOverrideRepairs && !_review.IsOverrideRepairReview)
                {
                    EditorGUILayout.HelpBox(
                        "Owned overrides prevent Apply. Preview repairs to restore inheritance. "
                        + "For intentional differences, register a Base Recipe specialization.",
                        MessageType.Warning);
                    if (GUILayout.Button("Preview Override Repairs"))
                    {
                        PreviewRepairs();
                    }
                }

                if (_review != null && _review.IsOverrideRepairReview)
                {
                    EditorGUILayout.HelpBox(
                        "Review the override reversions below. Apply Reviewed Repairs restores only these "
                        + "properties to their source Prefab values. Then review any remaining style changes.",
                        MessageType.Info);
                }

                using (new EditorGUI.DisabledScope(
                    _review == null
                    || _review.State != StyleReviewState.Stale))
                {
                    if (GUILayout.Button(_review != null && _review.IsOverrideRepairReview
                        ? "Apply Reviewed Repairs" : "Apply Reviewed Changes"))
                    {
                        Apply();
                    }
                }
            }

            DrawReview();
            DrawRecipes();

            EditorGUILayout.EndScrollView();
        }

        private void Preview(bool full = false)
        {
            try
            {
                _review = full
                    ? StyleRecipeProcessor.PreviewFull(_registry)
                    : StyleRecipeProcessor.Preview(_registry);
                if (_review.State == StyleReviewState.Ready)
                {
                    StyleRecipeGuards.RecordReady(_registry, _review);
                }
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogError(exception.Message);
            }
        }

        private void Apply()
        {
            try
            {
                _review = _review.IsOverrideRepairReview
                    ? StyleRecipeProcessor.ApplyOverrideRepairs(_registry, _review)
                    : StyleRecipeProcessor.Apply(_registry, _review);
                StyleRecipeGuards.RecordReady(_registry, _review);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogError(exception.Message);
                _review = StyleRecipeProcessor.Preview(_registry);
            }
        }

        private void PreviewRepairs()
        {
            try
            {
                _review = StyleRecipeProcessor.PreviewOverrideRepairs(_registry);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogError(exception.Message);
                _review = null;
            }
        }

        private void DrawReview()
        {
            if (_review == null)
            {
                EditorGUILayout.HelpBox(
                    _registry == null
                        ? "Create or select a Style Recipe Registry."
                        : "Preview the registry before applying changes.",
                    MessageType.Info);
                return;
            }

            MessageType messageType = _review.State switch
            {
                StyleReviewState.Ready => MessageType.Info,
                StyleReviewState.Stale => MessageType.Warning,
                _ => MessageType.Error,
            };
            EditorGUILayout.HelpBox(
                $"{_review.State} | {_review.Assets.Count} assets | "
                + $"{_review.Changes.Count} changes | {_review.Errors.Count} errors",
                messageType);

            if (_review.Errors.Count == 0 && _review.Changes.Count == 0)
            {
                return;
            }

            _reviewScroll = EditorGUILayout.BeginScrollView(
                _reviewScroll,
                GUILayout.Height(Mathf.Clamp(position.height * 0.3f, 80f, 280f)));
            foreach (string error in _review.Errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            foreach (string change in _review.Changes)
            {
                Rect changeRect = GUILayoutUtility.GetRect(
                    new GUIContent(change),
                    EditorStyles.wordWrappedLabel,
                    GUILayout.MinWidth(0f),
                    GUILayout.ExpandWidth(true));
                EditorGUI.SelectableLabel(
                    changeRect,
                    change,
                    EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawRecipes()
        {
            if (_registry == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Registered Recipes", EditorStyles.boldLabel);
            PrefabStyleRecipe[] recipes = _registry.Recipes ?? Array.Empty<PrefabStyleRecipe>();
            float contentHeight = Mathf.Max(1, recipes.Length)
                * (EditorGUIUtility.singleLineHeight + 8f);
            float viewportHeight = Mathf.Clamp(position.height * 0.35f, 100f, 360f);
            _recipesScroll = EditorGUILayout.BeginScrollView(
                _recipesScroll,
                GUILayout.Height(Mathf.Min(contentHeight, viewportHeight)));
            foreach (PrefabStyleRecipe recipe in recipes)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField(
                            recipe,
                            typeof(PrefabStyleRecipe),
                            false);
                    }

                    using (new EditorGUI.DisabledScope(recipe == null))
                    {
                        if (GUILayout.Button("Select", GUILayout.Width(52f)))
                        {
                            Selection.activeObject = recipe;
                            EditorGUIUtility.PingObject(recipe);
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static StyleRecipeRegistry FindFirstRegistry()
        {
            string[] guids = AssetDatabase.FindAssets("t:StyleRecipeRegistry");
            if (guids.Length == 0)
            {
                return null;
            }

            Array.Sort(guids, StringComparer.Ordinal);
            return AssetDatabase.LoadAssetAtPath<StyleRecipeRegistry>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
