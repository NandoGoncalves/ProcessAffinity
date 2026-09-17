using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ProcessAffinityUI.Configuration
{
    /// <summary>
    /// Lecture et écriture du fichier de règles.
    ///
    /// Il vit dans %APPDATA%\ProcessAffinity et jamais à côté de l'exécutable :
    /// une installation sous Program Files n'est pas accessible en écriture, et
    /// les règles sont propres à l'utilisateur.
    /// </summary>
    internal static class RuleStore
    {
        /// <summary>
        /// Version du format, écrite dès la première sauvegarde. Un fichier portant
        /// un numéro supérieur vient d'une version plus récente de l'application :
        /// on préfère l'ignorer que d'en mal interpréter le contenu.
        /// </summary>
        /// <remarks>
        /// Version 2 : deux champs optionnels, mode d'efficacité et CPU Sets. Une
        /// règle de version 1 reste lisible et se comporte comme avant — c'est
        /// précisément pour cela que le numéro de format a été écrit dès la
        /// première version.
        /// </remarks>
        public const int CurrentFormatVersion = 2;

        private static readonly object SyncRoot = new object();

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,

            // Les champs optionnels de la version 2 ne sont ecrits que
            // renseignes : une regle qui ne se mele ni du mode d efficacite ni
            // des CPU Sets reste litteralement une regle de version 1.
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        public static string DirectoryPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ProcessAffinity");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(DirectoryPath, "rules.json"); }
        }

        /// <summary>
        /// Règles du fichier, indexées par chemin d'exécutable sans distinction de
        /// casse. Jamais null : un fichier absent, illisible ou d'un format inconnu
        /// donne un ensemble vide plutôt qu'une exception au démarrage.
        /// </summary>
        public static Dictionary<string, ProcessRule> Load(out string error)
        {
            error = null;

            Dictionary<string, ProcessRule> rulesByPath =
                new Dictionary<string, ProcessRule>(StringComparer.OrdinalIgnoreCase);

            lock (SyncRoot)
            {
                try
                {
                    if (!File.Exists(FilePath))
                    {
                        return rulesByPath;
                    }

                    RuleFile file = JsonSerializer.Deserialize<RuleFile>(File.ReadAllText(FilePath), SerializerOptions);

                    if (file == null)
                    {
                        return rulesByPath;
                    }

                    if (file.FormatVersion > CurrentFormatVersion)
                    {
                        error = "The rules file uses format version " + file.FormatVersion
                                + ", which this version of ProcessAffinity does not understand. No rule was applied.";

                        return rulesByPath;
                    }

                    if (file.Rules == null)
                    {
                        return rulesByPath;
                    }

                    foreach (ProcessRule rule in file.Rules)
                    {
                        if (rule == null || string.IsNullOrWhiteSpace(rule.ExecutablePath))
                        {
                            continue;
                        }

                        rulesByPath[rule.ExecutablePath] = rule;
                    }
                }
                catch (Exception exception)
                {
                    error = "The rules file could not be read: " + exception.Message;
                }
            }

            return rulesByPath;
        }

        /// <summary>
        /// Réécrit le fichier entier. Passe par un fichier temporaire puis un
        /// remplacement : une écriture interrompue ne laisse pas un JSON tronqué à
        /// la place des règles.
        /// </summary>
        public static bool TrySave(IEnumerable<ProcessRule> rules, out string error)
        {
            error = null;

            lock (SyncRoot)
            {
                try
                {
                    Directory.CreateDirectory(DirectoryPath);

                    RuleFile file = new RuleFile
                    {
                        FormatVersion = CurrentFormatVersion,
                        Rules = new List<ProcessRule>(rules).ToArray(),
                    };

                    string temporaryPath = FilePath + ".tmp";

                    File.WriteAllText(temporaryPath, JsonSerializer.Serialize(file, SerializerOptions));

                    if (File.Exists(FilePath))
                    {
                        File.Replace(temporaryPath, FilePath, null);
                    }
                    else
                    {
                        File.Move(temporaryPath, FilePath);
                    }

                    return true;
                }
                catch (Exception exception)
                {
                    error = "The rules file could not be written: " + exception.Message;

                    return false;
                }
            }
        }
    }
}
