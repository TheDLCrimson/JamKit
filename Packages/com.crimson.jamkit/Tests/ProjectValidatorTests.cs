using System.Collections.Generic;
using JamKit.Editor;
using NUnit.Framework;

namespace JamKit.Tests
{
    public class ProjectValidatorTests
    {
        // Project integration guard (not a unit test): this repo is both JamKit and the GMTK entry,
        // so coupling to the current build/project settings is intentional for now. If JamKit later
        // becomes a standalone distributable package, move this test out of the package tests.
        [Test]
        public void CurrentProject_PassesJamKitValidation()
        {
            ValidationReport report = ProjectValidator.Validate();
            CollectionAssert.IsEmpty(report.Errors,
                "Clean project must pass JamKit validation with zero errors. Errors: " +
                string.Join(" | ", report.Errors));
        }

        // --- Preprocessor-guard classification (correction #1) -----------------------------------
        // FindUnguardedUnityEditorLines returns the 1-based lines where a UnityEditor reference is
        // reachable in a player build. A guarded reference must NOT be flagged; a player-reachable
        // one MUST be flagged. Line 1 of each source string below is the "using UnityEngine;" line.

        [Test]
        public void Guard_PlainUnityEditorUsing_IsFlagged()
        {
            string source = Join(
                "using UnityEngine;",
                "using UnityEditor;",
                "public class C {}");
            Assert.AreEqual(new List<int> { 2 }, ProjectValidator.FindUnguardedUnityEditorLines(source));
        }

        [Test]
        public void Guard_IfUnityEditor_IsNotFlagged()
        {
            string source = Join(
                "using UnityEngine;",
                "#if UNITY_EDITOR",
                "using UnityEditor;",
                "#endif",
                "public class C {}");
            CollectionAssert.IsEmpty(ProjectValidator.FindUnguardedUnityEditorLines(source));
        }

        [Test]
        public void Guard_IfUnityEditorOrOtherDefine_IsFlagged()
        {
            // OTHER may be defined in a build, so the region is not definitely editor-only.
            string source = Join(
                "using UnityEngine;",
                "#if UNITY_EDITOR || SOME_DEFINE",
                "using UnityEditor;",
                "#endif",
                "public class C {}");
            Assert.AreEqual(new List<int> { 3 }, ProjectValidator.FindUnguardedUnityEditorLines(source));
        }

        [Test]
        public void Guard_IfNotUnityEditor_IsFlagged()
        {
            string source = Join(
                "using UnityEngine;",
                "#if !UNITY_EDITOR",
                "using UnityEditor;",
                "#endif",
                "public class C {}");
            Assert.AreEqual(new List<int> { 3 }, ProjectValidator.FindUnguardedUnityEditorLines(source));
        }

        [Test]
        public void Guard_ElseBranchOfUnityEditor_IsFlagged()
        {
            // The #if branch is editor-only (safe); the #else branch is the player branch (flagged).
            string source = Join(
                "using UnityEngine;",
                "#if UNITY_EDITOR",
                "using UnityEditor;",     // line 3: safe
                "#else",
                "using UnityEditor;",     // line 5: player-reachable
                "#endif",
                "public class C {}");
            Assert.AreEqual(new List<int> { 5 }, ProjectValidator.FindUnguardedUnityEditorLines(source));
        }

        [Test]
        public void Guard_NestedGuards_OnlyDefinitelyExcludedRegionIsSafe()
        {
            // Inner reference sits under an enclosing #if UNITY_EDITOR, so it is safe even though the
            // inner condition (SOME_DEFINE) is unknown. The trailing reference is player-reachable.
            string source = Join(
                "using UnityEngine;",     // 1
                "#if UNITY_EDITOR",       // 2
                "#if SOME_DEFINE",        // 3
                "using UnityEditor;",     // 4: safe (enclosing UNITY_EDITOR excludes it from player)
                "#endif",                 // 5
                "#endif",                 // 6
                "using UnityEditor;",     // 7: player-reachable
                "public class C {}");     // 8
            Assert.AreEqual(new List<int> { 7 }, ProjectValidator.FindUnguardedUnityEditorLines(source));
        }

        [Test]
        public void Guard_LineComment_IsIgnored()
        {
            string source = Join(
                "using UnityEngine;",
                "// this mentions UnityEditor in a comment only",
                "public class C {}");
            CollectionAssert.IsEmpty(ProjectValidator.FindUnguardedUnityEditorLines(source));
        }

        [Test]
        public void Guard_SubstringIdentifier_IsNotFlagged()
        {
            // MyUnityEditorHelper is not the UnityEditor namespace token.
            string source = Join(
                "using UnityEngine;",
                "public class MyUnityEditorHelper {}");
            CollectionAssert.IsEmpty(ProjectValidator.FindUnguardedUnityEditorLines(source));
        }

        private static string Join(params string[] lines)
        {
            return string.Join("\n", lines);
        }
    }
}
