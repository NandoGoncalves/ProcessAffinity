using System;
using System.Runtime.InteropServices;

namespace ProcessAffinityUI.Threading
{
    /// <summary>
    /// Accès natif aux processus, sans élévation.
    /// La lecture comme l'écriture de l'affinité et de la priorité passent par
    /// le descripteur de sécurité du processus cible : PROCESS_QUERY_LIMITED_INFORMATION
    /// n'y change rien pour un processus d'un autre compte.
    /// Process.ProcessorAffinity, lui, exige PROCESS_QUERY_INFORMATION, plus
    /// restrictif encore.
    /// </summary>
    internal static class NativeProcessAccess
    {
        private const int ProcessQueryLimitedInformation = 0x1000;
        private const int ProcessSetInformation = 0x0200;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessAffinityMask(IntPtr process, out nuint processAffinityMask, out nuint systemAffinityMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>
        /// Masque d'affinité du processus, ou false si la lecture échoue — un
        /// échec ne doit pas être confondu avec un masque vide.
        /// </summary>
        public static bool TryGetProcessorAffinity(int processID, out nuint processorAffinity)
        {
            processorAffinity = 0;

            IntPtr processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processID);

            if (processHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                nuint systemAffinity;

                return GetProcessAffinityMask(processHandle, out processorAffinity, out systemAffinity);
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }

        /// <summary>
        /// Le processus accorde-t-il PROCESS_SET_INFORMATION, droit requis pour
        /// écrire affinité et priorité. Sondé séparément de la lecture : quelques
        /// processus sont lisibles sans être modifiables, notamment ceux d'une
        /// session élevée du même utilisateur.
        /// </summary>
        public static bool CanModifyProcess(int processID)
        {
            IntPtr processHandle = OpenProcess(ProcessSetInformation, false, processID);

            if (processHandle == IntPtr.Zero)
            {
                return false;
            }

            CloseHandle(processHandle);

            return true;
        }
    }
}
