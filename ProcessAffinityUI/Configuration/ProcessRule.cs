using System;

namespace ProcessAffinityUI.Configuration
{
    /// <summary>
    /// Règle persistante attachée à un exécutable : l'affinité et la priorité que
    /// l'utilisateur a choisies, réappliquées au démarrage et à chaque lancement.
    ///
    /// L'identité est le chemin complet de l'exécutable, comparé sans tenir compte
    /// de la casse — Windows ne la distingue pas sur les chemins.
    /// </summary>
    public sealed class ProcessRule
    {
        public string ExecutablePath { get; set; }

        /// <summary>
        /// Masque d'affinité en décimal, sous forme de chaîne. Un nombre JSON perd
        /// de la précision au-delà de 2^53, or un masque en couvre 64.
        /// </summary>
        public string AffinityMask { get; set; }

        /// <summary>
        /// Classe de priorité, dans les valeurs de <see cref="Threading.ProcessPriorityEnum"/>
        /// — celles qu'attend SetPriority, et non l'échelle 0-31 que rend WMI.
        /// </summary>
        public int PriorityClass { get; set; }

        /// <summary>
        /// Mode d'efficacité demandé, ou null quand la règle ne s'en mêle pas.
        /// Optionnel depuis la version 2 du format : une règle de version 1 le
        /// laisse absent et se comporte comme avant, sans jamais y toucher.
        /// </summary>
        public int? EfficiencyMode { get; set; }

        /// <summary>
        /// CPU Sets voulus, exprimés en numéros de processeur logique et non en
        /// identifiants : ceux-ci sont opaques et propres à une machine, alors
        /// qu'un fichier de règles peut voyager. La conversion passe par la
        /// topologie au moment de l'application.
        ///
        /// Null quand la règle ne s'en mêle pas ; vide signifie « aucune
        /// préférence », ce qui est un réglage à part entière.
        /// </summary>
        public int[] CpuSetProcessors { get; set; }

        public nuint GetAffinityMask()
        {
            ulong mask;

            return ulong.TryParse(this.AffinityMask, out mask) ? (nuint)mask : (nuint)0;
        }

        public void SetAffinityMask(nuint mask)
        {
            this.AffinityMask = ((ulong)mask).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Contenu du fichier. Le numéro de format est écrit dès la première version :
    /// sans lui, une évolution ultérieure ne saurait pas distinguer un fichier
    /// ancien d'un fichier corrompu.
    /// </summary>
    public sealed class RuleFile
    {
        public int FormatVersion { get; set; }

        public ProcessRule[] Rules { get; set; }
    }

    /// <summary>
    /// Sort d'une règle sur un processus donné. Tout ce qui n'est pas
    /// <see cref="Applied"/> est un échec et se signale sur la tuile.
    /// </summary>
    public enum RuleStateEnum
    {
        /// <summary>Aucune règle pour cet exécutable.</summary>
        None = 0,

        /// <summary>Écrite, puis relue conforme.</summary>
        Applied,

        /// <summary>
        /// Écrite, mais relue différente. Windows rétrograde la priorité temps
        /// réel sans le dire, et peut ajuster un masque.
        /// </summary>
        Contested,

        /// <summary>Droits insuffisants sur le processus cible.</summary>
        Denied,

        /// <summary>Le chemin de la règle n'existe plus sur le disque.</summary>
        Orphan,

        /// <summary>
        /// Masque vide une fois restreint aux processeurs logiques de cette
        /// machine : la règle vient d'une machine qui en comptait davantage.
        /// </summary>
        InvalidMask,
    }
}
