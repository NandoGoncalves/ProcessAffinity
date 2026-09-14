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
        private static extern uint GetPriorityClass(IntPtr process);

        /// <summary>
        /// Classe de priorité réelle du processus, ou null si la lecture échoue.
        ///
        /// Indispensable : Win32_Process.Priority est figé à l'énumération. Il ne
        /// bouge que lorsque l'application écrit elle-même par WMI, et ne reflète
        /// jamais un changement venu de l'extérieur — mesuré : priorité passée en
        /// BelowNormal par un tiers, WMI rendait toujours la valeur d'origine.
        ///
        /// Les valeurs rendues sont celles de <see cref="ProcessPriorityEnum"/> :
        /// les constantes Win32 et l'énumération coïncident.
        /// </summary>
        public static int? TryGetPriorityClass(int processID)
        {
            IntPtr processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processID);

            if (processHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                uint priorityClass = GetPriorityClass(processHandle);

                return priorityClass == 0 ? (int?)null : (int)priorityClass;
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder imageName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>
        /// Chemin de l'exécutable, ou null si la lecture échoue. Win32_Process
        /// rend un ExecutablePath vide pour les processus qui tournent élevés :
        /// leur tuile se retrouvait sans icône, or c'est à l'icône qu'on les
        /// reconnaît. QueryFullProcessImageName se contente de
        /// PROCESS_QUERY_LIMITED_INFORMATION et les rend souvent lisibles.
        /// </summary>
        public static string TryGetImagePath(int processID)
        {
            IntPtr processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processID);

            if (processHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                int size = 1024;
                System.Text.StringBuilder imageName = new System.Text.StringBuilder(size);

                return QueryFullProcessImageName(processHandle, 0, imageName, ref size)
                    ? imageName.ToString()
                    : null;
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }

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
