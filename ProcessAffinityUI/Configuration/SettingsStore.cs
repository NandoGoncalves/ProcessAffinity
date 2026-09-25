using System;
using System.IO;
using System.Text.Json;

namespace ProcessAffinityUI.Configuration
{
    /// <summary>
    /// Choix retenus par l'utilisateur pour ne plus avoir à répondre deux fois à
    /// la même question.
    ///
    /// Fichier distinct de celui des règles : ce sont des préférences d'interface,
    /// pas de la configuration appliquée aux processus. Les mêler exposerait les
    /// règles à une réécriture pour un motif qui ne les concerne pas, et
    /// obligerait à faire évoluer leur version de format à chaque nouvelle
    /// préférence.
    /// </summary>
    internal static class SettingsStore
    {
        public const int CurrentFormatVersion = 1;

        private static readonly object SyncRoot = new object();

        private static AppSettings _settings;

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// Le même dossier que les règles, redirection comprise : les préférences
        /// suivent le stockage des règles, et il n'existe qu'un seul point d'entrée
        /// pour le déplacer, <see cref="RuleEngine.RedirectStoreTo"/>.
        ///
        /// Ce fichier a un temps porté sa propre redirection, ajoutée quand
        /// <see cref="RuleStore"/> n'en avait pas encore. Deux mécanismes
        /// équivalents ne doivent pas coexister : on peut alors en rediriger un et
        /// pas l'autre, et une sonde croirait travailler à l'écart alors qu'elle
        /// écrirait chez l'utilisateur.
        /// </summary>
        public static string DirectoryPath
        {
            get { return RuleStore.DirectoryPath; }
        }

        public static string FilePath
        {
            get { return Path.Combine(DirectoryPath, "settings.json"); }
        }

        /// <summary>
        /// Jamais null : un fichier absent ou illisible donne des préférences
        /// vides, c'est-à-dire « demander à chaque fois ». Une préférence perdue
        /// fait reposer une question, ce qui est sans gravité ; elle ne doit
        /// jamais empêcher l'application de démarrer.
        /// </summary>
        public static AppSettings Current
        {
            get
            {
                lock (SyncRoot)
                {
                    if (_settings == null)
                    {
                        _settings = Load();
                    }

                    return _settings;
                }
            }
        }

        private static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return new AppSettings();
                }

                AppSettings loaded =
                    JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), SerializerOptions);

                if (loaded == null || loaded.FormatVersion > CurrentFormatVersion)
                {
                    // Écrit par une version plus récente : on préfère reposer la
                    // question que d'interpréter de travers un choix qu'on ne
                    // comprend pas.
                    return new AppSettings();
                }

                return loaded;
            }
            catch
            {
                return new AppSettings();
            }
        }

        /// <summary>
        /// Écrit les préférences. Le retour dit si l'écriture a abouti : l'appelant
        /// ne doit pas promettre à l'utilisateur qu'on se souviendra de son choix
        /// si le fichier n'a pas pu être écrit.
        /// </summary>
        public static bool TrySave(out string error)
        {
            error = null;

            lock (SyncRoot)
            {
                try
                {
                    Directory.CreateDirectory(DirectoryPath);

                    AppSettings settings = _settings ?? new AppSettings();
                    settings.FormatVersion = CurrentFormatVersion;

                    string temporaryPath = FilePath + ".tmp";

                    File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));

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
                    error = "The settings file could not be written: " + exception.Message;

                    return false;
                }
            }
        }

        /// <summary>Relit le fichier. Pour les vérifications.</summary>
        public static void Reload()
        {
            lock (SyncRoot)
            {
                _settings = null;
            }
        }

        private const string ExitValue = "exit";
        private const string MinimizeValue = "minimize";

        /// <summary>
        /// Le geste mémorisé pour le bouton « Close », ou null si la question doit
        /// être posée. Une valeur que cette version ne connaît pas vaut null.
        /// </summary>
        public static CloseChoice? GetRememberedCloseChoice()
        {
            string value = Current.CloseButtonAction;

            if (string.Equals(value, ExitValue, StringComparison.OrdinalIgnoreCase))
            {
                return CloseChoice.Exit;
            }

            if (string.Equals(value, MinimizeValue, StringComparison.OrdinalIgnoreCase))
            {
                return CloseChoice.Minimize;
            }

            return null;
        }

        public static bool TryRememberCloseChoice(CloseChoice choice, out string error)
        {
            Current.CloseButtonAction = choice == CloseChoice.Exit ? ExitValue : MinimizeValue;

            return TrySave(out error);
        }

        /// <summary>Repose la question la prochaine fois.</summary>
        public static bool TryForgetCloseChoice(out string error)
        {
            Current.CloseButtonAction = null;

            return TrySave(out error);
        }
    }

    /// <summary>Les trois issues de la demande de fermeture.</summary>
    public enum CloseChoice
    {
        /// <summary>Ne rien faire : la fenêtre reste telle quelle.</summary>
        Cancel,

        /// <summary>Quitter : l'application des règles s'interrompt.</summary>
        Exit,

        /// <summary>Ranger dans la zone de notification : l'application poursuit.</summary>
        Minimize,
    }

    /// <summary>Ce que contient le fichier de préférences.</summary>
    public sealed class AppSettings
    {
        public int FormatVersion { get; set; }

        /// <summary>
        /// Ce que le bouton « Close » doit faire sans demander : « exit »,
        /// « minimize », ou null pour poser la question. Une valeur inconnue est
        /// traitée comme null — on repose la question plutôt que de deviner.
        /// </summary>
        public string CloseButtonAction { get; set; }
    }
}
