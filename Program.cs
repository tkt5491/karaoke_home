using System;
using System.Text;
using System.Windows.Forms;
using LibVLCSharp.Shared;

namespace karaoke_home
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }  // log tiếng Việt + emoji không thành "?"
            Core.Initialize();                                         // nạp thư viện native VLC — phải trước mọi new LibVLC()

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}