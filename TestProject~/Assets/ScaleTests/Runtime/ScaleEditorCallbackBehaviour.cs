using UnityEngine;

namespace SuperHeroUnite.UI.ScaleTests
{
    [ExecuteAlways]
    public sealed class ScaleEditorCallbackBehaviour : MonoBehaviour
    {
        public static int EnableCount { get; private set; }

        private void OnEnable()
        {
            EnableCount++;
        }
    }
}
