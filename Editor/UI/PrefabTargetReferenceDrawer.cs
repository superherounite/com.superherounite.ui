using UnityEditor;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    [CustomPropertyDrawer(typeof(PrefabTargetReference))]
    public sealed class PrefabTargetReferenceDrawer : PropertyDrawer
    {
        private const float ButtonSpacing = 3f;
        private const float CaptureButtonWidth = 62f;
        private const float SelectButtonWidth = 52f;
        private const float ClearButtonWidth = 45f;

        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            return (EditorGUIUtility.singleLineHeight * 2f)
                + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            SerializedProperty globalObjectId = property.FindPropertyRelative("_globalObjectId");
            SerializedProperty displayPath = property.FindPropertyRelative("_displayPath");
            SerializedProperty componentType = property.FindPropertyRelative("_componentType");
            SerializedProperty expectedKind = property.FindPropertyRelative("_expectedKind");

            EditorGUI.BeginProperty(position, label, property);
            Rect pathRect = new(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            string pathText = string.IsNullOrEmpty(displayPath.stringValue)
                ? "Not captured"
                : displayPath.stringValue;
            var pathContent = new GUIContent(
                pathText,
                $"{componentType.stringValue}\n{globalObjectId.stringValue}");
            EditorGUI.LabelField(pathRect, label, pathContent);

            Rect controlsRect = new(
                position.x + EditorGUIUtility.labelWidth,
                position.y + EditorGUIUtility.singleLineHeight
                    + EditorGUIUtility.standardVerticalSpacing,
                position.width - EditorGUIUtility.labelWidth,
                EditorGUIUtility.singleLineHeight);
            Rect captureRect = TakeButton(ref controlsRect, CaptureButtonWidth);
            Rect selectRect = TakeButton(ref controlsRect, SelectButtonWidth);
            Rect clearRect = TakeButton(ref controlsRect, ClearButtonWidth);

            PrefabStyleRecipe recipe = property.serializedObject.targetObject as PrefabStyleRecipe;
            using (new EditorGUI.DisabledScope(recipe == null || recipe.OwnerPrefab == null))
            {
                if (GUI.Button(captureRect, "Capture"))
                {
                    Capture(
                        property,
                        recipe,
                        globalObjectId,
                        displayPath,
                        componentType,
                        (PrefabTargetKind)expectedKind.enumValueIndex);
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(globalObjectId.stringValue)))
                {
                    if (GUI.Button(selectRect, "Select"))
                    {
                        var reference = new PrefabTargetReference();
                        reference.Configure(
                            globalObjectId.stringValue,
                            displayPath.stringValue,
                            componentType.stringValue,
                            (PrefabTargetKind)expectedKind.enumValueIndex);
                        if (!PrefabTargetResolver.OpenOwnerAndSelectTarget(
                            recipe,
                            reference,
                            out string error))
                        {
                            Debug.LogError(error);
                        }

                        GUIUtility.ExitGUI();
                    }
                }
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(globalObjectId.stringValue)))
            {
                if (GUI.Button(clearRect, "Clear"))
                {
                    Undo.RecordObject(property.serializedObject.targetObject, "Clear Style Target");
                    globalObjectId.stringValue = string.Empty;
                    displayPath.stringValue = string.Empty;
                    componentType.stringValue = string.Empty;
                    property.serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(property.serializedObject.targetObject);
                }
            }

            EditorGUI.EndProperty();
        }

        private static Rect TakeButton(ref Rect remaining, float width)
        {
            var button = new Rect(remaining.x, remaining.y, width, remaining.height);
            remaining.x += width + ButtonSpacing;
            remaining.width -= width + ButtonSpacing;
            return button;
        }

        private static void Capture(
            SerializedProperty property,
            PrefabStyleRecipe recipe,
            SerializedProperty globalObjectId,
            SerializedProperty displayPath,
            SerializedProperty componentType,
            PrefabTargetKind expectedKind)
        {
            if (!PrefabTargetResolver.TryCaptureSelection(
                recipe.OwnerPrefab,
                expectedKind,
                out PrefabTargetResolver.CapturedTarget capturedTarget,
                out string error))
            {
                Debug.LogError(error);
                return;
            }

            Undo.RecordObject(property.serializedObject.targetObject, "Capture Style Target");
            globalObjectId.stringValue = capturedTarget.GlobalObjectId;
            displayPath.stringValue = capturedTarget.DisplayPath;
            componentType.stringValue = capturedTarget.ComponentType;
            property.serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(property.serializedObject.targetObject);
        }
    }
}
