using System;

using UnityEditor;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    [InitializeOnLoad]
    internal static class PlayModeTuningState
    {
        private const string SessionKey = "SuperHeroUnite.UI.PlayModeTuning.Drafts.v1";

        internal static PlayModeTuningSession Session { get; }

        static PlayModeTuningState()
        {
            string json = SessionState.GetString(SessionKey, string.Empty);
            try
            {
                Session = PlayModeTuningSession.FromJson(json);
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning($"Could not restore Play Mode tuning drafts: {exception.Message}");
                Session = new PlayModeTuningSession();
            }

            Session.Changed += SaveDrafts;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreLiveValues;
            EditorApplication.quitting += RestoreLiveValues;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode)
            {
                RestoreLiveValues();
            }
        }

        private static void RestoreLiveValues()
        {
            Session.RestoreLiveValues();
        }

        private static void SaveDrafts()
        {
            SessionState.SetString(SessionKey, Session.ToJson());
        }
    }
}
