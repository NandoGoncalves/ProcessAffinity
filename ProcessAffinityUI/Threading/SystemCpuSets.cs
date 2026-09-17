using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ProcessAffinityUI.Threading
{
    /// <summary>
    /// Topologie des processeurs logiques, telle que la rend
    /// GetSystemCpuSetInformation : classe d'efficacité, cœur physique dont
    /// dépend chaque processeur logique, et groupe.
    ///
    /// L'information est fixe pour la durée de la session — un seul appel, mis en
    /// cache. L'API n'existe qu'à partir de Windows 10 : en son absence, la
    /// lecture rend null et l'appelant retombe sur une grille plate.
    /// </summary>
    public static class SystemCpuSets
    {
        /// <summary>Taille minimale d'une entrée en x64 ; elles sont parcourues par leur propre Size.</summary>
        private const int MinimumEntrySize = 32;

        /// <summary>CpuSetInformation, seul type défini à ce jour.</summary>
        private const int CpuSetInformationType = 0;

        private const int ErrorInsufficientBuffer = 122;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemCpuSetInformation(
            IntPtr information,
            uint bufferLength,
            out uint returnedLength,
            IntPtr process,
            uint flags);

        private static LogicalProcessor[] _cache;
        private static bool _read;

        /// <summary>
        /// Un processeur logique et son rattachement.
        /// </summary>
        public readonly struct LogicalProcessor
        {
            public LogicalProcessor(int index, int coreIndex, int efficiencyClass, int group)
                : this(index, coreIndex, efficiencyClass, group, 0)
            {
            }

            public LogicalProcessor(int index, int coreIndex, int efficiencyClass, int group, uint id)
            {
                this.Index = index;
                this.CoreIndex = coreIndex;
                this.EfficiencyClass = efficiencyClass;
                this.Group = group;
                this.Id = id;
            }

            /// <summary>Numéro du processeur logique, celui du masque d'affinité.</summary>
            public int Index { get; }

            /// <summary>
            /// Identifiant du CPU Set, celui qu'attend SetProcessDefaultCpuSets.
            /// Sans rapport avec le numéro de processeur logique : il est opaque
            /// et ne doit jamais être reconstruit à la main.
            /// </summary>
            public uint Id { get; }

            /// <summary>
            /// Cœur physique. Deux processeurs logiques qui le partagent sont deux
            /// fils du même cœur : les cocher tous les deux ne donne pas deux cœurs.
            /// </summary>
            public int CoreIndex { get; }

            /// <summary>
            /// Classe d'efficacité. La plus élevée désigne les cœurs de
            /// performance, la plus basse les cœurs d'efficacité. Homogène sur les
            /// machines antérieures aux architectures hybrides : une seule valeur.
            /// </summary>
            public int EfficiencyClass { get; }

            public int Group { get; }
        }

        /// <summary>
        /// Topologie, ou null si l'API est absente ou échoue. Mise en cache au
        /// premier appel, y compris l'échec.
        /// </summary>
        public static LogicalProcessor[] TryGet()
        {
            if (_read)
            {
                return _cache;
            }

            _read = true;
            _cache = Read();

            return _cache;
        }

        private static LogicalProcessor[] Read()
        {
            try
            {
                // Première passe : tampon vide pour obtenir la taille requise.
                uint requiredLength;

                if (GetSystemCpuSetInformation(IntPtr.Zero, 0, out requiredLength, IntPtr.Zero, 0))
                {
                    // Réussir sans tampon n'a de sens que s'il n'y a rien à rendre.
                    return null;
                }

                if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || requiredLength < MinimumEntrySize)
                {
                    return null;
                }

                IntPtr buffer = Marshal.AllocHGlobal((int)requiredLength);

                try
                {
                    uint returnedLength;

                    if (!GetSystemCpuSetInformation(buffer, requiredLength, out returnedLength, IntPtr.Zero, 0))
                    {
                        return null;
                    }

                    return Parse(buffer, returnedLength);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Antérieur à Windows 10.
                return null;
            }
            catch (DllNotFoundException)
            {
                return null;
            }
        }

        private static LogicalProcessor[] Parse(IntPtr buffer, uint length)
        {
            List<LogicalProcessor> processors = new List<LogicalProcessor>();

            int offset = 0;

            while (offset + MinimumEntrySize <= (int)length)
            {
                IntPtr entry = IntPtr.Add(buffer, offset);

                int size = Marshal.ReadInt32(entry, 0);
                int type = Marshal.ReadInt32(entry, 4);

                if (size < MinimumEntrySize)
                {
                    // Taille incohérente : on ne sait plus où commence la suivante.
                    break;
                }

                if (type == CpuSetInformationType)
                {
                    // Disposition de l'union CpuSet : Id à 8, Group à 12, puis les
                    // cinq octets d'index, dont CoreIndex et EfficiencyClass.
                    uint id = (uint)Marshal.ReadInt32(entry, 8);
                    int group = (ushort)Marshal.ReadInt16(entry, 12);
                    int logicalProcessorIndex = Marshal.ReadByte(entry, 14);
                    int coreIndex = Marshal.ReadByte(entry, 15);
                    int efficiencyClass = Marshal.ReadByte(entry, 18);

                    processors.Add(new LogicalProcessor(
                        logicalProcessorIndex, coreIndex, efficiencyClass, group, id));
                }

                offset += size;
            }

            return processors.Count == 0 ? null : processors.ToArray();
        }
    }
}
