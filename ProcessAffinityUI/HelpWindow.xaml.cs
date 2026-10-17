using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Documentation de l'application.
    ///
    /// Le contenu est bâti en code et vit donc dans l'assembly : aucun fichier
    /// voisin à déployer, et rien à perdre en copiant l'exécutable ailleurs.
    /// L'application reste autonome.
    /// </summary>
    public partial class HelpWindow : Window
    {
        public HelpWindow()
        {
            InitializeComponent();

            Version version = Assembly.GetExecutingAssembly().GetName().Version;

            this.VersionTextBlock.Text = "Version " + (version == null ? "unknown" : version.ToString(3));

            BuildContent();
        }

        private void BuildContent()
        {
            Heading("What this tool is for");

            Body("Windows Task Manager shows you what is running and lets you end it. "
                 + "What it does not let you reach — or only through buried dialogs that forget "
                 + "their setting the moment the process restarts — is how a process is scheduled "
                 + "across your processors. That is what ProcessAffinity is for.");

            Body("On a modern processor this matters more than it used to. Performance cores and "
                 + "efficiency cores do not run the same work at the same speed, and Windows decides "
                 + "the placement for you. When its decision is wrong for your workload, this tool "
                 + "lets you override it — and, unlike Task Manager, lets the override survive a restart "
                 + "of the program.");

            Heading("What it lets you change");

            Bullet("Affinity — the set of logical processors a program may run on. "
                   + "A hard limit: the scheduler will not place a thread outside it.");

            Bullet("Priority — how the scheduler ranks the program against everything else.");

            Bullet("CPU Sets — a preference rather than a limit. You name the processors you would "
                   + "rather the program used; Windows is free to ignore it under pressure.");

            Bullet("Efficiency mode (EcoQoS) — asks Windows to throttle the program, or never to "
                   + "throttle it. What this changes depends on your processor and power plan: it is "
                   + "a request, not a guarantee.");

            Bullet("Saved rules — any of the above, recorded for an executable and applied again "
                   + "every time that program starts, and when ProcessAffinity loads.");

            Bullet("Conformity monitoring — once a rule exists, ProcessAffinity checks that it still "
                   + "holds and puts it back if something else changes it. A rule that has to be put "
                   + "back over and over is reported as contested rather than enforced forever.");

            Heading("Reading a tile");

            Body("Each tile is one process. The coloured bars are the last seventeen seconds of CPU "
                 + "use, the most recent on the right; the number is the share of the whole machine, "
                 + "as Task Manager reports it. The grey bars behind them, and the thin gauge on the "
                 + "left edge, are memory — the private working set, again as Task Manager shows it. "
                 + "Both curves are switched on and off by the CPU and RAM boxes in the top bar.");

            Body("The name band carries the state: grey when affinity and priority cannot be changed "
                 + "without elevation, blue when a saved rule applies, red when a rule is failing. "
                 + "Hover a tile for the full detail.");

            Heading("Typical uses");

            Bullet("Keeping a background indexer or antivirus scan off the cores your work needs.");

            Bullet("Pinning a latency-sensitive program to performance cores, and keeping chatty "
                   + "background services on the efficiency ones.");

            Bullet("Working around a program that behaves badly across many cores — some older "
                   + "software spends more time synchronising than computing.");

            Bullet("Seeing which processes are restricted to a given core: pick that core in the "
                   + "list at the bottom left, and only the processes confined to it remain.");

            Heading("Precautions");

            Warning("Real-time priority can make the machine stop responding. "
                    + "A process at real-time priority outranks keyboard and mouse input. "
                    + "Worse, without the SeIncreaseBasePriorityPrivilege privilege Windows quietly "
                    + "downgrades it to High and raises no error at all — so the setting you asked for "
                    + "may not be the setting you got. ProcessAffinity reads the value back after "
                    + "writing it and tells you when the two differ.");

            Warning("More than half of all processes cannot be changed without elevation. "
                    + "In an ordinary session roughly 56 % of entries are neither readable nor "
                    + "writable: their security descriptor forbids it. Running as administrator "
                    + "raises coverage from about 137 to 308 processes out of 309 on a typical "
                    + "machine. Sixteen protected processes — the kernel, csrss, lsass, Defender and "
                    + "their kin — remain out of reach whatever you do, and that is by design.");

            Warning("A rule applies to every launch of the same executable. "
                    + "Rules are keyed by the path of the program, not by one running instance. "
                    + "If you pin one window of a browser to two cores, every window of that browser "
                    + "will be pinned to those two cores, now and at every future start. When several "
                    + "running instances disagree, ProcessAffinity asks which configuration to keep "
                    + "rather than picking one silently.");

            Warning("Affinity is a hard limit; CPU Sets are only a preference. "
                    + "Ticking a processor under CPU Sets while the affinity above excludes it does "
                    + "nothing at all: the hard limit wins. The affinity window warns you when the two "
                    + "disagree, but it does not correct what you typed.");

            Warning("If a saved rule makes the machine unusable, start with --no-rules. "
                    + "A configuration that confines everything to a single core would leave you with "
                    + "an unresponsive machine, and with ProcessAffinity unable to help. Starting it "
                    + "with the --no-rules switch loads no rule at all for that session and leaves "
                    + "the rules file untouched, so you can undo the damage.");

            Body("Two further things worth knowing. Changing affinity or priority on a service entry "
                 + "affects its host process, and therefore every other service sharing that host — "
                 + "the tooltip lists them when there is more than one. And rules live in "
                 + "%APPDATA%\\ProcessAffinity\\rules.json: if you ever go back to an older version of "
                 + "ProcessAffinity, it will refuse a file written by a newer one and apply nothing, "
                 + "which is deliberate but worth knowing before you downgrade.");

            Heading("Where things are kept");

            Body("Saved rules and preferences are in %APPDATA%\\ProcessAffinity. Nothing is written "
                 + "next to the executable, so the program runs from a read-only folder without "
                 + "complaint. Startup registration, if you enable it, is a single value under "
                 + "HKEY_CURRENT_USER — never the machine-wide key, which would need elevation and "
                 + "would affect every account.");
        }

        private void Heading(string text)
        {
            this.ContentPanel.Children.Add(new TextBlock
            {
                Text = text,
                Style = (Style)this.FindResource("HeadingStyle"),
            });
        }

        private void Body(string text)
        {
            this.ContentPanel.Children.Add(new TextBlock
            {
                Text = text,
                Style = (Style)this.FindResource("BodyStyle"),
            });
        }

        /// <summary>Un point de liste, puce et retrait compris.</summary>
        private void Bullet(string text)
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Margin = new Thickness(0, 0, 0, 6);

            TextBlock marker = new TextBlock
            {
                Text = "•",
                FontSize = 12,
                Margin = new Thickness(2, 0, 8, 0),
            };

            TextBlock body = new TextBlock
            {
                Text = text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
            };

            Grid.SetColumn(marker, 0);
            Grid.SetColumn(body, 1);

            row.Children.Add(marker);
            row.Children.Add(body);

            this.ContentPanel.Children.Add(row);
        }

        /// <summary>
        /// Une précaution. Encadrée et détachée du corps : ce sont les passages
        /// qu'on doit pouvoir retrouver en parcourant la page des yeux.
        /// </summary>
        private void Warning(string text)
        {
            int separator = text.IndexOf(". ", StringComparison.Ordinal);

            string title = separator > 0 ? text.Substring(0, separator + 1) : text;
            string rest = separator > 0 ? text.Substring(separator + 2) : string.Empty;

            StackPanel panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
            });

            if (rest.Length > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = rest,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }

            this.ContentPanel.Children.Add(new Border
            {
                Style = (Style)this.FindResource("WarningStyle"),
                Child = panel,
            });
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
