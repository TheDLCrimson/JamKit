using JamKit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace JamKit.Tests
{
    public class InventoryModelTests
    {
        // Builds a real ItemDefinition and sets its private serialized _maxStackSize through the
        // serialization API (the actual contract InventoryModel reads), with no test-only production seam.
        private static ItemDefinition MakeItem(int maxStackSize)
        {
            ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
            SerializedObject so = new SerializedObject(item);
            so.FindProperty("_maxStackSize").intValue = maxStackSize;
            so.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        [Test]
        public void NewInventory_HasRequestedSlotCount_AllEmpty()
        {
            var inv = new InventoryModel(4);
            Assert.AreEqual(4, inv.SlotCount);
            for (int i = 0; i < inv.SlotCount; i++)
            {
                Assert.IsTrue(inv.GetSlot(i).IsEmpty);
            }
        }

        [Test]
        public void SlotCount_IsClampedToAtLeastOne()
        {
            Assert.AreEqual(1, new InventoryModel(0).SlotCount);
        }

        [Test]
        public void AddItem_FillsFirstEmptySlot()
        {
            var inv = new InventoryModel(3);
            var item = MakeItem(10);

            int leftover = inv.AddItem(item, 3);

            Assert.AreEqual(0, leftover);
            Assert.AreEqual(item, inv.GetSlot(0).Item);
            Assert.AreEqual(3, inv.GetSlot(0).Count);
            Assert.IsTrue(inv.GetSlot(1).IsEmpty);
        }

        [Test]
        public void AddItem_StacksIntoExistingMatchingSlot()
        {
            var inv = new InventoryModel(3);
            var item = MakeItem(10);

            inv.AddItem(item, 2);
            int leftover = inv.AddItem(item, 3);

            Assert.AreEqual(0, leftover);
            Assert.AreEqual(5, inv.GetSlot(0).Count, "Should have stacked into the same slot.");
            Assert.IsTrue(inv.GetSlot(1).IsEmpty);
        }

        [Test]
        public void AddItem_ClampsToMaxStack_AndSpillsIntoNextSlot()
        {
            var inv = new InventoryModel(3);
            var item = MakeItem(4);

            int leftover = inv.AddItem(item, 6);

            Assert.AreEqual(0, leftover, "6 fits across two slots of max 4.");
            Assert.AreEqual(4, inv.GetSlot(0).Count);
            Assert.AreEqual(2, inv.GetSlot(1).Count);
        }

        [Test]
        public void AddItem_ReturnsLeftover_WhenInventoryFull()
        {
            var inv = new InventoryModel(1);
            var item = MakeItem(4);

            int leftover = inv.AddItem(item, 10);

            Assert.AreEqual(6, leftover);
            Assert.AreEqual(4, inv.GetSlot(0).Count);
            Assert.IsTrue(inv.IsFull);
        }

        [Test]
        public void RemoveItem_Decrements_AndClearsAtZero()
        {
            var inv = new InventoryModel(2);
            var item = MakeItem(10);
            inv.AddItem(item, 3);

            Assert.AreEqual(2, inv.RemoveItem(0, 2));
            Assert.AreEqual(1, inv.GetSlot(0).Count);

            Assert.AreEqual(1, inv.RemoveItem(0, 5), "Removing more than present removes only what is there.");
            Assert.IsTrue(inv.GetSlot(0).IsEmpty);
        }

        [Test]
        public void SwapSlots_ExchangesContents()
        {
            var inv = new InventoryModel(2);
            var a = MakeItem(10);
            var b = MakeItem(10);
            inv.AddItem(a, 1);       // a -> slot 0
            inv.AddItem(b, 2);       // b -> first empty slot (1)
            Assert.AreEqual(b, inv.GetSlot(1).Item);

            inv.SwapSlots(0, 1);

            Assert.AreEqual(b, inv.GetSlot(0).Item);
            Assert.AreEqual(a, inv.GetSlot(1).Item);
        }

        [Test]
        public void IsFull_TrueOnlyWhenNoSlotCanAccept()
        {
            var inv = new InventoryModel(1);
            var item = MakeItem(2);

            Assert.IsFalse(inv.IsFull);
            inv.AddItem(item, 1);
            Assert.IsFalse(inv.IsFull, "Slot has room to stack more of the same item.");
            inv.AddItem(item, 1);
            Assert.IsTrue(inv.IsFull);
        }

        [Test]
        public void Events_FireOnChange()
        {
            var inv = new InventoryModel(2);
            var item = MakeItem(10);
            int slotChanged = 0;
            int invChanged = 0;
            inv.SlotChanged += _ => slotChanged++;
            inv.InventoryChanged += () => invChanged++;

            inv.AddItem(item, 1);

            Assert.GreaterOrEqual(slotChanged, 1);
            Assert.AreEqual(1, invChanged);
        }

        [Test]
        public void AddItem_NoOp_DoesNotFireInventoryChanged()
        {
            var inv = new InventoryModel(1);
            int invChanged = 0;
            inv.InventoryChanged += () => invChanged++;

            inv.AddItem(null, 1);

            Assert.AreEqual(0, invChanged);
        }
    }
}
