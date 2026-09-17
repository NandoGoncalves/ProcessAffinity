using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ProcessAffinityUI.Threading
{
    /// <summary>
    /// Mode d'efficacité — EcoQoS — et CPU Sets par défaut d'un processus.
    ///
    /// Deux mécanismes distincts, à ne pas confondre avec l'affinité :
    /// - le mode d'efficacité bride la montée en fréquence, ce qui vaut même sur
    ///   une machine à cœurs homogènes ;
    /// - les CPU Sets sont une affinité *souple* : l'ordonnanceur peut les
    ///   outrepasser, là où le masque d'affinité est une contrainte dure. Les
    ///   deux coexistent sur un même processus.
    /// </summary>
    public static class ProcessPowerThrottling
    {
        private const int ProcessQueryLimitedInformation = 0x1000;
        private const int ProcessSetInformation = 0x0200;

        /// <summary>ProcessPowerThrottling dans PROCESS_INFORMATION_CLASS.</summary>
        private const int ProcessPowerThrottlingClass = 4;

        private const uint CurrentVersion = 1;
        private const uint ExecutionSpeed = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessInformation(
            IntPtr process, int informationClass, ref PROCESS_POWER_THROTTLING_STATE information, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessInformation(
            IntPtr process, int informationClass, ref PROCESS_POWER_THROTTLING_STATE information, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDefaultCpuSets(IntPtr process, uint[] cpuSetIds, uint count);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessDefaultCpuSets(
            IntPtr process, uint[] cpuSetIds, uint cpuSetIdCount, out uint requiredIdCount);

        private static bool? _isEfficiencyModeSupported;
        private static bool? _areCpuSetsSupported;

        /// <summary>
        /// Le mode d'efficacité est-il disponible. Windows 10 version 1809 au
        /// minimum ; sinon la fonctionnalité se retire sans message.
        /// </summary>
        public static bool IsEfficiencyModeSupported
        {
            get
            {
                if (_isEfficiencyModeSupported == null)
                {
                    _isEfficiencyModeSupported = Probe(() => GetEfficiencyMode(Environment.ProcessId) != null);
                }

                return _isEfficiencyModeSupported.Value;
            }
        }

        /// <summary>Les CPU Sets sont-ils disponibles. Windows 10 au minimum.</summary>
        public static bool AreCpuSetsSupported
        {
            get
            {
                if (_areCpuSetsSupported == null)
                {
                    _areCpuSetsSupported = Probe(() => GetDefaultCpuSets(Environment.ProcessId) != null);
                }

                return _areCpuSetsSupported.Value;
            }
        }

        private static bool Probe(Func<bool> test)
        {
            try
            {
                return test();
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        /// <summary>
        /// État du mode d'efficacité, ou null si la lecture échoue.
        /// </summary>
        public static EfficiencyModeEnum? GetEfficiencyMode(int processID)
        {
            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processID);

            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                PROCESS_POWER_THROTTLING_STATE state = new PROCESS_POWER_THROTTLING_STATE();
                state.Version = CurrentVersion;

                if (!GetProcessInformation(handle, ProcessPowerThrottlingClass, ref state,
                        (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()))
                {
                    return null;
                }

                // Le bit n'est pas piloté : c'est Windows qui décide, l'état par
                // défaut. Piloté, le bit d'état dit lequel des deux.
                if ((state.ControlMask & ExecutionSpeed) == 0)
                {
                    return EfficiencyModeEnum.SystemManaged;
                }

                return (state.StateMask & ExecutionSpeed) != 0
                    ? EfficiencyModeEnum.Enabled
                    : EfficiencyModeEnum.Disabled;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>
        /// Pose le mode d'efficacité. Exige les mêmes droits en écriture que
        /// l'affinité.
        /// </summary>
        public static bool TrySetEfficiencyMode(int processID, EfficiencyModeEnum mode)
        {
            IntPtr handle = OpenProcess(ProcessSetInformation, false, processID);

            if (handle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                PROCESS_POWER_THROTTLING_STATE state = new PROCESS_POWER_THROTTLING_STATE();
                state.Version = CurrentVersion;

                switch (mode)
                {
                    case EfficiencyModeEnum.Enabled:
                        state.ControlMask = ExecutionSpeed;
                        state.StateMask = ExecutionSpeed;
                        break;

                    case EfficiencyModeEnum.Disabled:
                        state.ControlMask = ExecutionSpeed;
                        state.StateMask = 0;
                        break;

                    default:
                        // Rendre la main à Windows : ni contrôle, ni état.
                        state.ControlMask = 0;
                        state.StateMask = 0;
                        break;
                }

                return SetProcessInformation(handle, ProcessPowerThrottlingClass, ref state,
                    (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>
        /// CPU Sets par défaut du processus, ou null si la lecture échoue. Un
        /// tableau vide signifie « aucune restriction » : le processus peut aller
        /// partout, ce qui est l'état par défaut.
        /// </summary>
        public static uint[] GetDefaultCpuSets(int processID)
        {
            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processID);

            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                uint required;

                // Première passe. L'appel ne réussit avec un tampon vide que
                // lorsqu'il n'y a rien à rendre ; dès qu'il y a des identifiants
                // il échoue en renseignant le compte. Prendre cet échec pour une
                // erreur rendait toute restriction posée illisible.
                if (GetProcessDefaultCpuSets(handle, null, 0, out required))
                {
                    return new uint[0];
                }

                if (required == 0)
                {
                    return null;
                }

                uint[] ids = new uint[required];

                if (!GetProcessDefaultCpuSets(handle, ids, required, out required))
                {
                    return null;
                }

                Array.Resize(ref ids, (int)required);

                return ids;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>
        /// Pose les CPU Sets par défaut. Une liste vide ou nulle lève la
        /// restriction sans toucher au masque d'affinité, qui vit à part.
        /// </summary>
        public static bool TrySetDefaultCpuSets(int processID, uint[] cpuSetIds)
        {
            IntPtr handle = OpenProcess(ProcessSetInformation, false, processID);

            if (handle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (cpuSetIds == null || cpuSetIds.Length == 0)
                {
                    return SetProcessDefaultCpuSets(handle, null, 0);
                }

                return SetProcessDefaultCpuSets(handle, cpuSetIds, (uint)cpuSetIds.Length);
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }

    /// <summary>
    /// Trois états et non deux : « laissé à Windows » est l'état par défaut, et
    /// il doit pouvoir être rétabli après avoir forcé l'un des deux autres.
    /// </summary>
    public enum EfficiencyModeEnum
    {
        /// <summary>Windows décide. État par défaut de tout processus.</summary>
        SystemManaged = 0,

        /// <summary>Bridé : montée en fréquence limitée.</summary>
        Enabled = 1,

        /// <summary>Jamais bridé, même quand Windows le voudrait.</summary>
        Disabled = 2,
    }
}
