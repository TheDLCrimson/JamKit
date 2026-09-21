using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JamKit.Editor
{
    /// <summary>
    /// Generic inspector drawer for any <c>[SerializeReference]</c> field (single or <c>List</c>/array)
    /// whose element type derives from <see cref="SerializedPolymorphic"/>. Presents a type dropdown of
    /// all concrete subclasses (assembly-scanned), a per-array-element delete button, and an open-script
    /// shortcut, and draws the selected instance's own serialized fields beneath the header.
    /// </summary>
    /// <remarks>
    /// The concrete base type is derived from the drawn field at runtime (unwrapping <c>List&lt;T&gt;</c>
    /// and array element types), so one drawer serves every distinct <see cref="SerializedPolymorphic"/>
    /// hierarchy in any assembly. Candidate types are cached per discovered base.
    /// </remarks>
    [CustomPropertyDrawer(typeof(SerializedPolymorphic), true)]
    public class PolymorphicListDrawer : PropertyDrawer
    {
        private sealed class TypeCacheEntry
        {
            public List<Type> Types;
            public string[] Names; // index 0 == "None", then one per concrete type
        }

        private static readonly Dictionary<Type, TypeCacheEntry> CacheByBase = new Dictionary<Type, TypeCacheEntry>();
        private static readonly Color AccentColor = new Color(0.3f, 0.6f, 0.9f);
        private static readonly Color ContentBgColor = new Color(0f, 0f, 0f, 0.15f);
        private const float Padding = 6f;
        private const float LineHeight = 20f;
        private const float Spacing = 2f;

        // The array element currently being deleted, guarded so its (now-shifted) draw is skipped for one
        // repaint. Keyed by target object identity AND propertyPath - propertyPath alone collides whenever
        // two different objects (e.g. two ItemDefinition assets, or a multi-selection) expose a
        // same-shaped list at the same index, which would otherwise skip drawing an unrelated object's row.
        private static int _deletingTargetInstanceId;
        private static string _deletingPropertyPath;

        private static bool IsBeingDeleted(SerializedProperty property)
        {
            return _deletingPropertyPath == property.propertyPath &&
                   _deletingTargetInstanceId == property.serializedObject.targetObject.GetInstanceID();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (IsBeingDeleted(property))
            {
                return 0;
            }

            float height = LineHeight + Padding;

            if (property.managedReferenceValue != null)
            {
                SerializedProperty iterator = property.Copy();
                SerializedProperty endProperty = iterator.GetEndProperty();
                iterator.NextVisible(true);

                while (!SerializedProperty.EqualContents(iterator, endProperty))
                {
                    height += EditorGUI.GetPropertyHeight(iterator, true) + Spacing;
                    if (!iterator.NextVisible(false))
                    {
                        break;
                    }
                }

                height += Padding;
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (IsBeingDeleted(property))
            {
                return;
            }

            Type baseType = GetPolymorphicBaseType();
            TypeCacheEntry cache = GetCache(baseType);

            string typeName = property.managedReferenceFullTypename;
            int currentIndex = GetCurrentTypeIndex(cache, typeName);
            bool isSelected = property.managedReferenceValue != null;
            bool isInArray = IsPropertyInArray(property);

            EditorGUI.DrawRect(position, ContentBgColor);

            if (isSelected)
            {
                EditorGUI.DrawRect(new Rect(position.x, position.y, 2, position.height), AccentColor);
            }

            Rect headerRect = new Rect(
                position.x + Padding,
                position.y + Padding / 2,
                position.width - Padding * 2,
                LineHeight);

            float labelWidth = 70f;
            float buttonWidth = 24f;
            float buttonCount = isInArray ? 2f : 1f; // Edit + Delete for arrays, only Edit for single fields
            float popupWidth = headerRect.width - labelWidth - (buttonWidth * buttonCount) - (Spacing * (buttonCount + 1));

            Rect labelRect = new Rect(headerRect.x, headerRect.y, labelWidth, LineHeight);
            Rect popupRect = new Rect(labelRect.xMax + Spacing, headerRect.y, popupWidth, LineHeight);
            Rect editRect = new Rect(popupRect.xMax + Spacing, headerRect.y, buttonWidth, LineHeight);
            Rect deleteRect = new Rect(editRect.xMax + Spacing, headerRect.y, buttonWidth, LineHeight);

            GUIStyle labelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };
            GUI.Label(labelRect, ObjectNames.NicifyVariableName(baseType.Name), labelStyle);

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUI.Popup(popupRect, currentIndex, cache.Names);
            if (EditorGUI.EndChangeCheck())
            {
                if (newIndex == 0)
                {
                    property.managedReferenceValue = null;
                }
                else if (newIndex > 0 && newIndex <= cache.Types.Count)
                {
                    property.managedReferenceValue = Activator.CreateInstance(cache.Types[newIndex - 1]);
                }
                property.serializedObject.ApplyModifiedProperties();
            }

            GUI.enabled = isSelected;
            GUIStyle editButtonStyle = isInArray ? EditorStyles.miniButtonLeft : EditorStyles.miniButton;
            if (GUI.Button(editRect, new GUIContent(EditorGUIUtility.IconContent("d_editicon.sml").image, "Edit Script"), editButtonStyle))
            {
                if (property.managedReferenceValue != null)
                {
                    MonoScript script = FindScriptFromType(property.managedReferenceValue.GetType());
                    if (script != null)
                    {
                        AssetDatabase.OpenAsset(script);
                    }
                }
            }
            GUI.enabled = true;

            if (isInArray)
            {
                if (GUI.Button(deleteRect, new GUIContent(EditorGUIUtility.IconContent("d_TreeEditor.Trash").image, "Remove from list"), EditorStyles.miniButtonRight))
                {
                    DeletePropertyFromList(property);
                    return;
                }
            }

            if (property.managedReferenceValue != null)
            {
                Rect contentRect = new Rect(
                    position.x + Padding * 2,
                    headerRect.yMax + Padding,
                    position.width - Padding * 4,
                    position.height - LineHeight - Padding * 2);

                DrawProperties(contentRect, property);
            }
        }

        // Recovers the declared polymorphic base (e.g. ItemEffect) from the drawn field, unwrapping
        // List<T> and array element types so a collection of the base is treated as the base.
        private Type GetPolymorphicBaseType()
        {
            Type t = fieldInfo != null ? fieldInfo.FieldType : typeof(SerializedPolymorphic);

            if (t.IsArray)
            {
                return t.GetElementType();
            }

            if (t.IsGenericType)
            {
                Type[] args = t.GetGenericArguments();
                if (args.Length == 1)
                {
                    return args[0];
                }
            }

            return t;
        }

        private static bool IsPropertyInArray(SerializedProperty property)
        {
            return property.propertyPath.Contains(".Array.data[");
        }

        private void DeletePropertyFromList(SerializedProperty property)
        {
            _deletingTargetInstanceId = property.serializedObject.targetObject.GetInstanceID();
            _deletingPropertyPath = property.propertyPath;

            string path = property.propertyPath;
            string arrayPath = path.Substring(0, path.LastIndexOf(".Array.data[", StringComparison.Ordinal));
            SerializedProperty arrayProp = property.serializedObject.FindProperty(arrayPath);

            if (arrayProp != null && arrayProp.isArray)
            {
                int indexStart = path.LastIndexOf("[", StringComparison.Ordinal) + 1;
                int indexEnd = path.LastIndexOf("]", StringComparison.Ordinal);
                string indexStr = path.Substring(indexStart, indexEnd - indexStart);

                if (int.TryParse(indexStr, out int index))
                {
                    arrayProp.DeleteArrayElementAtIndex(index);
                    property.serializedObject.ApplyModifiedProperties();

                    EditorApplication.delayCall += () => { _deletingPropertyPath = null; };
                }
            }
        }

        private static void DrawProperties(Rect rect, SerializedProperty property)
        {
            float yOffset = 0;

            SerializedProperty iterator = property.Copy();
            SerializedProperty endProperty = iterator.GetEndProperty();
            iterator.NextVisible(true);

            while (!SerializedProperty.EqualContents(iterator, endProperty))
            {
                float propHeight = EditorGUI.GetPropertyHeight(iterator, true);
                Rect propRect = new Rect(rect.x, rect.y + yOffset, rect.width, propHeight);

                EditorGUI.PropertyField(propRect, iterator, true);

                yOffset += propHeight + Spacing;

                if (!iterator.NextVisible(false))
                {
                    break;
                }
            }
        }

        private static MonoScript FindScriptFromType(Type type)
        {
            string[] guids = AssetDatabase.FindAssets($"t:MonoScript {type.Name}");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);

                if (script != null && script.GetClass() == type)
                {
                    return script;
                }
            }

            return null;
        }

        private static int GetCurrentTypeIndex(TypeCacheEntry cache, string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
            {
                return 0;
            }

            string actualType = GetTypeFromFullName(fullTypeName);

            for (int i = 0; i < cache.Types.Count; i++)
            {
                if (cache.Types[i].FullName == actualType)
                {
                    return i + 1;
                }
            }

            return 0;
        }

        private static string GetTypeFromFullName(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
            {
                return null;
            }

            // managedReferenceFullTypename is "<assembly> <namespace.Type>"; we want the type part.
            string[] parts = fullTypeName.Split(' ');
            return parts.Length > 1 ? parts[1] : fullTypeName;
        }

        private static TypeCacheEntry GetCache(Type baseType)
        {
            if (CacheByBase.TryGetValue(baseType, out TypeCacheEntry entry))
            {
                return entry;
            }

            List<Type> types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(asm =>
                {
                    try { return asm.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                // Exclude abstracts, the base itself, and open generic declarations (e.g. Foo<T> : Base),
                // which have unbound type parameters and cannot be instantiated via Activator.CreateInstance.
                .Where(t => !t.IsAbstract
                            && !t.IsGenericTypeDefinition
                            && !t.ContainsGenericParameters
                            && baseType.IsAssignableFrom(t)
                            && t != baseType)
                .OrderBy(t => t.Name)
                .ToList();

            List<string> names = new List<string> { "None" };
            names.AddRange(types.Select(t => ObjectNames.NicifyVariableName(t.Name)));

            entry = new TypeCacheEntry { Types = types, Names = names.ToArray() };
            CacheByBase[baseType] = entry;
            return entry;
        }
    }
}
