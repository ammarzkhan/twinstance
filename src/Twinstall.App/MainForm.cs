using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Twinstall.Core;
using Twinstall.Platform;

namespace Twinstall.App
{
    internal enum Step { ChooseApp, Result, Accounts, Review, Done }

    /// <summary>
    /// One job per screen.
    ///
    /// The previous version put app selection, a raw detection dump, an empty grid and the
    /// apply controls on one page, so the first thing a new user saw was four things they
    /// could not do yet. This walks through them instead, and only ever shows applications
    /// that are actually installed — a dropdown listing fifteen apps you do not have is not a
    /// menu, it is a quiz.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly Label _title = new Label();
        private readonly Label _crumb = new Label();
        private readonly Panel _content = new Panel();
        private readonly Label _status = new Label();
        private readonly FlatButton _back = new FlatButton("Back", ButtonKind.Subtle);
        private readonly FlatButton _next = new FlatButton("Continue", ButtonKind.Primary);
        private readonly FlatButton _remove = new FlatButton("Remove Twinstall's changes", ButtonKind.Danger);

        private readonly AppConfig _config = new AppConfig();
        private DetectionResult _detection;
        private IList<Preset> _presets = new List<Preset>();
        private readonly List<Preset> _installed = new List<Preset>();
        private string _chosenExe;
        private bool _taskbarOptIn;
        private Step _step = Step.ChooseApp;

        internal MainForm()
        {
            Text = "Twinstall";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 640);
            MinimumSize = new Size(660, 600);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;

            Icon = Branding.AppIcon();
            BuildChrome();
            Load += (s, e) => { LoadPresets(); Show(_previewStep ?? Step.ChooseApp); };

            // Choosing the handler happens in Settings, i.e. outside this window. Re-check on
            // the way back so the last screen answers "did that work?" without being asked —
            // and only rebuild when the answer actually changed, so it doesn't flicker.
            FormClosing += (s, e) => OnClosing(e);

            Activated += (s, e) =>
            {
                if (_step != Step.Done) return;
                bool now = Registration.WeHandle(_config.Scheme);
                if (now == _handlerWasSet) return;
                _handlerWasSet = now;
                ShowLater(Step.Done);
            };
        }

        private bool _handlerWasSet;

        /// <summary>
        /// Closing halfway through leaves a machine that has been changed but does not work:
        /// shortcuts, icons, a registered ProgId and possibly a system-wide taskbar setting, and
        /// sign-ins still landing on the wrong account. Offer to put it back rather than leave
        /// that behind silently — but do not decide for them, because the shortcuts alone are
        /// genuinely useful and they may simply intend to finish later.
        /// </summary>
        private void OnClosing(FormClosingEventArgs e)
        {
            if (!_lastRegistered || Registration.WeHandle(_config.Scheme)) return;

            DialogResult choice = Ui.Choose(
                "Twinstall is not finished.\r\n\r\n"
              + "It has added shortcuts, icons and a registry entry, but nothing is handling "
              + (_config.Scheme ?? "these") + ":// links yet — so a sign-in will still open the "
              + "wrong account, which is the problem Twinstall exists to fix.\r\n\r\n"
              + "Yes\t\tUndo everything Twinstall changed\r\n"
              + "No\t\tLeave it as it is; I'll finish later\r\n"
              + "Cancel\tGo back and finish now");

            if (choice == DialogResult.Cancel) { e.Cancel = true; return; }
            if (choice != DialogResult.Yes) return;

            UndoEverything();
            Ui.Info("Twinstall's changes have been removed.\r\n\r\n"
                  + "Your profile folders were left untouched — including anything you have "
                  + "already signed into.");
        }

        private Step? _previewStep;

        /// <summary>
        /// Opens straight onto one screen with representative data, so the layout of every step
        /// can be looked at without clicking through the whole flow. Development affordance —
        /// it seeds nothing on disk and applies nothing. Reached only via --preview.
        /// </summary>
        internal void PreviewAt(Step step)
        {
            _previewStep = step;
            Load += (s, e) =>
            {
                // A real saved setup wins: previewing should show this machine's actual state
                // where there is one, and only invent data when there is nothing to show.
                bool haveReal = !string.IsNullOrWhiteSpace(_config.ExePath);
                if (_chosenExe == null)
                    _chosenExe = haveReal ? _config.ExePath
                               : (_installed.Count > 0 ? Presets.FindInstalled(_installed[0]) : null);

                if (step >= Step.Result && _chosenExe != null && !haveReal)
                {
                    // Real detection minus the launch test: genuine data, nothing started.
                    _detection = Detector.Run(_chosenExe, runProbe: false);
                    _detection.Probe = ProbeVerdict.Honoured;
                    _detection.ProbeDetail = "(preview — the launch test was not run)";
                    _config.ExePath = _detection.ExePath;
                    _config.Scheme = _detection.Scheme;
                }

                if (step >= Step.Accounts && _config.Instances.Count == 0)
                {
                    string root = _detection?.ProfileRoot ?? @"C:\Users\you\AppData\Roaming";
                    _config.Instances.Add(new Instance
                    { Name = "Work", DataDir = PathUtil.Join(root, "Work"), Colour = "#2563EB", Badge = true });
                    _config.Instances.Add(new Instance
                    { Name = "Personal", DataDir = PathUtil.Join(root, "Personal"), Colour = "#059669", Badge = true });
                }

                if (step == Step.Done) { _lastShortcuts = 2; _lastRegistered = _config.Scheme != null; }

                Show(step);
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyWindowChrome(Handle);
        }

        // ------------------------------------------------------------- chrome --
        private void BuildChrome()
        {
            _title.Font = Theme.Title;
            _title.ForeColor = Theme.Text;
            _title.AutoSize = false;
            _title.Location = new Point(32, 28);
            _title.Size = new Size(640, 34);
            Controls.Add(_title);

            _crumb.Font = Theme.Small;
            _crumb.ForeColor = Theme.TextMuted;
            _crumb.AutoSize = false;
            _crumb.Location = new Point(34, 64);
            _crumb.Size = new Size(640, 20);
            Controls.Add(_crumb);

            _content.Location = new Point(32, 96);
            _content.Size = new Size(656, 448);
            // A stock Panel has no SupportsTransparentBackColor, so it gets the page colour.
            _content.BackColor = Theme.Background;
            _content.AutoScroll = true;
            Controls.Add(_content);

            _status.Font = Theme.Small;
            _status.ForeColor = Theme.TextMuted;
            _status.AutoSize = false;
            _status.Location = new Point(34, 566);
            _status.Size = new Size(360, 36);
            Controls.Add(_status);

            _back.Size = new Size(96, 40);
            _back.Location = new Point(432, 562);
            _back.Click += (s, e) => GoBack();
            Controls.Add(_back);

            _next.Size = new Size(150, 40);
            _next.Location = new Point(538, 562);
            _next.Click += (s, e) => GoNext();
            Controls.Add(_next);

            // Footer chrome, not page content — see Relayout.
            _remove.Size = new Size(230, 40);
            _remove.Location = new Point(34, 562);
            _remove.Visible = false;
            _remove.Click += (s, e) => RemoveEverything();
            Controls.Add(_remove);

            Resize += (s, e) => Relayout();
        }

        /// <summary>
        /// The footer row: undo on the left, status text in the middle, Back and Continue on the
        /// right. Everything is measured from the window edges so it holds at any size.
        ///
        /// The undo button belongs here rather than at the bottom of step 1, which is where it
        /// used to be. Step 1 lists every known app this PC has installed, so its height is
        /// decided by the machine — five apps and a saved config came to 502px in a 448px panel,
        /// which put the button below the fold behind a scrollbar. That is survivable for a list
        /// of apps, where scrolling for more of a list is what a list looks like, but not for
        /// this: it is the only way to undo a setup from the window, and the page it was on is
        /// the one screen nobody thinks to scroll. In the footer it sits beside Continue at a
        /// fixed distance from the bottom edge, whether the PC has one known app or twenty.
        /// </summary>
        private void Relayout()
        {
            _title.Size = new Size(ClientSize.Width - 64, 34);
            _crumb.Size = new Size(ClientSize.Width - 64, 20);
            _content.Size = new Size(ClientSize.Width - 64, ClientSize.Height - 192);

            int row = ClientSize.Height - 78;
            _next.Location = new Point(ClientSize.Width - 182, row);
            _back.Location = new Point(ClientSize.Width - 288, row);
            _remove.Location = new Point(34, row);

            // The status line takes what is left between the two groups. Its message on step 1
            // names the configured app, so it is at its longest on exactly the screen where the
            // undo button is also present — hence taken from the buttons rather than fixed.
            int left = _remove.Visible ? _remove.Right + 16 : 34;
            int right = (_back.Visible ? _back.Left : _next.Left) - 8;
            _status.Location = new Point(left, row + 4);
            _status.Size = new Size(Math.Max(0, right - left), 36);

            // Nothing inside the panel is touched here. The children that follow its width are
            // anchored as they are added — see AddFullWidth.
        }

        /// <summary>
        /// Adds a child of the content panel whose width has to keep pace with the panel's.
        ///
        /// A step's controls are positioned absolutely and sized from <c>_content.Width</c> once,
        /// while the page is being built. <see cref="Relayout"/> resizes the panel on every Resize
        /// event, but re-running the builder from there is not an option: a rebuild disposes
        /// controls that may still be inside their own Click handler, which is the whole reason
        /// <see cref="ShowLater"/> exists. An anchor moves the job to WinForms instead.
        ///
        /// Without it, dragging the window down to its 660x600 minimum left every row wider than
        /// the panel it sits in — AutoScroll then put up a horizontal scrollbar and the rows ran
        /// under the vertical one. Widening left a ragged right margin instead.
        ///
        /// Three things measured on .NET 8 against the running window rather than assumed, because
        /// each of them decides whether this works at all:
        /// - **Nothing moves at the default size**, and that rests on <c>Show(Step)</c> batching
        ///   the build between SuspendLayout and ResumeLayout. The anchor records a child's gap
        ///   from the panel's *client* edge at its first layout; batching means that one pass adds
        ///   every child and settles the vertical scrollbar together, so the gap recorded is the
        ///   real one. Rows measured 632px wide before this change and 632px after — only resizing
        ///   behaves differently. Add the children unbatched and the scrollbar arrives mid-build
        ///   instead, after the gap is already banked, and every row loses its width to it.
        ///   Don't "tidy" the hand-picked -24/-26/-28/-30 widths at the call sites into one number
        ///   either: they are what the recorded gap is made of.
        /// - **A control that was invisible while the panel resized still comes back at the right
        ///   width when it is shown.** BuildResult's technical-details box is added hidden and
        ///   toggled by a button, and it was the one case with a real chance of not working.
        /// - The panel still scrolls itself slightly on resize, keeping the focused control in
        ///   view. That is WinForms and it predates this: step 4 went from -26px to -9px, and
        ///   stopped scrolling sideways at all.
        /// </summary>
        private void AddFullWidth(Control c)
        {
            c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _content.Controls.Add(c);
        }

        private void LoadPresets()
        {
            _presets = Presets.Load();
            _installed.Clear();
            foreach (Preset p in _presets)
                if (Presets.FindInstalled(p) != null) _installed.Add(p);

            AppConfig saved = ConfigStore.Load();
            if (!string.IsNullOrWhiteSpace(saved.ExePath))
            {
                _config.ExePath = saved.ExePath;
                _config.Scheme = saved.Scheme;
                _config.DefaultDataDir = saved.DefaultDataDir;
                foreach (Instance i in saved.Instances) _config.Instances.Add(i);
            }
            _taskbarOptIn = Registration.TaskbarNeverCombine();
            _taskbarWasOnBefore = _taskbarOptIn;
        }

        /// <summary>So a rollback only reverts the taskbar setting if this run turned it on.</summary>
        private bool _taskbarWasOnBefore;

        /// <summary>
        /// On by default. Someone setting up a second account of an app they use daily wants to
        /// reach it, and hunting the Start menu for it is the thing they were already doing.
        /// It is a visible, labelled checkbox on the review screen, so it is never a surprise.
        /// </summary>
        private bool _desktopShortcuts = true;

        // ------------------------------------------------------------ stepping --
        private void Show(Step step)
        {
            _step = step;
            _content.SuspendLayout();
            // Panels are rebuilt per step rather than hidden, so the controls have to be
            // disposed here — they are not the Form's Controls, so nothing else will.
            foreach (Control c in ToArray(_content.Controls))
            {
                _content.Controls.Remove(c);
                c.Dispose();
            }

            // Undoing a setup is offered from step 1 only; the later steps are mid-setup, and
            // the way out of those is the Back button. Cleared here so no step has to remember.
            _remove.Visible = false;

            switch (step)
            {
                case Step.ChooseApp: BuildChooseApp(); break;
                case Step.Result:    BuildResult();    break;
                case Step.Accounts:  BuildAccounts();  break;
                case Step.Review:    BuildReview();    break;
                default:             BuildDone();      break;
            }

            _content.ResumeLayout();
            Relayout();
        }

        /// <summary>
        /// Rebuilds the page *after* the current event finishes.
        ///
        /// Rows and swatches rebuild the page from their own Click handler, and the rebuild
        /// disposes the control that is still executing that handler. WinForms tolerates that
        /// unpredictably — the symptom is a page that half-repaints, with the new button text
        /// but the old contents. Deferring by one message lets the handler return first.
        /// </summary>
        private void ShowLater(Step step)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() => Show(step)));
        }

        private static Control[] ToArray(Control.ControlCollection c)
        {
            var list = new List<Control>();
            foreach (Control x in c) list.Add(x);
            return list.ToArray();
        }

        private void Head(string title, string crumb)
        {
            _title.Text = title;
            _crumb.Text = crumb;
        }

        private void Say(string message) { _status.Text = message; }

        // ------------------------------------------------- step 1: choose app --
        private void BuildChooseApp()
        {
            Head("Run two accounts, side by side", "Step 1 of 4  ·  Choose an app");
            _back.Visible = false;
            _next.Text = "Check this app";
            _next.Enabled = _chosenExe != null;

            int y = 0;
            if (_installed.Count == 0)
            {
                y = AddNote(y, "None of the apps Twinstall knows about were found on this PC. "
                             + "That is fine — any Chromium or Electron app works. Point it at the .exe.");
            }
            else
            {
                y = AddNote(y, "These are installed on this PC. Twinstall will start the one you pick, "
                             + "once, to make sure it really supports separate accounts.");

                foreach (Preset p in _installed)
                {
                    string path = Presets.FindInstalled(p);
                    var row = new ChoiceRow
                    {
                        Title = p.DisplayName,
                        Subtitle = path,
                        Width = _content.Width - 24,
                        Location = new Point(0, y),
                        Selected = string.Equals(path, _chosenExe, StringComparison.OrdinalIgnoreCase),
                        Tag2 = path
                    };
                    row.Click += (s, e) => ChooseApp((string)((ChoiceRow)s).Tag2);
                    AddFullWidth(row);
                    y += 68;
                }
            }

            var browse = new FlatButton("Choose another app…", ButtonKind.Secondary)
            {
                Location = new Point(0, y + 6),
                Size = new Size(200, 40)
            };
            browse.Click += (s, e) => Browse();
            _content.Controls.Add(browse);

            if (_config.Instances.Count > 0)
            {
                // Lives in the footer, beside Continue — see Relayout for why it is not here.
                _remove.Visible = true;
                // The status line is small, and smaller still now the undo button shares the
                // footer with it, while a Store app's path runs to about ninety characters —
                // it was being clipped mid-word ("...\app\Claude.ex"), which reads as a
                // rendering fault rather than as a long path. The executable's own name is the
                // part a user recognises, and the full path is already shown on the row above.
                Say("Twinstall is already set up for "
                    + (string.IsNullOrWhiteSpace(_config.ExePath)
                        ? "an app"
                        : Path.GetFileNameWithoutExtension(_config.ExePath))
                    + ".");
            }
            else
            {
                Say(string.Empty);
            }
        }

        private int AddNote(int y, string text)
        {
            var note = new Label
            {
                Text = text,
                Font = Theme.Body,
                ForeColor = Theme.TextMuted,
                AutoSize = false,
                Location = new Point(2, y),
                Size = new Size(_content.Width - 28, 44)
            };
            AddFullWidth(note);
            return y + 54;
        }

        private void ChooseApp(string path)
        {
            _chosenExe = path;
            ShowLater(Step.ChooseApp);
        }

        private void Browse()
        {
            using (var dlg = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", Title = "Choose the application" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) ChooseApp(dlg.FileName);
            }
        }

        // ----------------------------------------------------- step 2: result --
        private async Task RunDetectionAsync()
        {
            Head("Checking " + FriendlyName(_chosenExe), "Step 2 of 4  ·  Making sure it works");
            _step = Step.Result;
            _content.Controls.Clear();
            _back.Visible = false;
            _next.Enabled = false;
            _next.Text = "Continue";

            AddNote(0, "Starting it once with a throwaway profile. A window will appear and close again — "
                     + "that is the test, and nothing on your PC has been changed yet.");
            Say("Working…");
            UseWaitCursor = true;

            string exe = _chosenExe;
            DetectionResult result = await Task.Run(() => Detector.Run(exe, runProbe: true)).ConfigureAwait(true);

            UseWaitCursor = false;
            _detection = result;

            if (result.Probe == ProbeVerdict.Honoured)
            {
                _config.ExePath = result.ExePath;
                _config.Scheme = result.Scheme;
                _config.DefaultDataDir = result.Profiles?.Best?.Directory;

                if (_config.Instances.Count == 0 && result.Profiles?.Best != null)
                {
                    _config.Instances.Add(new Instance
                    {
                        Name = result.Profiles.Best.Name,
                        DataDir = result.Profiles.Best.Directory,
                        Colour = "#2563EB",
                        Badge = true
                    });
                }
            }
            Show(Step.Result);
        }

        private void BuildResult()
        {
            DetectionResult r = _detection;
            bool ok = r != null && r.Probe == ProbeVerdict.Honoured;
            string appName = FriendlyName(r?.ExePath ?? _chosenExe);

            Head(ok ? appName + " can do this" : appName + " cannot do this",
                 "Step 2 of 4  ·  Result");

            _back.Visible = true;
            _next.Visible = ok;
            _next.Text = "Set up accounts";
            _next.Enabled = ok;

            int y = 0;
            var summary = new Label
            {
                Text = ok
                    ? "It created a separate profile where it was told to, so two accounts will work."
                    : LaunchProbe.Explain(r?.Probe ?? ProbeVerdict.Invalid, appName),
                Font = Theme.Body,
                ForeColor = ok ? Theme.Text : Theme.Bad,
                AutoSize = false,
                Location = new Point(2, y),
                Size = new Size(_content.Width - 28, 48)
            };
            AddFullWidth(summary);
            y += 58;

            if (r != null)
            {
                y = AddFact(y, r.IsChromium, r.IsChromium
                    ? "Built on Chromium, so it supports separate profiles"
                    : "Not a Chromium or Electron app — separate profiles are not possible");

                if (r.StubResolved)
                    y = AddFact(y, true, "Found the real program behind the launcher shortcut");

                // Assert the profile only when the ranking actually identified one.
                // ProfileDiscoveryOutcome exists precisely because unpackaged apps share
                // %APPDATA%: scanning it on a real machine returns a Chromium profile for every
                // Electron app installed. With no name match the ranking falls back to
                // most-recently-written — whichever app you used last — and this row printed
                // that as a green tick regardless. Setting up OpenCode, it stated that the
                // existing account lived in "kimi-desktop", which belongs to Kimi; accepting
                // that would have pointed the default instance at another app's data. The same
                // row was right for Kimi only because Kimi happened to be the app running.
                if (r.Profiles != null)
                {
                    switch (r.Profiles.Outcome)
                    {
                        case ProfileDiscoveryOutcome.Unique:
                            if (r.Profiles.Best != null)
                                y = AddFact(y, true, "Your existing account lives in “" + r.Profiles.Best.Name + "”");
                            break;

                        case ProfileDiscoveryOutcome.Ambiguous:
                            y = AddFact(y, false, "Could not tell which existing profile is this app's — "
                                               + "you will choose it on the next step");
                            break;

                        case ProfileDiscoveryOutcome.NoneFound:
                            y = AddFact(y, false, "No existing profile yet — run the app once, then check again");
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(r.Scheme))
                    y = AddFact(y, true, "Sign-in links use " + r.Scheme + ":// — Twinstall can route those");
                else
                    y = AddFact(y, true, "No sign-in links to route — profiles alone are enough");

                y = AddFact(y, ok, ok ? "Separate profiles confirmed by actually running it"
                                      : "The profile test did not pass");
            }

            var details = new FlatButton("Technical details", ButtonKind.Subtle)
            {
                Location = new Point(-6, y + 8),
                Size = new Size(150, 32)
            };
            var dump = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.TextMuted,
                Font = Theme.Mono,
                Location = new Point(0, y + 46),
                Size = new Size(_content.Width - 26, 150),
                Visible = false,
                Text = Describe(r)
            };
            details.Click += (s, e) => { dump.Visible = !dump.Visible; };
            _content.Controls.Add(details);
            AddFullWidth(dump);

            Say(ok ? "Nothing has been changed yet." : "Nothing was changed.");
        }

        private int AddFact(int y, bool good, string text)
        {
            var f = new FactRow(text, good) { Location = new Point(2, y), Width = _content.Width - 28 };
            AddFullWidth(f);
            return y + 30;
        }

        // --------------------------------------------------- step 3: accounts --
        private void BuildAccounts()
        {
            Head("Your accounts", "Step 3 of 4  ·  Name them and pick colours");
            _back.Visible = true;
            _next.Visible = true;
            _next.Text = "Review changes";
            _next.Enabled = _config.Instances.Count >= 2;

            int y = AddNote(0, "Each one gets its own profile folder and its own badged taskbar icon. "
                             + "The first is the account you already use.");

            foreach (Instance inst in _config.Instances)
            {
                Instance captured = inst;
                var row = new ChoiceRow
                {
                    Title = inst.Name,
                    Subtitle = inst.DataDir,
                    Dot = Theme.FromHex(inst.Colour),
                    Width = _content.Width - 24,
                    Location = new Point(0, y),
                    Tag2 = captured
                };
                row.Click += (s, e) => BeginInvoke(new Action(() => EditInstance(captured)));
                AddFullWidth(row);
                y += 68;
            }

            var add = new FlatButton("Add an account", ButtonKind.Secondary)
            {
                Location = new Point(0, y + 6),
                Size = new Size(170, 40)
            };
            add.Click += (s, e) => AddInstance();
            _content.Controls.Add(add);

            Say(_config.Instances.Count < 2
                ? "Add a second account — that is the point of Twinstall."
                : "Click an account to rename it, recolour it, or remove it.");
        }

        private void AddInstance()
        {
            using (var dlg = new InstanceDialog(null, _detection?.ProfileRoot, OtherProfileDirs(null)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Value == null) return;
                if (!EnsureIsolated(dlg.Value, null)) return;
                _config.Instances.Add(dlg.Value);
            }
            Show(Step.Accounts);
        }

        private void EditInstance(Instance existing)
        {
            int index = _config.Instances.IndexOf(existing);
            if (index < 0) return;

            using (var dlg = new InstanceDialog(existing, _detection?.ProfileRoot, OtherProfileDirs(existing)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                if (dlg.RemoveRequested)
                {
                    _config.Instances.RemoveAt(index);
                    Log.Write("removed account " + existing.Name);
                }
                else
                {
                    if (dlg.Value == null) return;
                    if (!EnsureIsolated(dlg.Value, existing)) return;
                    _config.Instances[index] = dlg.Value;
                }
            }
            Show(Step.Accounts);
        }

        /// <summary>
        /// Every configured folder except the one being edited. Editing an account has to
        /// exclude itself, or the isolation check compares it against its own folder, decides
        /// the two are not separate, and refuses to save a perfectly valid account.
        /// </summary>
        private List<string> OtherProfileDirs(Instance excluding)
        {
            var dirs = new List<string>();
            foreach (Instance i in _config.Instances)
                if (!ReferenceEquals(i, excluding)) dirs.Add(i.DataDir);
            return dirs;
        }

        /// <summary>
        /// No two accounts may share or nest their profile folders. This is the guarantee the
        /// whole product rests on, so it is enforced rather than suggested.
        /// </summary>
        private bool EnsureIsolated(Instance candidate, Instance ignoring)
        {
            foreach (Instance other in _config.Instances)
            {
                if (ReferenceEquals(other, ignoring)) continue;

                if (string.Equals(other.Name, candidate.Name, StringComparison.OrdinalIgnoreCase))
                {
                    Ui.Error("There is already an account called “" + candidate.Name + "”.");
                    return false;
                }

                var issues = IsolationCheck.Validate(other.DataDir, candidate.DataDir);
                if (issues.Count > 0)
                {
                    Ui.Error("These two accounts would not be kept separate:\r\n\r\n"
                           + "  " + other.Name + "  →  " + other.DataDir + "\r\n"
                           + "  " + candidate.Name + "  →  " + candidate.DataDir + "\r\n\r\n"
                           + issues[0] + ".");
                    return false;
                }
            }
            return true;
        }

        // ----------------------------------------------------- step 4: review --
        private void BuildReview()
        {
            Head("Ready when you are", "Step 4 of 4  ·  What will change");
            _back.Visible = true;
            _next.Visible = true;
            _next.Text = "Set up Twinstall";
            _next.Enabled = true;

            int y = AddNote(0, "Everything below is per-user. No administrator prompt, nothing outside your "
                             + "own profile, and all of it can be undone from this window.");

            y = AddOption(y, "Also put shortcuts on the Desktop", _desktopShortcuts,
                "Named “" + Registration.AppLabel(_config) + " - <account>” so they still make sense "
              + "sitting among everything else. The Start-menu ones are added either way.",
                on => _desktopShortcuts = on);

            y = AddOption(y, "Also set Windows to never combine taskbar buttons", _taskbarOptIn,
                "Needed for per-window icons. It affects every app on the system and only takes "
              + "effect after you sign out and back in. Off by default, never changed silently.",
                on => _taskbarOptIn = on);

            y += 6;
            foreach (string line in Registration.DescribeChanges(_config, _taskbarOptIn, _desktopShortcuts))
            {
                string text = "•   " + line;
                int width = _content.Width - 30;
                var l = new Label
                {
                    Text = text,
                    Font = Theme.Body,
                    ForeColor = Theme.Text,
                    AutoSize = false,
                    Location = new Point(4, y),
                    Size = new Size(width, MeasuredHeight(text, Theme.Body, width))
                };
                AddFullWidth(l);
                y = l.Bottom + 8;
            }

            Say(string.Empty);
        }

        /// <summary>
        /// One opt-in: a checkbox, and the explanation that belongs to it.
        ///
        /// Both of these sit above the list of changes rather than below it, because the list is
        /// the part of this screen whose length varies by machine — the scheme, the badges and
        /// these two options each add a line to it — and whatever comes last is what gets pushed
        /// out of sight. The note under the taskbar box is the only place anyone is told that
        /// setting is system-wide and applies to every app, so it is precisely the sentence that
        /// must not depend on how many bullets this particular PC happens to produce. Above the
        /// list it is on screen at every window size; below it, it was not on screen at the
        /// default one.
        ///
        /// Putting them first also stops them moving. Ticking a box adds or removes a bullet, and
        /// while the bullets were above, that shifted both boxes by a row — so the control you had
        /// just clicked jumped out from under the pointer, and the other one slid into its place.
        /// </summary>
        private int AddOption(int y, string label, bool ticked, string note, Action<bool> onChange)
        {
            var box = new CheckBox
            {
                Text = "  " + label,
                Font = Theme.Body,
                ForeColor = Theme.Text,
                BackColor = Theme.Background,
                Checked = ticked,
                AutoSize = false,
                FlatStyle = FlatStyle.Standard,
                Location = new Point(2, y),
                Size = new Size(_content.Width - 30, 26)
            };
            // Deferred, not immediate: this rebuild disposes the checkbox whose event is still
            // running. See ShowLater. The taskbar box used to call Show directly.
            box.CheckedChanged += (s, e) => { onChange(box.Checked); ShowLater(Step.Review); };
            AddFullWidth(box);

            int width = _content.Width - 52;
            var explain = new Label
            {
                Text = note,
                Font = Theme.Small,
                ForeColor = Theme.TextMuted,
                AutoSize = false,
                Location = new Point(24, y + 28),
                Size = new Size(width, MeasuredHeight(note, Theme.Small, width))
            };
            AddFullWidth(explain);
            return explain.Bottom + 12;
        }

        /// <summary>
        /// How tall wrapped text actually needs to be, at this width, in this font.
        ///
        /// Every height on this screen used to be a fixed number sized for its worst case. Each
        /// bullet reserved 34px — two lines — because two of them are file paths that wrap once
        /// the window is narrow or the user's name is long. The other six are one line of 17px
        /// and were given the same 34, which wasted more vertical space than the page was over by.
        /// Measuring gives the long lines their second line and the short ones nothing they do
        /// not use, at whatever width the panel happens to be.
        /// </summary>
        private static int MeasuredHeight(string text, Font font, int width)
        {
            // A Label pads its text slightly more than DrawText reports; without the margin,
            // descenders on the final line are shaved.
            return TextRenderer.MeasureText(
                text, font, new Size(width, 4000), TextFormatFlags.WordBreak).Height + 4;
        }

        private void Apply()
        {
            try
            {
                AppPaths.EnsureHome();
                foreach (Instance i in _config.Instances) Directory.CreateDirectory(i.DataDir);
                File.WriteAllText(AppPaths.ConfigFile, InstanceConfig.Serialise(_config));
            }
            catch (IOException ex) { Ui.Error("Could not write the configuration.\r\n\r\n" + ex.Message); return; }
            catch (UnauthorizedAccessException ex) { Ui.Error("Could not write the configuration.\r\n\r\n" + ex.Message); return; }

            int icons = Router.ComposeIcons(_config);
            int shortcuts = Registration.CreateShortcuts(_config, _desktopShortcuts);
            bool registered = !string.IsNullOrWhiteSpace(_config.Scheme)
                              && Registration.RegisterProtocol(_config.Scheme);

            if (_taskbarOptIn != Registration.TaskbarNeverCombine())
                Registration.SetTaskbarNeverCombine(_taskbarOptIn);

            // So it can be removed from Settings like anything else, not only from in here.
            Registration.RegisterUninstallEntry();

            // Badges do not stay applied on their own — see SetIconWatcher. Only worth running
            // if something actually wants a badge.
            bool wantsBadges = false;
            foreach (Instance i in _config.Instances) if (i.Badge) { wantsBadges = true; break; }

            Registration.SetIconWatcher(wantsBadges);
            if (wantsBadges) Registration.StartIconWatcher();

            Log.Write("setup applied: " + _config.Instances.Count + " accounts, "
                      + icons + " icons, " + shortcuts + " shortcuts");

            _lastShortcuts = shortcuts;
            _lastRegistered = registered;
            Show(Step.Done);
        }

        private int _lastShortcuts;
        private bool _lastRegistered;

        // ------------------------------------------------------- step 5: done --
        private void BuildDone()
        {
            bool handled = Registration.WeHandle(_config.Scheme);
            bool needsHandler = _lastRegistered && !handled;
            _handlerWasSet = handled;

            Head(handled ? "You're set up" : "One step left",
                 handled ? "All done" : "Twinstall is not handling links yet");

            _back.Visible = false;
            _next.Visible = true;
            _next.Kind = ButtonKind.Primary;

            // No "Finish anyway". Finishing here means believing it works when it does not, and
            // the failure is silent and confusing — a sign-in quietly opening the wrong account.
            // The way out is the X, which offers to undo everything rather than leave a
            // half-configured machine.
            _next.Text = needsHandler ? "Open Settings" : "Finish";
            _next.Enabled = true;

            int y = AddFact(0, true, _lastShortcuts.ToString(CultureInfo.InvariantCulture)
                                     + " Start-menu shortcut(s) added, one per account");
            if (_taskbarOptIn)
                y = AddFact(y, true, "Taskbar setting applies after you sign out and back in");

            if (!needsHandler)
            {
                if (handled) y = AddFact(y, true, "Twinstall is handling " + _config.Scheme + ":// links");
                y = AddNote(y + 6, "Open your second account from the Start menu and sign in as usual.");

                var test = new FlatButton("Test a link", ButtonKind.Secondary)
                {
                    Location = new Point(0, y),
                    Size = new Size(150, 40)
                };
                test.Click += (s, e) => Registration.SelfTest(_config.Scheme);
                _content.Controls.Add(test);

                AddLogButton(y + 52);
                Say("Everything Twinstall changed can be undone from the first screen.");
                return;
            }

            // --- the handler is still not set, and without it none of this works ------
            y = AddFact(y, false, "Nothing is handling " + _config.Scheme
                                  + ":// yet — sign-ins will still go to the wrong account");

            // Windows protects this setting against being changed programmatically, on purpose:
            // it is how browsers used to hijack each other. So the user has to click it, and the
            // most we can do is put them one click away. Say that once, briefly, and no more —
            // telling someone how to navigate to a page the button already opens is noise.
            y = AddNote(y + 4, "Windows won't let any app set this for itself. "
                             + "The button below opens the exact page — two clicks and you're done.");

            var steps = new Label
            {
                Text = "In the Settings window that opens:",
                Font = Theme.BodyStrong,
                ForeColor = Theme.Text,
                AutoSize = false,
                Location = new Point(2, y),
                Size = new Size(_content.Width - 30, 22)
            };
            AddFullWidth(steps);
            y += 28;

            var hint = new SettingsHint
            {
                Scheme = _config.Scheme ?? "claude",
                Location = new Point(0, y),
                Width = Math.Max(200, _content.Width - 26)
            };
            AddFullWidth(hint);
            y += hint.Height + 4;

            var recheck = new FlatButton("Check again", ButtonKind.Subtle)
            {
                Location = new Point(-6, y),
                Size = new Size(140, 36)
            };
            recheck.Click += (s, e) => ShowLater(Step.Done);
            _content.Controls.Add(recheck);

            AddLogButton(y + 40);
            Say("Come back here afterwards — this window re-checks by itself.");
        }

        private void OpenDefaultsPage()
        {
            Registration.OpenOurDefaultsPage();
            Say("Settings is open on Twinstall's page — do the two steps above, then come back.");
        }

        private void AddLogButton(int y)
        {
            var log = new FlatButton("Open log", ButtonKind.Subtle)
            {
                Location = new Point(-6, y),
                Size = new Size(110, 34)
            };
            log.Click += (s, e) => OpenLog();
            _content.Controls.Add(log);
        }

        // ---------------------------------------------------------- navigation --
        private async void GoNext()
        {
            switch (_step)
            {
                case Step.ChooseApp:
                    if (_chosenExe == null || !File.Exists(_chosenExe))
                    {
                        Ui.Error("Choose an application first — pick one from the list, or use "
                               + "“Choose another app…” to point at its .exe.");
                        return;
                    }
                    await RunDetectionAsync().ConfigureAwait(true);
                    break;

                case Step.Result:   Show(Step.Accounts); break;
                case Step.Accounts:
                    if (_config.Instances.Count < 2)
                    {
                        Ui.Error("Add a second account first — running two at once is the point.");
                        return;
                    }
                    Show(Step.Review);
                    break;
                case Step.Review:   Apply(); break;

                default:
                    // On the Done step the primary button is either "Finish" (handler set) or
                    // "Choose Twinstall" (not set). It never offers to finish regardless.
                    if (_lastRegistered && !Registration.WeHandle(_config.Scheme)) OpenDefaultsPage();
                    else Close();
                    break;
            }
        }

        private void GoBack()
        {
            switch (_step)
            {
                case Step.Result:   Show(Step.ChooseApp); break;
                case Step.Accounts: Show(Step.Result);    break;
                case Step.Review:   Show(Step.Accounts);  break;
                default:            Show(Step.ChooseApp); break;
            }
        }

        // ------------------------------------------------------------- helpers --
        /// <summary>
        /// A name fit to put in a heading. The executable's file name is not it: Slack's is
        /// "slack.exe", which renders as "slack can do this". Preferred order is the preset's
        /// display name, then the binary's own ProductName, then the file name capitalised.
        /// </summary>
        private string FriendlyName(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return "The app";

            foreach (Preset p in _presets)
            {
                string found = Presets.FindInstalled(p);
                if (found != null && PathUtil.SamePath(found, exePath)) return p.DisplayName;
            }

            try
            {
                if (File.Exists(exePath))
                {
                    string product = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath).ProductName;
                    if (!string.IsNullOrWhiteSpace(product)) return product.Trim();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            string name = Path.GetFileNameWithoutExtension(exePath);
            if (string.IsNullOrEmpty(name)) return "The app";
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        private static string Describe(DetectionResult r)
        {
            if (r == null) return "(nothing)";
            var sb = new StringBuilder();
            sb.AppendLine("executable   : " + r.ExePath);
            if (r.StubResolved) sb.AppendLine("               (resolved from a launcher stub)");
            sb.AppendLine("chromium     : " + r.MarkerScore.ToString(CultureInfo.InvariantCulture) + " markers");
            if (!string.IsNullOrEmpty(r.MarkerDirectory)) sb.AppendLine("markers in   : " + r.MarkerDirectory);
            sb.AppendLine("packaged     : " + r.Packaged);
            sb.AppendLine("profile root : " + r.ProfileRoot);
            if (r.Profiles != null)
            {
                sb.AppendLine("profiles     : " + r.Profiles.Outcome + " of "
                              + r.Profiles.Ranked.Count.ToString(CultureInfo.InvariantCulture) + " candidates");
                foreach (RankedProfile p in r.Profiles.Ranked)
                    sb.AppendLine("               " + (p.NameMatched ? "* " : "  ") + p.Candidate.Name);
            }
            if (r.Schemes != null)
                foreach (SchemeRegistration s in r.Schemes)
                    sb.AppendLine("scheme       : " + s.Scheme + "://  owner=" + s.Owner
                                  + " declaredByPackage=" + s.DeclaredByPackage
                                  + (s.Command == null ? string.Empty : "\r\n               " + s.Command));
            sb.AppendLine("launch test  : " + r.Probe);
            sb.AppendLine("               " + r.ProbeDetail);
            return sb.ToString();
        }

        private void RemoveEverything()
        {
            if (!Ui.Confirm("Remove Twinstall's shortcuts, icons, registry entries and settings?\r\n\r\n"
                          + "Your profile folders and everything signed into them are NOT touched — "
                          + "delete those yourself if you want them gone."))
                return;

            UndoEverything();
            Show(Step.ChooseApp);
            Ui.Info("Twinstall's changes are gone.\r\n\r\nYour profile folders were left untouched.");
        }

        /// <summary>
        /// Undoes everything setup wrote. Profile folders are deliberately not touched: they
        /// hold live sessions, and deleting an account someone has already signed into because
        /// they closed a window would be indefensible.
        /// </summary>
        private void UndoEverything()
        {
            AppConfig saved = InstanceConfig.Load(AppPaths.ConfigFile);
            Registration.RemoveAllRegistration(saved);
            Registration.RemoveShortcuts(saved);
            Registration.RemoveUninstallEntry();

            // Only revert the taskbar setting if this run turned it on.
            if (_taskbarOptIn && !_taskbarWasOnBefore) Registration.SetTaskbarNeverCombine(false);

            try
            {
                if (File.Exists(AppPaths.ConfigFile)) File.Delete(AppPaths.ConfigFile);
                if (Directory.Exists(AppPaths.IconsDir)) Directory.Delete(AppPaths.IconsDir, recursive: true);
            }
            catch (IOException ex) { Log.Write("cleanup: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log.Write("cleanup: " + ex.Message); }

            Log.Write("rolled back: registration, shortcuts, icons and config removed");
            _config.Instances.Clear();
            _config.ExePath = null;
            _lastRegistered = false;
        }

        private void OpenLog()
        {
            try
            {
                if (!File.Exists(AppPaths.LogFile)) { Say("Nothing logged yet."); return; }
                using (System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true })) { }
            }
            catch (System.ComponentModel.Win32Exception ex) { Say("Could not open the log: " + ex.Message); }
            catch (InvalidOperationException ex) { Say("Could not open the log: " + ex.Message); }
        }
    }
}
