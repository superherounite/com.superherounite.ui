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
