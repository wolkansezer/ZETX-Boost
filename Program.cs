using System;
using System.Threading;
using System.Windows.Forms;

namespace ZETXBoost
{
    internal static class Program
    {
        public const string AppName = "ZETX Boost";
        public const string Version = "1.0.0";

        [STAThread]
        private static int Main(string[] args)
        {
            // Alt işlem modu: tek bir oyunu Steam'de "oynuyor" gösterir.
            if (args.Length >= 1 && args[0] == "--idle")
                return IdleWorker.Run(args);

            bool created;
            using (var mutex = new Mutex(true, "ZETXBoost_SingleInstance_Mutex", out created))
            {
                if (!created)
                {
                    MessageBox.Show("ZETX Boost zaten çalışıyor (sistem tepsisine bak).", AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool minimized = Array.Exists(args, a => a == "--minimized");
                Application.Run(new MainForm(minimized));
            }
            return 0;
        }
    }
}
