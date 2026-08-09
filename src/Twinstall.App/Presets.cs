using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Twinstall.Core;
using Twinstall.Platform;

namespace Twinstall.App
{
    internal sealed class Preset
    {
        internal string Id { get; set; }
        internal string DisplayName { get; set; }
        internal string ExeName { get; set; }
        internal IList<string> Hints { get; } = new List<string>();
        internal bool Verified { get; set; }

        /// <summary>
        /// False only for an app measured as impossible to support — OpenCode discards
        /// --user-data-dir and substitutes its own path, so no second profile can be made.
        ///
        /// Such an app is kept in the file and hidden from the list rather than deleted. The
        /// entry is where the evidence lives, and without it the next person to wonder why
        /// OpenCode is missing has to repeat the whole investigation to find out. Offering it
        /// would be worse still: the user picks it, waits through a launch test, and is told no.
        ///
        /// Absent means true. Only an explicit false hides an app.
        /// </summary>
        internal bool Supported { get; set; } = true;
    }

    /// <summary>
    /// Convenience only. Detection is fully automatic, so a preset carries nothing but a name
    /// and some places to look — no profile paths, no schemes. An app missing from the list
    /// works identically via Browse; that is the whole point of detecting rather than
    /// maintaining a table.
    /// </summary>
    internal static class Presets
    {
        /// <summary>
        /// Prefers a presets file sitting beside the executable, because that one can be edited
        /// to add an app; falls back to the copy embedded in the binary, which is what makes a
        /// single-file build genuinely single.
        /// </summary>
        private static string ReadPresetJson()
        {
            try
            {
                string file = Path.Combine(AppContext.BaseDirectory, "presets", "apps.json");
                if (File.Exists(file)) return File.ReadAllText(file);
            }
            catch (IOException ex) { Log.Write("presets file unreadable: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log.Write("presets file unreadable: " + ex.Message); }

            try
            {
                using (Stream s = System.Reflection.Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("Twinstall.App.presets.json"))
                {
                    if (s == null) return null;
                    using (var reader = new StreamReader(s)) return reader.ReadToEnd();
                }
            }
            catch (IOException ex) { Log.Write("embedded presets unreadable: " + ex.Message); return null; }
        }

        internal static IList<Preset> Load()
        {
            var presets = new List<Preset>();
            try
            {
                string json = ReadPresetJson();
                if (string.IsNullOrWhiteSpace(json)) return presets;

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (!doc.RootElement.TryGetProperty("apps", out JsonElement apps)) return presets;

                    foreach (JsonElement app in apps.EnumerateArray())
                    {
                        var p = new Preset
                        {
                            Id = Text(app, "id"),
                            DisplayName = Text(app, "displayName"),
                            ExeName = Text(app, "exeName"),
                            Verified = app.TryGetProperty("verified", out JsonElement v)
                                       && v.ValueKind == JsonValueKind.True,

                            // Absent means supported. Only an explicit false hides an app.
                            Supported = !(app.TryGetProperty("supported", out JsonElement s)
                                          && s.ValueKind == JsonValueKind.False)
                        };
                        if (app.TryGetProperty("hints", out JsonElement hints))
                            foreach (JsonElement h in hints.EnumerateArray())
                                if (h.ValueKind == JsonValueKind.String) p.Hints.Add(h.GetString());

                        if (!string.IsNullOrWhiteSpace(p.DisplayName)) presets.Add(p);
                    }
                }
            }
            catch (JsonException ex) { Log.Write("presets unreadable: " + ex.Message); }
            catch (IOException ex) { Log.Write("presets unreadable: " + ex.Message); }
            return presets;
        }

        private static string Text(JsonElement obj, string name)
        {
            return obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() : null;
        }

        /// <summary>
        /// First hint that actually resolves to a file on this machine, or null.
        ///
        /// A hint may contain one '*' segment — '%LOCALAPPDATA%\slack\app-*' and
        /// '%ProgramFiles%\WindowsApps\Claude_*\app' both need it, for a Squirrel version
        /// folder and an MSIX package folder respectively. Matches are tried newest first,
        /// using the same numeric ordering as stub resolution so app-4.10.0 beats app-4.9.0.
        /// </summary>
        internal static string FindInstalled(Preset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.ExeName)) return null;

            foreach (string hint in preset.Hints)
            {
                foreach (string dir in ExpandHint(hint))
                {
                    try
                    {
                        string candidate = Path.Combine(dir, preset.ExeName);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException) { }
                }
            }
            return null;
        }

        /// <summary>
        /// Traces a hint step by step and reports why it produced what it did. Diagnostic
        /// only, and deliberately verbose: "not found" on its own is useless when the cause
        /// could be an environment variable, a permission, or a parsing slip.
        /// </summary>
        internal static IList<string> DescribeHint(string hint)
        {
            var report = new List<string>();
            if (string.IsNullOrWhiteSpace(hint)) { report.Add("(empty hint)"); return report; }

            string expanded;
            try { expanded = Environment.ExpandEnvironmentVariables(hint); }
            catch (ArgumentException ex) { report.Add("expand failed: " + ex.Message); return report; }
            report.Add("expanded=" + expanded);

            int star = expanded.IndexOf('*', StringComparison.Ordinal);
            if (star < 0)
            {
                report.Add("no wildcard; Directory.Exists=" + Directory.Exists(expanded));
                return report;
            }

            int before = expanded.LastIndexOf('\\', star);
            int after = expanded.IndexOf('\\', star);
            if (before < 0) { report.Add("no separator before the wildcard"); return report; }

            string parent = expanded.Substring(0, before);
            string pattern = after < 0 ? expanded.Substring(before + 1)
                                       : expanded.Substring(before + 1, after - before - 1);
            string tail = after < 0 ? string.Empty : expanded.Substring(after + 1);

            report.Add("parent=[" + parent + "] pattern=[" + pattern + "] tail=[" + tail + "]");
            report.Add("Directory.Exists(parent)=" + Directory.Exists(parent));

            try
            {
                string[] matches = Directory.GetDirectories(parent, pattern);
                report.Add("GetDirectories -> " + matches.Length.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                foreach (string m in matches) report.Add("  match=" + m);
            }
            catch (Exception ex)
            {
                // Diagnostic: any failure is worth reporting, so the catch is intentionally
                // wide. This is the only place in the codebase where that is the right call.
                report.Add("GetDirectories THREW " + ex.GetType().Name + ": " + ex.Message);
            }
            return report;
        }

        private static List<string> ExpandHint(string hint)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(hint)) return results;

            string expanded;
            try { expanded = Environment.ExpandEnvironmentVariables(hint); }
            catch (ArgumentException) { return results; }

            int star = expanded.IndexOf('*', StringComparison.Ordinal);
            if (star < 0)
            {
                if (Directory.Exists(expanded)) results.Add(expanded);
                return results;
            }

            // Split into <parent>\<pattern>\<tail>
            int before = expanded.LastIndexOf('\\', star);
            int after = expanded.IndexOf('\\', star);
            if (before < 0) return results;

            string parent = expanded.Substring(0, before);
            string pattern = after < 0
                ? expanded.Substring(before + 1)
                : expanded.Substring(before + 1, after - before - 1);
            string tail = after < 0 ? string.Empty : expanded.Substring(after + 1);

            string[] matches;
            try
            {
                matches = Directory.Exists(parent) ? Directory.GetDirectories(parent, pattern) : Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                // C:\Program Files\WindowsApps grants traverse but not read, so a normal
                // application cannot enumerate it — Directory.Exists says true and
                // GetDirectories throws. Every Store app landed here and silently resolved to
                // nothing. PackageIndex reads the install paths out of HKCU instead and
                // confirms them by traversal, which is permitted.
                var viaIndex = new List<string>();
                foreach (string root in PackageIndex.PackageRootsMatching(pattern))
                    viaIndex.Add(tail.Length == 0 ? root : Path.Combine(root, tail));
                return viaIndex;
            }
            catch (IOException) { return results; }

            Array.Sort(matches, (a, b) =>
                LauncherStub.CompareVersionFolders(Path.GetFileName(b), Path.GetFileName(a)));

            foreach (string m in matches)
                results.Add(tail.Length == 0 ? m : Path.Combine(m, tail));

            return results;
        }
    }
}
