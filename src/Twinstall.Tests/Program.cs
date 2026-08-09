using System;
using System.Collections.Generic;
using Twinstall.Core;

// Plain console runner rather than a test framework: it runs identically under `dotnet run`
// in CI and under mono locally, with no package restore.
static class Tests
{
    static readonly string[] Crlf = { "\r\n" };

    // Hoisted out of the calls that use them: CA1861 objects to constant array arguments.
    static readonly string[] LegacyLine  = { "Work\tC:\\p\\work\t#059669\t1" };
    static readonly string[] PictureLine = { "Work\tC:\\p\\work\t#059669\t1\tC:\\pics\\me.png" };

    static int passed, failed;
    static readonly List<string> failures = new List<string>();

    static void Check(bool condition, string name)
    {
        if (condition) { passed++; }
        else { failed++; failures.Add(name); }
    }

    static void Eq(object actual, object expected, string name)
    {
        bool ok = (actual == null && expected == null) ||
                  (actual != null && actual.Equals(expected));
        if (!ok) failures.Add(name + "  (expected <" + expected + "> got <" + actual + ">)");
        if (ok) passed++; else failed++;
    }

    // ---------------------------------------------------------------- paths --
    static void PathTests()
    {
        Check(PathUtil.SamePath(@"C:\a\b", @"C:\a\b\"), "trailing separator ignored");
        Check(PathUtil.SamePath(@"C:\A\B", @"c:\a\b"), "case-insensitive compare");
        Check(PathUtil.IsInside(@"C:\a\b\c", @"C:\a\b"), "nested path detected");
        Check(!PathUtil.IsInside(@"C:\a\b", @"C:\a\b"), "a path is not inside itself");

        // The bug that shipped in the Claude-specific version: substring matching meant
        // 'work' and 'work2' were treated as the same instance.
        Check(!PathUtil.IsInside(@"C:\x\work2", @"C:\x\work"), "work2 is NOT inside work");
        Check(!PathUtil.SamePath(@"C:\x\work", @"C:\x\work2"), "work and work2 are different");

        Eq(PathUtil.Join(null), "", "null parts joins to empty rather than throwing");
    }

    // ------------------------------------------------------------- isolation --
    static void IsolationTests()
    {
        Check(IsolationCheck.IsValid(@"C:\p\Claude", @"C:\p\second"), "siblings are isolated");
        Check(IsolationCheck.IsValid(@"C:\p\work", @"C:\p\work2"), "work / work2 accepted");
        Check(!IsolationCheck.IsValid(@"C:\p\Claude", @"C:\p\Claude"), "identical rejected");
        Check(!IsolationCheck.IsValid(@"C:\p\Claude", @"C:\p\Claude\sub"), "child rejected");
        Check(!IsolationCheck.IsValid(@"C:\p\Claude\sub", @"C:\p\Claude"), "parent rejected");
        Check(!IsolationCheck.IsValid("", @"C:\p\x"), "empty rejected");
    }

    // ---------------------------------------------------------- command line --
    static void CommandLineTests()
    {
        Eq(CommandLine.ExtractDataDir("\"C:\\a\\App.exe\" --user-data-dir=\"C:\\p\\second\"", @"C:\p\Default"),
           @"C:\p\second", "quoted value");
        Eq(CommandLine.ExtractDataDir(@"C:\a\App.exe --user-data-dir=C:\p\second --other", @"C:\p\Default"),
           @"C:\p\second", "bare value stops at space");
        Eq(CommandLine.ExtractDataDir("\"C:\\a\\App.exe\" --user-data-dir=\"C:\\my profile\\x\"", @"C:\p\Default"),
           @"C:\my profile\x", "quoted value containing spaces");
        Eq(CommandLine.ExtractDataDir(@"C:\a\App.exe", @"C:\p\Default"),
           @"C:\p\Default", "absent flag falls back to default");
        Eq(CommandLine.ExtractDataDir(null, @"C:\p\Default"), @"C:\p\Default", "null command line");

        Check(CommandLine.IsChildProcess(@"App.exe --type=renderer"), "renderer detected");
        Check(!CommandLine.IsChildProcess(@"App.exe --user-data-dir=C:\p\x"), "main process not a child");

        // Observed on a real machine: a separate command-line tool is also called claude.exe, lives
        // elsewhere, and was being counted as a running desktop instance. That is enough to
        // turn "nothing running, ask" into "only one running" and route a sign-in nowhere.
        const string desktop = @"C:\Program Files\WindowsApps\Claude_1.0_x64__abc\app\claude.exe";
        Check(CommandLine.RefersToApp("\"" + desktop + "\" --user-data-dir=\"C:\\p\\x\"", desktop),
              "the configured executable refers to the app");
        Check(!CommandLine.RefersToApp(@"C:\Users\a\AppData\Roaming\Claude\claude-code\2.1\claude.exe --stream", desktop),
              "a same-named CLI elsewhere is not the app");
        Check(CommandLine.RefersToApp(
                "\"C:\\Program Files\\WindowsApps\\Claude_1.0_x64__abc\\app\\helper.exe\"", desktop),
              "a helper in the same package is the app");
        Check(!CommandLine.RefersToApp(null, desktop), "a null command line refers to nothing");
    }

    // ------------------------------------------------------------- packaging --
    static void PackageTests()
    {
        Eq(PackagePaths.FamilyFromFullName("Claude_1.24012.11.0_x64__pzs8sxrjxfjjc"),
           "Claude_pzs8sxrjxfjjc", "family name derived from full name");
        Eq(PackagePaths.FamilyFromFullName("Nonsense"), null, "malformed full name rejected");

        const string packaged = @"C:\Program Files\WindowsApps\Claude_1.24012.11.0_x64__pzs8sxrjxfjjc\app\Claude.exe";
        const string plain    = @"C:\Users\a\AppData\Local\Programs\slack\slack.exe";

        Check(PackagePaths.IsPackaged(packaged), "WindowsApps path is packaged");
        Check(!PackagePaths.IsPackaged(plain), "Programs path is unpackaged");

        Eq(PackagePaths.ProfileRoot(packaged, @"C:\Users\a\AppData\Local", @"C:\Users\a\AppData\Roaming"),
           @"C:\Users\a\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming",
           "packaged profile root is virtualised");
        Eq(PackagePaths.ProfileRoot(plain, @"C:\Users\a\AppData\Local", @"C:\Users\a\AppData\Roaming"),
           @"C:\Users\a\AppData\Roaming",
           "unpackaged profile root is roaming appdata");

        // A normal application cannot LIST C:\Program Files\WindowsApps - the ACL grants
        // traverse but not read, so GetDirectories throws while Directory.Exists says true.
        // Every Store app therefore failed preset lookup, silently. MrtCache under HKCU
        // records the install paths instead; these decode its key names.
        Eq(PackagePaths.DecodeMrtCacheKey(
             @"C:%5CProgram Files%5CWindowsApps%5CClaude_1.25927.0.0_x64__pzs8sxrjxfjjc%5Cresources.pri"),
           @"C:\Program Files\WindowsApps\Claude_1.25927.0.0_x64__pzs8sxrjxfjjc\resources.pri",
           "MrtCache key decodes to a path");
        Eq(PackagePaths.PackageRoot(PackagePaths.DecodeMrtCacheKey(
             @"C:%5CProgram Files%5CWindowsApps%5CClaude_1.25927.0.0_x64__pzs8sxrjxfjjc%5Cresources.pri")),
           @"C:\Program Files\WindowsApps\Claude_1.25927.0.0_x64__pzs8sxrjxfjjc",
           "decoded key yields the package root");
        Eq(PackagePaths.DecodeMrtCacheKey(null), null, "null MrtCache key is safe");

        Check(PackagePaths.MatchesPattern("Claude_1.25927.0.0_x64__pzs8sxrjxfjjc", "Claude_*"),
              "package full name matches its family pattern");
        Check(!PackagePaths.MatchesPattern("ClaudeOther_1.0_x64__abc", "Claude_*"),
              "a different package is not matched");
        Check(PackagePaths.MatchesPattern("app-4.51.180", "app-*"), "version folder matches");
        Check(PackagePaths.MatchesPattern("exact", "exact"), "a pattern with no star is an equality test");
        Check(!PackagePaths.MatchesPattern("exact", "other"), "non-matching literal rejected");
        Check(PackagePaths.MatchesPattern("prefix-middle-suffix", "prefix-*-suffix"), "star in the middle");
        Check(!PackagePaths.MatchesPattern("ab", "abc*"), "shorter than the prefix cannot match");

        RepointTests();
    }

    /// <summary>
    /// A Store update replaces the versioned package folder, so a configuration holding the
    /// absolute path goes stale on its own. This is the real 8 August 2026 case: Claude moved
    /// from 1.25927.0.0 to 1.26832.0.0 underneath a working install, and every launch then
    /// reported "cannot find the application it was set up for".
    /// </summary>
    static void RepointTests()
    {
        Func<HashSet<string>, Func<string, bool>> fs = set => p => set.Contains(p);

        const string stale = @"C:\Program Files\WindowsApps\Claude_1.25927.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe";
        const string fresh = @"C:\Program Files\WindowsApps\Claude_1.26832.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe";

        var roots = new List<string> {
            @"C:\Program Files\WindowsApps\Claude_1.26832.0.0_x64__pzs8sxrjxfjjc"
        };
        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fresh };

        Eq(PackagePaths.RepointToInstalledPackage(stale, roots, fs(onDisk)), fresh,
           "a stale package path re-points at the installed version");

        // Newest first is the caller's contract, so the first family match wins. An older
        // version left on disk must not win over the current one.
        var both = new List<string> {
            @"C:\Program Files\WindowsApps\Claude_1.26832.0.0_x64__pzs8sxrjxfjjc",
            @"C:\Program Files\WindowsApps\Claude_1.25927.0.0_x64__pzs8sxrjxfjjc"
        };
        Eq(PackagePaths.RepointToInstalledPackage(stale, both, fs(onDisk)), fresh,
           "the newest matching package is chosen");

        // A different package that happens to be installed is not a candidate - re-pointing at
        // some other vendor's executable would be far worse than reporting the app as missing.
        var foreign = new List<string> {
            @"C:\Program Files\WindowsApps\Slack_1.0.0_x64__abcdefghijklm"
        };
        Eq(PackagePaths.RepointToInstalledPackage(stale, foreign,
             fs(new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                 @"C:\Program Files\WindowsApps\Slack_1.0.0_x64__abcdefghijklm\app\Claude.exe" })),
           null, "a different package family is never substituted");

        // The root exists but the executable inside it does not.
        Eq(PackagePaths.RepointToInstalledPackage(stale, roots,
             fs(new HashSet<string>(StringComparer.OrdinalIgnoreCase))),
           null, "a package root without the executable is rejected");

        // Unpackaged apps have no second candidate: a missing slack.exe means uninstalled.
        Eq(PackagePaths.RepointToInstalledPackage(
             @"C:\Users\a\AppData\Local\Programs\slack\slack.exe", roots, fs(onDisk)),
           null, "an unpackaged path is never re-pointed");

        Eq(PackagePaths.RepointToInstalledPackage(null, roots, fs(onDisk)), null,
           "a null path is safe");
        Eq(PackagePaths.RepointToInstalledPackage(stale, null, fs(onDisk)), null,
           "null roots are safe");
        Eq(PackagePaths.RepointToInstalledPackage(stale, roots, null), null,
           "a null existence check is safe");

        // The package root itself carries no executable tail to transplant.
        Eq(PackagePaths.RepointToInstalledPackage(
             @"C:\Program Files\WindowsApps\Claude_1.25927.0.0_x64__pzs8sxrjxfjjc", roots, fs(onDisk)),
           null, "a bare package root has nothing to re-point");

        // A deeper layout must carry over whole, not just the file name.
        const string nested = @"C:\Program Files\WindowsApps\Claude_1.25927.0.0_x64__pzs8sxrjxfjjc\app\bin\Claude.exe";
        Eq(PackagePaths.RepointToInstalledPackage(nested, roots,
             fs(new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                 @"C:\Program Files\WindowsApps\Claude_1.26832.0.0_x64__pzs8sxrjxfjjc\app\bin\Claude.exe" })),
           @"C:\Program Files\WindowsApps\Claude_1.26832.0.0_x64__pzs8sxrjxfjjc\app\bin\Claude.exe",
           "the whole path below the package root carries over");
    }

    // -------------------------------------------------------------- chromium --
    static void ChromiumTests()
    {
        var electron = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            PathUtil.Join(@"C:\app", @"resources\app.asar"),
            PathUtil.Join(@"C:\app", "icudtl.dat"),
            PathUtil.Join(@"C:\app", "chrome_100_percent.pak")
        };
        var webview2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            @"C:\t\WebView2Loader.dll"
        };
        Func<HashSet<string>, Func<string, bool>> fs = set => p => set.Contains(p);

        Check(ChromiumDetector.IsChromiumApp(@"C:\app\App.exe", fs(electron)), "electron app detected");
        Check(!ChromiumDetector.IsChromiumApp(@"C:\t\Teams.exe", fs(webview2)), "webview2 app rejected");
        Check(!ChromiumDetector.IsChromiumApp(@"C:\n\Notepad.exe", fs(new HashSet<string>())), "plain win32 rejected");
        Eq(ChromiumDetector.Score(@"C:\app\App.exe", fs(electron)), 3, "marker score counted");

        // Visual Studio Code: only Code.exe at the install root, every Chromium file in a
        // commit-hash folder beside it. Found by running the probe — the folder-only scan
        // called a plainly-Electron app unsupported.
        var vscode = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            PathUtil.Join(@"C:\vsc\e4c7e7b1d6", "icudtl.dat"),
            PathUtil.Join(@"C:\vsc\e4c7e7b1d6", "chrome_100_percent.pak"),
            PathUtil.Join(@"C:\vsc\e4c7e7b1d6", "v8_context_snapshot.bin"),
            PathUtil.Join(@"C:\vsc\e4c7e7b1d6", "LICENSES.chromium.html")
        };
        Func<string, IEnumerable<string>> vscSubs = d =>
            PathUtil.SamePath(d, @"C:\vsc") ? new[] { @"C:\vsc\bin", @"C:\vsc\e4c7e7b1d6" } : Array.Empty<string>();

        Eq(ChromiumDetector.Score(@"C:\vsc\Code.exe", fs(vscode)), 0,
           "the exe's own folder alone scores nothing - this was the bug");
        Eq(ChromiumDetector.Score(@"C:\vsc\Code.exe", fs(vscode), vscSubs), 4,
           "markers are found one level down");
        Check(ChromiumDetector.IsChromiumApp(@"C:\vsc\Code.exe", fs(vscode), vscSubs),
              "vscode accepted once subdirectories are swept");
        Eq(ChromiumDetector.LocateMarkers(@"C:\vsc\Code.exe", fs(vscode), vscSubs).Directory,
           @"C:\vsc\e4c7e7b1d6", "the winning directory is reported");

        // The sweep must not turn the WebView2 rejection into a false positive.
        Func<string, IEnumerable<string>> teamsSubs = d => new[] { @"C:\t\assets" };
        Check(!ChromiumDetector.IsChromiumApp(@"C:\t\Teams.exe", fs(webview2), teamsSubs),
              "webview2 still rejected with the sweep on");
        Eq(ChromiumDetector.Score(@"C:\app\App.exe", fs(electron), vscSubs), 3,
           "the exe's own folder still wins when it is the best");
        Eq(ChromiumDetector.LocateMarkers(@"C:\n\Notepad.exe", fs(new HashSet<string>()), vscSubs).Directory,
           "", "no markers means no directory");
    }

    // ---------------------------------------------------------- launcher stub --
    static void LauncherStubTests()
    {
        Func<HashSet<string>, Func<string, bool>> fs = set => p => set.Contains(p);

        // Squirrel: stub plus Update.exe at the root, real app in app-<version>.
        var slack = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            @"C:\s\slack.exe", @"C:\s\Update.exe",
            @"C:\s\app-4.50.143\slack.exe", @"C:\s\app-4.51.180\slack.exe"
        };
        Func<string, IEnumerable<string>> subs = d => PathUtil.SamePath(d, @"C:\s")
            ? new[] { @"C:\s\app-4.50.143", @"C:\s\app-4.51.180", @"C:\s\packages" }
            : Array.Empty<string>();

        Eq(LauncherStub.Resolve(@"C:\s\slack.exe", fs(slack), subs), @"C:\s\app-4.51.180\slack.exe",
           "squirrel stub resolves to the newest app folder");
        Eq(LauncherStub.Resolve(@"C:\s\app-4.51.180\slack.exe", fs(slack), subs), null,
           "an already-resolved path is left alone");

        // Without Update.exe this is not Squirrel and we must not go guessing.
        var noUpdater = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            @"C:\s\slack.exe", @"C:\s\app-4.51.180\slack.exe"
        };
        Eq(LauncherStub.Resolve(@"C:\s\slack.exe", fs(noUpdater), subs), null,
           "no Update.exe means not a squirrel stub");

        // The versioned folder must actually contain the same executable name.
        var wrongExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            @"C:\s\slack.exe", @"C:\s\Update.exe", @"C:\s\app-4.51.180\other.exe"
        };
        Eq(LauncherStub.Resolve(@"C:\s\slack.exe", fs(wrongExe), subs), null,
           "a version folder without the executable is not a match");

        Eq(LauncherStub.Resolve(@"C:\s\slack.exe", fs(slack), null), null, "no lister, no resolution");
        Eq(LauncherStub.Resolve(null, fs(slack), subs), null, "null path is safe");

        // An ordinal sort puts app-4.9.0 above app-4.10.0. Slack has shipped both shapes.
        var v = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            @"C:\v\a.exe", @"C:\v\Update.exe", @"C:\v\app-4.9.0\a.exe", @"C:\v\app-4.10.0\a.exe"
        };
        Func<string, IEnumerable<string>> vsubs = d => PathUtil.SamePath(d, @"C:\v")
            ? new[] { @"C:\v\app-4.9.0", @"C:\v\app-4.10.0" } : Array.Empty<string>();
        Eq(LauncherStub.Resolve(@"C:\v\a.exe", fs(v), vsubs), @"C:\v\app-4.10.0\a.exe",
           "versions order numerically, not as strings");

        Check(LauncherStub.CompareVersionFolders("app-4.10.0", "app-4.9.0") > 0, "4.10.0 beats 4.9.0");
        Check(LauncherStub.CompareVersionFolders("app-5.0", "app-4.99.99") > 0, "major version wins");
        Eq(LauncherStub.CompareVersionFolders("app-4.51.180", "app-4.51.180"), 0, "equal versions compare equal");
        Eq(LauncherStub.CompareVersionFolders("app-4.51", "app-4.51.0"), 0, "missing segments count as zero");
        Check(LauncherStub.CompareVersionFolders("app-4.51.180-beta", "app-4.51.179") > 0,
              "a suffix does not derail the numeric compare");
    }

    // --------------------------------------------------------------- routing --
    static void RoutingTests()
    {
        var insts = new List<Instance> {
            new Instance { Name = "Main",   DataDir = @"C:\p\Claude" },
            new Instance { Name = "Second", DataDir = @"C:\p\second" }
        };

        var bothRunning = new Dictionary<int, string> { { 100, "Main" }, { 200, "Second" } };

        var r = RouteDecision.Choose(insts, bothRunning, new List<int> { 999, 200, 100 });
        Eq(r.Target, "Second", "z-order picks the most recently used window");
        Eq(r.Reason, RouteReason.ZOrder, "reason is z-order");

        r = RouteDecision.Choose(insts, bothRunning, new List<int> { 100, 200 });
        Eq(r.Target, "Main", "z-order flips when the other window is on top");

        r = RouteDecision.Choose(insts, new Dictionary<int, string> { { 200, "Second" } }, new List<int>());
        Eq(r.Reason, RouteReason.OnlyOneRunning, "single running instance wins without z-order");
        Eq(r.Target, "Second", "single running instance is the target");

        r = RouteDecision.Choose(insts, new Dictionary<int, string>(), new List<int>());
        Eq(r.Reason, RouteReason.NeedsPrompt, "nothing running asks the user");
        Eq(r.Target, null, "prompt has no target");

        r = RouteDecision.Choose(new List<Instance>(), null, null);
        Eq(r.Reason, RouteReason.NoInstances, "no configuration is reported, not guessed");

        // several pids of the same instance must not look like ambiguity
        var oneInstanceTwoPids = new Dictionary<int, string> { { 1, "Second" }, { 2, "Second" } };
        r = RouteDecision.Choose(insts, oneInstanceTwoPids, new List<int> { 2, 1 });
        Eq(r.Reason, RouteReason.OnlyOneRunning, "multiple pids of one instance is not ambiguous");
    }

    // ---------------------------------------------------------------- config --
    static void ConfigTests()
    {
        string[] lines = {
            "# comment",
            "EXE\tC:\\app\\App.exe",
            "SCHEME\tslack",
            "DEFAULT\tC:\\p\\Slack",
            "Work\tC:\\p\\work\t#059669\t1",
            "Personal\tC:\\p\\personal\t#7C3AED\t0",
            "malformed"
        };
        AppConfig cfg = InstanceConfig.Parse(lines);
        Eq(cfg.Scheme, "slack", "scheme parsed");
        Eq(cfg.Instances.Count, 2, "malformed and comment lines skipped");
        Eq(cfg.Instances[0].Name, "Work", "instance name parsed");
        Eq(cfg.Instances[0].Badge, true, "badge flag parsed");
        Eq(cfg.Instances[1].Badge, false, "badge flag false parsed");

        AppConfig round = InstanceConfig.Parse(
            InstanceConfig.Serialise(cfg).Split(Crlf, StringSplitOptions.RemoveEmptyEntries));
        Eq(round.Instances.Count, 2, "round trip keeps instances");
        Eq(round.Scheme, "slack", "round trip keeps scheme");
        Eq(round.Instances[0].Colour, "#059669", "round trip keeps colour");

        Eq(InstanceConfig.Parse(null).Instances.Count, 0, "null input is safe");

        // The picture column was added after people had config files. A line written by the
        // older version has four fields, and must still load rather than being discarded.
        AppConfig old = InstanceConfig.Parse(LegacyLine);
        Eq(old.Instances.Count, 1, "a four-field line from an older version still parses");
        Eq(old.Instances[0].Image, null, "no picture means no picture, not empty string");

        AppConfig withPic = InstanceConfig.Parse(PictureLine);
        Eq(withPic.Instances[0].Image, @"C:\pics\me.png", "a picture path is read");
        Eq(InstanceConfig.Parse(
             InstanceConfig.Serialise(withPic).Split(Crlf, StringSplitOptions.RemoveEmptyEntries))
           .Instances[0].Image, @"C:\pics\me.png", "picture survives a round trip");

        var noPic = new AppConfig();
        noPic.Instances.Add(new Instance { Name = "A", DataDir = @"C:\p\a", Colour = "#111111", Badge = true });
        Eq(InstanceConfig.Parse(
             InstanceConfig.Serialise(noPic).Split(Crlf, StringSplitOptions.RemoveEmptyEntries))
           .Instances[0].Image, null, "an empty picture column round-trips as null");
    }

    // --------------------------------------------------------- launch probe --
    static void LaunchProbeTests()
    {
        const string temp = @"C:\Users\a\AppData\Local\Temp";
        string dir = LaunchProbe.ProbeDirectory(temp, "abc123");
        Eq(dir, @"C:\Users\a\AppData\Local\Temp\Twinstall\probe-abc123", "probe directory derived");

        // A token is never user-supplied today, but this path is handed to another vendor's
        // exe as somewhere to write, so escaping it must be impossible rather than unlikely.
        Eq(LaunchProbe.ProbeDirectory(temp, @"..\..\Windows"), "", "token with separators rejected");
        Eq(LaunchProbe.ProbeDirectory(temp, "a.b"), "", "token with a dot rejected");
        Eq(LaunchProbe.ProbeDirectory(temp, "C:x"), "", "token with a drive letter rejected");
        Eq(LaunchProbe.ProbeDirectory("", "abc"), "", "empty temp root rejected");
        Eq(LaunchProbe.ProbeDirectory(temp, null), "", "null token rejected");

        // The launcher writes the flag and ProcessMap reads it back; they must agree.
        Eq(LaunchProbe.Arguments(dir), "--user-data-dir=\"" + dir + "\"", "arguments are quoted");
        Eq(CommandLine.ExtractDataDir(LaunchProbe.Arguments(dir), @"C:\fallback"), dir,
           "arguments round-trip through ExtractDataDir");
        const string spaced = @"C:\Users\Alex Taylor\AppData\Local\Temp\Twinstall\probe-x";
        Eq(CommandLine.ExtractDataDir(LaunchProbe.Arguments(spaced), @"C:\fallback"), spaced,
           "a path containing spaces survives the round trip");
        Eq(LaunchProbe.Arguments(""), "", "no arguments without a directory");

        Func<HashSet<string>, Func<string, bool>> fs = set => p => set.Contains(p);
        var populated = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            PathUtil.Join(dir, "Local State")
        };
        var lateMarker = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            PathUtil.Join(dir, @"Default\Preferences")
        };
        var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Check(LaunchProbe.ProfileCreated(dir, fs(populated)), "Local State counts as created");
        Check(LaunchProbe.ProfileCreated(dir, fs(lateMarker)), "Default\\Preferences counts as created");
        Check(!LaunchProbe.ProfileCreated(dir, fs(empty)), "empty directory is not created");
        Check(!LaunchProbe.ProfileCreated(dir, null), "null probe is not created");

        Eq(LaunchProbe.Evaluate(dir, fs(populated), 1, 30), ProbeVerdict.Honoured, "marker means honoured");
        Eq(LaunchProbe.Evaluate(dir, fs(empty), 1, 30), ProbeVerdict.Pending, "no marker yet is pending");
        Eq(LaunchProbe.Evaluate(dir, fs(empty), 30, 30), ProbeVerdict.NotHonoured, "timeout means not honoured");

        // An app that writes its profile exactly as the window closes is still supported.
        Eq(LaunchProbe.Evaluate(dir, fs(populated), 30, 30), ProbeVerdict.Honoured,
           "creation is checked before the clock");

        // "Windows would not start it" is not the same answer as "the app ignored the flag".
        // OpenAI Codex denies CreateProcess from every route while Claude, in the same folder
        // with identical ACLs, starts fine — so the two outcomes must stay distinct.
        Check(LaunchProbe.Explain(ProbeVerdict.LaunchBlocked, "x.exe")
                != LaunchProbe.Explain(ProbeVerdict.NotHonoured, "x.exe"),
              "a blocked launch reads differently from an ignored flag");
        Check(LaunchProbe.Explain(ProbeVerdict.LaunchBlocked, "x.exe")
                .Contains("Nothing was changed", StringComparison.Ordinal),
              "a blocked launch still promises nothing was changed");

        Eq(LaunchProbe.Evaluate(dir, null, 1, 30), ProbeVerdict.Invalid, "no file probe is invalid");
        Eq(LaunchProbe.Evaluate(dir, fs(empty), 1, 0), ProbeVerdict.Invalid, "zero timeout is invalid");
        Eq(LaunchProbe.Evaluate("", fs(empty), 1, 30), ProbeVerdict.Invalid, "no directory is invalid");

        // The probe launches the real app, so it must never point at real data.
        var live = new List<string> { @"C:\p\Claude", @"C:\p\second" };
        Check(LaunchProbe.IsSafeTarget(dir, live), "a temp probe directory is safe");
        Check(!LaunchProbe.IsSafeTarget(@"C:\p\Claude", live), "probing a live profile is refused");
        Check(!LaunchProbe.IsSafeTarget(@"C:\p\Claude\probe", live), "probing inside a live profile is refused");
        Check(!LaunchProbe.IsSafeTarget(@"C:\p", live), "a probe containing a live profile is refused");
        Check(LaunchProbe.IsSafeTarget(@"C:\p\Claude2", live), "Claude2 is not Claude");
        Check(LaunchProbe.IsSafeTarget(dir, null), "no known profiles is safe");
        Check(!LaunchProbe.IsSafeTarget("", live), "an empty probe directory is refused");
    }

    // ----------------------------------------------------- profile discovery --
    static ProfileCandidate Candidate(string dir, int score, bool localState, int daysAgo)
    {
        return new ProfileCandidate(dir, score, localState,
            new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero).AddDays(-daysAgo));
    }

    static void ProfileDiscoveryTests()
    {
        // VS Code's ProductName is "Visual Studio Code" but its profile folder is "Code".
        // Only the executable's own file name matches, which is why it is in the list.
        IList<string> vsc = ProfileDiscovery.IdentityNames(
            @"C:\p\Microsoft VS Code\Code.exe", "Visual Studio Code", "Visual Studio Code", "Code.exe");
        Check(ProfileDiscovery.NameMatches("Code", vsc), "exe base name identifies the folder");
        Check(ProfileDiscovery.NameMatches("Visual Studio Code", vsc), "product name also identifies it");
        Check(!ProfileDiscovery.NameMatches("Codex", vsc), "a near miss is not a match");
        Check(!ProfileDiscovery.NameMatches("Slack", vsc), "an unrelated folder is not a match");

        // VS Code's real InternalName is "electron", shared by most Electron apps, and
        // %APPDATA%\electron exists on some machines. Matching it would pick a stranger's
        // profile with full confidence.
        Check(!ProfileDiscovery.NameMatches("electron", vsc), "the shared Electron internal name identifies nothing");
        Eq(vsc.Count, 2, "only Code and Visual Studio Code survive as identities");

        // The real %APPDATA% on the test machine: twelve Electron apps, all with Local State.
        // Markers cannot discriminate here; only the name can.
        var shared = new List<ProfileCandidate> {
            Candidate(@"C:\r\ClickUp",        3, true, 0),   // most recently used decoy
            Candidate(@"C:\r\Discord",        3, true, 1),
            Candidate(@"C:\r\Code",           3, true, 9),   // the one we actually want
            Candidate(@"C:\r\Docker Desktop", 2, true, 2)
        };
        ProfileDiscoveryResult r = ProfileDiscovery.Rank(shared, vsc);
        Eq(r.Outcome, ProfileDiscoveryOutcome.Unique, "a name match resolves a crowded root");
        Eq(r.Best.Name, "Code", "the name match wins over the most recently modified");
        Eq(r.Ranked.Count, 4, "the others are still reported for the user to see");
        Check(r.Ranked[0].NameMatched, "the winner is flagged as a name match");

        // Claude: its own root, holding the original profile and a Twinstall second instance.
        IList<string> claude = ProfileDiscovery.IdentityNames(@"C:\wa\Claude_x\app\claude.exe", "Claude", null, null);
        var twoProfiles = new List<ProfileCandidate> {
            Candidate(@"C:\r\Claude", 3, true, 5),
            Candidate(@"C:\r\second", 3, true, 0)
        };
        r = ProfileDiscovery.Rank(twoProfiles, claude);
        Eq(r.Outcome, ProfileDiscoveryOutcome.Unique, "the original profile is identifiable");
        Eq(r.Best.Name, "Claude", "'second' does not outrank 'Claude' by being newer");

        // No name match and several candidates: say so rather than guess.
        r = ProfileDiscovery.Rank(shared, ProfileDiscovery.IdentityNames(@"C:\p\Unknown.exe", null, null, null));
        Eq(r.Outcome, ProfileDiscoveryOutcome.Ambiguous, "no name match among several is ambiguous");
        Eq(r.Best.Name, "ClickUp", "with nothing better, most recently modified leads");

        // The 9 August 2026 case. Kimi keeps its profile in "kimi-desktop" and OpenCode in
        // "ai.opencode.desktop", so neither could identify its own folder under exact matching
        // and the fallback named Kimi's folder as OpenCode's account. Token matching fixes both.
        IList<string> oc = ProfileDiscovery.IdentityNames(@"C:\p\OpenCode.exe", "OpenCode", null, null);
        IList<string> kimi = ProfileDiscovery.IdentityNames(@"C:\p\Kimi.exe", "Kimi", null, null);

        Check(ProfileDiscovery.NameMatches("ai.opencode.desktop", oc), "a dotted folder matches on a token");
        Check(ProfileDiscovery.NameMatches("kimi-desktop", kimi), "a hyphenated folder matches on a token");
        Check(!ProfileDiscovery.NameMatches("kimi-desktop", oc), "and not on somebody else's token");

        var neitherMatches = new List<ProfileCandidate> {
            Candidate(@"C:\r\kimi-desktop",        3, true, 0),   // in use, so newest
            Candidate(@"C:\r\ai.opencode.desktop", 3, true, 5)
        };
        r = ProfileDiscovery.Rank(neitherMatches, oc);
        Eq(r.Outcome, ProfileDiscoveryOutcome.Unique, "the right folder is now identifiable");
        Eq(r.Best.Name, "ai.opencode.desktop", "the token match beats the newer decoy");
        r = ProfileDiscovery.Rank(neitherMatches, kimi);
        Eq(r.Best.Name, "kimi-desktop", "and each app finds its own");

        // The reason token matching is safe. A different build of the same product is a
        // different application with its own profile, and must never match.
        IList<string> discord = ProfileDiscovery.IdentityNames(@"C:\p\Discord.exe", "Discord", null, null);
        Check(ProfileDiscovery.NameMatches("Discord", discord), "Discord matches its own folder");
        Check(!ProfileDiscovery.NameMatches("DiscordCanary", discord), "camel case is one token, so Canary is not Discord");
        Check(!ProfileDiscovery.NameMatches("DiscordPTB", discord), "nor is PTB");
        Check(!ProfileDiscovery.NameMatches("Discord-Canary", discord), "and separating it does not help it either");
        Check(!ProfileDiscovery.NameMatches("Code - Insiders", vsc), "Insiders is not Visual Studio Code");
        Check(ProfileDiscovery.NameMatches("Code", vsc), "the real one still matches");

        // The real %APPDATA% on the development machine, 9 Aug 2026: every folder there holding
        // a Local State. Both apps must find their own and nothing else, against the actual
        // neighbours rather than a convenient pair.
        var realRoot = new List<ProfileCandidate> {
            Candidate(@"C:\r\ai.opencode.desktop", 3, true, 5),
            Candidate(@"C:\r\arduino-ide",         3, true, 6),
            Candidate(@"C:\r\asus_framework",      3, true, 7),
            Candidate(@"C:\r\ClickUp",             3, true, 1),
            Candidate(@"C:\r\Code",                3, true, 2),
            Candidate(@"C:\r\Docker Desktop",      3, true, 8),
            Candidate(@"C:\r\eigent",              3, true, 9),
            Candidate(@"C:\r\kimi-desktop",        3, true, 0),   // in use, so newest
            Candidate(@"C:\r\Loom",                3, true, 3),
            Candidate(@"C:\r\Riot Client",         3, true, 10),
            Candidate(@"C:\r\riot-client-ux",      3, true, 11),
            Candidate(@"C:\r\Slack",               3, true, 4),
            Candidate(@"C:\r\UI Launcher",         3, true, 12)
        };
        r = ProfileDiscovery.Rank(realRoot, oc);
        Eq(r.Outcome, ProfileDiscoveryOutcome.Unique, "OpenCode resolves against the real root");
        Eq(r.Best.Name, "ai.opencode.desktop", "and picks its own folder out of thirteen");
        r = ProfileDiscovery.Rank(realRoot, kimi);
        Eq(r.Outcome, ProfileDiscoveryOutcome.Unique, "Kimi resolves against the real root");
        Eq(r.Best.Name, "kimi-desktop", "even though it is also the most recently written");
        r = ProfileDiscovery.Rank(realRoot, vsc);
        Eq(r.Best.Name, "Code", "and VS Code is unaffected by the loosening");

        // Generic tails must never carry a match on their own, or every Electron app on the
        // machine would answer to "desktop".
        Check(!ProfileDiscovery.NameMatches("kimi-desktop",
                  ProfileDiscovery.IdentityNames(@"C:\p\Whatever.exe", "desktop", null, null)),
              "'desktop' is not an identity");

        // One candidate needs no name match to be the answer.
        r = ProfileDiscovery.Rank(new List<ProfileCandidate> { Candidate(@"C:\r\Whatever", 1, false, 3) },
                                  ProfileDiscovery.IdentityNames(@"C:\p\App.exe", null, null, null));
        Eq(r.Outcome, ProfileDiscoveryOutcome.Unique, "a lone candidate is the answer");

        // Folders with no markers are not profiles.
        r = ProfileDiscovery.Rank(new List<ProfileCandidate> { Candidate(@"C:\r\Empty", 0, false, 1) }, claude);
        Eq(r.Outcome, ProfileDiscoveryOutcome.NoneFound, "no markers means the app has never run");
        Eq(r.Best, null, "nothing found means no best");

        Eq(ProfileDiscovery.Rank(null, claude).Outcome, ProfileDiscoveryOutcome.NoRoot, "null input is NoRoot");

        // Local State outranks a folder with only corroborating markers.
        r = ProfileDiscovery.Rank(new List<ProfileCandidate> {
            Candidate(@"C:\r\a", 2, false, 0),
            Candidate(@"C:\r\b", 1, true,  9)
        }, ProfileDiscovery.IdentityNames(@"C:\p\None.exe", null, null, null));
        Eq(r.Best.Name, "b", "Local State is definitive, other markers only corroborate");
    }

    // -------------------------------------------------------- scheme matching --
    static void SchemeMatcherTests()
    {
        Eq(SchemeMatcher.ExtractExecutable("\"C:\\a\\App.exe\" \"%1\""), @"C:\a\App.exe", "quoted handler");
        Eq(SchemeMatcher.ExtractExecutable(@"C:\a\App.exe %1"), @"C:\a\App.exe", "bare handler");

        // VS Code puts flags between the exe and %1; taking the whole string never matches.
        Eq(SchemeMatcher.ExtractExecutable("\"C:\\p\\Microsoft VS Code\\Code.exe\" --open-url -- \"%1\""),
           @"C:\p\Microsoft VS Code\Code.exe", "arguments before %1 are discarded");
        Eq(SchemeMatcher.ExtractExecutable(""), "", "empty command");
        Eq(SchemeMatcher.ExtractExecutable(null), "", "null command");
        Eq(SchemeMatcher.ExtractExecutable("\"unterminated"), "", "unterminated quote is not a path");

        const string slack = @"C:\Users\a\AppData\Local\slack\app-4.51.180\slack.exe";
        Eq(SchemeMatcher.Classify("\"" + slack + "\" \"%1\"", slack), SchemeOwner.Direct, "handler is the app");

        // The real state of this machine: claude:// is held by the predecessor router.
        const string claudeExe = @"C:\Program Files\WindowsApps\Claude_1.25927.0.0_x64__pzs8sxrjxfjjc\app\claude.exe";
        Eq(SchemeMatcher.Classify("\"C:\\Users\\a\\AppData\\Local\\ClaudeRouter\\ClaudeRouter.exe\" \"%1\"", claudeExe),
           SchemeOwner.Foreign, "a scheme already taken over reads as foreign");

        // A helper inside the same package is still the app.
        Eq(SchemeMatcher.Classify(
             "\"C:\\Program Files\\WindowsApps\\Claude_1.25927.0.0_x64__pzs8sxrjxfjjc\\app\\resources\\helper.exe\" \"%1\"",
             claudeExe),
           SchemeOwner.SamePackage, "same package family counts as the app");

        // Different package, same publisher shape: must not be confused for ours.
        Eq(SchemeMatcher.Classify(
             "\"C:\\Program Files\\WindowsApps\\Other_1.0.0.0_x64__pzs8sxrjxfjjc\\app\\other.exe\" \"%1\"",
             claudeExe),
           SchemeOwner.Foreign, "a different package is foreign even under one publisher");

        Eq(SchemeMatcher.Classify("", claudeExe), SchemeOwner.Unknown, "no command is unknown");
        Eq(SchemeMatcher.Classify("\"x.exe\"", ""), SchemeOwner.Unknown, "no target is unknown");

        // Manifest parsing: namespace prefix must not matter.
        const string manifest =
            "<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\" " +
            "xmlns:uap=\"http://schemas.microsoft.com/appx/manifest/uap/windows10\" " +
            "xmlns:uap3=\"http://schemas.microsoft.com/appx/manifest/uap/windows10/3\">" +
            "<Applications><Application Id=\"Claude\"><Extensions>" +
            "<uap3:Extension Category=\"windows.protocol\"><uap3:Protocol Name=\"claude\" /></uap3:Extension>" +
            "<uap:Extension Category=\"windows.protocol\"><uap:Protocol Name=\"anthropic\" /></uap:Extension>" +
            "<uap:Extension Category=\"windows.protocol\"><uap:Protocol Name=\"CLAUDE\" /></uap:Extension>" +
            "</Extensions></Application></Applications></Package>";

        IList<string> protocols = SchemeMatcher.ProtocolsFromManifest(manifest);
        Eq(protocols.Count, 2, "duplicate scheme names collapse case-insensitively");
        Eq(protocols[0], "claude", "uap3 protocol read");
        Eq(protocols[1], "anthropic", "uap protocol read");

        Eq(SchemeMatcher.ProtocolsFromManifest("<not xml").Count, 0, "malformed manifest yields nothing");
        Eq(SchemeMatcher.ProtocolsFromManifest(null).Count, 0, "null manifest yields nothing");
        Eq(SchemeMatcher.ProtocolsFromManifest("<Package />").Count, 0, "a manifest with no protocols is empty");

        // The package directory the manifest is read from.
        Eq(PackagePaths.PackageRoot(claudeExe),
           @"C:\Program Files\WindowsApps\Claude_1.25927.0.0_x64__pzs8sxrjxfjjc", "package root derived");
        Eq(PackagePaths.PackageRoot(@"C:\Users\a\AppData\Local\slack\slack.exe"), null, "unpackaged has no root");
    }

    static int Main()
    {
        PathTests();
        IsolationTests();
        CommandLineTests();
        PackageTests();
        ChromiumTests();
        LauncherStubTests();
        RoutingTests();
        ConfigTests();
        LaunchProbeTests();
        ProfileDiscoveryTests();
        SchemeMatcherTests();

        Console.WriteLine();
        Console.WriteLine("passed: " + passed + "   failed: " + failed);
        if (failed > 0)
        {
            Console.WriteLine();
            foreach (string f in failures) Console.WriteLine("  FAIL  " + f);
            return 1;
        }
        Console.WriteLine("all green");
        return 0;
    }
}
