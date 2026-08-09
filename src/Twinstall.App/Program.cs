using System;
using System.Globalization;
using System.Windows.Forms;

namespace Twinstall.App
{
    internal static class Program
    {
        /// <summary>
        /// One binary, four jobs, dispatched on argv.
        ///
        /// The order matters. A protocol callback sits between a browser sign-in and the app
        /// opening, so the routing path must not pay for the management UI: nothing here
        /// touches a WinForms type before the mode is known, which keeps those assemblies
        /// unloaded on the path that actually has a user waiting.
        /// </summary>
        [STAThread]
        internal static int Main(string[] argv)
        {
            try
            {
                if (argv != null && argv.Length > 0)
                {
                    string first = argv[0];

                    if (string.Equals(first, "--launch", StringComparison.OrdinalIgnoreCase))
                        return Router.Launch(argv.Length > 1 ? argv[1] : string.Empty);

                    if (string.Equals(first, "--watch", StringComparison.OrdinalIgnoreCase))
                    {
                        int seconds = 0;
                        if (argv.Length > 1)
                            int.TryParse(argv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds);
                        return Router.Watch(seconds);
                    }

                    // Recompose badged icons from the saved config. Needed after the target
                    // app updates and replaces the logo we derived them from, and useful on
                    // its own when an icon file has been lost or locked.
                    if (string.Equals(first, "--compose", StringComparison.OrdinalIgnoreCase))
                    {
                        Twinstall.Core.AppConfig cfg = ConfigStore.Load();
                        int written = Router.ComposeIcons(cfg);
                        Log.Write("--compose wrote " + written.ToString(CultureInfo.InvariantCulture) + " icon(s)");
                        return written > 0 ? 0 : 1;
                    }

                    // Why didn't it find my app? Answers that without guesswork: shows every
                    // preset, every hint, what each hint expands to, and whether the
                    // executable was found there.
                    if (string.Equals(first, "--presets", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Write("--- preset diagnostic ---");
                        System.Collections.Generic.IList<Preset> all = Presets.Load();
                        Log.Write("apps.json base directory: " + AppContext.BaseDirectory);
                        Log.Write("presets loaded: " + all.Count.ToString(CultureInfo.InvariantCulture));
                        foreach (Preset p in all)
                        {
                            string found = Presets.FindInstalled(p);
                            Log.Write("  " + p.DisplayName + "  exeName=" + (p.ExeName ?? "(null)")
                                      + "  hints=" + p.Hints.Count.ToString(CultureInfo.InvariantCulture)
                                      + (p.Supported ? string.Empty : "  [HIDDEN: measured as unsupportable]")
                                      + "  -> " + (found ?? "NOT FOUND"));
                            foreach (string h in p.Hints)
                                Log.Write("      hint: " + h + "  =>  "
                                          + string.Join(" ; ", Presets.DescribeHint(h)));
                        }
                        return 0;
                    }

                    // What Settings → Apps → Installed apps invokes.
                    if (string.Equals(first, "--uninstall", StringComparison.OrdinalIgnoreCase))
                        return Uninstaller.Run();

                    // Re-applies everything from the saved config against *this* copy of the
                    // executable. The reason it exists: shortcuts and the registered handler
                    // record an absolute path, so moving Twinstall — or setting it up from a
                    // build output and later installing it properly — silently breaks both.
                    if (string.Equals(first, "--repair", StringComparison.OrdinalIgnoreCase))
                    {
                        Twinstall.Core.AppConfig cfg = ConfigStore.Load();
                        if (string.IsNullOrWhiteSpace(cfg.ExePath))
                        {
                            Log.Write("--repair: nothing configured");
                            return 1;
                        }

                        int icons = Router.ComposeIcons(cfg);
                        int links = Registration.CreateShortcuts(cfg, alsoOnDesktop: true);
                        bool proto = Registration.RegisterProtocol(cfg.Scheme);
                        Registration.RegisterUninstallEntry();

                        // The login entry records an absolute path too, so it goes stale in
                        // exactly the way this switch exists to fix — and it was the one thing
                        // --repair did not put back, which left badges silently dead after the
                        // next sign-in with nothing on screen to say why.
                        bool wantsBadges = false;
                        foreach (Twinstall.Core.Instance i in cfg.Instances)
                            if (i.Badge) { wantsBadges = true; break; }
                        Registration.SetIconWatcher(wantsBadges);

                        Log.Write("--repair: re-pointed at " + AppPaths.SelfExe
                                  + " (" + icons.ToString(CultureInfo.InvariantCulture) + " icons, "
                                  + links.ToString(CultureInfo.InvariantCulture) + " shortcuts, protocol="
                                  + proto + ", watcher=" + wantsBadges + ")");
                        return proto ? 0 : 1;
                    }

                    if (string.Equals(first, "--version", StringComparison.OrdinalIgnoreCase))
                    {
                        Ui.Info("Twinstall " + typeof(Program).Assembly.GetName().Version);
                        return 0;
                    }

                    // Anything else that isn't a switch is the URL we were invoked for.
                    if (!first.StartsWith("--", StringComparison.Ordinal))
                        return Router.Route(first);
                }

                ApplicationConfiguration.Initialize();

                bool justInstalled = argv != null && argv.Length > 0
                    && string.Equals(argv[0], "--installed", StringComparison.OrdinalIgnoreCase);

                if (justInstalled)
                {
                    Installer.CompleteInstall();
                }
                else if (argv == null || argv.Length == 0)
                {
                    // A copy sitting in Downloads offers to move in properly. Declining is
                    // fine — it carries on running from where it is. --portable skips the ask.
                    if (Installer.OfferInstall()) return 0;   // the installed copy took over
                }

                // --preview <1-5> opens one screen directly with representative data. It exists
                // so the layout can be reviewed without clicking through the flow; it seeds
                // nothing on disk, starts no application, and applies nothing.
                if (argv != null && argv.Length > 1
                    && string.Equals(argv[0], "--preview", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(argv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int which)
                    && which >= 1 && which <= 5)
                {
                    var preview = new MainForm();
                    preview.PreviewAt((Step)(which - 1));
                    Application.Run(preview);
                    return 0;
                }

                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                // Last line of defence. An unhandled exception in a protocol handler is a
                // silent no-op from the user's side — the browser says it handed the link
                // over and nothing opens — so it has to leave a trace and say so.
                Log.Write("FATAL: " + ex);
                Ui.Error("Twinstall hit an unexpected problem.\r\n\r\n" + ex.Message
                       + "\r\n\r\nDetails are in:\r\n" + AppPaths.LogFile);
                return 1;
            }
        }
    }
}
