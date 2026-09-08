using System;
using System.Windows.Forms;

namespace Jarvis
{
    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "witnesses")));
        }
    }
}
