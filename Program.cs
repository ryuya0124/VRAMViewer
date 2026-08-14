using System;
using System.Windows.Forms;
using VramMonitor.Forms;

namespace VramMonitor
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
