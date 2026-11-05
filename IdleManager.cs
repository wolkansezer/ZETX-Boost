using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ZETXBoost
{
    internal enum IdleOwner { HourBoost, CardFarm }

    internal sealed class IdleSession
    {
        public uint AppId;
        public string Name;
        public Process Proc;
        public DateTime Started;
        public IdleOwner Owner;
        public volatile bool Stopping;
    }

    /// <summary>Alt işlemleri (kasılan oyunları) başlatır, durdurur ve izler.</summary>
    internal sealed class IdleManager : IDisposable
    {
        public const int SteamMaxGames = 32;

        private readonly object _lock = new object();
        private readonly Dictionary<uint, IdleSession> _map = new Dictionary<uint, IdleSession>();

        /// <summary>Oyun kendiliğinden kapandığında (hata). Arka plan iş parçacığından çağrılır.</summary>
        public event Action<IdleSession, int> SessionFailed;

        public int Count { get { lock (_lock) return _map.Count; } }

        public bool IsRunning(uint appId) { lock (_lock) return _map.ContainsKey(appId); }

        public List<IdleSession> Snapshot() { lock (_lock) return _map.Values.ToList(); }

        public bool Start(uint appId, string name, string dllPath, IdleOwner owner, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                error = "steam_api64.dll bulunamadı. Ayarlar sekmesinden \"Otomatik Bul\" ile seç.";
                return false;
            }

            var psi = new ProcessStartInfo(Environment.ProcessPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--idle");
            psi.ArgumentList.Add(appId.ToString());
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add(dllPath);
            psi.Environment["SteamAppId"] = appId.ToString();
            psi.Environment["SteamGameId"] = appId.ToString();

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var s = new IdleSession { AppId = appId, Name = name, Proc = proc, Started = DateTime.Now, Owner = owner };
            proc.Exited += (o, e) => OnExited(s);

            lock (_lock)
            {
                if (_map.ContainsKey(appId)) return true;
                if (_map.Count >= SteamMaxGames)
                {
                    error = "Steam aynı anda en fazla " + SteamMaxGames + " oyun kasılmasına izin verir.";
                    return false;
                }
                _map[appId] = s;
            }

            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                lock (_lock) { _map.Remove(appId); }
                error = "İşlem başlatılamadı: " + ex.Message;
                return false;
            }
            return true;
        }

        private void OnExited(IdleSession s)
        {
            bool removed = false;
            lock (_lock)
            {
                if (_map.TryGetValue(s.AppId, out var cur) && ReferenceEquals(cur, s))
                {
                    _map.Remove(s.AppId);
                    removed = true;
                }
            }
            if (!removed || s.Stopping) return;

            int code = -1;
            try { code = s.Proc.ExitCode; } catch { }
            SessionFailed?.Invoke(s, code);
        }

        public void Stop(uint appId)
        {
            IdleSession s;
            lock (_lock)
            {
                if (!_map.TryGetValue(appId, out s)) return;
                _map.Remove(appId);
            }
            StopGracefully(new List<IdleSession> { s });
        }

        public void StopAll(IdleOwner? owner = null)
        {
            List<IdleSession> list;
            lock (_lock)
            {
                list = _map.Values.Where(x => owner == null || x.Owner == owner.Value).ToList();
                foreach (var s in list) _map.Remove(s.AppId);
            }
            StopGracefully(list);
        }

        /// <summary>
        /// Önce tüm alt işlemlere "kapan" sinyali gönderir (SteamAPI_Shutdown çağırırlar,
        /// Steam süreyi kaydeder), en fazla 5 sn bekler; kapanmayanı zorla sonlandırır.
        /// </summary>
        private static void StopGracefully(List<IdleSession> list)
        {
            if (list.Count == 0) return;
            foreach (var s in list)
            {
                s.Stopping = true;
                try
                {
                    if (EventWaitHandle.TryOpenExisting(IdleWorker.StopEventName(s.Proc.Id), out var ev))
                        using (ev) ev.Set();
                }
                catch { }
            }

            var deadline = DateTime.Now.AddSeconds(5);
            foreach (var s in list)
            {
                try
                {
                    int left = (int)Math.Max(0, (deadline - DateTime.Now).TotalMilliseconds);
                    if (!s.Proc.HasExited && !s.Proc.WaitForExit(left)) s.Proc.Kill();
                }
                catch { }
                try { s.Proc.Dispose(); } catch { }
            }
        }

        public void Dispose() { StopAll(); }
    }
}
