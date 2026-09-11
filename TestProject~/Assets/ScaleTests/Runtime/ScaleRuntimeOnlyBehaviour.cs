using UnityEngine;

namespace SuperHeroUnite.UI.ScaleTests
{
    public sealed class ScaleRuntimeOnlyBehaviour : MonoBehaviour
    {
        [SerializeField] private int _marker = 73;

        public int Marker => _marker;
    }
}
