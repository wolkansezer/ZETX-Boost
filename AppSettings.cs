using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace ZETXBoost
{
    public sealed class BoostGame
    {
        public uint AppId { get; set; }
        public string Name { get; set; }
        public double BoostedSeconds { get; set; }
    }

    public sealed class AppSettings
    {
        public List<BoostGame> Queue { get; set; } = new List<BoostGame>();
        public string SteamApiDllPath { get; set; } = "";
        public string WebApiKey { get; set; } = "";
        public string SteamId64 { get; set; } = "";
        public string ProtectedCookie { get; set; } = "";
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTray { get; set; } = true;
        public bool AutoStartBoost { get; set; }
        public int StopAfterMinutes { get; set; }
        public int RestartIntervalMinutes { get; set; }
        public int CardMode { get; set; }
        public int CardRecheckMinutes { get; set; } = 5;
        public double TotalBoostedSeconds { get; set; }

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZETX Boost");

        private static string FilePath => Path.Combine(Folder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                    if (s != null)
                    {
                        if (s.Queue == null) s.Queue = new List<BoostGame>();
                        return s;
                    }
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Copy(tmp, FilePath, true);
                File.Delete(tmp);
            }
            catch { }
        }

        public string GetCookie() => Dpapi.Unprotect(ProtectedCookie);
        public void SetCookie(string value) => ProtectedCookie = Dpapi.Protect(value);

        public static void ApplyStartup(bool enable)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return;
                    if (enable) k.SetValue("ZETX Boost", "\"" + Environment.ProcessPath + "\" --minimized");
                    else k.DeleteValue("ZETX Boost", false);
                }
            }
            catch { }
        }
    }

    /// <summary>Windows DPAPI: çerez yalnızca bu Windows kullanıcısıyla çözülebilir.</summary>
    internal static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            byte[] r = Run(Encoding.UTF8.GetBytes(plain), true);
            return r == null ? "" : Convert.ToBase64String(r);
        }

        public static string Unprotect(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return "";
            try
            {
                byte[] r = Run(Convert.FromBase64String(b64), false);
                return r == null ? "" : Encoding.UTF8.GetString(r);
            }
            catch { return ""; }
        }

        private static byte[] Run(byte[] data, bool protect)
        {
            var inBlob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            var outBlob = new DATA_BLOB();
            try
            {
                Marshal.Copy(data, 0, inBlob.pbData, data.Length);
                bool ok = protect
                    ? CryptProtectData(ref inBlob, "ZETX Boost", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob)
                    : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob);
                if (!ok) return null;
                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }
    }
}
