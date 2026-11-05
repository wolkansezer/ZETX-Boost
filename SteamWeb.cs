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
            raw = raw.Trim().Trim('"');
            int idx = raw.IndexOf("steamLoginSecure=", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) raw = raw.Substring(idx + "steamLoginSecure=".Length);
            int semi = raw.IndexOf(';');
            if (semi >= 0) raw = raw.Substring(0, semi);
            raw = raw.Trim().Replace(" ", "");
            // Çözülmüş hâli (|| ile) yapıştırıldıysa Steam'in beklediği kodlu biçime çevir.
            raw = raw.Replace("|", "%7C");
            return raw;
        }

        /// <summary>
        /// steamLoginSecure çerezini çözüp denetler. Sorun yoksa null, varsa Türkçe açıklama döner.
        /// Çerez biçimi: STEAMID64%7C%7C(JWT)  — JWT içinde sub (SteamID), aud (web:community / web:store), exp.
        /// </summary>
        public static string ValidateCookie(string cookie, out string steamId64)
        {
            steamId64 = null;
            if (string.IsNullOrWhiteSpace(cookie))
                return "steamLoginSecure çerezi boş.";

            string decoded = Uri.UnescapeDataString(cookie);
            int sep = decoded.IndexOf("||", StringComparison.Ordinal);
            if (sep < 0)
            {
                if (decoded.StartsWith("eyJ"))
                    return "Çerezin başı eksik. Value değeri 7656... ile başlamalı; tamamını kopyala (çift tıklayıp Ctrl+A, Ctrl+C).";
                return "Bu bir steamLoginSecure değeri gibi görünmüyor. Value sütunundaki değer 7656...%7C%7CeyJ... şeklinde olmalı.";
            }

            string idPart = decoded.Substring(0, sep).Trim();
            if (idPart.Length != 17 || !idPart.StartsWith("7656") || !idPart.All(char.IsDigit))
                return "Çerezin başındaki SteamID okunamadı. Değeri eksiksiz kopyaladığından emin ol.";
            steamId64 = idPart;

            string jwt = decoded.Substring(sep + 2).Trim();
            string[] parts = jwt.Split('.');
            if (parts.Length < 2)
                return "Çerez yarım kopyalanmış görünüyor (çok kısa). Value değerinin tamamını kopyala.";

            try
            {
                string b64 = parts[1].Replace('-', '+').Replace('_', '/');
                b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
                string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;

                    if (root.TryGetProperty("aud", out var aud))
                    {
                        string audText = aud.ValueKind == JsonValueKind.Array
                            ? string.Join(",", aud.EnumerateArray().Select(a => a.GetString()))
                            : aud.GetString() ?? "";
                        if (!audText.Contains("web:community"))
                        {
                            if (audText.Contains("web:store"))
                                return "Bu çerez MAĞAZA sitesinin (store.steampowered.com) çerezi.\n\n" +
                                       "steamcommunity.com adresine git, F12 > Application > Cookies > https://steamcommunity.com altındaki steamLoginSecure değerini kopyala.";
                            if (audText.Contains("client") || audText.Contains("renew"))
                                return "Bu çerez Steam uygulamasına ait. Tarayıcıda steamcommunity.com'daki steamLoginSecure değerini kopyala.";
                        }
                    }

                    if (root.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out long expUnix))
                    {
                        var expires = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                        if (expires < DateTimeOffset.UtcNow)
                            return "Çerezin süresi dolmuş (" + expires.ToLocalTime().ToString("dd.MM.yyyy HH:mm") + ").\n\n" +
                                   "steamcommunity.com sayfasını yenile (F5), sonra steamLoginSecure değerini yeniden kopyala.";
                    }

                    if (root.TryGetProperty("sub", out var sub) && sub.GetString() is string subId && subId.Length == 17)
                        steamId64 = subId;
                }
            }
            catch
            {
                return "Çerez çözülemedi; büyük ihtimalle eksik kopyalandı. Value değerini baştan sona tekrar kopyala.";
            }
            return null;
        }

        /// <summary>steamLoginSecure çerezinin başındaki SteamID64'ü çıkarır.</summary>
        public static string SteamIdFromCookie(string cookie)
        {
            var m = Regex.Match(Uri.UnescapeDataString(cookie ?? ""), @"^(7656\d{13})");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Rozet sayfalarını tarar; kalan kart düşüşü olan oyunları döndürür.</summary>
        public static async Task<List<CardGame>> ScanBadgesAsync(string steamId64, string cookie)
        {
            // Çerezler CookieContainer'da: Steam /profiles/ID adresini /id/özeladın adresine
            // yönlendirdiğinde de oturum çerezi gönderilmeye devam eder.
            var jar = new CookieContainer();
            var community = new Uri("https://steamcommunity.com/");
            void Add(string n, string v)
            {
                jar.Add(community, new Cookie(n, v, "/", "steamcommunity.com") { Secure = true });
            }
            Add("steamLoginSecure", cookie);
            Add("Steam_Language", "english");
            Add("birthtime", "283993201");
            Add("mature_content", "1");

            var handler = new HttpClientHandler
            {
                CookieContainer = jar,
                UseCookies = true,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                AutomaticDecompression = DecompressionMethods.All
            };

            var result = new List<CardGame>();
            using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
                http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");

                int page = 1, maxPage = 1;
                do
                {
                    string url = "https://steamcommunity.com/profiles/" + steamId64 + "/badges/?l=english&p=" + page;
                    string html;
                    using (var resp = await http.GetAsync(url))
                    {
                        int code = (int)resp.StatusCode;
                        if (code == 429)
                            throw new InvalidOperationException("Steam çok fazla istek dedi (429). Birkaç dakika sonra tekrar dene.");
                        if (code == 401 || code == 403)
                            throw new InvalidOperationException("Steam erişimi reddetti (" + code + "). Çerezi yeniden al.");
                        if (!resp.IsSuccessStatusCode)
                            throw new InvalidOperationException("Steam rozet sayfası hata verdi: HTTP " + code);

                        string finalUrl = resp.RequestMessage?.RequestUri?.ToString() ?? "";
                        if (finalUrl.Contains("/login"))
                            throw new InvalidOperationException("Steam oturumu geçersiz (giriş sayfasına yönlendirildi). steamLoginSecure çerezini yeniden al.");
                        html = await resp.Content.ReadAsStringAsync();
                    }

                    if (Regex.IsMatch(html, @"g_steamID\s*=\s*false"))
                        throw new InvalidOperationException("Steam oturumu açık görünmüyor. Tarayıcıda steamcommunity.com'a giriş yapıp steamLoginSecure çerezini YENİDEN kopyala (çerez her girişte değişir).");

                    var mLogged = Regex.Match(html, @"g_steamID\s*=\s*""(\d{17})""");
                    if (mLogged.Success && mLogged.Groups[1].Value != steamId64)
                        throw new InvalidOperationException("Çerez başka bir hesaba ait (" + mLogged.Groups[1].Value + "). SteamID64 alanını boş bırakıp tekrar dene.");

                    if (page == 1)
                        foreach (Match pm in Regex.Matches(html, @"class=""pagelink""[^>]*href=""[^""]*[?&]p=(\d+)"))
                            if (int.TryParse(pm.Groups[1].Value, out int pn) && pn > maxPage) maxPage = pn;

                    ParseBadgeRows(html, result);

                    page++;
                    if (page <= maxPage) await Task.Delay(800);
                } while (page <= maxPage && page <= 50);
            }
            return result;
        }

        internal static void ParseBadgeRows(string html, List<CardGame> result)
        {
            string[] rows = Regex.Split(html, @"<div class=""badge_row[ ""]");
            for (int i = 1; i < rows.Length; i++)
            {
                string r = rows[i];
                var mId = Regex.Match(r, @"/gamecards/(\d+)");
                if (!mId.Success || !uint.TryParse(mId.Groups[1].Value, out uint id)) continue;
                var mDrops = Regex.Match(r, @"(\d+)\s+card drops?\s+remaining", RegexOptions.IgnoreCase);
                if (!mDrops.Success) continue;
                int drops = int.Parse(mDrops.Groups[1].Value, CultureInfo.InvariantCulture);
                if (drops <= 0) continue;

                double hours = 0;
                var mHrs = Regex.Match(r, @"([\d,\.]+)\s+hrs? on record", RegexOptions.IgnoreCase);
                if (mHrs.Success)
                    double.TryParse(mHrs.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out hours);

                string name = "AppID " + id;
                var mName = Regex.Match(r, @"<div class=""badge_title"">\s*(.*?)(&nbsp;|<)", RegexOptions.Singleline);
                if (mName.Success)
                {
                    string n = WebUtility.HtmlDecode(mName.Groups[1].Value).Trim();
                    if (n.Length > 0) name = n;
                }

                if (result.All(x => x.AppId != id))
                    result.Add(new CardGame { AppId = id, Name = name, DropsLeft = drops, Hours = hours });
            }
        }
    }
}
