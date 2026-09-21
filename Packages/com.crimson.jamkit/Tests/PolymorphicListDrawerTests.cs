using System.Reflection;
using JamKit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace JamKit.Tests
{
    public class PolymorphicListDrawerTests
    {
        private ItemDefinition _objectA;
        private ItemDefinition _objectB;

        [SetUp]
        public void SetUp()
        {
            _objectA = ScriptableObject.CreateInstance<ItemDefinition>();
            _objectB = ScriptableObject.CreateInstance<ItemDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_objectA);
            Object.DestroyImmediate(_objectB);
            SetDeletingKey(0, null); // reset shared static state so it never leaks into real Inspector usage
        }

        private static void SetDeletingKey(int instanceId, string propertyPath)
        {
            typeof(PolymorphicListDrawer)
                .GetField("_deletingTargetInstanceId", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, instanceId);
            typeof(PolymorphicListDrawer)
                .GetField("_deletingPropertyPath", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, propertyPath);
        }

        private static bool InvokeIsBeingDeleted(SerializedProperty property)
        {
            MethodInfo method = typeof(PolymorphicListDrawer).GetMethod("IsBeingDeleted", BindingFlags.NonPublic | BindingFlags.Static);
            return (bool)method.Invoke(null, new object[] { property });
        }

        [Test]
        public void IsBeingDeleted_SamePropertyPathOnDifferentObjects_OnlyMatchesTheObjectActuallyBeingDeletedFrom()
        {
            var soA = new SerializedObject(_objectA);
            var soB = new SerializedObject(_objectB);
            SerializedProperty propA = soA.FindProperty("_effects");
            SerializedProperty propB = soB.FindProperty("_effects");

            // Both objects expose an "_effects" field, so their propertyPath strings are identical - exactly
            // the collision this fix targets.
            Assert.AreEqual(propA.propertyPath, propB.propertyPath);

            SetDeletingKey(_objectA.GetInstanceID(), propA.propertyPath);

            Assert.IsTrue(InvokeIsBeingDeleted(propA), "The object actually being deleted from must match.");
            Assert.IsFalse(InvokeIsBeingDeleted(propB), "An unrelated object with the same property path must not be affected.");
        }

        [Test]
        public void IsBeingDeleted_NoDeletionInProgress_IsFalse()
        {
            var so = new SerializedObject(_objectA);
            SerializedProperty prop = so.FindProperty("_effects");

            Assert.IsFalse(InvokeIsBeingDeleted(prop));
        }
    }
}
