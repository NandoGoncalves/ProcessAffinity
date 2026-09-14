using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ProcessAffinityUI.Threading
{
    /// <summary>
    /// Temps processeur de tous les processus, obtenus en un seul appel système.
    /// NtQuerySystemInformation n'ouvre aucun handle et n'effectue donc aucun
    /// contrôle d'accès par processus, là où Process.TotalProcessorTime échouait
    /// en « accès refusé » sur la majorité des processus en session non élevée.
    /// C'est la source qu'utilise le Gestionnaire des tâches.
    /// </summary>
    internal static class SystemProcessTimes
    {
        private const int SystemProcessInformation = 5;
        private const int StatusSuccess = 0;
        private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

        private const int InitialBufferLength = 512 * 1024;
        private const int BufferMargin = 64 * 1024;
        private const int MaximumAttempts = 8;

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(
            int systemInformationClass,
            IntPtr systemInformation,
            int systemInformationLength,
            out int returnLength);

        /// <summary>
        /// Temps processeur indexés par PID, ou null si l'appel système échoue.
        /// <paramref name="timestamp"/> est relevé au plus près de l'appel, pour
        /// que le temps écoulé corresponde exactement aux compteurs retournés.
        /// </summary>
        public static Dictionary<int, ProcessTimes> GetProcessTimes(out long timestamp)
        {
            int length = InitialBufferLength;
            timestamp = 0;

            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                IntPtr buffer = Marshal.AllocHGlobal(length);

                try
                {
                    int returnLength;
                    int status = NtQuerySystemInformation(SystemProcessInformation, buffer, length, out returnLength);

                    if (status == StatusSuccess)
                    {
                        timestamp = System.Diagnostics.Stopwatch.GetTimestamp();

                        return Parse(buffer);
                    }

                    if (status != StatusInfoLengthMismatch)
                    {
                        return null;
                    }

                    // Le nombre de processus peut changer entre deux appels :
                    // on reprend la taille annoncée, avec une marge.
                    length = (returnLength > length ? returnLength : length) + BufferMargin;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return null;
        }

        private static Dictionary<int, ProcessTimes> Parse(IntPtr buffer)
        {
            Dictionary<int, ProcessTimes> processTimesByProcessID = new Dictionary<int, ProcessTimes>(512);
            IntPtr entry = buffer;

            while (true)
            {
                SYSTEM_PROCESS_INFORMATION information =
                    Marshal.PtrToStructure<SYSTEM_PROCESS_INFORMATION>(entry);

                int processID = (int)information.UniqueProcessId.ToInt64();

                processTimesByProcessID[processID] = new ProcessTimes(
                    information.CreateTime,
                    information.KernelTime + information.UserTime,
                    GetActivitySignature(information));

                if (information.NextEntryOffset == 0)
                {
                    break;
                }

                entry = IntPtr.Add(entry, (int)information.NextEntryOffset);
            }

            return processTimesByProcessID;
        }

        /// <summary>
        /// Résume l'état du processus en un seul nombre : il change dès que
        /// l'une de ces grandeurs bouge. C'est l'équivalent du
        /// __InstanceModificationEvent de WMI, qui partait à la moindre variation
        /// de propriété — sauf qu'ici tout est déjà dans le tampon relevé.
        /// CycleTime en est délibérément absent : il compte les cycles au plus
        /// près, si bien que presque tout processus bouge à chaque seconde — deux
        /// tuiles sur trois restaient allumées en permanence, ce qui n'est plus un
        /// clignotement. Les compteurs retenus ne bougent que sur un processus qui
        /// travaille réellement.
        /// </summary>
        private static long GetActivitySignature(SYSTEM_PROCESS_INFORMATION information)
        {
            unchecked
            {
                long signature = information.KernelTime + information.UserTime;

                signature = (signature * 31) + information.WorkingSetPrivateSize;
                signature = (signature * 31) + information.HandleCount;
                signature = (signature * 31) + information.NumberOfThreads;
                signature = (signature * 31) + information.HardFaultCount;

                return signature;
            }
        }

        /// <summary>
        /// Compteurs d'un processus, en unités de 100 ns.
        /// </summary>
        public readonly struct ProcessTimes
        {
            public ProcessTimes(long createTime, long totalProcessorTime, long activitySignature)
            {
                this.CreateTime = createTime;
                this.TotalProcessorTime = totalProcessorTime;
                this.ActivitySignature = activitySignature;
            }

            /// <summary>État résumé : toute variation vaut « il s'est passé quelque chose ».</summary>
            public long ActivitySignature { get; }

            /// <summary>Date de création : identifie un PID réutilisé.</summary>
            public long CreateTime { get; }

            /// <summary>Somme des temps noyau et utilisateur.</summary>
            public long TotalProcessorTime { get; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        // Préfixe de la structure réelle, qui se poursuit par les compteurs
        // mémoire puis le tableau des threads. Les champs sont déclarés jusqu'à
        // UniqueProcessKey pour que la disposition séquentielle place CreateTime,
        // UserTime, KernelTime et UniqueProcessId aux bons décalages.
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_PROCESS_INFORMATION
        {
            public uint NextEntryOffset;
            public uint NumberOfThreads;
            public long WorkingSetPrivateSize;
            public uint HardFaultCount;
            public uint NumberOfThreadsHighWatermark;
            public ulong CycleTime;
            public long CreateTime;
            public long UserTime;
            public long KernelTime;
            public UNICODE_STRING ImageName;
            public int BasePriority;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
            public uint HandleCount;
            public uint SessionId;
            public UIntPtr UniqueProcessKey;
        }
    }
}
