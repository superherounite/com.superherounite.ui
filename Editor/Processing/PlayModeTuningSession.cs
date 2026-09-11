using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor
{
    [Serializable]
    internal sealed class PlayModeTuningSession
    {
        [SerializeField] private List<PlayModeTuningDraft> _drafts = new();
        [NonSerialized] private readonly List<LiveChange> _liveChanges = new();

        internal IReadOnlyList<PlayModeTuningDraft> Drafts => _drafts;
        internal bool HasLiveValues => _liveChanges.Count > 0;
        internal event Action Changed;

        internal void SetColor(PlayModeTuningBinding binding, Component target, Color value)
        {
            RequirePlayMode();
            if (!IsFinite(value))
            {
                throw new InvalidOperationException("Color channels must be finite numbers.");
            }

            if (!binding.IsColor)
            {
                throw new InvalidOperationException("This binding owns PPUM, not a color.");
            }

            PlayModeTuningDraft draft = PrepareDraft(binding, target);
            LiveChange live = GetLiveChange(binding, target, draft);
            binding.WriteColor(target, value);
            live.LastColor = value;
            draft.SetColor(value);
            Changed?.Invoke();
        }

        internal void SetFloat(PlayModeTuningBinding binding, Component target, float value)
        {
            RequirePlayMode();
            if (!IsFinite(value) || value < 0.01f)
            {
                throw new InvalidOperationException("PPUM must be a finite number of at least 0.01.");
            }

            if (binding.IsColor)
            {
                throw new InvalidOperationException("This binding owns a color, not PPUM.");
            }

            PlayModeTuningDraft draft = PrepareDraft(binding, target);
            LiveChange live = GetLiveChange(binding, target, draft);
            binding.WriteFloat(target, value);
            live.LastFloat = value;
            draft.SetFloat(value);
            Changed?.Invoke();
        }

        internal PlayModeTuningDraft FindDraft(PlayModeTuningBinding binding)
        {
            string recipeId = PlayModeTuningDraft.GetAssetId(binding.Recipe);
            return _drafts.FirstOrDefault(draft => draft.RecipeId == recipeId && draft.BindingKey == binding.Key);
        }

        internal void RestoreLiveValues()
        {
            foreach (LiveChange live in _liveChanges)
            {
                live.Restore();
            }

            _liveChanges.Clear();
            Changed?.Invoke();
        }

        internal void RemoveDraft(PlayModeTuningDraft draft)
        {
            foreach (LiveChange live in _liveChanges.Where(change => change.Draft == draft).ToArray())
            {
                live.Restore();
                _liveChanges.Remove(live);
            }

            _drafts.Remove(draft);
            Changed?.Invoke();
        }

        internal void Clear()
        {
            RestoreLiveValues();
            _drafts.Clear();
            Changed?.Invoke();
        }

        internal string ToJson()
        {
            return JsonUtility.ToJson(this);
        }

        internal static PlayModeTuningSession FromJson(string json)
        {
            var session = new PlayModeTuningSession();
            if (!string.IsNullOrEmpty(json))
            {
                JsonUtility.FromJsonOverwrite(json, session);
            }

            session._drafts ??= new List<PlayModeTuningDraft>();
            return session;
        }

        internal PlayModeTuningReview Review()
        {
            RequireEditMode();
            var review = new PlayModeTuningReview();
            var fingerprint = new StringBuilder(ToJson());
            var writes = new Dictionary<string, PlayModeTuningDraft>(StringComparer.Ordinal);
            foreach (PlayModeTuningDraft draft in _drafts)
            {
                if (!draft.TryResolve(out PlayModeTuningBinding binding, out string error))
                {
                    review.AddError(error);
                    continue;
                }

                Object asset = binding.SourceAsset;
                string path = AssetDatabase.GetAssetPath(asset);
                fingerprint.AppendLine(EditorJsonUtility.ToJson(binding.Recipe));
                fingerprint.AppendLine(EditorJsonUtility.ToJson(asset));
                fingerprint.AppendLine(path);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)
                    || !AssetDatabase.IsOpenForEdit(asset, StatusQueryOptions.ForceUpdate))
                {
                    review.AddError($"{draft.Label}: source must be an editable asset under Assets/: {path}");
                    continue;
                }

                if (AssetDatabase.LoadAllAssetsAtPath(path).Any(EditorUtility.IsDirty))
                {
                    review.AddError($"{path} has unsaved asset changes. Save or revert them before reviewing tuning drafts.");
                    continue;
                }

                if (draft.IsColor ? !IsFinite(draft.ColorValue)
                    : !IsFinite(draft.FloatValue) || draft.FloatValue < 0.01f)
                {
                    review.AddError($"{draft.Label}: the draft value is invalid.");
                    continue;
                }

                bool sourceUnchanged = draft.IsColor
                    ? binding.SourceColor.Equals(draft.OriginalColor)
                    : binding.SourceFloat.Equals(draft.OriginalFloat);
                bool alreadyApplied = draft.IsColor
                    ? binding.SourceColor.Equals(draft.ColorValue)
                    : binding.SourceFloat.Equals(draft.FloatValue);
                if (!sourceUnchanged && !alreadyApplied)
                {
                    review.AddError($"{draft.Label}: the source value changed since tuning. Discard this draft and tune again.");
                    continue;
                }

                if (writes.TryGetValue(draft.SourceId, out PlayModeTuningDraft other))
                {
                    bool equal = draft.IsColor == other.IsColor && (draft.IsColor
                        ? draft.ColorValue.Equals(other.ColorValue)
                        : draft.FloatValue.Equals(other.FloatValue));
                    if (!equal)
                    {
                        review.AddError($"Conflicting drafts for {path}: {other.Label} and {draft.Label}. Discard one draft.");
                    }
                }
                else
                {
                    writes.Add(draft.SourceId, draft);
                }
            }

            Dictionary<string, List<string>> usages = FindSourceUsages(writes.Keys);
            foreach (KeyValuePair<string, PlayModeTuningDraft> pair in writes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                PlayModeTuningDraft draft = pair.Value;
                Object asset = PlayModeTuningDraft.ResolveAsset(pair.Key);
                string usage = string.Join("\n", usages[pair.Key]);
                fingerprint.AppendLine(usage);
                bool different = draft.IsColor
                    ? !((ColorToken)asset).Value.Equals(draft.ColorValue)
                    : !((ImageStyle)asset).PixelsPerUnitMultiplier.Equals(draft.FloatValue);
                if (!different)
                {
                    continue;
                }

                string before = draft.IsColor
                    ? FormatColor(((ColorToken)asset).Value)
                    : FormatFloat(((ImageStyle)asset).PixelsPerUnitMultiplier);
                string after = draft.IsColor ? FormatColor(draft.ColorValue) : FormatFloat(draft.FloatValue);
                review.AddWrite(draft,
                    $"{AssetDatabase.GetAssetPath(asset)} ({asset.name})\n"
                    + $"{(draft.IsColor ? "Color" : "PPUM")}: {before} → {after}\n"
                    + $"Shared use: {usages[pair.Key].Count} binding(s)\n{usage}");
            }

            review.Fingerprint = Hash128.Compute(fingerprint.ToString()).ToString();
            return review;
        }

        internal void WriteReviewedChanges(PlayModeTuningReview review)
        {
            RequireEditMode();
            if (review == null)
            {
                throw new InvalidOperationException("Review Style Changes before writing them.");
            }

            PlayModeTuningReview current = Review();
            if (current.Errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join("\n", current.Errors));
            }

            if (review.Errors.Count > 0 || review.Fingerprint != current.Fingerprint)
            {
                throw new InvalidOperationException("Drafts or source dependencies changed after review. Review Style Changes again.");
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Write Play Mode Style Changes");
            var assets = new List<Object>();
            foreach (PlayModeTuningDraft draft in current.Writes)
            {
                Object asset = PlayModeTuningDraft.ResolveAsset(draft.SourceId);
                using var serialized = new SerializedObject(asset);
                SerializedProperty property = serialized.FindProperty(draft.IsColor ? "_value" : "_pixelsPerUnitMultiplier");
                if (draft.IsColor)
                {
                    property.colorValue = draft.ColorValue;
                }
                else
                {
                    property.floatValue = draft.FloatValue;
                }

                serialized.ApplyModifiedProperties();
                assets.Add(asset);
            }

            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            foreach (Object asset in assets)
            {
                AssetDatabase.SaveAssetIfDirty(asset);
            }

            Clear();
        }

        internal static string FormatColor(Color value)
        {
            return $"RGBA({FormatFloat(value.r)}, {FormatFloat(value.g)}, {FormatFloat(value.b)}, {FormatFloat(value.a)})";
        }

        internal static string FormatFloat(float value)
        {
            return value.ToString("G9", CultureInfo.InvariantCulture);
        }

        private PlayModeTuningDraft PrepareDraft(PlayModeTuningBinding binding, Component target)
        {
            if (!PlayModeTuningBinding.TryFindBinding(binding.Recipe, binding.Key, out PlayModeTuningBinding current)
                || current.SourceAsset != binding.SourceAsset)
            {
                throw new InvalidOperationException("The binding changed or has duplicate property ownership. Refresh the Recipe before tuning.");
            }

            if (!binding.ValidateTarget(target, out string error))
            {
                throw new InvalidOperationException(error);
            }

            if (!AssetDatabase.GetAssetPath(binding.Recipe).StartsWith("Assets/", StringComparison.Ordinal)
                || !AssetDatabase.GetAssetPath(binding.SourceAsset).StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Choose saved project Recipe and source assets under Assets/.");
            }

            PlayModeTuningDraft draft = FindDraft(binding);
            if (_liveChanges.Any(change => change.Target == target
                && change.Binding.Property == binding.Property && change.Draft != draft))
            {
                throw new InvalidOperationException("Another draft already tunes this component property. Restore Live Values first.");
            }

            if (draft != null)
            {
                if (!draft.TryResolve(out _, out error))
                {
                    throw new InvalidOperationException(error);
                }

                return draft;
            }

            draft = new PlayModeTuningDraft(binding);
            _drafts.Add(draft);
            return draft;
        }

        private LiveChange GetLiveChange(PlayModeTuningBinding binding, Component target, PlayModeTuningDraft draft)
        {
            LiveChange live = _liveChanges.FirstOrDefault(change => change.Target == target && change.Binding.Property == binding.Property);
            if (live != null && live.Draft != draft)
            {
                throw new InvalidOperationException("Another draft already tunes this component property. Restore Live Values first.");
            }

            if (live == null)
            {
                live = new LiveChange(binding, target, draft);
                _liveChanges.Add(live);
            }

            return live;
        }

        private static Dictionary<string, List<string>> FindSourceUsages(IEnumerable<string> sourceIds)
        {
            var usages = sourceIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
            if (usages.Count == 0)
            {
                return usages;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:PrefabStyleRecipe", new[] { "Assets" }).OrderBy(value => value, StringComparer.Ordinal))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (PrefabStyleRecipe recipe in AssetDatabase.LoadAllAssetsAtPath(path).OfType<PrefabStyleRecipe>())
                {
                    foreach (PlayModeTuningBinding binding in PlayModeTuningBinding.GetBindings(recipe))
                    {
                        if (!EditorUtility.IsPersistent(binding.SourceAsset))
                        {
                            continue;
                        }

                        string sourceId = PlayModeTuningDraft.GetAssetId(binding.SourceAsset);
                        if (usages.TryGetValue(sourceId, out List<string> references))
                        {
                            references.Add($"{path} ({recipe.name}) / {binding.Label}");
                        }
                    }
                }
            }

            foreach (List<string> references in usages.Values)
            {
                references.Sort(StringComparer.Ordinal);
            }

            return usages;
        }

        private static bool IsFinite(Color value)
        {
            return IsFinite(value.r) && IsFinite(value.g) && IsFinite(value.b) && IsFinite(value.a);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void RequirePlayMode()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                throw new InvalidOperationException("Enter Play Mode and wait for compilation and import to finish before tuning.");
            }
        }

        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                throw new InvalidOperationException("Return to Edit Mode and wait for compilation and import before reviewing or writing styles.");
            }
        }

        private sealed class LiveChange
        {
            private readonly Color _originalColor;
            private readonly float _originalFloat;

            internal PlayModeTuningBinding Binding { get; }
            internal Component Target { get; }
            internal PlayModeTuningDraft Draft { get; }
            internal Color LastColor { get; set; }
            internal float LastFloat { get; set; }

            internal LiveChange(PlayModeTuningBinding binding, Component target, PlayModeTuningDraft draft)
            {
                Binding = binding;
                Target = target;
                Draft = draft;
                if (binding.IsColor)
                {
                    _originalColor = binding.ReadColor(target);
                    LastColor = _originalColor;
                }
                else
                {
                    _originalFloat = binding.ReadFloat(target);
                    LastFloat = _originalFloat;
                }
            }

            internal void Restore()
            {
                if (Target == null || EditorUtility.IsPersistent(Target)
                    || !Target.gameObject.scene.IsValid() || !Target.gameObject.scene.isLoaded
                    || EditorSceneManager.IsPreviewScene(Target.gameObject.scene))
                {
                    return;
                }

                // Runtime animation or behaviour may have taken ownership since the last write.
                // Restore only the value still supplied by this preview.
                if (Binding.IsColor)
                {
                    if (Binding.ReadColor(Target).Equals(LastColor))
                    {
                        Binding.RestoreColor(Target, _originalColor);
                    }
                }
                else if (Binding.ReadFloat(Target).Equals(LastFloat))
                {
                    Binding.RestoreFloat(Target, _originalFloat);
                }
            }
        }
    }

    internal sealed class PlayModeTuningReview
    {
        private readonly List<string> _changes = new();
        private readonly List<string> _errors = new();
        private readonly List<PlayModeTuningDraft> _writes = new();

        internal IReadOnlyList<string> Changes => _changes;
        internal IReadOnlyList<string> Errors => _errors;
        internal IReadOnlyList<PlayModeTuningDraft> Writes => _writes;
        internal string Fingerprint { get; set; }

        internal void AddWrite(PlayModeTuningDraft draft, string description)
        {
            _writes.Add(draft);
            _changes.Add(description);
        }

        internal void AddError(string error)
        {
            _errors.Add(error);
        }
    }
}
