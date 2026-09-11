using System;
using System.Collections.Generic;
using System.Text;

namespace SuperHeroUnite.UI.Editor
{
    public enum StyleReviewState
    {
        Ready,
        Stale,
        Error,
    }

    /// <summary>Contains the deterministic result of a registry preview.</summary>
    public sealed class StyleReview
    {
        private readonly List<string> _assets = new();
        private readonly List<string> _changes = new();
        private readonly List<string> _errors = new();
        private readonly HashSet<string> _changedOwnerPaths = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Assets => _assets;
        public IReadOnlyList<string> Changes => _changes;
        public IReadOnlyList<string> Errors => _errors;

        public StyleReviewState State
        {
            get
            {
                if (_errors.Count > 0)
                {
                    return StyleReviewState.Error;
                }

                return _changes.Count > 0
                    ? StyleReviewState.Stale
                    : StyleReviewState.Ready;
            }
        }

        internal StyleRecipeRegistry Registry { get; set; }
        internal string Fingerprint { get; set; }
        internal int OwnerPrefabLoadCount { get; set; }
        internal int ConsumerPrefabLoadCount { get; set; }
        internal int AppliedOwnerPrefabCount { get; set; }
        internal bool CanReuseInspection { get; set; } = true;
        internal IReadOnlyCollection<string> ChangedOwnerPaths => _changedOwnerPaths;

        internal void Append(StyleReview review)
        {
            _assets.AddRange(review._assets);
            _changes.AddRange(review._changes);
            _errors.AddRange(review._errors);
            _changedOwnerPaths.UnionWith(review._changedOwnerPaths);
            OwnerPrefabLoadCount += review.OwnerPrefabLoadCount;
            ConsumerPrefabLoadCount += review.ConsumerPrefabLoadCount;
            CanReuseInspection &= review.CanReuseInspection;
        }

        internal StyleReview CopyWithoutWorkCounts()
        {
            var copy = new StyleReview();
            copy.Append(this);
            copy.OwnerPrefabLoadCount = 0;
            copy.ConsumerPrefabLoadCount = 0;
            return copy;
        }

        internal void AddChangedOwner(string path)
        {
            _changedOwnerPaths.Add(path);
        }

        internal void AddAsset(string path)
        {
            _assets.Add(path);
        }

        internal void AddChange(string description)
        {
            _changes.Add(description);
        }

        internal void AddError(string description)
        {
            _errors.Add(description);
        }

        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append($"{State}: {_assets.Count} assets, {_changes.Count} changes, {_errors.Count} errors");
            foreach (string error in _errors)
            {
                text.AppendLine().Append(error);
            }

            foreach (string change in _changes)
            {
                text.AppendLine().Append(change);
            }

            return text.ToString();
        }
    }
}
