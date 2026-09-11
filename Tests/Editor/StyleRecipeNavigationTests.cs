using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleRecipeNavigationTests
    {
        [Test]
        [Category("EditorGraphics")]
        public void OpenRecipeCreatesSeparateInspectorWithoutChangingPrefabSelectionOrFiles()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Opening an Inspector requires a graphics device. Run without -nographics to verify navigation.");
            }

            string testRoot = "Assets/__SuperHeroUIRecipeNavigationTests_" + Guid.NewGuid().ToString("N");
            Object[] previousSelection = Selection.objects;
            Object previousActiveObject = Selection.activeObject;
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene previewScene = default;
            GameObject temporaryRoot = null;
            HashSet<int> originalWindowIds = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(testRoot));
                previewScene = EditorSceneManager.NewPreviewScene();
                temporaryRoot = new GameObject("Zone Danger Button", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(temporaryRoot, previewScene);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temporaryRoot, testRoot + "/Button.prefab");
                Object.DestroyImmediate(temporaryRoot);
                temporaryRoot = null;
                var recipe = ScriptableObject.CreateInstance<PrefabStyleRecipe>();
                var serializedRecipe = new SerializedObject(recipe);
                serializedRecipe.FindProperty("_ownerPrefab").objectReferenceValue = prefab;
                serializedRecipe.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(recipe, testRoot + "/Unrelated Recipe.asset");
                AssetDatabase.SaveAssetIfDirty(recipe);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, previewScene);
                Selection.activeGameObject = instance;
                Assert.That(Selection.activeGameObject, Is.EqualTo(instance));
                Object[] selectedObjects = Selection.objects;
                originalWindowIds = Resources.FindObjectsOfTypeAll<EditorWindow>()
                    .Select(window => window.GetInstanceID()).ToHashSet();
                Dictionary<string, byte[]> before = CaptureFiles(testRoot);
                int dirtyCount = EditorUtility.GetDirtyCount(recipe);
                bool sceneDirty = previewScene.isDirty;
                MethodInfo openRecipe = typeof(StyleRecipeNavigation).GetMethod(
                    "OpenRecipe", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(openRecipe, Is.Not.Null);

                openRecipe.Invoke(null, new object[] { recipe });

                EditorWindow[] openedWindows = Resources.FindObjectsOfTypeAll<EditorWindow>()
                    .Where(window => !originalWindowIds.Contains(window.GetInstanceID())).ToArray();
                Assert.That(openedWindows.Any(window => window.GetType().Name == "PropertyEditor"), Is.True,
                    "Opening a recipe should create a separate Property Editor window.");
                Assert.That(Selection.activeGameObject, Is.EqualTo(instance));
                Assert.That(Selection.objects, Is.EqualTo(selectedObjects));
                Assert.That(EditorUtility.GetDirtyCount(recipe), Is.EqualTo(dirtyCount));
                Assert.That(previewScene.isDirty, Is.EqualTo(sceneDirty));
                Dictionary<string, byte[]> after = CaptureFiles(testRoot);
                Assert.That(after.Keys, Is.EquivalentTo(before.Keys));
                foreach (KeyValuePair<string, byte[]> file in before)
                {
                    Assert.That(after[file.Key], Is.EqualTo(file.Value), "Navigation wrote " + file.Key);
                }
            }
            finally
            {
                if (originalWindowIds != null)
                {
                    foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    {
                        if (!originalWindowIds.Contains(window.GetInstanceID()))
                        {
                            window.Close();
                        }
                    }
                }

                Selection.objects = previousSelection.Where(target => target != null).ToArray();
                if (previousActiveObject != null)
                {
                    Selection.activeObject = previousActiveObject;
                }

                if (temporaryRoot != null)
                {
                    Object.DestroyImmediate(temporaryRoot);
                }

                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }

                if (previewScene.IsValid() && previewScene.isLoaded)
                {
                    EditorSceneManager.ClosePreviewScene(previewScene);
                }

                if (AssetDatabase.IsValidFolder(testRoot))
                {
                    AssetDatabase.DeleteAsset(testRoot);
                }
            }
        }

        private static Dictionary<string, byte[]> CaptureFiles(string root)
        {
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        }
    }
}
