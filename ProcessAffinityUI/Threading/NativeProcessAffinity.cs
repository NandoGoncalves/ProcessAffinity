using System;
using System.Runtime.InteropServices;

namespace ProcessAffinityUI.Threading
{
    /// <summary>
    /// Lecture du masque d'affinité sans élévation.
    /// Process.ProcessorAffinity ouvre un handle avec PROCESS_QUERY_INFORMATION,
    /// refusé sur les processus des autres utilisateurs. GetProcessAffinityMask
    /// se contente de PROCESS_QUERY_LIMITED_INFORMATION, le droit prévu pour les
    /// interroger. L'écriture, elle, reste soumise à l'élévation.
    /// </summary>
    internal static class NativeProcessAffinity
    {
        private const int ProcessQueryLimitedInformation = 0x1000;

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
    }
}
