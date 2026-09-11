using UnityEngine;
using UnityEngine.UI;

namespace SuperHeroUnite.UI.CallbackProbe
{
    [ExecuteAlways]
    [AddComponentMenu("")]
    public sealed class PrefabInspectionCallbackProbe : MonoBehaviour
    {
        public static Color ColorValue { get; set; } = Color.white;
        public static int EnableCount { get; private set; }

        public static void ResetState()
        {
            ColorValue = Color.white;
            EnableCount = 0;
        }

        private void OnEnable()
        {
            EnableCount++;
            Image image = GetComponentInChildren<Image>(true);
            if (image != null)
            {
                image.color = ColorValue;
            }
        }
    }
}
