using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace JamKit.Tests
{
    public class SaveServiceTests
    {
        [Serializable]
        private class TestData
        {
            public int Number;
            public string Text;
        }

        private const string TestJsonFileName = "JamKitTests_SaveServiceRoundTrip";
        private const string TestIntKey = "JamKitTests_SaveService_Int";

        private SaveService _saveService;
        private GameObject _host;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject(nameof(SaveServiceTests));
            _saveService = _host.AddComponent<SaveService>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_host);
            PlayerPrefs.DeleteKey(TestIntKey);

            string path = Path.Combine(Application.persistentDataPath, TestJsonFileName + ".json");
            if (File.Exists(path)) File.Delete(path);
        }

        [Test]
        public void SetInt_GetInt_RoundTrips()
        {
            _saveService.SetInt(TestIntKey, 7);

            Assert.AreEqual(7, _saveService.GetInt(TestIntKey));
        }

        [Test]
        public void GetInt_MissingKey_ReturnsSuppliedDefault()
        {
            Assert.AreEqual(99, _saveService.GetInt(TestIntKey, 99));
        }

        [Test]
        public void SaveJson_LoadJson_RoundTrips()
        {
            var data = new TestData { Number = 5, Text = "hello" };
            _saveService.SaveJson(TestJsonFileName, data);

            TestData loaded = _saveService.LoadJson(TestJsonFileName, (TestData)null);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(5, loaded.Number);
            Assert.AreEqual("hello", loaded.Text);
        }

        [Test]
        public void LoadJson_MissingFile_ReturnsSuppliedDefault()
        {
            var fallback = new TestData { Number = -1, Text = "default" };

            TestData loaded = _saveService.LoadJson("JamKitTests_DoesNotExist_" + Guid.NewGuid(), fallback);

            Assert.AreSame(fallback, loaded);
        }

        [Test]
        public void LoadJson_CorruptFile_ReturnsSuppliedDefaultAndLogs()
        {
            // Simulate a truncated/garbage save (e.g. a crash mid-write): the file exists but is unparseable.
            string path = Path.Combine(Application.persistentDataPath, TestJsonFileName + ".json");
            File.WriteAllText(path, "not-json-at-all");
            var fallback = new TestData { Number = -1, Text = "default" };

            LogAssert.Expect(LogType.Error, new Regex("failed to parse"));
            TestData loaded = _saveService.LoadJson(TestJsonFileName, fallback);

            Assert.AreSame(fallback, loaded);
        }
    }
}
