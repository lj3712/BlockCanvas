// Program.cs — adds panning/scrolling (middle-drag or Space+left) + wheel scroll.
// Also keeps prior features: typed/dynamic ports, grouping, hierarchy, save/load.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace BlockCanvas
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            ApplicationConfiguration.Initialize();
            Application.Run(new MainWindow());
        }
    }
}
