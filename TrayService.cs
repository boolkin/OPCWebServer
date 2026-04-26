using System;
using System.Drawing;
using System.Windows.Forms;

namespace OPCWebServer
{
    public class TrayService : IDisposable
    {
        private NotifyIcon _trayIcon;
        private ToolStripMenuItem _startStopItem;
        private Action _onToggleServer;
        private Action _onShowWindow;

        public TrayService(string appName, Action onToggle, Action onShow)
        {
            _onToggleServer = onToggle;
            _onShowWindow = onShow;

            var menu = new ContextMenuStrip();
            // 1. Создаем пункт с названием агрегата из конфига
            var titleItem = new ToolStripMenuItem(appName) 
            { 
                Enabled = false, // Делаем его серым/неактивным (просто текст)
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) // Выделяем жирным
            };
            menu.Items.Add(titleItem);

            // 2. Добавляем разделитель под названием
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Показать", null, (s, e) => _onShowWindow());
            
            _startStopItem = new ToolStripMenuItem("Старт") { ForeColor = Color.Green };
            _startStopItem.Click += (s, e) => _onToggleServer();
            menu.Items.Add(_startStopItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, (s, e) => Application.Exit());

            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Error,
                ContextMenuStrip = menu,
                Visible = true,
                Text = "OPCWebServer"
            };
            _trayIcon.DoubleClick += (s, e) => _onShowWindow();
        }

        public void UpdateStatus(bool isRunning)
        {
            _trayIcon.Icon = isRunning ? SystemIcons.Shield : SystemIcons.Error;
            _startStopItem.Text = isRunning ? "Стоп" : "Старт";
            _startStopItem.ForeColor = isRunning ? Color.Red : Color.Green;
        }

        public void Dispose() => _trayIcon.Dispose();
    }
}
