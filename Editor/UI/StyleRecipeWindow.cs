using System;

using UnityEditor;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    public sealed class StyleRecipeWindow : EditorWindow
    {
        [SerializeField] private StyleRecipeRegistry _registry;

        private StyleReview _review;
        private Vector2 _scroll;

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
            EditorGUI.BeginChangeCheck();
            _registry = (StyleRecipeRegistry)EditorGUILayout.ObjectField(
                "Registry",
                _registry,
                typeof(StyleRecipeRegistry),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                _review = null;
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

                using (new EditorGUI.DisabledScope(
                    _review == null
                    || _review.State != StyleReviewState.Stale))
                {
                    if (GUILayout.Button("Apply Reviewed Changes"))
                    {
                        Apply();
                    }
                }
            }

            DrawReview();
            DrawRecipes();
        }

        private void Preview()
        {
            try
            {
                _review = StyleRecipeProcessor.Preview(_registry);
                if (_review.State == StyleReviewState.Ready)
                {
                    StyleRecipeGuards.RecordReady(_registry);
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
                _review = StyleRecipeProcessor.Apply(_registry, _review);
                StyleRecipeGuards.RecordReady(_registry);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogError(exception.Message);
                _review = StyleRecipeProcessor.Preview(_registry);
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

            _scroll = EditorGUILayout.BeginScrollView(
                _scroll,
                GUILayout.MinHeight(140f));
            foreach (string error in _review.Errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            foreach (string change in _review.Changes)
            {
                EditorGUILayout.SelectableLabel(
                    change,
                    EditorStyles.wordWrappedLabel,
                    GUILayout.MinHeight(EditorGUIUtility.singleLineHeight));
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
            foreach (PrefabStyleRecipe recipe
                in _registry.Recipes ?? Array.Empty<PrefabStyleRecipe>())
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
