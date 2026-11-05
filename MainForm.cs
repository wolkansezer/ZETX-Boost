using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZETXBoost
{
    internal sealed class MainForm : Form
    {
        private const string OwnerName = "Volkan Sezer";
        private const string OwnerMail = "volkan.sezer@outlook.com.tr";
        private const string OwnerInstagram = "@zetxtv";
        private const string OwnerInstagramUrl = "https://www.instagram.com/zetxtv/";

        private readonly AppSettings _s;
        private readonly IdleManager _idle = new IdleManager();
        private readonly CardFarmEngine _cards;
        private readonly Timer _timer = new Timer { Interval = 1000 };
        private readonly bool _startMinimized;
        private NotifyIcon _tray;
        private bool _reallyClose;
        private bool _trayTipShown;

        // Saat kasma durumu
        private bool _boosting;
        private DateTime _boostStarted;
        private DateTime _lastRestart;
        private DateTime? _restartAt;
        private int _tickCount;
        private bool _steamRunning;

        // Sayfalar
        private readonly Panel _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        private readonly Dictionary<NavButton, Control> _pages = new Dictionary<NavButton, Control>();
        private Label _statusBar;

        // Gösterge
        private StatCard _cSteam, _cUser, _cActive, _cTotal;
        private DarkListView _lvSessions;
        private TextBox _log;
        private HudButton _btnDashStart, _btnDashStop;

        // Saat kasma
        private DarkListView _lvQueue, _lvSearch;
        private TextBox _tbAppId, _tbSearch;
        private NumericUpDown _numStopAfter, _numRestart;
        private HudButton _btnBoostStart, _btnBoostStop;
        private Label _lblBoostInfo;

        // Kart farmı
        private TextBox _tbSteamId, _tbCookie;
        private ComboBox _cbMode;
        private NumericUpDown _numRecheck;
        private DarkListView _lvCards;
        private Label _lblCardSummary;
        private HudButton _btnCardStart, _btnCardStop, _btnCardScan;

        // Kütüphane
        private DarkListView _lvLib;
        private TextBox _tbLibFilter;
        private List<GameInfo> _libAll = new List<GameInfo>();

        // Ayarlar
        private TextBox _tbDll, _tbApiKey;
        private CheckBox _chkStartup, _chkTray, _chkAutoBoost;

        public MainForm(bool startMinimized)
        {
            _startMinimized = startMinimized;
            _s = AppSettings.Load();
            _cards = new CardFarmEngine(_idle, () => _s.SteamApiDllPath);

            Text = Program.AppName + " v" + Program.Version;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1080, 700);
            Size = new Size(1180, 760);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }

            BuildLayout();
            BuildTray();
            LoadSettingsToUi();

            _idle.SessionFailed += (s, code) =>
            {
                if (IsDisposed) return;
                try { BeginInvoke(new Action(() => OnSessionFailed(s, code))); } catch { }
            };
            _cards.Log += AddLog;
            _cards.Updated += RefreshCardList;
            _cards.Finished += () =>
            {
                UpdateCardButtons();
                ShowTip("Kart farmı tamamlandı", "Kalan kart düşüşü yok.");
            };

            _timer.Tick += (o, e) => OnTick();
            _timer.Start();
        }

        // ───────────────────────────── Yerleşim ─────────────────────────────

        private void BuildLayout()
        {
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 210, BackColor = Theme.Panel, Padding = new Padding(5, 0, 5, 10) };
            var logo = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Panel };
            logo.Paint += (o, e) =>
            {
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var f = new Font("Segoe UI Black", 20f))
                using (var b1 = new SolidBrush(Theme.Accent))
                using (var b2 = new SolidBrush(Theme.Accent2))
                {
                    e.Graphics.DrawString("ZETX", f, b1, 12, 18);
                    var w = e.Graphics.MeasureString("ZETX", f).Width;
                    e.Graphics.DrawString("BOOST", f, b2, 12 + w - 8, 18);
                }
                using (var b = new SolidBrush(Theme.Muted))
                    e.Graphics.DrawString("Steam saat & kart booster", Theme.Body, b, 16, 58);
                using (var p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, 10, 88, logo.Width - 10, 88);
            };

            var nav = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Theme.Panel,
                Padding = new Padding(0, 10, 0, 0)
            };

            var sign = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                ForeColor = Theme.Muted,
                Text = "© 2026 " + OwnerName + "\nInstagram " + OwnerInstagram,
                Font = new Font("Segoe UI", 8.25f),
                TextAlign = ContentAlignment.MiddleCenter
            };

            sidebar.Controls.Add(nav);
            sidebar.Controls.Add(sign);
            sidebar.Controls.Add(logo);
            nav.BringToFront();

            AddPage(nav, "⚡  Gösterge", BuildDashboard());
            AddPage(nav, "⏱  Saat Kasma", BuildBoostPage());
            AddPage(nav, "♠  Kart Farmı", BuildCardPage());
            AddPage(nav, "☰  Kütüphane", BuildLibraryPage());
            AddPage(nav, "⚙  Ayarlar", BuildSettingsPage());
            AddPage(nav, "ℹ  Hakkında", BuildAboutPage());

            _statusBar = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                BackColor = Theme.Panel,
                ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Text = "Hazır"
            };

            Controls.Add(_content);
            Controls.Add(sidebar);
            Controls.Add(_statusBar);
            _content.BringToFront();

            ShowPage(_pages.Keys.First());
        }

        private void AddPage(FlowLayoutPanel nav, string title, Control page)
        {
            var b = new NavButton(title);
            b.Click += (o, e) => ShowPage(b);
            nav.Controls.Add(b);
            page.Visible = false;
            page.Dock = DockStyle.Fill;
            _content.Controls.Add(page);
            _pages[b] = page;
        }

        private void ShowPage(NavButton active)
        {
            foreach (var kv in _pages)
            {
                kv.Key.Active = kv.Key == active;
                kv.Value.Visible = kv.Key == active;
            }
            foreach (var lv in _pages[active].Controls.OfType<Control>().SelectMany(Descendants).OfType<DarkListView>())
                lv.FitLastColumn();
        }

        private static IEnumerable<Control> Descendants(Control c)
        {
            yield return c;
            foreach (Control ch in c.Controls)
                foreach (var d in Descendants(ch)) yield return d;
        }

        // ── yardımcılar ──

        private static TableLayoutPanel NewPage()
        {
            return new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 0,
                BackColor = Theme.Bg,
                Padding = new Padding(22, 16, 22, 12)
            };
        }

        private static void AddRow(TableLayoutPanel t, Control c, SizeType st = SizeType.AutoSize, float h = 0)
        {
            t.RowCount++;
            t.RowStyles.Add(st == SizeType.AutoSize ? new RowStyle(SizeType.AutoSize) : new RowStyle(st, h));
            c.Dock = DockStyle.Fill;
            t.Controls.Add(c, 0, t.RowCount - 1);
        }

        private static FlowLayoutPanel Row(params Control[] items)
        {
            var f = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                BackColor = Theme.Bg,
                Margin = new Padding(0, 2, 0, 2)
            };
            f.Controls.AddRange(items);
            return f;
        }

        private static Label Title(string t) => new Label
        {
            Text = t,
            AutoSize = true,
            Font = Theme.H2,
            ForeColor = Theme.Text,
            Margin = new Padding(0, 6, 0, 4)
        };

        private static Label Lbl(string t, bool muted = false) => new Label
        {
            Text = t,
            AutoSize = true,
            ForeColor = muted ? Theme.Muted : Theme.Text,
            Margin = new Padding(0, 9, 6, 0)
        };

        private static TextBox Tb(int width, bool password = false) => new TextBox
        {
            Width = width,
            BackColor = Theme.Panel2,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 5, 8, 5),
            UseSystemPasswordChar = password
        };

        private static NumericUpDown Num(int min, int max, int value) => new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Max(min, Math.Min(max, value)),
            Width = 70,
            BackColor = Theme.Panel2,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 5, 14, 5)
        };

        private static HudButton Btn(string t, Color c, EventHandler click)
        {
            var b = new HudButton(t, c);
            b.Click += click;
            return b;
        }

        private static CheckBox Chk(string t) => new CheckBox
        {
            Text = t,
            AutoSize = true,
            ForeColor = Theme.Text,
            Margin = new Padding(0, 6, 0, 6)
        };

        private static DarkListView Lv(params (string title, int width)[] cols)
        {
            var lv = new DarkListView();
            foreach (var c in cols) lv.Columns.Add(c.title, c.width);
            return lv;
        }

        private static string Fmt(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }

        // ───────────────────────────── Gösterge ─────────────────────────────

        private Control BuildDashboard()
        {
            var t = NewPage();
            AddRow(t, Title("Gösterge Paneli"));

            var cards = new TableLayoutPanel { ColumnCount = 4, RowCount = 1, Height = 92, BackColor = Theme.Bg, Margin = new Padding(0, 4, 0, 4) };
            for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            cards.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            _cSteam = new StatCard("Steam durumu", Theme.Green);
            _cUser = new StatCard("Hesap", Theme.Accent);
            _cActive = new StatCard("Aktif oyun", Theme.Accent2);
            _cTotal = new StatCard("Toplam kasılan süre", Theme.Accent);
            cards.Controls.Add(_cSteam, 0, 0);
            cards.Controls.Add(_cUser, 1, 0);
            cards.Controls.Add(_cActive, 2, 0);
            cards.Controls.Add(_cTotal, 3, 0);
            AddRow(t, cards, SizeType.Absolute, 96);

            _btnDashStart = Btn("▶  Kuyruğu Kasmaya Başla", Theme.Green, (o, e) => StartBoost());
            _btnDashStop = Btn("■  Tümünü Durdur", Theme.Red, (o, e) => StopEverything());
            AddRow(t, Row(_btnDashStart, _btnDashStop,
                Btn("↻  Steam'i Kontrol Et", Theme.Accent, (o, e) => RefreshSteamState(true))));

            AddRow(t, Title("Aktif Oturumlar"));
            _lvSessions = Lv(("AppID", 90), ("Oyun", 380), ("Mod", 120), ("Oturum Süresi", 140));
            AddRow(t, _lvSessions, SizeType.Percent, 55);

            AddRow(t, Title("Olay Günlüğü"));
            _log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.Panel,
                ForeColor = Theme.Muted,
                BorderStyle = BorderStyle.None,
                Font = Theme.Mono
            };
            AddRow(t, _log, SizeType.Percent, 45);
            return t;
        }

        private void AddLog(string msg)
        {
            if (_log == null || IsDisposed) return;
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine;
            if (_log.TextLength > 60000) _log.Clear();
            _log.AppendText(line);
            _statusBar.Text = msg;
        }

        // ───────────────────────────── Saat Kasma ─────────────────────────────

        private Control BuildBoostPage()
        {
            var t = NewPage();
            AddRow(t, Title("Saat Kasma (Hour Boost)"));
            AddRow(t, Lbl("Kuyruktaki oyunlar Steam'de \"oynanıyor\" görünür ve oyun süren artar.\nOyunun yüklü olması gerekmez, kütüphanende olması yeterli. Aynı anda en fazla 32 oyun.", true));

            _tbAppId = Tb(110);
            _tbAppId.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddByAppId(); } };
            _tbSearch = Tb(260);
            _tbSearch.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = SearchStore(); } };
            AddRow(t, Row(Lbl("AppID:"), _tbAppId, Btn("+ Ekle", Theme.Accent, (o, e) => AddByAppId()),
                Lbl("   Oyun ara:"), _tbSearch, Btn("Mağazada Ara", Theme.Accent, (o, e) => _ = SearchStore())));

            var split = new SplitContainer
            {
                Orientation = Orientation.Horizontal,
                BackColor = Theme.Bg,
                SplitterWidth = 6
            };
            split.Panel1.BackColor = Theme.Bg;
            split.Panel2.BackColor = Theme.Bg;

            _lvQueue = Lv(("AppID", 90), ("Oyun", 360), ("Durum", 170), ("Kasılan Süre", 130));
            _lvQueue.MultiSelect = true;
            _lvQueue.KeyDown += (o, e) => { if (e.KeyCode == Keys.Delete) RemoveSelectedFromQueue(); };
            var qTitle = new Label { Text = "BOOST KUYRUĞU", Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Accent2, Font = Theme.Bold };
            split.Panel1.Controls.Add(_lvQueue);
            split.Panel1.Controls.Add(qTitle);
            _lvQueue.BringToFront();

            _lvSearch = Lv(("AppID", 90), ("Arama Sonucu (çift tık = kuyruğa ekle)", 500));
            _lvSearch.DoubleClick += (o, e) => AddSelectedFrom(_lvSearch);
            var sTitle = new Label { Text = "ARAMA SONUÇLARI", Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Accent, Font = Theme.Bold };
            split.Panel2.Controls.Add(_lvSearch);
            split.Panel2.Controls.Add(sTitle);
            _lvSearch.BringToFront();
            AddRow(t, split, SizeType.Percent, 100);
            bool splitSet = false;
            split.SizeChanged += (o, e) =>
            {
                if (splitSet || split.Height < 260) return;
                splitSet = true;
                try
                {
                    split.Panel1MinSize = 110;
                    split.Panel2MinSize = 80;
                    split.SplitterDistance = (int)(split.Height * 0.62);
                }
                catch { }
            };

            _btnBoostStart = Btn("▶  Başlat", Theme.Green, (o, e) => StartBoost());
            _btnBoostStop = Btn("■  Durdur", Theme.Red, (o, e) => StopBoost("Saat kasma durduruldu."));
            AddRow(t, Row(_btnBoostStart, _btnBoostStop,
                Btn("Seçiliyi Kaldır", Theme.Accent2, (o, e) => RemoveSelectedFromQueue()),
                Btn("Kuyruğu Temizle", Theme.Accent2, (o, e) => ClearQueue()),
                Btn("Süreleri Sıfırla", Theme.Muted, (o, e) => ResetTimes())));

            _numStopAfter = Num(0, 100000, _s.StopAfterMinutes);
            _numStopAfter.ValueChanged += (o, e) => { _s.StopAfterMinutes = (int)_numStopAfter.Value; _s.Save(); };
            _numRestart = Num(0, 1440, _s.RestartIntervalMinutes);
            _numRestart.ValueChanged += (o, e) => { _s.RestartIntervalMinutes = (int)_numRestart.Value; _s.Save(); };
            _lblBoostInfo = Lbl("", true);
            AddRow(t, Row(Lbl("Şu kadar dakika sonra durdur (0 = sınırsız):"), _numStopAfter,
                Lbl("Yeniden başlatma aralığı dk (0 = kapalı):"), _numRestart, _lblBoostInfo));
            return t;
        }

        private void RefreshQueueList()
        {
            _lvQueue.BeginUpdate();
            var selected = new HashSet<uint>(_lvQueue.SelectedItems.Cast<ListViewItem>().Select(i => (uint)i.Tag));
            _lvQueue.Items.Clear();
            foreach (var g in _s.Queue)
            {
                var it = new ListViewItem(g.AppId.ToString()) { Tag = g.AppId, ForeColor = Theme.Text, UseItemStyleForSubItems = false };
                it.SubItems.Add(g.Name ?? "").ForeColor = Theme.Text;
                it.SubItems.Add("").ForeColor = Theme.Muted;
                it.SubItems.Add(Fmt(g.BoostedSeconds)).ForeColor = Theme.Accent;
                it.Selected = selected.Contains(g.AppId);
                _lvQueue.Items.Add(it);
            }
            _lvQueue.EndUpdate();
            UpdateQueueStatuses();
        }

        private void UpdateQueueStatuses()
        {
            foreach (ListViewItem it in _lvQueue.Items)
            {
                uint id = (uint)it.Tag;
                var g = _s.Queue.FirstOrDefault(x => x.AppId == id);
                if (g == null) continue;
                bool run = _idle.IsRunning(id);
                string st = run ? "● Kasılıyor" : (_boosting ? "Sırada (32 sınırı)" : "Beklemede");
                if (run && _cards.IsFarming(id)) st = "● Kart farmında";
                var stItem = it.SubItems[2];
                if (stItem.Text != st)
                {
                    stItem.Text = st;
                    stItem.ForeColor = run ? Theme.Green : Theme.Muted;
                }
                string tm = Fmt(g.BoostedSeconds);
                if (it.SubItems[3].Text != tm) it.SubItems[3].Text = tm;
            }
            _lvQueue.Invalidate();
        }

        private void AddToQueue(uint appId, string name)
        {
            if (_s.Queue.Any(q => q.AppId == appId))
            {
                AddLog(name + " zaten kuyrukta.");
                return;
            }
            _s.Queue.Add(new BoostGame { AppId = appId, Name = name });
            _s.Save();
            RefreshQueueList();
            AddLog("Kuyruğa eklendi: " + name + " (" + appId + ")");
            if (_boosting && _idle.Count < IdleManager.SteamMaxGames)
                StartGame(appId, name, IdleOwner.HourBoost);
        }

        private async void AddByAppId()
        {
            string txt = _tbAppId.Text.Trim();
            var m = System.Text.RegularExpressions.Regex.Match(txt, @"(\d{1,10})");
            if (!m.Success || !uint.TryParse(m.Groups[1].Value, out uint id) || id == 0)
            {
                AddLog("Geçerli bir AppID gir (ör. 730) ya da mağaza linkini yapıştır.");
                return;
            }
            _tbAppId.Enabled = false;
            string name = await SteamWeb.GetAppNameAsync(id) ?? ("AppID " + id);
            _tbAppId.Enabled = true;
            _tbAppId.Clear();
            AddToQueue(id, name);
        }

        private async Task SearchStore()
        {
            string term = _tbSearch.Text.Trim();
            if (term.Length < 2) return;
            _lvSearch.Items.Clear();
            _lvSearch.Items.Add(new ListViewItem(new[] { "", "Aranıyor..." }) { ForeColor = Theme.Muted });
            try
            {
                var res = await SteamWeb.SearchStoreAsync(term);
                _lvSearch.Items.Clear();
                foreach (var g in res)
                    _lvSearch.Items.Add(new ListViewItem(new[] { g.AppId.ToString(), g.Name }) { Tag = g, ForeColor = Theme.Text });
                if (res.Count == 0)
                    _lvSearch.Items.Add(new ListViewItem(new[] { "", "Sonuç yok." }) { ForeColor = Theme.Muted });
            }
            catch (Exception ex)
            {
                _lvSearch.Items.Clear();
                AddLog("Mağaza araması başarısız: " + ex.Message);
            }
        }

        private void AddSelectedFrom(ListView lv)
        {
            foreach (ListViewItem it in lv.SelectedItems)
                if (it.Tag is GameInfo g) AddToQueue(g.AppId, g.Name);
        }

        private void RemoveSelectedFromQueue()
        {
            var ids = _lvQueue.SelectedItems.Cast<ListViewItem>().Select(i => (uint)i.Tag).ToList();
            if (ids.Count == 0) return;
            foreach (var id in ids)
            {
                if (_idle.IsRunning(id) && !_cards.IsFarming(id)) _idle.Stop(id);
                _s.Queue.RemoveAll(q => q.AppId == id);
            }
            _s.Save();
            RefreshQueueList();
        }

        private void ClearQueue()
        {
            if (_s.Queue.Count == 0) return;
            if (MessageBox.Show("Kuyruktaki tüm oyunlar silinsin mi?", Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            StopBoost(null);
            _s.Queue.Clear();
            _s.Save();
            RefreshQueueList();
        }

        private void ResetTimes()
        {
            foreach (var g in _s.Queue) g.BoostedSeconds = 0;
            _s.Save();
            RefreshQueueList();
        }

        private bool EnsureReady()
        {
            if (string.IsNullOrEmpty(_s.SteamApiDllPath) || !File.Exists(_s.SteamApiDllPath))
            {
                AddLog("steam_api64.dll aranıyor...");
                AutoFindDll(false);
                if (string.IsNullOrEmpty(_s.SteamApiDllPath) || !File.Exists(_s.SteamApiDllPath))
                {
                    MessageBox.Show("steam_api64.dll bulunamadı.\n\nAyarlar sekmesinden \"Gözat\" ile herhangi bir Steam oyununun klasöründeki steam_api64.dll dosyasını seç.",
                        Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            RefreshSteamState(false);
            if (!_steamRunning)
            {
                MessageBox.Show("Steam açık değil. Önce Steam'i aç ve hesabına giriş yap.", Program.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private bool StartGame(uint id, string name, IdleOwner owner)
        {
            if (_idle.Start(id, name, _s.SteamApiDllPath, owner, out string err)) return true;
            AddLog(name + ": " + err);
            return false;
        }

        private void StartBoost()
        {
            if (_s.Queue.Count == 0)
            {
                AddLog("Kuyruk boş. Saat Kasma sekmesinden oyun ekle.");
                ShowPage(_pages.Keys.ElementAt(1));
                return;
            }
            if (_cards.Running)
            {
                if (MessageBox.Show("Kart farmı çalışıyor. Durdurup saat kasmaya geçilsin mi?", Program.AppName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                _cards.Stop();
                UpdateCardButtons();
            }
            if (!EnsureReady()) return;

            int started = 0;
            foreach (var g in _s.Queue)
            {
                if (_idle.Count >= IdleManager.SteamMaxGames) break;
                if (StartGame(g.AppId, g.Name, IdleOwner.HourBoost)) started++;
            }
            _boosting = true;
            _boostStarted = DateTime.Now;
            _lastRestart = DateTime.Now;
            _restartAt = null;
            AddLog("Saat kasma başladı: " + started + " oyun.");
            if (_s.Queue.Count > IdleManager.SteamMaxGames)
                AddLog("Not: Steam sınırı nedeniyle yalnızca ilk 32 oyun kasılıyor.");
            UpdateBoostButtons();
            UpdateQueueStatuses();
        }

        private void StopBoost(string message)
        {
            _idle.StopAll(IdleOwner.HourBoost);
            _boosting = false;
            _restartAt = null;
            _s.Save();
            if (message != null) AddLog(message);
            UpdateBoostButtons();
            UpdateQueueStatuses();
        }

        private void StopEverything()
        {
            Cursor = Cursors.WaitCursor;
            StopBoost(null);
            _cards.Stop(true);
            Cursor = Cursors.Default;
            UpdateCardButtons();
            AddLog("Tüm oturumlar düzgün kapatıldı, süreler Steam'e kaydedildi.");
        }

        private void UpdateBoostButtons()
        {
            _btnBoostStart.Enabled = !_boosting;
            _btnBoostStop.Enabled = _boosting;
            _btnDashStart.Enabled = !_boosting;
        }

        private void OnSessionFailed(IdleSession s, int code)
        {
            AddLog(s.Name + " kapandı: " + IdleWorker.DescribeExitCode(code));
            if (s.Owner == IdleOwner.CardFarm) _cards.OnSessionFailed(s.AppId);
            UpdateQueueStatuses();
        }

        // ───────────────────────────── Kart Farmı ─────────────────────────────

        private Control BuildCardPage()
        {
            var t = NewPage();
            AddRow(t, Title("Kart Farmı (Trading Cards)"));
            AddRow(t, Lbl("Rozet sayfanı tarar, kart düşüşü kalan oyunları otomatik kasar, kartı biten oyundan sonrakine geçer.\nBunun için Steam oturum çerezi (steamLoginSecure) gerekir.", true));

            _tbSteamId = Tb(170);
            _tbCookie = Tb(330, true);
            var show = Chk("Göster");
            show.CheckedChanged += (o, e) => _tbCookie.UseSystemPasswordChar = !show.Checked;
            var help = new LinkLabel
            {
                Text = "Çerez nasıl alınır?",
                AutoSize = true,
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.Accent2,
                Margin = new Padding(6, 9, 0, 0)
            };
            help.LinkClicked += (o, e) => MessageBox.Show(
                "1) Tarayıcıda steamcommunity.com adresine Steam hesabınla giriş yap.\n" +
                "2) F12 ile Geliştirici Araçları'nı aç.\n" +
                "3) Chrome/Edge: Application (Uygulama) > Cookies > https://steamcommunity.com\n" +
                "   Firefox: Storage (Depolama) > Cookies\n" +
                "4) 'steamLoginSecure' satırının Value (Değer) kısmını kopyala ve buraya yapıştır.\n\n" +
                "Bu değer şifren gibidir, kimseyle paylaşma. ZETX Boost onu yalnızca bu bilgisayarda,\nWindows hesabına özel şifreleyerek saklar ve sadece steamcommunity.com'a gönderir.",
                "steamLoginSecure çerezi", MessageBoxButtons.OK, MessageBoxIcon.Information);

            AddRow(t, Row(Lbl("SteamID64:"), _tbSteamId, Lbl("steamLoginSecure:"), _tbCookie, show, help));

            _cbMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Panel2,
                ForeColor = Theme.Text,
                Width = 300,
                Margin = new Padding(0, 5, 14, 5)
            };
            _cbMode.Items.AddRange(new object[]
            {
                "Akıllı (2 saat altı birlikte, sonra tek tek)",
                "Hepsi birden (32'ye kadar)",
                "Tek tek (sırayla)"
            });
            _cbMode.SelectedIndex = Math.Max(0, Math.Min(2, _s.CardMode));
            _cbMode.SelectedIndexChanged += (o, e) => { _s.CardMode = _cbMode.SelectedIndex; _cards.Mode = _s.CardMode; _s.Save(); };
            _numRecheck = Num(1, 120, _s.CardRecheckMinutes);
            _numRecheck.ValueChanged += (o, e) => { _s.CardRecheckMinutes = (int)_numRecheck.Value; _cards.RecheckMinutes = _s.CardRecheckMinutes; _s.Save(); };
            AddRow(t, Row(Lbl("Mod:"), _cbMode, Lbl("Kontrol aralığı (dk):"), _numRecheck));

            _btnCardScan = Btn("Rozetleri Tara", Theme.Accent, async (o, e) => await ScanCards());
            _btnCardStart = Btn("▶  Farmı Başlat", Theme.Green, async (o, e) => await StartCards());
            _btnCardStop = Btn("■  Durdur", Theme.Red, (o, e) => { _cards.Stop(); UpdateCardButtons(); });
            AddRow(t, Row(_btnCardScan, _btnCardStart, _btnCardStop));

            _lvCards = Lv(("AppID", 90), ("Oyun", 360), ("Kalan Kart", 110), ("Oynanma (saat)", 130), ("Durum", 150));
            AddRow(t, _lvCards, SizeType.Percent, 100);
            _lblCardSummary = Lbl("Henüz tarama yapılmadı.", true);
            AddRow(t, _lblCardSummary);
            return t;
        }

        private bool ReadCardCredentials(out string sid, out string cookie)
        {
            cookie = SteamWeb.NormalizeCookie(_tbCookie.Text);
            sid = _tbSteamId.Text.Trim();
            if (string.IsNullOrEmpty(sid)) sid = SteamWeb.SteamIdFromCookie(cookie) ?? SteamLocator.GetActiveSteamId64() ?? "";
            _tbSteamId.Text = sid;
            if (cookie.Length < 20 || sid.Length != 17)
            {
                MessageBox.Show("SteamID64 (17 haneli) ve steamLoginSecure çerezi gerekli.\n\"Çerez nasıl alınır?\" bağlantısına bak.",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            _s.SteamId64 = sid;
            _s.SetCookie(cookie);
            _s.Save();
            return true;
        }

        private async Task ScanCards()
        {
            if (!ReadCardCredentials(out string sid, out string cookie)) return;
            _btnCardScan.Enabled = false;
            _lblCardSummary.Text = "Rozet sayfaları taranıyor...";
            bool ok = await _cards.ScanOnlyAsync(sid, cookie);
            _btnCardScan.Enabled = true;
            RefreshCardList();
            if (!ok)
            {
                _lblCardSummary.Text = "Tarama başarısız: " + _cards.LastError;
                MessageBox.Show("Rozet taraması başarısız:\n\n" + _cards.LastError, Program.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task StartCards()
        {
            if (!ReadCardCredentials(out string sid, out string cookie)) return;
            if (_boosting)
            {
                if (MessageBox.Show("Saat kasma çalışıyor. Durdurup kart farmına geçilsin mi?", Program.AppName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                StopBoost("Saat kasma durduruldu (kart farmına geçildi).");
            }
            if (!EnsureReady()) return;
            _cards.Mode = _cbMode.SelectedIndex;
            _cards.RecheckMinutes = (int)_numRecheck.Value;
            _btnCardStart.Enabled = false;
            await _cards.StartAsync(sid, cookie);
            UpdateCardButtons();
        }

        private void UpdateCardButtons()
        {
            if (_btnCardStart == null) return;
            _btnCardStart.Enabled = !_cards.Running;
            _btnCardStop.Enabled = _cards.Running;
        }

        private void RefreshCardList()
        {
            if (_lvCards == null || IsDisposed) return;
            _lvCards.BeginUpdate();
            _lvCards.Items.Clear();
            foreach (var g in _cards.Games)
            {
                bool farm = _cards.IsFarming(g.AppId) && _idle.IsRunning(g.AppId);
                var it = new ListViewItem(g.AppId.ToString()) { ForeColor = Theme.Text, UseItemStyleForSubItems = false };
                it.SubItems.Add(g.Name).ForeColor = Theme.Text;
                it.SubItems.Add(g.DropsLeft.ToString()).ForeColor = Theme.Accent2;
                it.SubItems.Add(g.Hours.ToString("0.0")).ForeColor = Theme.Text;
                it.SubItems.Add(farm ? "● Kasılıyor" : "Bekliyor").ForeColor = farm ? Theme.Green : Theme.Muted;
                _lvCards.Items.Add(it);
            }
            _lvCards.EndUpdate();
            int total = _cards.Games.Sum(g => g.DropsLeft);
            _lblCardSummary.Text = _cards.Games.Count + " oyun · " + total + " kart düşüşü kaldı" +
                                   (_cards.Running ? " · sonraki kontrol " + _cards.NextCheck.ToString("HH:mm") : "");
            UpdateCardButtons();
        }

        // ───────────────────────────── Kütüphane ─────────────────────────────

        private Control BuildLibraryPage()
        {
            var t = NewPage();
            AddRow(t, Title("Oyun Kütüphanesi"));
            AddRow(t, Lbl("Yüklü oyunlar bilgisayarından okunur. Tüm kütüphane için Ayarlar'a Steam Web API anahtarı gir.\nÇift tık ya da seçip \"Kuyruğa Ekle\" ile saat kasma kuyruğuna gönder.", true));

            _tbLibFilter = Tb(240);
            _tbLibFilter.TextChanged += (o, e) => FillLibrary();
            AddRow(t, Row(
                Btn("Yüklü Oyunlar", Theme.Accent, (o, e) => LoadInstalled()),
                Btn("Tüm Kütüphane (Web API)", Theme.Accent, async (o, e) => await LoadOwned()),
                Lbl("   Filtre:"), _tbLibFilter,
                Btn("+  Seçilileri Kuyruğa Ekle", Theme.Green, (o, e) => AddSelectedFrom(_lvLib))));

            _lvLib = Lv(("AppID", 90), ("Oyun", 460), ("Bilgi", 140));
            _lvLib.DoubleClick += (o, e) => AddSelectedFrom(_lvLib);
            AddRow(t, _lvLib, SizeType.Percent, 100);
            return t;
        }

        private void LoadInstalled()
        {
            _libAll = SteamLocator.GetInstalledGames();
            FillLibrary();
            AddLog("Yüklü oyunlar: " + _libAll.Count);
        }

        private async Task LoadOwned()
        {
            string key = _s.WebApiKey?.Trim() ?? "";
            string sid = !string.IsNullOrEmpty(_s.SteamId64) ? _s.SteamId64 : SteamLocator.GetActiveSteamId64();
            if (key.Length < 20 || string.IsNullOrEmpty(sid))
            {
                MessageBox.Show("Ayarlar sekmesine Steam Web API anahtarını gir.\nSteamID otomatik algılanamazsa Kart Farmı sekmesine SteamID64 yaz.",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                AddLog("Kütüphane Steam'den alınıyor...");
                _libAll = await SteamWeb.GetOwnedGamesAsync(key, sid);
                FillLibrary();
                AddLog("Kütüphanede " + _libAll.Count + " oyun bulundu.");
            }
            catch (Exception ex)
            {
                AddLog("Kütüphane alınamadı: " + ex.Message);
            }
        }

        private void FillLibrary()
        {
            string f = _tbLibFilter.Text.Trim();
            _lvLib.BeginUpdate();
            _lvLib.Items.Clear();
            foreach (var g in _libAll)
            {
                if (f.Length > 0 && g.Name.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) < 0 && g.AppId.ToString() != f) continue;
                _lvLib.Items.Add(new ListViewItem(new[] { g.AppId.ToString(), g.Name, g.Extra ?? "" }) { Tag = g, ForeColor = Theme.Text });
            }
            _lvLib.EndUpdate();
        }

        // ───────────────────────────── Ayarlar ─────────────────────────────

        private Control BuildSettingsPage()
        {
            var t = NewPage();
            AddRow(t, Title("Ayarlar"));

            AddRow(t, Lbl("steam_api64.dll (Steam bağlantısı için gerekli, herhangi bir Steam oyununun klasöründe bulunur):"));
            _tbDll = Tb(560);
            _tbDll.ReadOnly = true;
            AddRow(t, Row(_tbDll,
                Btn("Otomatik Bul", Theme.Accent, (o, e) => AutoFindDll(true)),
                Btn("Gözat...", Theme.Accent, (o, e) => BrowseDll())));

            AddRow(t, Lbl("Steam Web API anahtarı (isteğe bağlı, tüm kütüphaneyi listelemek için):"));
            _tbApiKey = Tb(330, true);
            var apiLink = new LinkLabel
            {
                Text = "Anahtar al: steamcommunity.com/dev/apikey",
                AutoSize = true,
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.Accent2,
                Margin = new Padding(4, 9, 0, 0)
            };
            apiLink.LinkClicked += (o, e) => OpenUrl("https://steamcommunity.com/dev/apikey");
            AddRow(t, Row(_tbApiKey, apiLink));

            AddRow(t, Title("Davranış"));
            _chkStartup = Chk("Windows açılınca ZETX Boost'u başlat (tepside)");
            _chkTray = Chk("Simge durumuna küçültünce sistem tepsisine gizle");
            _chkAutoBoost = Chk("Program açılınca kuyruktaki oyunları otomatik kas");
            AddRow(t, _chkStartup);
            AddRow(t, _chkTray);
            AddRow(t, _chkAutoBoost);

            AddRow(t, Row(Btn("Ayarları Kaydet", Theme.Green, (o, e) => SaveSettingsFromUi(true)),
                Btn("Ayar Klasörünü Aç", Theme.Muted, (o, e) => { Directory.CreateDirectory(AppSettings.Folder); OpenUrl(AppSettings.Folder); })));

            var spacer = new Panel { BackColor = Theme.Bg };
            AddRow(t, spacer, SizeType.Percent, 100);
            return t;
        }

        private void LoadSettingsToUi()
        {
            _tbDll.Text = _s.SteamApiDllPath;
            _tbApiKey.Text = _s.WebApiKey;
            _chkStartup.Checked = _s.StartWithWindows;
            _chkTray.Checked = _s.MinimizeToTray;
            _chkAutoBoost.Checked = _s.AutoStartBoost;
            _tbSteamId.Text = !string.IsNullOrEmpty(_s.SteamId64) ? _s.SteamId64 : (SteamLocator.GetActiveSteamId64() ?? "");
            _tbCookie.Text = _s.GetCookie();
            _cards.Mode = _s.CardMode;
            _cards.RecheckMinutes = _s.CardRecheckMinutes;
            RefreshQueueList();
            UpdateBoostButtons();
            UpdateCardButtons();
        }

        private void SaveSettingsFromUi(bool notify)
        {
            _s.WebApiKey = _tbApiKey.Text.Trim();
            _s.StartWithWindows = _chkStartup.Checked;
            _s.MinimizeToTray = _chkTray.Checked;
            _s.AutoStartBoost = _chkAutoBoost.Checked;
            AppSettings.ApplyStartup(_s.StartWithWindows);
            _s.Save();
            if (notify) AddLog("Ayarlar kaydedildi.");
        }

        private void AutoFindDll(bool notify)
        {
            Cursor = Cursors.WaitCursor;
            string found = null;
            try { found = SteamLocator.FindSteamApiDll(); } catch { }
            Cursor = Cursors.Default;
            if (found == null)
            {
                if (notify) MessageBox.Show("Yüklü oyunlarda steam_api64.dll bulunamadı. \"Gözat\" ile elle seç.", Program.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            UseDll(found);
        }

        private void BrowseDll()
        {
            using (var d = new OpenFileDialog { Filter = "steam_api64.dll|steam_api64.dll|DLL (*.dll)|*.dll", Title = "steam_api64.dll seç" })
            {
                string steam = SteamLocator.GetSteamPath();
                if (steam != null) d.InitialDirectory = Path.Combine(steam, "steamapps", "common");
                if (d.ShowDialog(this) == DialogResult.OK) UseDll(d.FileName);
            }
        }

        private void UseDll(string source)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Folder);
                string dest = Path.Combine(AppSettings.Folder, "steam_api64.dll");
                if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                {
                    if (_idle.Count > 0)
                    {
                        AddLog("DLL değiştirmek için önce tüm oturumları durdur.");
                        return;
                    }
                    File.Copy(source, dest, true);
                }
                _s.SteamApiDllPath = dest;
                _s.Save();
                _tbDll.Text = dest;
                AddLog("steam_api64.dll hazır (" + source + ")");
            }
            catch (Exception ex)
            {
                AddLog("DLL kopyalanamadı: " + ex.Message);
            }
        }

        // ───────────────────────────── Hakkında ─────────────────────────────

        private Control BuildAboutPage()
        {
            var t = NewPage();

            var head = new Panel { Height = 120, BackColor = Theme.Bg };
            head.Paint += (o, e) =>
            {
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var f = new Font("Segoe UI Black", 34f))
                using (var b1 = new SolidBrush(Theme.Accent))
                using (var b2 = new SolidBrush(Theme.Accent2))
                {
                    e.Graphics.DrawString("ZETX", f, b1, 0, 6);
                    float w = e.Graphics.MeasureString("ZETX", f).Width;
                    e.Graphics.DrawString("BOOST", f, b2, w - 14, 6);
                }
                using (var b = new SolidBrush(Theme.Muted))
                    e.Graphics.DrawString("Sürüm " + Program.Version + "  ·  Steam saat kasma ve kart farmı aracı", Theme.Body, b, 6, 78);
            };
            AddRow(t, head, SizeType.Absolute, 120);

            var box = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Panel,
                Padding = new Padding(18, 14, 18, 14),
                Margin = new Padding(0, 6, 0, 10)
            };
            box.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            box.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            void Pair(string k, Control v)
            {
                var kl = new Label { Text = k, AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Bold, Margin = new Padding(0, 6, 0, 6) };
                v.Margin = new Padding(0, 6, 0, 6);
                box.RowCount++;
                box.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                box.Controls.Add(kl, 0, box.RowCount - 1);
                box.Controls.Add(v, 1, box.RowCount - 1);
            }

            Pair("Program Sahibi", new Label { Text = OwnerName, AutoSize = true, ForeColor = Theme.Text, Font = Theme.H2 });

            var mail = new LinkLabel { Text = OwnerMail, AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent2, Font = Theme.Bold };
            mail.LinkClicked += (o, e) => OpenUrl("mailto:" + OwnerMail + "?subject=" + Uri.EscapeDataString("ZETX Boost"));
            Pair("E-posta", mail);

            var insta = new LinkLabel { Text = "instagram.com/zetxtv  (" + OwnerInstagram + ")", AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent2, Font = Theme.Bold };
            insta.LinkClicked += (o, e) => OpenUrl(OwnerInstagramUrl);
            Pair("Instagram", insta);

            Pair("Sürüm", new Label { Text = Program.Version, AutoSize = true, ForeColor = Theme.Text });
            Pair("Ayar klasörü", new Label { Text = AppSettings.Folder, AutoSize = true, ForeColor = Theme.Text });
            AddRow(t, box);

            AddRow(t, Title("Özellikler"));
            AddRow(t, Lbl(
                "• Aynı anda 32 oyuna kadar saat kasma (oyunun yüklü olması gerekmez)\n" +
                "• Akıllı kart farmı: rozet sayfası taraması, 2 saat kuralı, otomatik oyun değiştirme\n" +
                "• Süre sınırı ve periyodik yeniden başlatma\n" +
                "• Mağaza araması, yüklü oyunlar ve Web API ile tüm kütüphane\n" +
                "• Steam açıkken normal oynamaya ve sohbete devam edebilirsin\n" +
                "• Sistem tepsisi, Windows ile başlama, oturum çerezi DPAPI ile şifreli"));

            AddRow(t, Title("Uyarı"));
            AddRow(t, Lbl(
                "ZETX Boost bağımsız bir araçtır; Valve Corporation veya Steam ile bağlantılı değildir.\n" +
                "Steam ve Steam logosu Valve Corporation'ın ticari markalarıdır. Kullanımdan doğacak sorumluluk kullanıcıya aittir.", true));

            AddRow(t, new Panel { BackColor = Theme.Bg }, SizeType.Percent, 100);
            return t;
        }

        private void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { AddLog("Açılamadı: " + ex.Message); }
        }

        // ───────────────────────────── Tepsi ─────────────────────────────

        private void BuildTray()
        {
            var menu = new ContextMenuStrip { BackColor = Theme.Panel2, ForeColor = Theme.Text, ShowImageMargin = false };
            menu.Items.Add("ZETX Boost'u Göster", null, (o, e) => RestoreFromTray());
            menu.Items.Add("Kuyruğu Kasmaya Başla", null, (o, e) => { RestoreFromTray(); StartBoost(); });
            menu.Items.Add("Tümünü Durdur", null, (o, e) => StopEverything());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Çıkış", null, (o, e) => { _reallyClose = true; Close(); });

            _tray = new NotifyIcon
            {
                Icon = Icon ?? SystemIcons.Application,
                Text = Program.AppName,
                Visible = true,
                ContextMenuStrip = menu
            };
            _tray.DoubleClick += (o, e) => RestoreFromTray();
        }

        private void RestoreFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
        }

        private void ShowTip(string title, string text)
        {
            try { _tray.ShowBalloonTip(4000, title, text, ToolTipIcon.Info); } catch { }
        }

        // ───────────────────────────── Döngü ─────────────────────────────

        private void RefreshSteamState(bool log)
        {
            _steamRunning = SteamLocator.IsSteamRunning();
            _cSteam.Value.Text = _steamRunning ? "● Açık" : "○ Kapalı";
            _cSteam.Value.ForeColor = _steamRunning ? Theme.Green : Theme.Red;
            string acc = SteamLocator.GetAccountName();
            string sid = SteamLocator.GetActiveSteamId64();
            _cUser.Value.Text = !string.IsNullOrEmpty(acc) ? acc : (sid ?? "Giriş yok");
            if (log) AddLog("Steam " + (_steamRunning ? "açık" : "kapalı") + (sid != null ? " · SteamID " + sid : ""));
        }

        private void OnTick()
        {
            _tickCount++;
            if (_tickCount % 5 == 1) RefreshSteamState(false);

            var sessions = _idle.Snapshot();
            // Süre muhasebesi
            if (sessions.Count > 0) _s.TotalBoostedSeconds += sessions.Count;
            foreach (var s in sessions)
            {
                if (s.Owner != IdleOwner.HourBoost) continue;
                var g = _s.Queue.FirstOrDefault(q => q.AppId == s.AppId);
                if (g != null) g.BoostedSeconds += 1;
            }

            // Saat kasma: süre sınırı ve yeniden başlatma
            if (_boosting)
            {
                double elapsedMin = (DateTime.Now - _boostStarted).TotalMinutes;
                _lblBoostInfo.Text = "Oturum: " + Fmt((DateTime.Now - _boostStarted).TotalSeconds);
                if (_s.StopAfterMinutes > 0 && elapsedMin >= _s.StopAfterMinutes)
                {
                    StopBoost("Süre sınırına ulaşıldı (" + _s.StopAfterMinutes + " dk), saat kasma durdu.");
                    ShowTip("ZETX Boost", "Süre sınırı doldu, kasma durduruldu.");
                }
                else if (_restartAt.HasValue)
                {
                    if (DateTime.Now >= _restartAt.Value)
                    {
                        _restartAt = null;
                        foreach (var g in _s.Queue)
                        {
                            if (_idle.Count >= IdleManager.SteamMaxGames) break;
                            if (!_idle.IsRunning(g.AppId)) StartGame(g.AppId, g.Name, IdleOwner.HourBoost);
                        }
                        _lastRestart = DateTime.Now;
                        AddLog("Oyunlar yeniden başlatıldı.");
                    }
                }
                else if (_s.RestartIntervalMinutes > 0 && (DateTime.Now - _lastRestart).TotalMinutes >= _s.RestartIntervalMinutes)
                {
                    _idle.StopAll(IdleOwner.HourBoost);
                    _restartAt = DateTime.Now.AddSeconds(5);
                    AddLog("Planlı yeniden başlatma (5 sn)...");
                }
            }
            else _lblBoostInfo.Text = "";

            _cards.Tick();

            // Gösterge
            _cActive.Value.Text = sessions.Count + " / " + IdleManager.SteamMaxGames;
            _cTotal.Value.Text = Fmt(_s.TotalBoostedSeconds);
            SyncSessionList(sessions);
            if (_lvQueue.Visible || _tickCount % 5 == 0) UpdateQueueStatuses();

            string tip = Program.AppName + " · " + sessions.Count + " oyun kasılıyor";
            if (_tray != null && _tray.Text != tip) _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            if (_tickCount % 60 == 0) _s.Save();
        }

        private void SyncSessionList(List<IdleSession> sessions)
        {
            var ids = new HashSet<uint>(sessions.Select(s => s.AppId));
            for (int i = _lvSessions.Items.Count - 1; i >= 0; i--)
                if (!ids.Contains((uint)_lvSessions.Items[i].Tag)) _lvSessions.Items.RemoveAt(i);

            foreach (var s in sessions)
            {
                ListViewItem it = null;
                foreach (ListViewItem x in _lvSessions.Items)
                    if ((uint)x.Tag == s.AppId) { it = x; break; }
                string mode = s.Owner == IdleOwner.CardFarm ? "Kart Farmı" : "Saat Kasma";
                string dur = Fmt((DateTime.Now - s.Started).TotalSeconds);
                if (it == null)
                {
                    it = new ListViewItem(s.AppId.ToString()) { Tag = s.AppId, ForeColor = Theme.Text, UseItemStyleForSubItems = false };
                    it.SubItems.Add(s.Name ?? "").ForeColor = Theme.Text;
                    it.SubItems.Add(mode).ForeColor = s.Owner == IdleOwner.CardFarm ? Theme.Accent2 : Theme.Accent;
                    it.SubItems.Add(dur).ForeColor = Theme.Green;
                    _lvSessions.Items.Add(it);
                }
                else if (it.SubItems[3].Text != dur)
                {
                    it.SubItems[3].Text = dur;
                }
            }
            _lvSessions.Invalidate();
        }

        // ───────────────────────────── Form olayları ─────────────────────────────

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshSteamState(false);
            AddLog("ZETX Boost hazır. Steam " + (_steamRunning ? "açık." : "kapalı — kasmak için Steam'i aç."));
            if (string.IsNullOrEmpty(_s.SteamApiDllPath) || !File.Exists(_s.SteamApiDllPath))
            {
                AutoFindDll(false);
                if (string.IsNullOrEmpty(_s.SteamApiDllPath))
                    AddLog("steam_api64.dll bulunamadı. Ayarlar > Gözat ile seç.");
            }
            if (_startMinimized && _s.MinimizeToTray)
            {
                BeginInvoke(new Action(() => { Hide(); ShowInTaskbar = false; }));
            }
            if (_s.AutoStartBoost && _s.Queue.Count > 0)
            {
                if (_steamRunning) StartBoost();
                else AddLog("Otomatik kasma: Steam kapalı olduğu için başlatılmadı.");
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && _s != null && _s.MinimizeToTray && _tray != null)
            {
                Hide();
                ShowInTaskbar = false;
                if (!_trayTipShown)
                {
                    _trayTipShown = true;
                    ShowTip("ZETX Boost tepside çalışıyor", "Açmak için simgeye çift tıkla.");
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_reallyClose && e.CloseReason == CloseReason.UserClosing && _idle.Count > 0)
            {
                var r = MessageBox.Show("Kasılan oyunlar var.\n\nEvet: tümünü durdur ve çık\nHayır: tepsiye gizle, kasmaya devam et",
                    Program.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.No)
                {
                    e.Cancel = true;
                    Hide();
                    ShowInTaskbar = false;
                    return;
                }
            }
            _timer.Stop();
            _cards.Stop(true);
            _idle.StopAll();
            SaveSettingsFromUi(false);
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            base.OnFormClosing(e);
        }
    }
}
