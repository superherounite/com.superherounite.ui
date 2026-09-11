using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class PlayModeTuningSessionTests
    {
        private const string StatePrefix = "SuperHeroUI.PlayModeTuningSessionTests.";
        private static readonly string[] s_assetNames =
        {
            "Owner.prefab", "Color.asset", "Image.asset", "Recipe.asset", "Registry.asset",
        };

        private static string TestRoot => SessionState.GetString(StatePrefix + "Root", string.Empty);

        [SetUp]
        public void SetUp()
        {
            // Unity reruns NUnit SetUp when resuming an Edit Mode test after a domain reload.
            // Keep the same assets, original settings, and file snapshots until its TearDown.
            if (!string.IsNullOrEmpty(TestRoot))
            {
                Assert.That(AssetDatabase.IsValidFolder(TestRoot), Is.True,
                    "The active tuning fixture disappeared across a domain reload.");
                return;
            }

            SessionState.SetBool(StatePrefix + "OptionsEnabled", EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(StatePrefix + "Options", (int)EditorSettings.enterPlayModeOptions);
            EditorSettings.enterPlayModeOptionsEnabled = false;
            string testRoot = "Assets/__SuperHeroUIPlayModeTuningTests_" + Guid.NewGuid().ToString("N");
            SessionState.SetString(StatePrefix + "Root", testRoot);
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(testRoot));
            CreateFixture();
            foreach (string name in s_assetNames)
            {
                SessionState.SetString(StatePrefix + name, File.ReadAllText(TestRoot + "/" + name));
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EditorApplication.isPlaying)
            {
                yield return new ExitPlayMode();
            }

            int windowInstanceId = SessionState.GetInt(StatePrefix + "WindowInstanceId", 0);
            if (windowInstanceId != 0)
            {
                if (EditorUtility.InstanceIDToObject(windowInstanceId) is PlayModeTuningWindow window)
                {
                    window.Close();
                }

                PlayModeTuningState.Session.Clear();
                SessionState.EraseInt(StatePrefix + "WindowInstanceId");
            }

            int lifecycleInstanceId = SessionState.GetInt(StatePrefix + "LifecycleInstanceId", 0);
            if (lifecycleInstanceId != 0)
            {
                PlayModeTuningState.Session.Clear();
                Object lifecycleTarget = EditorUtility.InstanceIDToObject(lifecycleInstanceId);
                if (lifecycleTarget != null)
                {
                    Object.DestroyImmediate(lifecycleTarget);
                }

                SessionState.EraseInt(StatePrefix + "LifecycleInstanceId");
            }

            string testRoot = TestRoot;
            bool folderDeleted = !AssetDatabase.IsValidFolder(testRoot) || AssetDatabase.DeleteAsset(testRoot);

            EditorSettings.enterPlayModeOptions =
                (EnterPlayModeOptions)SessionState.GetInt(StatePrefix + "Options", 0);
            EditorSettings.enterPlayModeOptionsEnabled =
                SessionState.GetBool(StatePrefix + "OptionsEnabled", false);
            Assert.That(folderDeleted && !Directory.Exists(testRoot), Is.True,
                "Could not remove the tuning fixture: " + testRoot);
            SessionState.EraseString(StatePrefix + "Root");
            SessionState.EraseString(StatePrefix + "Drafts");
            SessionState.EraseBool(StatePrefix + "OptionsEnabled");
            SessionState.EraseInt(StatePrefix + "Options");
            foreach (string name in s_assetNames)
            {
                SessionState.EraseString(StatePrefix + name);
            }
        }

        [UnityTest]
        public IEnumerator LivePreviewSurvivesSerializationAndReviewedWriteCanBeUndoneAndBaked()
        {
            yield return new EnterPlayMode();

            Fixture liveFixture = LoadFixture();
            Image liveImage = InstantiateImage(liveFixture);
            var liveSession = new PlayModeTuningSession();
            liveSession.SetColor(liveFixture.ColorBinding, liveImage, Color.magenta);
            liveSession.SetFloat(liveFixture.MultiplierBinding, liveImage, 3.25f);

            Assert.That(liveImage.color, Is.EqualTo(Color.magenta));
            Assert.That(liveImage.pixelsPerUnitMultiplier, Is.EqualTo(3.25f));
            Assert.That(liveFixture.Color.Value, Is.EqualTo(Color.white));
            Assert.That(liveFixture.Image.PixelsPerUnitMultiplier, Is.EqualTo(1f));
            AssertFilesUnchanged();
            PlayModeTuningSession roundTrip = PlayModeTuningSession.FromJson(liveSession.ToJson());
            Assert.That(roundTrip.Drafts.Count, Is.EqualTo(2));
            Assert.That(roundTrip.Drafts.Single(draft => draft.IsColor).ColorValue,
                Is.EqualTo(Color.magenta));
            Assert.That(roundTrip.Drafts.Single(draft => !draft.IsColor).FloatValue,
                Is.EqualTo(3.25f));
            StoreDrafts(liveSession);
            liveSession.RestoreLiveValues();
            Assert.That(liveImage.color, Is.EqualTo(Color.white));
            Assert.That(liveImage.pixelsPerUnitMultiplier, Is.EqualTo(1f));

            yield return new ExitPlayMode();

            Fixture fixture = LoadFixture();
            PlayModeTuningSession session = RestoreDrafts();
            AssertFilesUnchanged();
            PlayModeTuningReview review = session.Review();
            Assert.That(review.Errors, Is.Empty);
            Assert.That(review.Changes.Count, Is.EqualTo(2));
            AssertFilesUnchanged();

            session.WriteReviewedChanges(review);

            Assert.That(fixture.Color.Value, Is.EqualTo(Color.magenta));
            Assert.That(fixture.Image.PixelsPerUnitMultiplier, Is.EqualTo(3.25f));
            Assert.That(session.Drafts, Is.Empty);
            Assert.That(File.ReadAllText(TestRoot + "/Owner.prefab"),
                Is.EqualTo(SessionState.GetString(StatePrefix + "Owner.prefab", string.Empty)));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(fixture.Color.Value, Is.EqualTo(Color.white));
            Assert.That(fixture.Image.PixelsPerUnitMultiplier, Is.EqualTo(1f));
            Undo.PerformRedo();
            Assert.That(fixture.Color.Value, Is.EqualTo(Color.magenta));
            Assert.That(fixture.Image.PixelsPerUnitMultiplier, Is.EqualTo(3.25f));

            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Stale));
            StyleReview applied = StyleRecipeProcessor.Apply(fixture.Registry, preview);
            Assert.That(applied.State, Is.EqualTo(StyleReviewState.Ready));
            Assert.That(StyleRecipeProcessor.Preview(fixture.Registry).State, Is.EqualTo(StyleReviewState.Ready));
            string bakedPrefab = File.ReadAllText(TestRoot + "/Owner.prefab");
            StyleReview repeated = StyleRecipeProcessor.Apply(fixture.Registry, applied);
            Assert.That(repeated.State, Is.EqualTo(StyleReviewState.Ready));
            Assert.That(repeated.Changes, Is.Empty);
            Assert.That(File.ReadAllText(TestRoot + "/Owner.prefab"), Is.EqualTo(bakedPrefab));
            GameObject bakedRoot = PrefabUtility.LoadPrefabContents(TestRoot + "/Owner.prefab");
            try
            {
                Image bakedImage = bakedRoot.GetComponent<Image>();
                Assert.That(bakedImage.color, Is.EqualTo(Color.magenta));
                Assert.That(bakedImage.pixelsPerUnitMultiplier, Is.EqualTo(3.25f));
                Assert.That(bakedImage.raycastTarget, Is.False);
                Assert.That(((RectTransform)bakedRoot.transform).anchoredPosition,
                    Is.EqualTo(new Vector2(17f, 29f)));
                Assert.That(bakedRoot.GetComponentsInChildren<Component>(true)
                    .Any(component => component != null
                        && component.GetType().Assembly == typeof(PlayModeTuningSession).Assembly), Is.False);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(bakedRoot);
            }
        }

        [UnityTest]
        public IEnumerator ExitingPlayModeRestoresSceneValuesAndKeepsDraftsWhenReloadsAreDisabled()
        {
            PlayModeTuningState.Session.Clear();
            Fixture fixture = LoadFixture();
            Image sceneImage = InstantiateImage(fixture);
            SessionState.SetInt(StatePrefix + "LifecycleInstanceId", sceneImage.gameObject.GetInstanceID());
            EditorSettings.enterPlayModeOptions =
                EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            EditorSettings.enterPlayModeOptionsEnabled = true;

            yield return new EnterPlayMode(false);

            var liveObject = (GameObject)EditorUtility.InstanceIDToObject(
                SessionState.GetInt(StatePrefix + "LifecycleInstanceId", 0));
            Image liveImage = liveObject.GetComponent<Image>();
            Fixture liveFixture = LoadFixture();
            PlayModeTuningState.Session.SetColor(liveFixture.ColorBinding, liveImage, Color.magenta);
            PlayModeTuningState.Session.SetFloat(liveFixture.MultiplierBinding, liveImage, 3.25f);
            Assert.That(liveImage.color, Is.EqualTo(Color.magenta));
            Assert.That(liveImage.pixelsPerUnitMultiplier, Is.EqualTo(3.25f));
            Assert.That(PlayModeTuningState.Session.HasLiveValues, Is.True);
            AssertFilesUnchanged();

            yield return new ExitPlayMode();

            var restoredObject = (GameObject)EditorUtility.InstanceIDToObject(
                SessionState.GetInt(StatePrefix + "LifecycleInstanceId", 0));
            Assert.That(restoredObject, Is.Not.Null);
            Image restoredImage = restoredObject.GetComponent<Image>();
            Assert.That(restoredImage.color, Is.EqualTo(Color.white));
            Assert.That(restoredImage.pixelsPerUnitMultiplier, Is.EqualTo(1f));
            Assert.That(PlayModeTuningState.Session.HasLiveValues, Is.False);
            Assert.That(PlayModeTuningState.Session.Drafts.Count, Is.EqualTo(2));
            Assert.That(PlayModeTuningState.Session.Drafts.Single(draft => draft.IsColor).ColorValue,
                Is.EqualTo(Color.magenta));
            Assert.That(PlayModeTuningState.Session.Drafts.Single(draft => !draft.IsColor).FloatValue,
                Is.EqualTo(3.25f));
            Assert.That(PlayModeTuningState.Session.Review().Errors, Is.Empty);
            AssertFilesUnchanged();
        }

        [UnityTest]
        public IEnumerator RestorePreservesLaterRuntimeWritesAndCanRestoreRepeatedTuning()
        {
            yield return new EnterPlayMode();

            Fixture fixture = LoadFixture();
            Image image = InstantiateImage(fixture);
            var session = new PlayModeTuningSession();
            session.SetColor(fixture.ColorBinding, image, Color.red);
            session.SetColor(fixture.ColorBinding, image, Color.magenta);
            session.SetFloat(fixture.MultiplierBinding, image, 2f);
            image.pixelsPerUnitMultiplier = 4f;

            session.RestoreLiveValues();

            Assert.That(image.color, Is.EqualTo(Color.white));
            Assert.That(image.pixelsPerUnitMultiplier, Is.EqualTo(4f));
            Assert.That(session.Drafts.Count, Is.EqualTo(2));
            session.SetColor(fixture.ColorBinding, image, Color.yellow);
            session.RemoveDraft(session.Drafts.Single(draft => draft.IsColor));
            Assert.That(image.color, Is.EqualTo(Color.white));
            Assert.That(session.Drafts.Count, Is.EqualTo(1));
            session.SetFloat(fixture.MultiplierBinding, image, 5f);
            session.Clear();
            Assert.That(image.pixelsPerUnitMultiplier, Is.EqualTo(4f));
            Assert.That(session.Drafts, Is.Empty);
            AssertFilesUnchanged();

            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator InvalidPreviewValuesAndTargetsCannotChangeTheSessionOrAssets()
        {
            yield return new EnterPlayMode();

            AssertInvalidLiveValuesAndTargetsAreRejected();

            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ReviewRejectsSourceChangesAndBindingsThatWereReboundAfterTuning()
        {
            yield return new EnterPlayMode();

            Fixture liveFixture = LoadFixture();
            Image image = InstantiateImage(liveFixture);
            var liveSession = new PlayModeTuningSession();
            liveSession.SetColor(liveFixture.ColorBinding, image, Color.magenta);
            liveSession.SetFloat(liveFixture.MultiplierBinding, image, 3f);
            StoreDrafts(liveSession);
            liveSession.RestoreLiveValues();

            yield return new ExitPlayMode();

            AssertChangedAndReboundSourcesAreRejected();
        }

        [UnityTest]
        public IEnumerator ConflictingSharedTokenDraftsAndStaleReviewsRejectTheWholeWrite()
        {
            yield return new EnterPlayMode();

            Fixture liveFixture = LoadFixture();
            Image image = InstantiateImage(liveFixture);
            var liveSession = new PlayModeTuningSession();
            PlayModeTuningBinding secondColor = PlayModeTuningBinding.GetBindings(liveFixture.Recipe)
                .Where(binding => binding.IsColor).Last();
            liveSession.SetColor(liveFixture.ColorBinding, image, Color.red);
            liveSession.SetColor(secondColor, image.transform.GetChild(0).GetComponent<Image>(), Color.blue);
            liveSession.SetFloat(liveFixture.MultiplierBinding, image, 3f);
            StoreDrafts(liveSession);
            liveSession.RestoreLiveValues();

            yield return new ExitPlayMode();

            AssertConflictingDraftsAndStaleReviewsAreRejected();
        }

        [UnityTest]
        public IEnumerator MissingSourceAfterReviewPreventsWritingOtherSources()
        {
            yield return new EnterPlayMode();

            Fixture liveFixture = LoadFixture();
            Image image = InstantiateImage(liveFixture);
            var liveSession = new PlayModeTuningSession();
            liveSession.SetFloat(liveFixture.MultiplierBinding, image, 3f);
            liveSession.SetColor(liveFixture.ColorBinding, image, Color.magenta);
            StoreDrafts(liveSession);
            liveSession.RestoreLiveValues();

            yield return new ExitPlayMode();

            AssertMissingSourcePreventsAllWrites();
        }

        [UnityTest]
        public IEnumerator UnrelatedUnsavedStyleChangesBlockWritingUntilTheAssetIsSaved()
        {
            yield return new EnterPlayMode();

            Fixture liveFixture = LoadFixture();
            Image image = InstantiateImage(liveFixture);
            var liveSession = new PlayModeTuningSession();
            liveSession.SetFloat(liveFixture.MultiplierBinding, image, 3f);
            StoreDrafts(liveSession);
            liveSession.RestoreLiveValues();

            yield return new ExitPlayMode();

            AssertUnrelatedUnsavedStyleChangesBlockWriting();
        }

        [UnityTest]
        [Category("EditorGraphics")]
        public IEnumerator WindowRepaintsLiveDraftsAndReviewAndRestoresValuesWhenClosed()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Opening the tuning window requires a graphics device. Run without -nographics to verify its UI.");
            }

            yield return new EnterPlayMode();

            PlayModeTuningState.Session.Clear();
            Fixture liveFixture = LoadFixture();
            Image liveImage = InstantiateImage(liveFixture);
            PlayModeTuningWindow.Open();
            PlayModeTuningWindow liveWindow = EditorWindow.GetWindow<PlayModeTuningWindow>();
            SessionState.SetInt(StatePrefix + "WindowInstanceId", liveWindow.GetInstanceID());
            ConfigureWindow(liveWindow, liveFixture, liveImage);
            liveWindow.Focus();
            liveWindow.Repaint();
            for (int frame = 0; frame < 20 && (bool)GetField(liveWindow, "_refreshBindings"); frame++)
            {
                yield return null;
            }

            Assert.That(GetField(liveWindow, "_refreshBindings"), Is.False,
                "The window must process a real OnGUI Layout event.");
            Assert.That(GetField(liveWindow, "_runtimeTarget"), Is.SameAs(liveImage));
            PlayModeTuningState.Session.SetColor(liveFixture.ColorBinding, liveImage, Color.magenta);
            PlayModeTuningState.Session.SetFloat(liveFixture.MultiplierBinding, liveImage, 3.25f);
            liveWindow.Repaint();
            yield return null;
            yield return null;
            LogAssert.NoUnexpectedReceived();
            AssertFilesUnchanged();

            liveWindow.Close();

            Assert.That(liveImage.color, Is.EqualTo(Color.white));
            Assert.That(liveImage.pixelsPerUnitMultiplier, Is.EqualTo(1f));
            Assert.That(PlayModeTuningState.Session.HasLiveValues, Is.False);
            Assert.That(PlayModeTuningState.Session.Drafts.Count, Is.EqualTo(2));

            yield return new ExitPlayMode();

            PlayModeTuningWindow.Open();
            PlayModeTuningWindow reviewWindow = EditorWindow.GetWindow<PlayModeTuningWindow>();
            SessionState.SetInt(StatePrefix + "WindowInstanceId", reviewWindow.GetInstanceID());
            ConfigureWindow(reviewWindow, LoadFixture(), null);
            PlayModeTuningReview review = PlayModeTuningState.Session.Review();
            Assert.That(review.Errors, Is.Empty);
            Assert.That(review.Changes.Count, Is.EqualTo(2));
            SetField(reviewWindow, "_review", review);
            reviewWindow.Focus();
            reviewWindow.Repaint();
            for (int frame = 0; frame < 20 && (bool)GetField(reviewWindow, "_refreshBindings"); frame++)
            {
                yield return null;
            }

            yield return null;
            Assert.That(GetField(reviewWindow, "_refreshBindings"), Is.False);
            Assert.That(GetField(reviewWindow, "_review"), Is.SameAs(review));
            LogAssert.NoUnexpectedReceived();
            AssertFilesUnchanged();
        }

        [Test]
        public void LiveChangesAreRejectedOutsidePlayMode()
        {
            Fixture fixture = LoadFixture();
            var session = new PlayModeTuningSession();
            var target = new GameObject("Edit Mode Target", typeof(RectTransform), typeof(Image));
            try
            {
                Image image = target.GetComponent<Image>();
                Assert.That(() => session.SetColor(fixture.ColorBinding, image, Color.magenta),
                    Throws.InvalidOperationException);
                Assert.That(() => session.SetFloat(fixture.MultiplierBinding, image, 3f),
                    Throws.InvalidOperationException);
                Assert.That(session.Drafts, Is.Empty);
                Assert.That(image.color, Is.EqualTo(Color.white));
                Assert.That(image.pixelsPerUnitMultiplier, Is.EqualTo(1f));
                AssertFilesUnchanged();
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        private static void AssertInvalidLiveValuesAndTargetsAreRejected()
        {
            Fixture fixture = LoadFixture();
            Image image = InstantiateImage(fixture);
            var session = new PlayModeTuningSession();
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, 0.009f })
            {
                Assert.That(() => session.SetFloat(fixture.MultiplierBinding, image, value), Throws.Exception);
            }

            Assert.That(() => session.SetColor(fixture.ColorBinding, image,
                new Color(float.NaN, 1f, 1f, 1f)), Throws.Exception);
            Assert.That(() => session.SetColor(fixture.ColorBinding, image,
                new Color(1f, 1f, 1f, float.PositiveInfinity)), Throws.Exception);
            Assert.That(() => session.SetFloat(fixture.MultiplierBinding,
                fixture.Owner.GetComponent<Image>(), 2f), Throws.Exception);
            Assert.That(() => session.SetColor(fixture.ColorBinding, image.transform, Color.red), Throws.Exception);
            Assert.That(() => session.SetColor(fixture.ColorBinding, null, Color.red), Throws.Exception);
            Assert.That(session.Drafts, Is.Empty);
            Assert.That(image.color, Is.EqualTo(Color.white));
            Assert.That(image.pixelsPerUnitMultiplier, Is.EqualTo(1f));
            Assert.That(() => session.Review(), Throws.InvalidOperationException);
            session.SetFloat(fixture.MultiplierBinding, image, 0.01f);
            Assert.That(image.pixelsPerUnitMultiplier, Is.EqualTo(0.01f));
            AssertFilesUnchanged();
            session.RestoreLiveValues();
        }

        private static void AssertChangedAndReboundSourcesAreRejected()
        {
            Fixture fixture = LoadFixture();
            PlayModeTuningSession session = RestoreDrafts();
            SetField(fixture.Color, "_value", Color.cyan);
            PlayModeTuningReview changedSource = session.Review();
            Assert.That(changedSource.Errors, Is.Not.Empty);
            Assert.That(() => session.WriteReviewedChanges(changedSource), Throws.InvalidOperationException);
            Assert.That(fixture.Image.PixelsPerUnitMultiplier, Is.EqualTo(1f));
            SetField(fixture.Color, "_value", Color.white);
            AssetDatabase.SaveAssetIfDirty(fixture.Color);
            Assert.That(session.Review().Errors, Is.Empty);
            ColorToken replacement = CreateAsset<ColorToken>("Replacement.asset");
            SetField(fixture.Image, "_tint", replacement);
            PlayModeTuningReview rebound = session.Review();
            Assert.That(rebound.Errors, Is.Not.Empty);
            Assert.That(() => session.WriteReviewedChanges(rebound), Throws.InvalidOperationException);
            Assert.That(replacement.Value, Is.EqualTo(Color.white));
            Assert.That(fixture.Image.PixelsPerUnitMultiplier, Is.EqualTo(1f));
            Assert.That(session.Drafts.Count, Is.EqualTo(2));
        }

        private static void AssertConflictingDraftsAndStaleReviewsAreRejected()
        {
            Fixture fixture = LoadFixture();
            PlayModeTuningSession session = RestoreDrafts();
            PlayModeTuningReview conflict = session.Review();
            Assert.That(conflict.Errors, Is.Not.Empty);
            Assert.That(() => session.WriteReviewedChanges(conflict), Throws.InvalidOperationException);
            Assert.That(fixture.Color.Value, Is.EqualTo(Color.white));
            Assert.That(fixture.Image.PixelsPerUnitMultiplier, Is.EqualTo(1f));
            session.RemoveDraft(session.Drafts.Single(draft => draft.IsColor && draft.ColorValue == Color.blue));
            PlayModeTuningReview valid = session.Review();
            Assert.That(valid.Errors, Is.Empty);
            session.RemoveDraft(session.Drafts.Single(draft => !draft.IsColor));
            Assert.That(() => session.WriteReviewedChanges(valid), Throws.InvalidOperationException);
            Assert.That(fixture.Color.Value, Is.EqualTo(Color.white));
            Assert.That(session.Drafts.Count, Is.EqualTo(1));
            AssertFilesUnchanged();
        }

        private static void AssertMissingSourcePreventsAllWrites()
        {
            Fixture fixture = LoadFixture();
            PlayModeTuningSession session = RestoreDrafts();
            PlayModeTuningReview review = session.Review();
            Assert.That(review.Errors, Is.Empty);
            Assert.That(AssetDatabase.DeleteAsset(TestRoot + "/Color.asset"), Is.True);

            Assert.That(() => session.WriteReviewedChanges(review), Throws.InvalidOperationException);

            Assert.That(fixture.Image.PixelsPerUnitMultiplier, Is.EqualTo(1f));
            Assert.That(session.Review().Errors, Is.Not.Empty);
            Assert.That(session.Drafts.Count, Is.EqualTo(2));
            Assert.That(File.ReadAllText(TestRoot + "/Image.asset"),
                Is.EqualTo(SessionState.GetString(StatePrefix + "Image.asset", string.Empty)));
            Assert.That(File.ReadAllText(TestRoot + "/Owner.prefab"),
                Is.EqualTo(SessionState.GetString(StatePrefix + "Owner.prefab", string.Empty)));
        }

        private static void AssertUnrelatedUnsavedStyleChangesBlockWriting()
        {
            Fixture fixture = LoadFixture();
            PlayModeTuningSession session = RestoreDrafts();
            using (var serialized = new SerializedObject(fixture.Image))
            {
                serialized.FindProperty("_fillCenter").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            Assert.That(EditorUtility.IsDirty(fixture.Image), Is.True);
            PlayModeTuningReview blockedReview = session.Review();
            Assert.That(blockedReview.Errors, Is.Not.Empty);
            Assert.That(() => session.WriteReviewedChanges(blockedReview), Throws.InvalidOperationException);
            Assert.That(fixture.Image.PixelsPerUnitMultiplier, Is.EqualTo(1f));
            Assert.That(fixture.Image.FillCenter, Is.False);
            AssertFilesUnchanged();

            AssetDatabase.SaveAssetIfDirty(fixture.Image);

            PlayModeTuningReview savedReview = session.Review();
            Assert.That(savedReview.Errors, Is.Empty);
            Assert.That(savedReview.Changes.Count, Is.EqualTo(1));
            session.WriteReviewedChanges(savedReview);
            Assert.That(fixture.Image.PixelsPerUnitMultiplier, Is.EqualTo(3f));
            Assert.That(fixture.Image.FillCenter, Is.False);
        }

        private static void CreateFixture()
        {
            ColorToken color = CreateAsset<ColorToken>("Color.asset");
            ImageStyle image = CreateAsset<ImageStyle>("Image.asset");
            SetField(image, "_tint", color);
            SetField(image, "_ownsPixelsPerUnitMultiplier", true);
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            GameObject owner;
            try
            {
                var root = new GameObject("Owner", typeof(RectTransform), typeof(Image));
                SceneManager.MoveGameObjectToScene(root, previewScene);
                root.GetComponent<Image>().raycastTarget = false;
                ((RectTransform)root.transform).anchoredPosition = new Vector2(17f, 29f);
                var child = new GameObject("Shared Style", typeof(RectTransform), typeof(Image));
                SceneManager.MoveGameObjectToScene(child, previewScene);
                child.transform.SetParent(root.transform, false);
                owner = PrefabUtility.SaveAsPrefabAsset(root, TestRoot + "/Owner.prefab");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }

            GameObject loadedRoot = PrefabUtility.LoadPrefabContents(TestRoot + "/Owner.prefab");
            PrefabStyleRecipe.ImageBinding[] imageBindings;
            try
            {
                imageBindings = loadedRoot.GetComponentsInChildren<Image>(true).Select(component =>
                {
                    var target = new PrefabTargetReference();
                    target.Configure(GlobalObjectId.GetGlobalObjectIdSlow(component).ToString(),
                        PrefabTargetResolver.GetDisplayPath(loadedRoot.transform, component.transform),
                        component.GetType().FullName, PrefabTargetKind.Image);
                    var binding = new PrefabStyleRecipe.ImageBinding();
                    SetField(binding, "_target", target);
                    SetField(binding, "_style", image);
                    return binding;
                }).ToArray();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(loadedRoot);
            }

            PrefabStyleRecipe recipe = CreateAsset<PrefabStyleRecipe>("Recipe.asset");
            SetField(recipe, "_ownerPrefab", owner);
            SetField(recipe, "_images", imageBindings);
            StyleRecipeRegistry registry = CreateAsset<StyleRecipeRegistry>("Registry.asset");
            SetField(registry, "_recipes", new[] { recipe });
            SetField(registry, "_validateBeforePlay", false);
            AssetDatabase.SaveAssets();
        }

        private static Fixture LoadFixture()
        {
            return new Fixture();
        }

        private static Image InstantiateImage(Fixture fixture)
        {
            return Object.Instantiate(fixture.Owner).GetComponent<Image>();
        }

        private static void StoreDrafts(PlayModeTuningSession session)
        {
            SessionState.SetString(StatePrefix + "Drafts", session.ToJson());
        }

        private static PlayModeTuningSession RestoreDrafts()
        {
            return PlayModeTuningSession.FromJson(SessionState.GetString(StatePrefix + "Drafts", string.Empty));
        }

        private static void AssertFilesUnchanged()
        {
            foreach (string name in s_assetNames)
            {
                Assert.That(File.ReadAllText(TestRoot + "/" + name),
                    Is.EqualTo(SessionState.GetString(StatePrefix + name, string.Empty)),
                    "Tuning unexpectedly changed " + name);
            }
        }

        private static T CreateAsset<T>(string name) where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, TestRoot + "/" + name);
            return asset;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing test field {target.GetType().Name}.{name}");
            field.SetValue(target, value);
            if (target is Object asset)
            {
                EditorUtility.SetDirty(asset);
            }
        }

        private static object GetField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing test field {target.GetType().Name}.{name}");
            return field.GetValue(target);
        }

        private static void ConfigureWindow(PlayModeTuningWindow window, Fixture fixture, Component runtimeTarget)
        {
            SetField(window, "_recipe", fixture.Recipe);
            SetField(window, "_targetId", fixture.ColorBinding.TargetReference.GlobalObjectId);
            SetField(window, "_runtimeTarget", runtimeTarget);
            SetField(window, "_refreshBindings", true);
        }

        private sealed class Fixture
        {
            public GameObject Owner { get; } = AssetDatabase.LoadAssetAtPath<GameObject>(TestRoot + "/Owner.prefab");
            public ColorToken Color { get; } = AssetDatabase.LoadAssetAtPath<ColorToken>(TestRoot + "/Color.asset");
            public ImageStyle Image { get; } = AssetDatabase.LoadAssetAtPath<ImageStyle>(TestRoot + "/Image.asset");
            public PrefabStyleRecipe Recipe { get; } =
                AssetDatabase.LoadAssetAtPath<PrefabStyleRecipe>(TestRoot + "/Recipe.asset");
            public StyleRecipeRegistry Registry { get; } =
                AssetDatabase.LoadAssetAtPath<StyleRecipeRegistry>(TestRoot + "/Registry.asset");
            public PlayModeTuningBinding ColorBinding =>
                PlayModeTuningBinding.GetBindings(Recipe).First(binding => binding.IsColor);
            public PlayModeTuningBinding MultiplierBinding =>
                PlayModeTuningBinding.GetBindings(Recipe).First(binding => !binding.IsColor);
        }
    }
}
