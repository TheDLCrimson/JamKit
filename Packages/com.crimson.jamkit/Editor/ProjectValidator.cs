using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace JamKit.Editor
{
    /// <summary>
    /// Structured result of a <see cref="ProjectValidator.Validate"/> run.
    /// <see cref="Errors"/> are build-blocking defects; <see cref="Warnings"/> are advisory.
    /// </summary>
    public sealed class ValidationReport
    {
        /// <summary>Build-blocking defects. A non-empty list means the project failed validation.</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>Advisory issues (for example known-bloat folders) that do not fail validation.</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>True when there are no errors (warnings are allowed).</summary>
        public bool Passed => Errors.Count == 0;
    }

    /// <summary>
    /// Project-hygiene validator for JamKit projects.
    /// Each check maps 1:1 to a jam-fatal defect class found in the donor-project audit:
    /// wrong build settings, missing-script references, editor-API leaks into runtime code,
    /// a permissive Active Input Handling mode, and known import-bloat folders.
    /// Run from the menu (<c>JamKit &gt; Validate Project</c>) or call <see cref="Validate"/> directly.
    /// </summary>
    public static class ProjectValidator
    {
        private const string LogPrefix = "[JamKit Validate] ";
        private const string BootSceneName = "Boot";
        private const string JamKitPackagePrefix = "Packages/com.crimson.jamkit/";

        // A MonoBehaviour's serialized script reference, for example:
        //   m_Script: {fileID: 11500000, guid: abc..., type: 3}
        //   m_Script: {fileID: 0}                                (null / missing reference)
        private static readonly Regex MonoScriptRefRegex = new Regex(
            @"m_Script:\s*\{fileID:\s*(-?\d+)(?:,\s*guid:\s*([0-9a-fA-F]+))?",
            RegexOptions.Compiled);

        // Standalone "UnityEditor" namespace token (does not match e.g. MyUnityEditorHelper).
        private static readonly Regex UnityEditorTokenRegex = new Regex(
            @"\bUnityEditor\b", RegexOptions.Compiled);

        /// <summary>
        /// Resolves a Unity project-relative path (for example "Assets/Foo.cs" or
        /// "Packages/x/Bar.cs") to an absolute filesystem path anchored at the project root,
        /// so filesystem checks do not silently depend on the process working directory.
        /// Returns null for a null/empty input.
        /// </summary>
        private static string ToAbsoluteProjectPath(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                return null;
            }
            // Application.dataPath is "<projectRoot>/Assets"; its parent is the project root,
            // which also anchors "Packages/..." paths for on-disk (embedded and resolved) packages.
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, projectRelativePath));
        }

        /// <summary>
        /// Runs every project-hygiene check and returns a structured report.
        /// Pure query: it inspects project state and reads files, but never mutates the project.
        /// </summary>
        public static ValidationReport Validate()
        {
            var report = new ValidationReport();
            CheckBuildScenes(report);
            CheckMissingScripts(report);
            CheckEditorReferencesInRuntimeCode(report);
            CheckActiveInputHandling(report);
            CheckBloatFolders(report);
            return report;
        }

        [MenuItem("JamKit/Validate Project")]
        private static void ValidateMenuItem()
        {
            var report = Validate();
            foreach (var error in report.Errors)
            {
                Debug.LogError(LogPrefix + "ERROR: " + error);
            }
            foreach (var warning in report.Warnings)
            {
                Debug.LogWarning(LogPrefix + "WARNING: " + warning);
            }

            // Stable, machine-greppable summary line. The /preflight skill keys off "RESULT=".
            string result = report.Passed ? "PASS" : "FAIL";
            Debug.Log(LogPrefix + "RESULT=" + result +
                      " errors=" + report.Errors.Count +
                      " warnings=" + report.Warnings.Count);
        }

        // --- Check 1: build scenes non-empty, Boot present and first -----------------------------

        private static void CheckBuildScenes(ValidationReport report)
        {
            var enabled = new List<EditorBuildSettingsScene>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    enabled.Add(scene);
                }
            }

            if (enabled.Count == 0)
            {
                report.Errors.Add(
                    "Build settings scene list is empty (no enabled scenes). Add Boot, Menu, Game with Boot first.");
                return;
            }

            foreach (var scene in enabled)
            {
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(scene.path)) ||
                    !File.Exists(ToAbsoluteProjectPath(scene.path)))
                {
                    report.Errors.Add("Build scene path does not resolve to an asset: " + scene.path);
                }
            }

            int bootIndex = enabled.FindIndex(
                s => Path.GetFileNameWithoutExtension(s.path) == BootSceneName);
            if (bootIndex < 0)
            {
                report.Errors.Add("Build settings do not contain an enabled '" + BootSceneName + "' scene.");
            }
            else if (bootIndex != 0)
            {
                report.Errors.Add("'" + BootSceneName + "' must be the first enabled build scene (found at index " +
                                  bootIndex + "). Bootstrap requires Boot as the entry scene.");
            }
        }

        // --- Check 2: missing-script references in build scenes and their prefab deps ------------

        /// <summary>
        /// Flags genuinely missing serialized script references (a <c>m_Script</c> that no longer
        /// binds to a real asset) in the enabled build scenes AND in the prefab assets reachable
        /// from those scenes. A missing script serialized inside a prefab - for example a deleted
        /// service on <c>Bootstrap.prefab</c>, which Boot instantiates - never appears in the scene
        /// YAML, so scanning scenes alone would miss it; the prefab dependency closure is scanned
        /// with the identical detection logic (see <see cref="ScanAssetForMissingScripts"/> for the
        /// exact error-producing branches).
        /// This is deliberately narrow: it is NOT a general script-health analyzer. In particular a
        /// single-type file whose class name differs from its filename still binds on Unity
        /// 6000.3.19f1 (<c>MonoScript.GetClass()</c> is non-null), so that mismatch is not a missing
        /// reference and is intentionally out of scope.
        /// </summary>
        private static void CheckMissingScripts(ValidationReport report)
        {
            // Prefab paths deduplicated across every enabled scene so a prefab shared by several
            // scenes (or a nested prefab in the dependency closure) is scanned exactly once.
            var scannedPrefabs = new HashSet<string>();

            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled || !File.Exists(ToAbsoluteProjectPath(scene.path)))
                {
                    continue; // unresolved paths are already reported by CheckBuildScenes.
                }

                // 1. The scene asset itself.
                ScanAssetForMissingScripts(scene.path, report);

                // 2. Prefab assets reachable from the scene. AssetDatabase.GetDependencies already
                //    supplies the full recursive closure; we filter it to .prefab assets only and
                //    reuse the same missing-script detection - no separate asset-validation framework.
                foreach (string dependency in AssetDatabase.GetDependencies(scene.path, true))
                {
                    if (dependency.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase) &&
                        scannedPrefabs.Add(dependency))
                    {
                        ScanAssetForMissingScripts(dependency, report);
                    }
                }
            }
        }

        /// <summary>
        /// Read-only YAML scan of a single serialized asset (a <c>.unity</c> scene or a
        /// <c>.prefab</c>) for missing <c>m_Script</c> references. Four branches produce an error:
        /// (1) a null reference (<c>fileID: 0</c> with no guid);
        /// (2) a guid that resolves to no asset path;
        /// (3) a guid whose resolved path does not exist on disk (a deleted/dangling script);
        /// (4) a tertiary signal - the guid resolves to an existing <c>MonoScript</c> whose
        /// <c>GetClass()</c> is null (its class cannot be bound). Branch (4) deliberately skips a null
        /// <c>MonoScript</c> at an existing path (for example a precompiled DLL component, or a
        /// generic/abstract base) so those are never false-flagged; it also does not fire for the
        /// single-type filename/class rename, which still binds on Unity 6000.3.19f1.
        /// Every existence check anchors the Unity project-relative path to the project root first,
        /// so it never depends on the process working directory.
        /// </summary>
        private static void ScanAssetForMissingScripts(string assetPath, ValidationReport report)
        {
            string absolutePath = ToAbsoluteProjectPath(assetPath);
            if (absolutePath == null || !File.Exists(absolutePath))
            {
                return;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(absolutePath);
            }
            catch (System.Exception ex)
            {
                report.Errors.Add("Could not read asset '" + assetPath + "': " + ex.Message);
                return;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                var match = MonoScriptRefRegex.Match(lines[i]);
                if (!match.Success)
                {
                    continue;
                }

                string fileId = match.Groups[1].Value;
                string guid = match.Groups[2].Success ? match.Groups[2].Value : null;
                int lineNumber = i + 1;

                if (string.IsNullOrEmpty(guid))
                {
                    // fileID: 0 with no guid is a null (missing) script reference.
                    if (fileId == "0")
                    {
                        report.Errors.Add("Missing script reference (null m_Script) in " +
                                          assetPath + " (line " + lineNumber + ").");
                    }
                    continue;
                }

                // A guid that resolves to no path, or to a path that no longer exists on disk, is a
                // missing (deleted/unbindable) script. The path is deliberately anchored to the
                // project root before the existence check (never left to the process working
                // directory). The File.Exists guard matters because the AssetDatabase can hold a
                // stale guid->path mapping after a script is deleted, and it also distinguishes a
                // deleted script (path gone) from a valid precompiled-DLL component (path present but
                // LoadAssetAtPath<MonoScript> is null), which must not be flagged.
                // Accepted limitation: a package resolved only into Library/PackageCache (so that
                // <projectRoot>/Packages/<pkg> is not physically present) could be false-flagged; the
                // current package set all resolve on disk under the project (verified), so this does
                // not occur here.
                string resolvedPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(ToAbsoluteProjectPath(resolvedPath)))
                {
                    report.Errors.Add("Missing script reference (guid " + guid +
                                      " does not resolve to an existing asset) in " +
                                      assetPath + " (line " + lineNumber + ").");
                    continue;
                }

                // Tertiary signal: the guid resolves to an existing script asset whose class cannot be
                // bound. Safe against false positives because a null MonoScript at an existing path (for
                // example a precompiled DLL, or a generic/abstract base that is never a component) is
                // left alone. Note it does NOT catch a single-type file/class rename: on Unity
                // 6000.3.19f1 such a file still binds (GetClass() non-null), so that form is not a
                // missing reference and is intentionally not treated as one here.
                var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(resolvedPath);
                if (monoScript != null && monoScript.GetClass() == null)
                {
                    report.Errors.Add("Script reference resolves to a MonoScript whose class cannot be bound: " +
                                      resolvedPath + " referenced by " + assetPath + " (line " + lineNumber + ").");
                }
            }
        }

        // --- Check 3: UnityEditor references in runtime (player) code -----------------------------

        private static void CheckEditorReferencesInRuntimeCode(ValidationReport report)
        {
            // PlayerWithoutTestAssemblies is exactly the set of assemblies compiled into a build:
            // Assembly-CSharp runtime plus package runtime asmdefs. Editor asmdefs and Editor/ folders
            // are excluded by Unity's own resolution, so no manual asmdef parsing is needed.
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
            foreach (var assembly in assemblies)
            {
                foreach (var file in assembly.sourceFiles)
                {
                    // Scope to code we author; third-party package runtime is out of the audit's scope.
                    if (!(file.StartsWith("Assets/") || file.StartsWith(JamKitPackagePrefix)))
                    {
                        continue;
                    }

                    string text;
                    try
                    {
                        text = File.ReadAllText(ToAbsoluteProjectPath(file));
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (int lineNumber in FindUnguardedUnityEditorLines(text))
                    {
                        report.Errors.Add("UnityEditor reference in player (runtime) code: " + file + ":" +
                                          lineNumber + " (assembly " + assembly.name +
                                          "). Guard it with #if UNITY_EDITOR or move it to an Editor assembly.");
                    }
                }
            }
        }

        /// <summary>
        /// Returns the 1-based line numbers in <paramref name="sourceText"/> where a
        /// <c>UnityEditor</c> reference appears in code that would compile into a player build.
        /// </summary>
        /// <remarks>
        /// This is a deliberately conservative heuristic, not a full C# preprocessor.
        /// A region counts as editor-only (references there are ignored) only when it is
        /// <em>definitely</em> excluded from the player build under three-valued (Kleene) evaluation
        /// with <c>UNITY_EDITOR</c> treated as false and every other symbol as unknown.
        /// Supported and verified: <c>#if UNITY_EDITOR</c> (safe); <c>#if UNITY_EDITOR || OTHER</c>
        /// (flagged - OTHER may be defined in a build); <c>#if !UNITY_EDITOR</c> (flagged);
        /// the <c>#else</c> branch of <c>#if UNITY_EDITOR</c> (flagged); nested guards (a line is safe
        /// only if some enclosing region is definitely excluded). Any condition that cannot be
        /// classified is treated as reachable and therefore flagged, never silently assumed safe.
        /// Not handled: block comments (<c>/* ... */</c>) spanning a reference, and symbol values from
        /// asmdef <c>versionDefines</c>/scripting-define settings (all non-UNITY_EDITOR symbols are
        /// treated as unknown). Both err toward flagging, not toward a false clean result.
        /// </remarks>
        public static List<int> FindUnguardedUnityEditorLines(string sourceText)
        {
            var flagged = new List<int>();
            if (string.IsNullOrEmpty(sourceText))
            {
                return flagged;
            }

            string[] lines = sourceText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var frames = new Stack<PreprocessorFrame>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("#"))
                {
                    HandleDirective(trimmed, frames);
                    continue;
                }

                if (IsInsideExcludedRegion(frames))
                {
                    continue; // definitely editor-only code; UnityEditor references here are fine.
                }

                string code = StripLineComment(line);
                if (UnityEditorTokenRegex.IsMatch(code))
                {
                    flagged.Add(i + 1);
                }
            }

            return flagged;
        }

        private sealed class PreprocessorFrame
        {
            // True when the current branch region is definitely excluded from the player build.
            public bool Excluded;

            // True when some branch already seen in this #if/#elif/#else group is definitely taken
            // in the player build, which makes every later branch in the group excluded.
            public bool PriorBranchDefinitelyTrue;
        }

        private static bool IsInsideExcludedRegion(Stack<PreprocessorFrame> frames)
        {
            foreach (var frame in frames)
            {
                if (frame.Excluded)
                {
                    return true;
                }
            }
            return false;
        }

        private static void HandleDirective(string directiveLine, Stack<PreprocessorFrame> frames)
        {
            string body = directiveLine.Substring(1).TrimStart();
            int space = body.IndexOfAny(new[] { ' ', '\t' });
            string keyword = space < 0 ? body : body.Substring(0, space);
            string condition = space < 0 ? string.Empty : body.Substring(space + 1).Trim();

            switch (keyword)
            {
                case "if":
                {
                    Kleene value = EvaluatePlayerCondition(condition);
                    frames.Push(new PreprocessorFrame
                    {
                        Excluded = value == Kleene.False,
                        PriorBranchDefinitelyTrue = value == Kleene.True,
                    });
                    break;
                }
                case "elif":
                {
                    if (frames.Count == 0)
                    {
                        break;
                    }
                    var frame = frames.Peek();
                    Kleene value = EvaluatePlayerCondition(condition);
                    frame.Excluded = frame.PriorBranchDefinitelyTrue || value == Kleene.False;
                    if (value == Kleene.True)
                    {
                        frame.PriorBranchDefinitelyTrue = true;
                    }
                    break;
                }
                case "else":
                {
                    if (frames.Count == 0)
                    {
                        break;
                    }
                    var frame = frames.Peek();
                    frame.Excluded = frame.PriorBranchDefinitelyTrue;
                    break;
                }
                case "endif":
                {
                    if (frames.Count > 0)
                    {
                        frames.Pop();
                    }
                    break;
                }
                default:
                    break; // #define, #pragma, #region, #warning, etc. do not affect region exclusion.
            }
        }

        private static string StripLineComment(string line)
        {
            int index = line.IndexOf("//", System.StringComparison.Ordinal);
            return index < 0 ? line : line.Substring(0, index);
        }

        // --- Three-valued preprocessor-condition evaluation --------------------------------------

        private enum Kleene
        {
            False,
            True,
            Unknown,
        }

        // Evaluates a preprocessor boolean condition under player-build defines: UNITY_EDITOR is
        // false, true/false are literals, and every other symbol is unknown. Any parse failure
        // yields Unknown so the region is treated as reachable (never falsely marked editor-only).
        private static Kleene EvaluatePlayerCondition(string condition)
        {
            condition = StripLineComment(condition).Trim();
            if (condition.Length == 0)
            {
                return Kleene.Unknown;
            }

            List<string> tokens = TokenizeCondition(condition);
            if (tokens == null)
            {
                return Kleene.Unknown;
            }

            int position = 0;
            Kleene result = ParseOr(tokens, ref position);
            if (position != tokens.Count)
            {
                return Kleene.Unknown; // trailing tokens we did not understand.
            }
            return result;
        }

        private static List<string> TokenizeCondition(string condition)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < condition.Length)
            {
                char c = condition[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                }
                else if (c == '(' || c == ')' || c == '!')
                {
                    tokens.Add(c.ToString());
                    i++;
                }
                else if (c == '&' && i + 1 < condition.Length && condition[i + 1] == '&')
                {
                    tokens.Add("&&");
                    i += 2;
                }
                else if (c == '|' && i + 1 < condition.Length && condition[i + 1] == '|')
                {
                    tokens.Add("||");
                    i += 2;
                }
                else if (c == '_' || char.IsLetter(c))
                {
                    int start = i;
                    while (i < condition.Length && (condition[i] == '_' || char.IsLetterOrDigit(condition[i])))
                    {
                        i++;
                    }
                    tokens.Add(condition.Substring(start, i - start));
                }
                else
                {
                    return null; // unexpected character; caller treats as Unknown.
                }
            }
            return tokens;
        }

        private static Kleene ParseOr(List<string> tokens, ref int position)
        {
            Kleene value = ParseAnd(tokens, ref position);
            while (position < tokens.Count && tokens[position] == "||")
            {
                position++;
                Kleene right = ParseAnd(tokens, ref position);
                value = Or(value, right);
            }
            return value;
        }

        private static Kleene ParseAnd(List<string> tokens, ref int position)
        {
            Kleene value = ParseUnary(tokens, ref position);
            while (position < tokens.Count && tokens[position] == "&&")
            {
                position++;
                Kleene right = ParseUnary(tokens, ref position);
                value = And(value, right);
            }
            return value;
        }

        private static Kleene ParseUnary(List<string> tokens, ref int position)
        {
            if (position >= tokens.Count)
            {
                return Kleene.Unknown;
            }

            string token = tokens[position];
            if (token == "!")
            {
                position++;
                return Not(ParseUnary(tokens, ref position));
            }
            if (token == "(")
            {
                position++;
                Kleene inner = ParseOr(tokens, ref position);
                if (position < tokens.Count && tokens[position] == ")")
                {
                    position++;
                    return inner;
                }
                return Kleene.Unknown; // unbalanced parentheses.
            }

            position++;
            if (token == "true")
            {
                return Kleene.True;
            }
            if (token == "false")
            {
                return Kleene.False;
            }
            if (token == "UNITY_EDITOR")
            {
                return Kleene.False; // never defined in a player build.
            }
            return Kleene.Unknown; // any other define may or may not be set in a build.
        }

        private static Kleene Not(Kleene value)
        {
            if (value == Kleene.True)
            {
                return Kleene.False;
            }
            if (value == Kleene.False)
            {
                return Kleene.True;
            }
            return Kleene.Unknown;
        }

        private static Kleene And(Kleene a, Kleene b)
        {
            if (a == Kleene.False || b == Kleene.False)
            {
                return Kleene.False;
            }
            if (a == Kleene.True && b == Kleene.True)
            {
                return Kleene.True;
            }
            return Kleene.Unknown;
        }

        private static Kleene Or(Kleene a, Kleene b)
        {
            if (a == Kleene.True || b == Kleene.True)
            {
                return Kleene.True;
            }
            if (a == Kleene.False && b == Kleene.False)
            {
                return Kleene.False;
            }
            return Kleene.Unknown;
        }

        // --- Check 4: Active Input Handling is InputSystem-only -----------------------------------

        private static void CheckActiveInputHandling(ValidationReport report)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0)
            {
                report.Warnings.Add("Could not load ProjectSettings.asset to verify Active Input Handling.");
                return;
            }

            var serialized = new SerializedObject(assets[0]);
            var property = serialized.FindProperty("activeInputHandler");
            if (property == null)
            {
                report.Warnings.Add("Could not read 'activeInputHandler'; skipping the Active Input Handling check.");
                return;
            }

            // 0 = Input Manager (Old), 1 = Input System Package (New), 2 = Both.
            if (property.intValue != 1)
            {
                string current = property.intValue == 0
                    ? "Input Manager (Old)"
                    : property.intValue == 2 ? "Both" : property.intValue.ToString();
                report.Errors.Add("Active Input Handling must be 'Input System Package' only (found '" + current +
                                  "'). Set it in Project Settings > Player.");
            }
        }

        // --- Check 5: known bloat folders (warning only) ------------------------------------------

        private static void CheckBloatFolders(ValidationReport report)
        {
            // This is a warn-LIST of known import junk, not a block-list. The Unity MCP bridge
            // (com.coplaydev.unity-mcp / the MCPForUnity editor folder) is intentionally NOT listed:
            // it is actively-used, editor-only dev tooling, not import bloat, so it passes clean.
            // If a whitelist mechanism is ever introduced, the bridge folder must be named explicitly.
            var knownBloat = new[]
            {
                new KeyValuePair<string, string>("Assets/TextMesh Pro/Examples & Extras", "TMP Examples & Extras"),
                new KeyValuePair<string, string>("Assets/Plugins/PrimeTween/Demo", "PrimeTween Demo"),
            };

            foreach (var entry in knownBloat)
            {
                if (Directory.Exists(entry.Key))
                {
                    report.Warnings.Add("Known bloat folder present: " + entry.Value + " (" + entry.Key +
                                        "). Consider removing before shipping.");
                }
            }

            // Unity imports package samples under Assets/Samples/<package>/...
            if (Directory.Exists("Assets/Samples"))
            {
                foreach (var directory in Directory.GetDirectories("Assets/Samples"))
                {
                    report.Warnings.Add("Imported package samples present: " + directory.Replace('\\', '/') +
                                        ". Consider removing before shipping.");
                }
            }
        }
    }
}
