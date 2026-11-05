using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ZETXBoost
{
    /// <summary>
    /// Kart farmı (Idle Master / SingleBoostr mantığı):
    ///  Akıllı: 2 saatin altındaki oyunlar birlikte kasılır (32'ye kadar), hepsi 2 saati geçince tek tek kasılır.
    ///  Hepsi birden: kartı olan tüm oyunlar aynı anda.
    ///  Tek tek: sırayla bir oyun.
    /// Belirli aralıklarla rozet sayfası yeniden taranır, kartı biten oyun bırakılır.
    /// UI iş parçacığından (WinForms Timer) çağrılmalıdır.
    /// </summary>
    internal sealed class CardFarmEngine
    {
        private readonly IdleManager _idle;
        private readonly Func<string> _dll;
        private readonly HashSet<uint> _current = new HashSet<uint>();
        private DateTime _nextCheck;
        private bool _busy;
        private string _steamId, _cookie;

        public List<CardGame> Games { get; private set; } = new List<CardGame>();
        public bool Running { get; private set; }
        public int Mode { get; set; }
        public int RecheckMinutes { get; set; } = 5;
        public DateTime NextCheck => _nextCheck;
        public string LastError { get; private set; }

        public event Action<string> Log;
        public event Action Updated;
        public event Action Finished;

        public CardFarmEngine(IdleManager idle, Func<string> dllPath)
        {
            _idle = idle;
            _dll = dllPath;
        }

        public bool IsFarming(uint appId) => _current.Contains(appId);

        public async Task<bool> ScanOnlyAsync(string steamId, string cookie)
        {
            if (_busy) return false;
            _busy = true;
            try
            {
                LastError = null;
                Games = await SteamWeb.ScanBadgesAsync(steamId, cookie);
                Log?.Invoke("Tarama bitti: " + Games.Count + " oyunda toplam " + Games.Sum(g => g.DropsLeft) + " kart düşüşü kaldı.");
                Updated?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log?.Invoke("Tarama hatası: " + ex.Message);
                return false;
            }
            finally { _busy = false; }
        }

        public async Task StartAsync(string steamId, string cookie)
        {
            if (Running) return;
            _steamId = steamId;
            _cookie = cookie;
            Running = true;
            Log?.Invoke("Kart farmı başladı.");
            await RescanAndApplyAsync();
        }

        public void Stop(bool silent = false)
        {
            if (!Running && _current.Count == 0) return;
            Running = false;
            foreach (var id in _current.ToList()) _idle.Stop(id);
            _current.Clear();
            if (!silent) Log?.Invoke("Kart farmı durduruldu.");
            Updated?.Invoke();
        }

        public void OnSessionFailed(uint appId) => _current.Remove(appId);

        public async void Tick()
        {
            if (!Running || _busy) return;
            if (DateTime.Now >= _nextCheck) await RescanAndApplyAsync();
        }

        private async Task RescanAndApplyAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                try
                {
                    Games = await SteamWeb.ScanBadgesAsync(_steamId, _cookie);
                }
                catch (Exception ex)
                {
                    Log?.Invoke("Tarama hatası: " + ex.Message + " (1 dk sonra tekrar denenecek)");
                    _nextCheck = DateTime.Now.AddMinutes(1);
                    return;
                }
                if (!Running) return;
                Apply();
                _nextCheck = DateTime.Now.AddMinutes(Math.Max(1, RecheckMinutes));
            }
            finally
            {
                _busy = false;
                Updated?.Invoke();
            }
        }

        private void Apply()
        {
            var withDrops = Games.Where(g => g.DropsLeft > 0).ToList();
            if (withDrops.Count == 0)
            {
                Log?.Invoke("Tebrikler! Kalan kart düşüşü yok, farm tamamlandı.");
                Stop(true);
                Finished?.Invoke();
                return;
            }

            List<CardGame> target;
            if (Mode == 1)
                target = withDrops.Take(IdleManager.SteamMaxGames).ToList();
            else if (Mode == 2)
                target = withDrops.Take(1).ToList();
            else
            {
                var under = withDrops.Where(g => g.Hours < 2.0).Take(IdleManager.SteamMaxGames).ToList();
                target = under.Count > 0 ? under : withDrops.OrderByDescending(g => g.Hours).Take(1).ToList();
            }

            var ids = new HashSet<uint>(target.Select(t => t.AppId));
            foreach (var id in _current.ToList())
                if (!ids.Contains(id)) { _idle.Stop(id); _current.Remove(id); }

            foreach (var g in target)
            {
                if (_current.Contains(g.AppId)) continue;
                if (_idle.Start(g.AppId, g.Name, _dll(), IdleOwner.CardFarm, out string err))
                    _current.Add(g.AppId);
                else
                    Log?.Invoke(g.Name + ": " + err);
            }
            Log?.Invoke("Şu an kasılan: " + string.Join(", ", target.Select(t => t.Name)));
        }
    }
}
