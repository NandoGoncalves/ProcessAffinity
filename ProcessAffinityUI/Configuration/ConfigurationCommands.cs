using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using ProcessAffinityUI.Threading;

namespace ProcessAffinityUI.Configuration
{
    /// <summary>
    /// Enregistrer et retirer une configuration, pour une sélection quelconque.
    ///
    /// Partagé par le menu de la tuile et celui du panneau : le geste est le même,
    /// et le dédoubler laisserait les deux chemins diverger — c'est ce qui était
    /// arrivé à l'enregistrement, qui n'agissait que sur la tuile cliquée.
    /// </summary>
    public static class ConfigurationCommands
    {
        private const string ApplicationName = "ProcessAffinity";

        /// <summary>
        /// Une configuration candidate et les instances qui la présentent.
        /// </summary>
        public sealed class Candidate
        {
            public ProcessRule Rule { get; set; }

            public List<Process> Instances { get; set; }

            /// <summary>Ce que la règle dirait, en clair.</summary>
            public string Description
            {
                get
                {
                    StringBuilder builder = new StringBuilder();

                    builder.Append("Affinity ").Append(RuleEngine.DescribeMask(this.Rule.GetAffinityMask()))
                           .Append(", priority ").Append(RuleEngine.DescribePriority(this.Rule.PriorityClass));

                    if (this.Rule.EfficiencyMode != null)
                    {
                        builder.Append(", efficiency mode ")
                               .Append(RuleEngine.DescribeEfficiencyMode(this.Rule.EfficiencyMode.Value));
                    }

                    if (this.Rule.CpuSetProcessors != null)
                    {
                        builder.Append(", CPU sets ")
                               .Append(RuleEngine.DescribeCpuSetCount(this.Rule.CpuSetProcessors.Length));
                    }

                    return builder.ToString();
                }
            }

            /// <summary>« PID 1234, 5678 » — de quoi retrouver l'instance visée.</summary>
            public string InstanceSummary
            {
                get
                {
                    const int maximumListed = 12;

                    string pids = string.Join(", ", this.Instances.Take(maximumListed).Select(p => p.ProcessID.ToString()));

                    if (this.Instances.Count > maximumListed)
                    {
                        pids += ", and " + (this.Instances.Count - maximumListed) + " more";
                    }

                    return (this.Instances.Count == 1 ? "PID " : "PIDs ") + pids;
                }
            }
        }

        /// <summary>
        /// Posé par la fenêtre principale : demande laquelle de plusieurs
        /// configurations divergentes retenir pour un exécutable. Rend null si
        /// l'utilisateur passe cet exécutable.
        /// </summary>
        public static Func<string, List<Candidate>, Candidate> ConflictResolver { get; set; }

        /// <summary>Repeint les bandeaux des tuiles après une action de masse.</summary>
        public static Action MarkersChanged { get; set; }

        public static void Save(IEnumerable<Process> targets)
        {
            List<string> unreadableNames = new List<string>();
            List<string> refusedNames = new List<string>();
            List<string> skippedNames = new List<string>();

            // Les candidates d'abord, l'écriture ensuite : une règle porte un
            // chemin d'exécutable, et plusieurs instances du même programme peuvent
            // présenter des configurations différentes. Écrire au fil de la boucle
            // laisserait la dernière l'emporter en silence.
            Dictionary<string, List<Candidate>> candidatesByPath =
                new Dictionary<string, List<Candidate>>(StringComparer.OrdinalIgnoreCase);

            foreach (Process process in targets ?? Enumerable.Empty<Process>())
            {
                if (process == null)
                {
                    continue;
                }

                nuint? affinity = process.GetProcessorAffinity();

                if (affinity == null)
                {
                    unreadableNames.Add(process.ProcessName);
                    continue;
                }

                // Priorité lue en natif : Win32_Process.Priority est figé à
                // l'énumération, et enregistrer une priorité déjà modifiée par un
                // tiers reviendrait à graver sa valeur d'origine.
                int priorityClass = process.GetPriorityClass()
                                    ?? (int)Process.ToProcessPriorityEnum(process.Priority);

                string error;
                ProcessRule rule = RuleEngine.BuildCandidateRule(process, affinity.Value, priorityClass, out error);

                if (rule == null)
                {
                    refusedNames.Add(process.ProcessName);
                    continue;
                }

                AddCandidate(candidatesByPath, rule, process);
            }

            int savedCount = 0;

            foreach (KeyValuePair<string, List<Candidate>> group in candidatesByPath)
            {
                Candidate chosen = ResolveGroup(group.Value, ref skippedNames);

                if (chosen == null)
                {
                    continue;
                }

                string error;

                if (!RuleEngine.TryStore(chosen.Rule, out error))
                {
                    refusedNames.Add(chosen.Instances[0].ProcessName);
                    continue;
                }

                // Toutes les instances de cet exécutable portent désormais la règle,
                // et non la seule dont la configuration a été retenue.
                foreach (Candidate candidate in group.Value)
                {
                    foreach (Process instance in candidate.Instances)
                    {
                        instance.SetRuleState(RuleStateEnum.Applied, null);
                    }
                }

                savedCount++;
            }

            RefreshMarkers();

            ReportOutcome("saved", savedCount, unreadableNames, refusedNames, skippedNames);
        }

        /// <summary>
        /// Une seule configuration : elle s'impose. Plusieurs : on demande, et
        /// l'exécutable est passé si l'utilisateur ne tranche pas.
        /// </summary>
        private static Candidate ResolveGroup(List<Candidate> candidates, ref List<string> skippedNames)
        {
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            string name = candidates[0].Instances[0].ProcessName;

            Func<string, List<Candidate>, Candidate> resolver = ConflictResolver;

            if (resolver == null)
            {
                // Sans interlocuteur, on ne devine pas : mieux vaut ne rien écrire
                // que graver une configuration que personne n'a choisie.
                skippedNames.Add(name);
                return null;
            }

            Candidate chosen = resolver(name, candidates);

            if (chosen == null)
            {
                skippedNames.Add(name);
            }

            return chosen;
        }

        private static void AddCandidate(
            Dictionary<string, List<Candidate>> candidatesByPath, ProcessRule rule, Process process)
        {
            List<Candidate> candidates;

            if (!candidatesByPath.TryGetValue(rule.ExecutablePath, out candidates))
            {
                candidates = new List<Candidate>();
                candidatesByPath[rule.ExecutablePath] = candidates;
            }

            foreach (Candidate candidate in candidates)
            {
                if (SameConfiguration(candidate.Rule, rule))
                {
                    candidate.Instances.Add(process);
                    return;
                }
            }

            candidates.Add(new Candidate
            {
                Rule = rule,
                Instances = new List<Process> { process },
            });
        }

        /// <summary>
        /// Deux règles écriraient-elles la même chose ? Les CPU Sets sont comparés
        /// sans égard à l'ordre : c'est un ensemble, et l'ordre de parcours de la
        /// topologie n'est pas une donnée.
        /// </summary>
        public static bool SameConfiguration(ProcessRule first, ProcessRule second)
        {
            if (first.GetAffinityMask() != second.GetAffinityMask()
                || first.PriorityClass != second.PriorityClass
                || first.EfficiencyMode != second.EfficiencyMode)
            {
                return false;
            }

            if (first.CpuSetProcessors == null || second.CpuSetProcessors == null)
            {
                return first.CpuSetProcessors == null && second.CpuSetProcessors == null;
            }

            return first.CpuSetProcessors.Length == second.CpuSetProcessors.Length
                   && first.CpuSetProcessors.OrderBy(i => i)
                           .SequenceEqual(second.CpuSetProcessors.OrderBy(i => i));
        }

        public static void Remove(IEnumerable<Process> targets)
        {
            int removedCount = 0;
            List<string> refusedNames = new List<string>();

            // Une règle par chemin : sans ce regroupement, retirer trois instances
            // du même programme compterait trois suppressions pour une seule règle.
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Process> entries = (targets ?? Enumerable.Empty<Process>()).Where(p => p != null).ToList();

            foreach (Process process in entries)
            {
                if (!string.IsNullOrEmpty(process.ExecutablePath))
                {
                    paths.Add(process.ExecutablePath);
                }
            }

            foreach (string path in paths)
            {
                string error;

                // Le retour distingue « rien à retirer » d'un échec d'écriture.
                if (!RuleEngine.TryRemove(path, out error))
                {
                    if (!string.IsNullOrEmpty(error))
                    {
                        refusedNames.Add(System.IO.Path.GetFileName(path));
                    }

                    continue;
                }

                removedCount++;
            }

            // L'affinité et la priorité en cours ne sont pas touchées : retirer la
            // règle cesse de la réappliquer, cela ne remet rien en arrière.
            foreach (Process process in entries)
            {
                if (!string.IsNullOrEmpty(process.ExecutablePath) && !RuleEngine.HasRule(process.ExecutablePath))
                {
                    process.SetRuleState(RuleStateEnum.None, null);
                }
            }

            RefreshMarkers();

            ReportOutcome("removed", removedCount, new List<string>(), refusedNames, new List<string>());
        }

        /// <summary>Cibles qui portent déjà une règle. Décide de proposer le retrait.</summary>
        public static List<Process> GetRuledTargets(IEnumerable<Process> targets)
        {
            return (targets ?? Enumerable.Empty<Process>())
                   .Where(p => p != null && RuleEngine.HasRule(p.ExecutablePath))
                   .ToList();
        }

        private static void RefreshMarkers()
        {
            Action refresh = MarkersChanged;

            if (refresh != null)
            {
                refresh();
            }
        }

        private static void ReportOutcome(
            string verb, int count, List<string> unreadableNames, List<string> refusedNames, List<string> skippedNames)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("Configuration ").Append(verb).Append(" for ").Append(count)
                   .Append(count == 1 ? " program." : " programs.");

            if (string.Equals(verb, "saved", StringComparison.Ordinal) && count > 0)
            {
                builder.Append("\r\n\r\nIt will be applied at every start, and when ProcessAffinity loads.")
                       .Append("\r\n\r\n").Append(RuleEngine.FilePath);
            }

            AppendNames(builder, unreadableNames,
                "affinity could not be read, so there was nothing to save");

            AppendNames(builder, refusedNames,
                "could not be saved — critical process, service entry, or unknown executable path");

            AppendNames(builder, skippedNames,
                "skipped — the selection held several instances with different settings, and none was chosen");

            MessageBox.Show(builder.ToString(), ApplicationName,
                MessageBoxButton.OK,
                unreadableNames.Count + refusedNames.Count + skippedNames.Count > 0
                    ? MessageBoxImage.Warning
                    : MessageBoxImage.Information);
        }

        private static void AppendNames(StringBuilder builder, List<string> names, string reason)
        {
            if (names == null || names.Count == 0)
            {
                return;
            }

            const int maximumListed = 15;

            builder.Append("\r\n\r\n").Append(names.Count)
                   .Append(names.Count == 1 ? " process: " : " processes: ").Append(reason).Append("\r\n")
                   .Append(string.Join(", ", names.Take(maximumListed)));

            if (names.Count > maximumListed)
            {
                builder.Append(", and ").Append(names.Count - maximumListed).Append(" more");
            }
        }
    }
}
