using System;
using System.Collections.Generic;
using System.Reflection;

using TMPro;

using UnityEngine;
using UnityEngine.UI;

namespace SuperHeroUnite.UI.Editor
{
    internal static class StylePrefabInspectionPolicy
    {
        private static readonly Dictionary<Type, bool> s_reusableTypes = new();
        private static readonly Assembly s_uguiAssembly = typeof(Graphic).Assembly;
        private static readonly Assembly s_textMeshProAssembly = typeof(TMP_Text).Assembly;

        internal static bool CanReuseRegistry(StyleRecipeRegistry registry)
        {
            if (registry == null || registry.Recipes == null || registry.Recipes.Length == 0)
            {
                return false;
            }

            var prefabs = new HashSet<GameObject>();
            foreach (PrefabStyleRecipe recipe in registry.Recipes)
            {
                if (recipe == null)
                {
                    return false;
                }

                if (prefabs.Add(recipe.OwnerPrefab) && !CanReuse(recipe.OwnerPrefab))
                {
                    return false;
                }

                foreach (GameObject consumer in recipe.ConsumerPrefabs ?? Array.Empty<GameObject>())
                {
                    if (prefabs.Add(consumer) && !CanReuse(consumer))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal static bool CanReuse(GameObject root)
        {
            if (root == null)
            {
                return false;
            }

            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                {
                    return false;
                }

                Type type = component.GetType();
                if (!CanReuseType(type) || (!IsTrustedType(type) && component.runInEditMode))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool CanReuseType(Type type)
        {
            if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                return false;
            }

            if (!s_reusableTypes.TryGetValue(type, out bool canReuse))
            {
                canReuse = IsTrustedType(type) || !HasEditorCallbacks(type);
                s_reusableTypes.Add(type, canReuse);
            }

            return canReuse;
        }

        private static bool IsTrustedType(Type type)
        {
            return type.Assembly == s_uguiAssembly || type.Assembly == s_textMeshProAssembly;
        }

        private static bool HasEditorCallbacks(Type type)
        {
            // Prefab loading invokes serialization callbacks even without edit-mode messages.
            if (typeof(ISerializationCallbackReceiver).IsAssignableFrom(type)
                || type.IsDefined(typeof(ExecuteAlways), true)
                || type.IsDefined(typeof(ExecuteInEditMode), true))
            {
                return true;
            }

            // A private Unity message on a base class is omitted by inherited GetMethods lookups.
            for (Type current = type; current != null; current = current.BaseType)
            {
                foreach (MethodInfo method in current.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.Name == "OnValidate")
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
