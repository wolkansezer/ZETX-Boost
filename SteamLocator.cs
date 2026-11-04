using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZETXBoost
{
    internal sealed class GameInfo
    {
        public uint AppId;
        public string Name;
        public string Extra;
    }

    /// <summary>Yerel Steam kurulumunu, kütüphaneleri, yüklü oyunları ve aktif kullanıcıyı bulur.</summary>
    internal static class SteamLocator
    {
        private const ulong SteamId64Base = 76561197960265728UL;

        // Oyun olmayan araçlar (Redistributables vb.)
        private static readonly HashSet<uint> Ignored = new HashSet<uint> { 228980, 1070560, 1391110, 1628350, 1493710, 1826330, 250820 };

        public static string GetSteamPath()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (k?.GetValue("SteamPath") is string p && p.Length > 0)
                    {
                        p = p.Replace('/', '\\');
                        if (Directory.Exists(p)) return p;
                    }
                }
            }
            catch { }
            foreach (var c in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
                if (Directory.Exists(c)) return c;
            return null;
        }

        public static bool IsSteamRunning()
        {
            var ps = Process.GetProcessesByName("steam");
            bool r = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
            return r;
        }

        public static string GetActiveSteamId64()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                {
                    object v = k?.GetValue("ActiveUser");
                    if (v is int i && i != 0)
                        return (SteamId64Base + (ulong)(uint)i).ToString();
                }
            }
            catch { }
            return null;
        }

        public static string GetAccountName()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    return k?.GetValue("AutoLoginUser") as string;
            }
            catch { return null; }
        }

        public static List<string> GetLibraryFolders()
        {
            var result = new List<string>();
            string steam = GetSteamPath();
            if (steam == null) return result;
            result.Add(steam);

            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            try
            {
                if (File.Exists(vdf))
                {
                    string text = File.ReadAllText(vdf);
                    foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
                    {
                        string p = m.Groups[1].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(p) && !result.Any(x => string.Equals(x.TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                            result.Add(p);
                    }
                }
            }
            catch { }
            return result;
        }

        public static List<GameInfo> GetInstalledGames()
        {
            var list = new List<GameInfo>();
            var seen = new HashSet<uint>();
            foreach (var lib in GetLibraryFolders())
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                string[] files;
                try { files = Directory.GetFiles(apps, "appmanifest_*.acf"); } catch { continue; }
                foreach (var f in files)
                {
                    try
                    {
                        string t = File.ReadAllText(f);
                        var mId = Regex.Match(t, "\"appid\"\\s+\"(\\d+)\"");
                        var mName = Regex.Match(t, "\"name\"\\s+\"([^\"]*)\"");
                        if (!mId.Success || !uint.TryParse(mId.Groups[1].Value, out uint id)) continue;
                        if (Ignored.Contains(id) || !seen.Add(id)) continue;
                        list.Add(new GameInfo { AppId = id, Name = mName.Success ? mName.Groups[1].Value : "AppID " + id, Extra = "Yüklü" });
                    }
                    catch { }
                }
            }
            return list.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Yüklü oyun klasörlerinde en yeni steam_api64.dll'i arar.</summary>
        public static string FindSteamApiDll()
        {
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 5,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            };
            FileInfo best = null;
            int scanned = 0;
            foreach (var lib in GetLibraryFolders())
            {
                string common = Path.Combine(lib, "steamapps", "common");
                if (!Directory.Exists(common)) continue;
                string[] games;
                try { games = Directory.GetDirectories(common); } catch { continue; }
                foreach (var g in games)
                {
                    if (++scanned > 300) break;
                    try
                    {
                        string f = Directory.EnumerateFiles(g, "steam_api64.dll", opts).FirstOrDefault();
                        if (f == null) continue;
                        var fi = new FileInfo(f);
                        if (best == null || fi.LastWriteTimeUtc > best.LastWriteTimeUtc) best = fi;
                    }
                    catch { }
                }
            }
            return best?.FullName;
        }
    }
}
