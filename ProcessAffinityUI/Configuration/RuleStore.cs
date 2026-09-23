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

        /// <summary>
        /// Version de format du dernier fichier lu, zéro si aucun ne l'a été. C'est
        /// ce qui permet de reconnaître un franchissement de version au moment
        /// d'écrire, et de mettre l'ancien fichier de côté avant de le remplacer.
        /// </summary>
        private static int _lastLoadedFormatVersion;

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
        /// Nom de la copie conservée avant un franchissement de version, pour la
        /// version de format donnée.
        /// </summary>
        public static string GetBackupPath(int formatVersion)
        {
            return Path.Combine(DirectoryPath, "rules.v" + formatVersion + ".bak");
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
                        _lastLoadedFormatVersion = 0;

                        return rulesByPath;
                    }

                    RuleFile file = JsonSerializer.Deserialize<RuleFile>(File.ReadAllText(FilePath), SerializerOptions);

                    if (file == null)
                    {
                        _lastLoadedFormatVersion = 0;

                        return rulesByPath;
                    }

                    // Un fichier dépourvu du champ compte pour la version 1 : il est
                    // antérieur, et c'est bien sous ce nom qu'il faut le mettre de
                    // côté si on s'apprête à le remplacer.
                    _lastLoadedFormatVersion = Math.Max(file.FormatVersion, 1);

                    if (file.FormatVersion > CurrentFormatVersion)
                    {
                        error = "The rules file uses format version " + file.FormatVersion
                                + ", which this version of ProcessAffinity does not understand. No rule was applied."
                                + DescribeUsableBackup();

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

                    // La copie précède l'écriture : une fois le fichier réécrit au
                    // format courant, l'original n'est plus récupérable, et une
                    // version antérieure de l'application le refuserait en bloc.
                    if (!TryPreserveBeforeFormatChange(out error))
                    {
                        return false;
                    }

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

                    _lastLoadedFormatVersion = CurrentFormatVersion;

                    return true;
                }
                catch (Exception exception)
                {
                    error = "The rules file could not be written: " + exception.Message;

                    return false;
                }
            }
        }

        /// <summary>
        /// Met de côté le fichier existant lorsque l'écriture qui suit va changer sa
        /// version de format, dans un sens ou dans l'autre.
        ///
        /// Vers le haut, c'est un fichier ancien qu'on élève. Vers le bas, c'est plus
        /// grave : le fichier venait d'une version plus récente, il a été refusé en
        /// bloc, donc aucune règle n'a été chargée — la première sauvegarde de
        /// l'utilisateur remplace alors la totalité de ses règles par la seule qu'il
        /// vient d'enregistrer. Sans cette copie, elles seraient perdues sans trace.
        ///
        /// Une seule fois par franchissement : si la copie est déjà là, elle porte
        /// l'état d'origine, qui vaut mieux que le plus récent. L'échec est
        /// bloquant — ne pas pouvoir protéger le fichier n'autorise pas à l'écraser.
        /// </summary>
        private static bool TryPreserveBeforeFormatChange(out string error)
        {
            error = null;

            if (_lastLoadedFormatVersion <= 0
                || _lastLoadedFormatVersion == CurrentFormatVersion
                || !File.Exists(FilePath))
            {
                return true;
            }

            string backupPath = GetBackupPath(_lastLoadedFormatVersion);

            if (File.Exists(backupPath))
            {
                return true;
            }

            try
            {
                File.Copy(FilePath, backupPath);

                return true;
            }
            catch (Exception exception)
            {
                error = "The rules file is in format version " + _lastLoadedFormatVersion
                        + " and is about to be rewritten in format version " + CurrentFormatVersion
                        + ", but the copy meant to preserve the original could not be written: "
                        + exception.Message
                        + "\r\n\r\nNothing was changed, so the existing rules are intact.";

                return false;
            }
        }

        /// <summary>
        /// Phrase désignant la copie d'avant le franchissement, quand il en existe
        /// une que cette version sait lire. Elle est ajoutée au refus d'un format
        /// trop récent : c'est le seul moment où l'utilisateur en a besoin, et rien
        /// jusque-là ne lui a appris qu'elle existait.
        /// </summary>
        private static string DescribeUsableBackup()
        {
            string bestPath = null;

            for (int version = 1; version <= CurrentFormatVersion; version++)
            {
                string candidate = GetBackupPath(version);

                if (File.Exists(candidate))
                {
                    bestPath = candidate;
                }
            }

            if (bestPath == null)
            {
                return string.Empty;
            }

            return "\r\n\r\nA copy of the rules as they were before the format was upgraded is kept at "
                   + bestPath + ". This version can read it: rename it to " + FilePath + " to use it.";
        }
    }
}
