using System;

using NUnit.Framework;

using TMPro;

using UnityEngine;
using UnityEngine.UI;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StylePrefabInspectionPolicyTests
    {
        [TestCase(typeof(Image))]
        [TestCase(typeof(TextMeshProUGUI))]
        [TestCase(typeof(RuntimeOnlyBehaviour))]
        public void BuiltInUiAndRuntimeOnlyTypesCanReuseInspection(Type type)
        {
            Assert.That(StylePrefabInspectionPolicy.CanReuseType(type), Is.True);
            Assert.That(StylePrefabInspectionPolicy.CanReuseType(type), Is.True);
        }

        [TestCase(typeof(AlwaysBehaviour))]
        [TestCase(typeof(InheritedAlwaysBehaviour))]
        [TestCase(typeof(EditModeBehaviour))]
        [TestCase(typeof(InheritedEditModeBehaviour))]
        [TestCase(typeof(ValidationBehaviour))]
        [TestCase(typeof(InheritedPrivateValidationBehaviour))]
        [TestCase(typeof(OverloadedValidationBehaviour))]
        [TestCase(typeof(CustomImageBehaviour))]
        [TestCase(typeof(SerializationBehaviour))]
        [TestCase(typeof(InheritedSerializationBehaviour))]
        [TestCase(typeof(ExplicitSerializationBehaviour))]
        [TestCase(typeof(InheritedExplicitSerializationBehaviour))]
        public void CustomEditorCallbacksCannotReuseInspection(Type type)
        {
            Assert.That(StylePrefabInspectionPolicy.CanReuseType(type), Is.False);
            Assert.That(StylePrefabInspectionPolicy.CanReuseType(type), Is.False);
        }

        [TestCase(null)]
        [TestCase(typeof(GameObject))]
        public void MissingOrNonBehaviourTypesCannotReuseInspection(Type type)
        {
            Assert.That(StylePrefabInspectionPolicy.CanReuseType(type), Is.False);
        }

        [Test]
        public void MissingRootCannotReuseInspection()
        {
            Assert.That(StylePrefabInspectionPolicy.CanReuse(null), Is.False);
        }

        [Test]
        public void MissingOrEmptyRegistryCannotReuseInspection()
        {
            Assert.That(StylePrefabInspectionPolicy.CanReuseRegistry(null), Is.False);
            var registry = ScriptableObject.CreateInstance<StyleRecipeRegistry>();
            try
            {
                Assert.That(StylePrefabInspectionPolicy.CanReuseRegistry(registry), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(registry);
            }
        }

        [Test]
        public void RootContainingBuiltInUiCanReuseInspection()
        {
            var root = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            try
            {
                Assert.That(StylePrefabInspectionPolicy.CanReuse(root), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private sealed class RuntimeOnlyBehaviour : MonoBehaviour
        {
        }

        [ExecuteAlways]
        private class AlwaysBehaviour : MonoBehaviour
        {
        }

        private sealed class InheritedAlwaysBehaviour : AlwaysBehaviour
        {
        }

        [ExecuteInEditMode]
        private class EditModeBehaviour : MonoBehaviour
        {
        }

        private sealed class InheritedEditModeBehaviour : EditModeBehaviour
        {
        }

        private sealed class ValidationBehaviour : MonoBehaviour
        {
            private void OnValidate()
            {
            }
        }

        private class PrivateValidationBase : MonoBehaviour
        {
            private void OnValidate()
            {
            }
        }

        private sealed class InheritedPrivateValidationBehaviour : PrivateValidationBase
        {
        }

        private sealed class OverloadedValidationBehaviour : MonoBehaviour
        {
            private void OnValidate()
            {
            }

            private void OnValidate(int value)
            {
            }
        }

        private sealed class CustomImageBehaviour : Image
        {
        }

        private class SerializationBehaviour : MonoBehaviour, ISerializationCallbackReceiver
        {
            public void OnBeforeSerialize()
            {
            }

            public void OnAfterDeserialize()
            {
            }
        }

        private sealed class InheritedSerializationBehaviour : SerializationBehaviour
        {
        }

        private class ExplicitSerializationBehaviour : MonoBehaviour, ISerializationCallbackReceiver
        {
            void ISerializationCallbackReceiver.OnBeforeSerialize()
            {
            }

            void ISerializationCallbackReceiver.OnAfterDeserialize()
            {
            }
        }

        private sealed class InheritedExplicitSerializationBehaviour : ExplicitSerializationBehaviour
        {
        }
    }
}
