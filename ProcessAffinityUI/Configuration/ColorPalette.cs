using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;

namespace ProcessAffinityUI.Configuration
{
    /// <summary>
    /// Ce que le texte posé sur une couleur devient, et donc comment juger du
    /// contraste.
    /// </summary>
    public enum TextOverColorEnum
    {
        /// <summary>Rien n'est écrit par-dessus : le contraste ne se pose pas.</summary>
        None,

        /// <summary>
        /// La police bascule entre noir et blanc selon la luminance du fond —
        /// la règle écrite à l'étape 3d. Le contraste à juger est donc le
        /// meilleur des deux.
        /// </summary>
        Automatic,

        /// <summary>La police est noire quoi qu'il arrive.</summary>
        FixedBlack,
    }

    /// <summary>Une couleur de l'interface, avec son rôle nommé.</summary>
    public sealed class ColorRole
    {
        public string Key { get; set; }

        /// <summary>Intitulé affiché, en anglais comme le reste de l'IHM.</summary>
        public string DisplayName { get; set; }

        /// <summary>Ce que la couleur sert à distinguer.</summary>
        public string Description { get; set; }

        public Color Default { get; set; }

        public TextOverColorEnum TextOverColor { get; set; }
    }

    /// <summary>
    /// Palette de l'application : les teintes étaient codées en dur, elles sont
    /// désormais nommées, modifiables et rangées dans <c>settings.json</c>.
    ///
    /// Pas dans le fichier de règles : ce sont des préférences d'interface, et
    /// les y mettre ferait monter la version du format des règles pour un
    /// changement qui ne concerne en rien une règle — avec le refus en bloc que
    /// cela entraîne sur un binaire plus ancien.
    ///
    /// La lisibilité de l'application repose sur des nuances proches : le gris
    /// des barres de mémoire sur le fond de la tuile, le bleu d'une entrée sous
    /// règle contre le gris d'une entrée non modifiable. Qui perçoit mal les
    /// couleurs ne distingue rien de tout cela, d'où ce réglage.
    /// </summary>
    public static class ColorPalette
    {
        public const string TileBackground = "tileBackground";
        public const string CpuIdle = "cpuIdle";
        public const string CpuBelowNormal = "cpuBelowNormal";
        public const string CpuNormal = "cpuNormal";
        public const string CpuAboveNormal = "cpuAboveNormal";
        public const string CpuHigh = "cpuHigh";
        public const string CpuRealTime = "cpuRealTime";
        public const string MemoryBars = "memoryBars";
        public const string MemoryGauge = "memoryGauge";
        public const string ActivityHighlight = "activityHighlight";
        public const string NameBandResting = "nameBandResting";
        public const string NameBandNotModifiable = "nameBandNotModifiable";
        public const string NameBandRuled = "nameBandRuled";
        public const string NameBandFailedRule = "nameBandFailedRule";

        private static readonly object SyncRoot = new object();

        private static readonly ColorRole[] RoleList =
        {
            new ColorRole
            {
                Key = TileBackground,
                DisplayName = "Tile background",
                Description = "The body of every tile. The memory bars are drawn on top of it, "
                              + "so the two must stay distinguishable.",
                Default = Color.FromRgb(0xD3, 0xD3, 0xD3),
                TextOverColor = TextOverColorEnum.FixedBlack,
            },
            new ColorRole
            {
                Key = CpuIdle,
                DisplayName = "CPU bars — Idle priority",
                Description = "CPU history of a process running at idle priority. "
                              + "The bar grows more opaque as the load rises.",
                Default = Color.FromRgb(0x00, 0xFF, 0x00),
                TextOverColor = TextOverColorEnum.None,
            },
            new ColorRole
            {
                Key = CpuBelowNormal,
                DisplayName = "CPU bars — Below normal priority",
                Description = "CPU history of a process running below normal priority.",
                Default = Color.FromRgb(0xFF, 0xFF, 0x00),
                TextOverColor = TextOverColorEnum.None,
            },
            new ColorRole
            {
                Key = CpuNormal,
                DisplayName = "CPU bars — Normal priority",
                Description = "CPU history of a process at normal priority, which is almost all of them.",
                Default = Color.FromRgb(0xFF, 0x00, 0x00),
                TextOverColor = TextOverColorEnum.None,
            },
            new ColorRole
            {
                Key = CpuAboveNormal,
                DisplayName = "CPU bars — Above normal priority",
                Description = "CPU history of a process running above normal priority.",
                Default = Color.FromRgb(0xC8, 0x00, 0x64),
                TextOverColor = TextOverColorEnum.None,
            },
            new ColorRole
            {
                Key = CpuHigh,
                DisplayName = "CPU bars — High priority",
                Description = "CPU history of a process running at high priority.",
                Default = Color.FromRgb(0x64, 0x00, 0x64),
                TextOverColor = TextOverColorEnum.None,
            },
            new ColorRole
            {
                Key = CpuRealTime,
                DisplayName = "CPU bars — Real-time priority",
                Description = "CPU history of a process running at real-time priority — "
                              + "rare, and worth spotting at a glance.",
                Default = Color.FromRgb(0x00, 0x00, 0x00),
                TextOverColor = TextOverColorEnum.None,
            },
            new ColorRole
            {
                Key = MemoryBars,
                DisplayName = "Memory bars",
                Description = "Memory history, drawn behind the CPU bars. Only a shade away from "
                              + "the tile background by default.",
                Default = Color.FromRgb(0xBD, 0xBD, 0xBD),
                TextOverColor = TextOverColorEnum.None,
            },
            new ColorRole
            {
                Key = MemoryGauge,
                DisplayName = "Memory gauge",
                Description = "The thin gauge on the left edge of a tile, showing the latest memory reading.",
                Default = Color.FromRgb(0x8C, 0x8C, 0x8C),
                TextOverColor = TextOverColorEnum.None,
            },
            new ColorRole
            {
                Key = ActivityHighlight,
                DisplayName = "Name band — activity",
                Description = "Flashes on the name band when something happens to a process, "
                              + "and when a rule has just been applied to it.",
                Default = Color.FromRgb(0xFF, 0xFF, 0x00),
                TextOverColor = TextOverColorEnum.Automatic,
            },
            new ColorRole
            {
                Key = NameBandResting,
                DisplayName = "Name band — at rest",
                Description = "The name band of an ordinary process: modifiable, under no rule, doing nothing special.",
                Default = Colors.White,
                TextOverColor = TextOverColorEnum.Automatic,
            },
            new ColorRole
            {
                Key = NameBandNotModifiable,
                DisplayName = "Name band — not modifiable",
                Description = "Affinity and priority cannot be changed without elevation. "
                              + "Roughly half of all processes in an ordinary session.",
                Default = Color.FromRgb(0x66, 0x66, 0x66),
                TextOverColor = TextOverColorEnum.Automatic,
            },
            new ColorRole
            {
                Key = NameBandRuled,
                DisplayName = "Name band — rule applied",
                Description = "A saved rule holds on this process.",
                Default = Color.FromRgb(0x1F, 0x5C, 0x99),
                TextOverColor = TextOverColorEnum.Automatic,
            },
            new ColorRole
            {
                Key = NameBandFailedRule,
                DisplayName = "Name band — rule failing",
                Description = "A saved rule exists but is contested, denied, orphaned or invalid on this machine.",
                Default = Color.FromRgb(0xA5, 0x32, 0x2A),
                TextOverColor = TextOverColorEnum.Automatic,
            },
        };

        /// <summary>Couleurs effectives, défauts compris. Toujours complet.</summary>
        private static Dictionary<string, Color> _colors;

        /// <summary>
        /// Pinceaux gelés, un par rôle, reconstruits à chaque changement. Gelés
        /// pour être partageables entre toutes les tuiles sans copie.
        /// </summary>
        private static Dictionary<string, Brush> _brushes;

        /// <summary>
        /// Incrémenté à chaque changement. Les caches qui dérivent de la palette
        /// — celui des pinceaux de barre, par exemple — s'y comparent pour savoir
        /// s'ils sont périmés, sans avoir à s'abonner.
        /// </summary>
        public static int Revision { get; private set; }

        /// <summary>
        /// Levé après tout changement, sur le thread qui a fait le changement.
        /// C'est ce qui repeint l'application sans redémarrage.
        /// </summary>
        public static event Action Changed;

        public static IReadOnlyList<ColorRole> Roles
        {
            get { return RoleList; }
        }

        public static ColorRole GetRole(string key)
        {
            return RoleList.FirstOrDefault(role => role.Key == key);
        }

        public static Color Get(string key)
        {
            lock (SyncRoot)
            {
                EnsureLoaded();

                Color color;

                return _colors.TryGetValue(key, out color) ? color : Colors.Magenta;
            }
        }

        /// <summary>
        /// Pinceau gelé du rôle. Le même objet est rendu à tous les appelants
        /// tant que la couleur ne change pas : une tuile n'a jamais son pinceau
        /// à elle.
        /// </summary>
        public static Brush GetBrush(string key)
        {
            lock (SyncRoot)
            {
                EnsureLoaded();

                Brush brush;

                if (_brushes.TryGetValue(key, out brush))
                {
                    return brush;
                }

                brush = Freeze(Get(key));
                _brushes[key] = brush;

                return brush;
            }
        }

        public static Brush Freeze(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();

            return brush;
        }

        /// <summary>
        /// Pose une couleur et repeint. L'écriture du fichier est tentée ; son
        /// échec n'empêche pas le changement de prendre effet pour la session,
        /// et revient par <paramref name="error"/>.
        /// </summary>
        public static bool TrySet(string key, Color color, out string error)
        {
            error = null;

            lock (SyncRoot)
            {
                EnsureLoaded();

                Color current;

                if (_colors.TryGetValue(key, out current) && current == color)
                {
                    return true;
                }

                _colors[key] = color;
            }

            bool written = Persist(out error);

            Invalidate();

            return written;
        }

        /// <summary>Rend à un rôle sa couleur d'origine.</summary>
        public static bool TryReset(string key, out string error)
        {
            ColorRole role = GetRole(key);

            if (role == null)
            {
                error = null;
                return true;
            }

            return TrySet(key, role.Default, out error);
        }

        /// <summary>
        /// Rend toute la palette à ses couleurs d'origine. La sortie de secours
        /// d'une palette rendue illisible par tâtonnement.
        /// </summary>
        public static bool TryResetAll(out string error)
        {
            lock (SyncRoot)
            {
                EnsureLoaded();

                foreach (ColorRole role in RoleList)
                {
                    _colors[role.Key] = role.Default;
                }
            }

            bool written = Persist(out error);

            Invalidate();

            return written;
        }

        public static bool IsDefault(string key)
        {
            ColorRole role = GetRole(key);

            return role != null && Get(key) == role.Default;
        }

        public static bool HasAnyChange
        {
            get { return RoleList.Any(role => !IsDefault(role.Key)); }
        }

        /// <summary>
        /// Relit depuis les préférences. Appelée par les sondes après une
        /// redirection, et par <c>SettingsStore.Reload</c>.
        /// </summary>
        public static void Reload()
        {
            lock (SyncRoot)
            {
                _colors = null;
                _brushes = null;
            }

            Invalidate();
        }

        private static void Invalidate()
        {
            lock (SyncRoot)
            {
                _brushes = null;
                Revision++;
            }

            Action changed = Changed;

            if (changed != null)
            {
                changed();
            }
        }

        /// <summary>Doit être appelé sous le verrou.</summary>
        private static void EnsureLoaded()
        {
            if (_brushes == null)
            {
                _brushes = new Dictionary<string, Brush>(StringComparer.Ordinal);
            }

            if (_colors != null)
            {
                return;
            }

            _colors = new Dictionary<string, Color>(StringComparer.Ordinal);

            foreach (ColorRole role in RoleList)
            {
                _colors[role.Key] = role.Default;
            }

            Dictionary<string, string> stored = SettingsStore.Current.Colors;

            if (stored == null)
            {
                // Fichier écrit par une version antérieure, sans la moindre clé
                // de couleur : tout reste aux valeurs d'origine. C'est le cas
                // nominal d'une mise à jour, pas une anomalie.
                return;
            }

            foreach (KeyValuePair<string, string> pair in stored)
            {
                Color color;

                // Une clé inconnue — venue d'une version plus récente — est
                // ignorée sans bruit, de même qu'une valeur illisible : une
                // préférence d'affichage perdue ne vaut pas un message.
                if (_colors.ContainsKey(pair.Key) && TryParse(pair.Value, out color))
                {
                    _colors[pair.Key] = color;
                }
            }
        }

        private static bool Persist(out string error)
        {
            Dictionary<string, string> stored = new Dictionary<string, string>(StringComparer.Ordinal);

            lock (SyncRoot)
            {
                foreach (ColorRole role in RoleList)
                {
                    Color color = _colors[role.Key];

                    // Seuls les écarts sont écrits : un fichier qui ne contient
                    // que ce que l'utilisateur a changé reste lisible, et une
                    // évolution des couleurs d'origine profite à qui n'y a pas
                    // touché.
                    if (color != role.Default)
                    {
                        stored[role.Key] = Format(color);
                    }
                }
            }

            SettingsStore.Current.Colors = stored.Count == 0 ? null : stored;

            return SettingsStore.TrySave(out error);
        }

        public static string Format(Color color)
        {
            return "#" + color.R.ToString("X2", CultureInfo.InvariantCulture)
                       + color.G.ToString("X2", CultureInfo.InvariantCulture)
                       + color.B.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Lit « #RRGGBB », avec ou sans dièse. L'alpha n'est pas accepté : la
        /// transparence des barres de CPU est calculée depuis la charge, et une
        /// couleur à moitié transparente choisie ici se combinerait à ce calcul
        /// de façon illisible.
        /// </summary>
        public static bool TryParse(string text, out Color color)
        {
            color = Colors.Black;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1);
            }

            if (trimmed.Length != 6)
            {
                return false;
            }

            uint value;

            if (!uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);

            return true;
        }

        // ------------------------------------------------------------ contraste

        /// <summary>
        /// Luminance relative au sens de WCAG. Ce n'est pas la moyenne pondérée
        /// qui sert à basculer la police entre noir et blanc : celle-là décide,
        /// celle-ci mesure.
        /// </summary>
        public static double RelativeLuminance(Color color)
        {
            return (0.2126d * Linearize(color.R))
                   + (0.7152d * Linearize(color.G))
                   + (0.0722d * Linearize(color.B));
        }

        private static double Linearize(byte channel)
        {
            double value = channel / 255d;

            return value <= 0.03928d ? value / 12.92d : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }

        public static double ContrastRatio(Color first, Color second)
        {
            double a = RelativeLuminance(first);
            double b = RelativeLuminance(second);

            double lighter = Math.Max(a, b);
            double darker = Math.Min(a, b);

            return (lighter + 0.05d) / (darker + 0.05d);
        }

        /// <summary>
        /// Police que l'application posera sur ce fond. Reprend exactement la
        /// bascule de l'étape 3d — moyenne pondérée, seuil à 0,5 — pour que
        /// l'avertissement porte sur ce qui sera réellement affiché, et non sur
        /// ce qu'un autre calcul aurait choisi.
        /// </summary>
        public static Color GetTextColorOver(Color background)
        {
            double luminance = ((0.299d * background.R)
                                + (0.587d * background.G)
                                + (0.114d * background.B)) / 255d;

            return luminance < 0.5d ? Colors.White : Colors.Black;
        }

        /// <summary>
        /// Contraste obtenu pour ce rôle, et sa qualité. Retourne null quand
        /// aucun texte n'est posé sur la couleur.
        /// </summary>
        public static double? GetTextContrast(ColorRole role, Color color)
        {
            if (role == null || role.TextOverColor == TextOverColorEnum.None)
            {
                return null;
            }

            if (role.TextOverColor == TextOverColorEnum.FixedBlack)
            {
                return ContrastRatio(color, Colors.Black);
            }

            return ContrastRatio(color, GetTextColorOver(color));
        }

        /// <summary>Seuil WCAG AA pour du texte de taille ordinaire.</summary>
        public const double MinimumTextContrast = 4.5d;

        /// <summary>
        /// Deux rôles voisins à l'écran dont la distinction porte l'information.
        /// Un contraste trop faible entre eux ne gêne pas la lecture d'un texte,
        /// mais rend l'un indiscernable de l'autre — ce qui est pire, puisque
        /// rien ne le signale.
        /// </summary>
        public static readonly string[][] AdjacentPairs =
        {
            new[] { TileBackground, MemoryBars },
            new[] { MemoryBars, MemoryGauge },
            new[] { NameBandResting, NameBandNotModifiable },
            new[] { NameBandRuled, NameBandFailedRule },
            new[] { NameBandResting, NameBandRuled },
        };

        /// <summary>
        /// Seuil en deçà duquel deux rôles voisins deviennent difficiles à
        /// distinguer. Volontairement bas : les gris de la mémoire sont voisins
        /// par construction, et il ne s'agit pas de condamner le réglage
        /// d'origine mais de prévenir qu'on vient de rendre deux rôles
        /// identiques. La valeur est calée sous le plus serré des couples
        /// d'origine — vérifié par sonde, qui échoue si un couple d'origine
        /// passe sous ce seuil.
        /// </summary>
        public const double MinimumAdjacentContrast = 1.1d;
    }
}
