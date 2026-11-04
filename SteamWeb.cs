using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ZETXBoost
{
    internal sealed class CardGame
    {
        public uint AppId;
        public string Name;
        public int DropsLeft;
        public double Hours;
    }

    /// <summary>Steam mağaza, Web API ve rozet sayfası istekleri.</summary>
    internal static class SteamWeb
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var h = new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All };
            var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(30) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ZETXBoost/" + Program.Version);
            return c;
        }

        public static async Task<List<GameInfo>> SearchStoreAsync(string term)
        {
            var list = new List<GameInfo>();
            string url = "https://store.steampowered.com/api/storesearch/?l=turkish&cc=TR&term=" + Uri.EscapeDataString(term);
            string json = await Http.GetStringAsync(url);
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("items", out var items))
                    foreach (var it in items.EnumerateArray())
                    {
                        if (!it.TryGetProperty("id", out var idEl) || !idEl.TryGetUInt32(out uint id)) continue;
                        string name = it.TryGetProperty("name", out var n) ? n.GetString() : "AppID " + id;
                        list.Add(new GameInfo { AppId = id, Name = name, Extra = "Mağaza" });
                    }
            }
            return list;
        }

        public static async Task<string> GetAppNameAsync(uint appId)
        {
            try
            {
                string json = await Http.GetStringAsync("https://store.steampowered.com/api/appdetails?filters=basic&appids=" + appId);
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement.GetProperty(appId.ToString());
                    if (root.GetProperty("success").GetBoolean() &&
                        root.GetProperty("data").TryGetProperty("name", out var n))
                        return n.GetString();
                }
            }
            catch { }
            return null;
        }

        public static async Task<List<GameInfo>> GetOwnedGamesAsync(string apiKey, string steamId64)
        {
            var list = new List<GameInfo>();
            string url = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?include_appinfo=1&include_played_free_games=1&format=json"
                         + "&key=" + Uri.EscapeDataString(apiKey) + "&steamid=" + Uri.EscapeDataString(steamId64);
            string json = await Http.GetStringAsync(url);
            using (var doc = JsonDocument.Parse(json))
            {
                var resp = doc.RootElement.GetProperty("response");
                if (!resp.TryGetProperty("games", out var games))
                    throw new InvalidOperationException("Oyun listesi boş geldi. Profil gizliliğinde 'Oyun detayları' herkese açık olmalı.");
                foreach (var g in games.EnumerateArray())
                {
                    uint id = g.GetProperty("appid").GetUInt32();
                    string name = g.TryGetProperty("name", out var n) ? n.GetString() : "AppID " + id;
                    int mins = g.TryGetProperty("playtime_forever", out var p) ? p.GetInt32() : 0;
                    list.Add(new GameInfo { AppId = id, Name = name, Extra = (mins / 60.0).ToString("0.0") + " sa" });
                }
            }
            return list.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static string NormalizeCookie(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            raw = raw.Trim();
            if (raw.StartsWith("steamLoginSecure=", StringComparison.OrdinalIgnoreCase))
                raw = raw.Substring("steamLoginSecure=".Length);
            int semi = raw.IndexOf(';');
            if (semi >= 0) raw = raw.Substring(0, semi);
            return raw.Trim();
        }

        /// <summary>steamLoginSecure çerezinin başındaki SteamID64'ü çıkarır.</summary>
        public static string SteamIdFromCookie(string cookie)
        {
            var m = Regex.Match(cookie ?? "", @"^(7656\d{13})");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Rozet sayfalarını tarar; kalan kart düşüşü olan oyunları döndürür.</summary>
        public static async Task<List<CardGame>> ScanBadgesAsync(string steamId64, string cookie)
        {
            var result = new List<CardGame>();
            int page = 1, maxPage = 1;
            do
            {
                string url = "https://steamcommunity.com/profiles/" + steamId64 + "/badges/?l=english&p=" + page;
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("Cookie", "steamLoginSecure=" + cookie + "; Steam_Language=english");
                using (var resp = await Http.SendAsync(req))
                {
                    resp.EnsureSuccessStatusCode();
                    string html = await resp.Content.ReadAsStringAsync();

                    if (html.Contains("g_steamID = false"))
                        throw new InvalidOperationException("Steam oturumu geçersiz. steamLoginSecure çerezini yeniden al.");

                    if (page == 1)
                        foreach (Match pm in Regex.Matches(html, @"badges/\?p=(\d+)"))
                            if (int.TryParse(pm.Groups[1].Value, out int pn) && pn > maxPage) maxPage = pn;

                    string[] rows = Regex.Split(html, "<div class=\"badge_row ");
                    for (int i = 1; i < rows.Length; i++)
                    {
                        string r = rows[i];
                        var mId = Regex.Match(r, @"gamecards/(\d+)/");
                        var mDrops = Regex.Match(r, @"(\d+)\s+card drops?\s+remaining", RegexOptions.IgnoreCase);
                        if (!mId.Success || !mDrops.Success) continue;
                        if (!uint.TryParse(mId.Groups[1].Value, out uint id)) continue;
                        int drops = int.Parse(mDrops.Groups[1].Value, CultureInfo.InvariantCulture);
                        if (drops <= 0) continue;

                        double hours = 0;
                        var mHrs = Regex.Match(r, @"([\d,\.]+)\s+hrs? on record", RegexOptions.IgnoreCase);
                        if (mHrs.Success)
                            double.TryParse(mHrs.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out hours);

                        string name = "AppID " + id;
                        var mName = Regex.Match(r, "<div class=\"badge_title\">\\s*(.*?)(&nbsp;|<)", RegexOptions.Singleline);
                        if (mName.Success)
                        {
                            string n = WebUtility.HtmlDecode(mName.Groups[1].Value).Trim();
                            if (n.Length > 0) name = n;
                        }

                        if (result.All(x => x.AppId != id))
                            result.Add(new CardGame { AppId = id, Name = name, DropsLeft = drops, Hours = hours });
                    }
                }
                page++;
                if (page <= maxPage) await Task.Delay(700);
            } while (page <= maxPage && page <= 50);

            return result;
        }
    }
}
