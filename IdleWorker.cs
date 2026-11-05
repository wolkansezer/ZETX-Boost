using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZETXBoost
{
    /// <summary>
    /// Her kasılan oyun için ayrı çalışan gizli alt işlem.
    /// steam_api64.dll'i yükler, SteamAppId ile Steam'e bağlanır ve
    /// ana program kapanana kadar açık kalır. Steam bu süreyi oyun süresi sayar.
    /// Kullanım: ZETXBoost.exe --idle APPID ANA_PID DLL_YOLU
    /// </summary>
    internal static class IdleWorker
    {
        public const int ExitOk = 0;
        public const int ExitBadArgs = 1;
        public const int ExitDll = 2;
        public const int ExitInit = 3;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool InitBoolFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitFlatFn(IntPtr errMsg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VoidFn();

        public static int Run(string[] args)
        {
            if (args.Length < 4 ||
                !uint.TryParse(args[1], out uint appId) ||
                !int.TryParse(args[2], out int parentPid))
                return ExitBadArgs;

            string dllPath = args[3];
            string id = appId.ToString();
            Environment.SetEnvironmentVariable("SteamAppId", id);
            Environment.SetEnvironmentVariable("SteamGameId", id);

            IntPtr lib;
            try { lib = NativeLibrary.Load(dllPath); }
            catch { return ExitDll; }

            bool ok = false;
            try
            {
                // Yeni Steamworks SDK (1.58+)
                if (NativeLibrary.TryGetExport(lib, "SteamAPI_InitFlat", out IntPtr pFlat))
                {
                    IntPtr buf = Marshal.AllocHGlobal(1024);
                    try { ok = Marshal.GetDelegateForFunctionPointer<InitFlatFn>(pFlat)(buf) == 0; }
                    finally { Marshal.FreeHGlobal(buf); }
                }
                // Eski SDK'lar
                if (!ok && NativeLibrary.TryGetExport(lib, "SteamAPI_Init", out IntPtr pInit))
                    ok = Marshal.GetDelegateForFunctionPointer<InitBoolFn>(pInit)();
                if (!ok && NativeLibrary.TryGetExport(lib, "SteamAPI_InitSafe", out IntPtr pSafe))
                    ok = Marshal.GetDelegateForFunctionPointer<InitBoolFn>(pSafe)();
            }
            catch { ok = false; }

            if (!ok) return ExitInit;

            VoidFn runCallbacks = null;
            VoidFn shutdown = null;
            if (NativeLibrary.TryGetExport(lib, "SteamAPI_RunCallbacks", out IntPtr pRun))
                runCallbacks = Marshal.GetDelegateForFunctionPointer<VoidFn>(pRun);
            if (NativeLibrary.TryGetExport(lib, "SteamAPI_Shutdown", out IntPtr pShut))
                shutdown = Marshal.GetDelegateForFunctionPointer<VoidFn>(pShut);

            // Ana program bu olayı tetikleyince düzgün kapanırız (süre Steam'e kaydedilsin diye).
            using (var stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName(Environment.ProcessId)))
            {
                while (ParentAlive(parentPid))
                {
                    try { runCallbacks?.Invoke(); } catch { }
                    if (stopEvent.WaitOne(1000)) break;
                }
            }

            // Steam'e "oyun kapandı" bildirimi: oynanma süresi sunucuya bu sırada yazılır.
            try { runCallbacks?.Invoke(); } catch { }
            try { shutdown?.Invoke(); } catch { }
            Thread.Sleep(300);
            return ExitOk;
        }

        public static string StopEventName(int pid) => @"Local\ZETXBoost_Stop_" + pid;

        private static bool ParentAlive(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                    return !p.HasExited;
            }
            catch { return false; }
        }

        public static string DescribeExitCode(int code)
        {
            switch (code)
            {
                case ExitOk: return "Durduruldu";
                case ExitBadArgs: return "Hatalı parametre";
                case ExitDll: return "steam_api64.dll yüklenemedi";
                case ExitInit: return "Steam'e bağlanılamadı (Steam kapalı ya da oyun kütüphanende yok)";
                default: return "Beklenmedik çıkış (kod " + code + ")";
            }
        }
    }
}
