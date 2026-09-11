using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor
{
    [InitializeOnLoad]
    internal static class StyleRecipeNavigation
    {
        private const string AssetMenu = "Assets/Super Hero UI/Find Style Recipes";
        private const string ObjectMenu = "GameObject/Super Hero UI/Find Style Recipes";
        private const int VisibleLinkCount = 3;
        private static readonly StyleRecipeLookup s_lookup = new();
        private static readonly ConditionalWeakTable<UnityEditor.Editor, InspectorLinks> s_inspectors = new();
        private static readonly GUIContent s_refresh = new("Refresh", "Look for linked style recipes again.");
        private static int s_revision;

        static StyleRecipeNavigation()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawHeader;
            EditorApplication.projectChanged += InvalidateCatalog;
            EditorApplication.hierarchyChanged += InvalidateViews;
            Selection.selectionChanged += InvalidateViews;
            Undo.undoRedoPerformed += InvalidateViews;
            Undo.postprocessModifications += OnPostprocessModifications;
            PrefabStage.prefabStageOpened += OnStageChanged;
            PrefabStage.prefabStageClosing += OnStageChanged;
        }

        [MenuItem(AssetMenu, false, 2000)]
        [MenuItem(ObjectMenu, false, 10)]
        private static void FindForSelection(MenuCommand command)
        {
            Object target = command?.context != null ? command.context : Selection.activeObject;
            InvalidateCatalog();
            IReadOnlyList<StyleRecipeMatch> matches = s_lookup.Find(GetGameObject(target));
            if (matches.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Style Recipes",
                    "No recipe references this Prefab as its owner or a registered consumer.",
                    "OK");
            }
            else if (matches.Count == 1)
            {
                OpenRecipe(matches[0].Recipe);
            }
            else
            {
                ShowMenu(CreateLinks(matches));
            }
        }

        [MenuItem(AssetMenu, true)]
        private static bool ValidateAssetSelection()
        {
            return Selection.objects.Length == 1 && IsPrefabObject(GetGameObject(Selection.activeObject));
        }

        [MenuItem(ObjectMenu, true)]
        private static bool ValidateObjectSelection(MenuCommand command)
        {
            return command?.context != null
                ? IsPrefabObject(GetGameObject(command.context))
                : ValidateAssetSelection();
        }

        private static void DrawHeader(UnityEditor.Editor editor)
        {
            Object target = editor.target;
            if (target is not GameObject
                && !(target is AssetImporter importer
                    && importer.assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            // The asset Inspector also draws the root GameObject below its importer header.
            if (target is GameObject gameObject && AssetDatabase.IsMainAsset(gameObject))
            {
                return;
            }

            if (editor.targets.Length != 1)
            {
                if (IsPrefabObject(GetGameObject(target)))
                {
                    EditorGUILayout.LabelField("Style Recipes", "Select one object to find its recipes.");
                }

                return;
            }

            InspectorLinks view = s_inspectors.GetValue(editor, _ => new InspectorLinks());
            bool busy = EditorApplication.isCompiling || EditorApplication.isUpdating;
            if (!busy && Event.current.type == EventType.Layout
                && (view.Target != target || view.Revision != s_revision))
            {
                GameObject inspected = GetGameObject(target);
                view.Target = target;
                view.IsPrefab = IsPrefabObject(inspected);
                view.Links = view.IsPrefab ? CreateLinks(s_lookup.Find(inspected)) : Array.Empty<RecipeLink>();
                view.Revision = s_revision;
            }

            if (view.Target != target || !view.IsPrefab)
            {
                return;
            }

            // Navigation remains available when version control makes the asset fields read-only.
            bool previousEnabled = GUI.enabled;
            GUI.enabled = !busy;
            try
            {
                EditorGUILayout.Space(3f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Style Recipes", EditorStyles.miniBoldLabel);
                    if (GUILayout.Button(s_refresh, EditorStyles.miniButton, GUILayout.Width(56f)))
                    {
                        InvalidateCatalog();
                        editor.Repaint();
                    }
                }

                if (view.Links.Length == 0)
                {
                    EditorGUILayout.LabelField("No linked style recipe.", EditorStyles.miniLabel);
                }

                for (int index = 0; index < Math.Min(VisibleLinkCount, view.Links.Length); index++)
                {
                    RecipeLink link = view.Links[index];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(link.Relationship, EditorStyles.miniLabel, GUILayout.Width(68f));
                        if (GUILayout.Button(link.Label, EditorStyles.miniButton))
                        {
                            OpenRecipe(link.Recipe);
                            GUIUtility.ExitGUI();
                        }
                    }
                }

                if (view.Links.Length > VisibleLinkCount
                    && GUILayout.Button($"Show all {view.Links.Length} recipes", EditorStyles.miniButton))
                {
                    ShowMenu(view.Links);
                }
            }
            finally
            {
                GUI.enabled = previousEnabled;
            }
        }

        private static GameObject GetGameObject(Object target)
        {
            return target switch
            {
                GameObject gameObject => gameObject,
                Component component => component.gameObject,
                AssetImporter importer when importer.assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                    => AssetDatabase.LoadAssetAtPath<GameObject>(importer.assetPath),
                _ => null,
            };
        }

        private static bool IsPrefabObject(GameObject target)
        {
            return StyleRecipeLookup.HasPrefabContext(target);
        }

        private static RecipeLink[] CreateLinks(IReadOnlyList<StyleRecipeMatch> matches)
        {
            var links = new RecipeLink[matches.Count];
            for (int index = 0; index < matches.Count; index++)
            {
                StyleRecipeMatch match = matches[index];
                string relationship = match.Relationship == StyleRecipeRelationship.Inherited
                    ? "Base Prefab"
                    : match.Relationship.ToString();
                string recipePath = AssetDatabase.GetAssetPath(match.Recipe);
                links[index] = new RecipeLink(
                    match.Recipe,
                    relationship,
                    new GUIContent(match.Recipe.name,
                        $"Open in a separate Inspector.\nRecipe: {recipePath}\nPrefab: {match.PrefabPath}"),
                    new GUIContent($"{relationship}: {match.Recipe.name} — {recipePath}".Replace("/", " › ")));
            }

            return links;
        }

        private static void ShowMenu(IReadOnlyList<RecipeLink> links)
        {
            var menu = new GenericMenu { allowDuplicateNames = true };
            foreach (RecipeLink link in links)
            {
                PrefabStyleRecipe recipe = link.Recipe;
                menu.AddItem(link.MenuLabel, false, () => OpenRecipe(recipe));
            }

            menu.ShowAsContext();
        }

        private static void OpenRecipe(PrefabStyleRecipe recipe)
        {
            if (recipe != null)
            {
                EditorUtility.OpenPropertyEditor(recipe);
            }
        }

        private static void InvalidateCatalog()
        {
            s_lookup.Invalidate();
            InvalidateViews();
        }

        private static void InvalidateViews()
        {
            unchecked
            {
                s_revision++;
            }
        }

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            foreach (UndoPropertyModification modification in modifications)
            {
                if (modification.currentValue.target is PrefabStyleRecipe)
                {
                    InvalidateViews();
                    break;
                }
            }

            return modifications;
        }

        private static void OnStageChanged(PrefabStage stage)
        {
            InvalidateViews();
        }

        private sealed class InspectorLinks
        {
            public Object Target { get; set; }
            public int Revision { get; set; } = -1;
            public bool IsPrefab { get; set; }
            public RecipeLink[] Links { get; set; } = Array.Empty<RecipeLink>();
        }

        private sealed class RecipeLink
        {
            public PrefabStyleRecipe Recipe { get; }
            public string Relationship { get; }
            public GUIContent Label { get; }
            public GUIContent MenuLabel { get; }

            public RecipeLink(PrefabStyleRecipe recipe, string relationship, GUIContent label, GUIContent menuLabel)
            {
                Recipe = recipe;
                Relationship = relationship;
                Label = label;
                MenuLabel = menuLabel;
            }
        }
    }
}
