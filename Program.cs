using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace OPCWebServer
{
    public class MainForm : Form
    {
        private AppConfig config;
        private OpcService opcService = new OpcService();
        private ConfigService configService = new ConfigService();
        private TrayService trayService = null;
        private TagManager tagManager = null;
        private BindingSource tagBindingSource = new BindingSource();
        private bool isRunning = false;
        private bool isLoggingEnabled = false;

        private TextBox txtOpcServer = null, txtUdpIp = null, txtTagFilter = null, txtAppName = null;
        private TextBox txtLog = null;

        private NumericUpDown numRefresh = null, numPort = null, numUdpPort = null;
        private CheckBox cbWebEnabled = null, cbUdpEnabled = null;
        private ListBox lbAvailableTags = null;
        private Button btnStart = null, btnStop = null;

        private DataPollingService _polling;
        private UdpService _udpService;
        private WebService _webService;
        private Button btnStartWebOnly = null;

        public MainForm()
        {
            this.Size = new Size(900, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            config = configService.Load(); // Загружаем настройки
            InitInterface();
            this.Text = $"{config.OpcSettings.AppName} — OPCWebServer";
            // Инициализируем трей, передавая действия (Actions)
            trayService = new TrayService(config.OpcSettings.AppName, ToggleServer, ShowWindow);

            this.FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; this.Hide(); }
            };
            ToggleServer();
        }

        private void InitInterface()
        {
            var panelTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(5), BackColor = Color.FromArgb(235, 235, 235) };
            var btnLoad = new Button { Text = "Импорт", Width = 90, Height = 30 };
            var btnSave = new Button { Text = "Сохранить", Width = 90, Height = 30 };

            btnStart = new Button { Text = "СТАРТ", Width = 90, Height = 30 };
            btnStop = new Button { Text = "СТОП", Width = 90, Height = 30 };
            btnStart.Click += (s, e) => { if (!isRunning) ToggleServer(); };
            btnStop.Click += (s, e) => { if (isRunning) ToggleServer(); };

            btnSave.Click += (s, e) => { SyncConfigFromUi(); configService.Save(config); if (isRunning) ToggleServer(); MessageBox.Show("Сохранено"); };
            btnLoad.Click += (s, e) =>
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "JSON files (*.json)|*.json";
                    ofd.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    ofd.Title = "Выберите файл для импорта";

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        // Используем новый метод для загрузки выбранного файла
                        config = configService.LoadFromFile(ofd.FileName);

                        // Обновляем UI, заголовок окна и трей
                        UpdateUiFromConfig();
                        this.Text = $"{config.OpcSettings.AppName} — OPC-Web-Server";

                        MessageBox.Show($"Настройки импортированы из {Path.GetFileName(ofd.FileName)}");
                    }
                }
            };
            panelTop.Controls.AddRange(new Control[] { btnLoad, btnSave, btnStart, btnStop });

            var tabs = new TabControl { Dock = DockStyle.Fill };

            // --- ВКЛАДКА НАСТРОЙКИ ---
            var tabSettings = new TabPage("Настройки");
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(20) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

            txtOpcServer = new TextBox { Width = 300 };
            var btnBrowse = new Button { Text = "...", Width = 40 };
            btnBrowse.Click += BtnBrowse_Click;
            var pnlOpc = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            pnlOpc.Controls.AddRange(new Control[] { txtOpcServer, btnBrowse });

            grid.Controls.Add(new Label { Text = "AppName:" }, 0, 0); txtAppName = new TextBox { Width = 150 }; grid.Controls.Add(txtAppName, 1, 0);
            grid.Controls.Add(new Label { Text = "OPC Server ID:" }, 0, 1); grid.Controls.Add(pnlOpc, 1, 1);
            grid.Controls.Add(new Label { Text = "Refresh Rate:" }, 0, 2); numRefresh = new NumericUpDown { Maximum = 60000 }; grid.Controls.Add(numRefresh, 1, 2);
            grid.Controls.Add(new Label { Text = "Web Port:" }, 0, 3); numPort = new NumericUpDown { Maximum = 65535 }; grid.Controls.Add(numPort, 1, 3);
            grid.Controls.Add(new Label { Text = "UDP IP:" }, 0, 4); txtUdpIp = new TextBox { Width = 150 }; grid.Controls.Add(txtUdpIp, 1, 4);
            grid.Controls.Add(new Label { Text = "UDP Port:" }, 0, 5); numUdpPort = new NumericUpDown { Maximum = 65535 }; grid.Controls.Add(numUdpPort, 1, 5);
            cbWebEnabled = new CheckBox { Text = "Web Server Enabled", AutoSize = true }; grid.Controls.Add(cbWebEnabled, 1, 6);
            cbUdpEnabled = new CheckBox { Text = "UDP Send Enabled", AutoSize = true }; grid.Controls.Add(cbUdpEnabled, 1, 7);
            tabSettings.Controls.Add(grid);

            // --- ВКЛАДКА ТЕГИ ---
            var tabTags = new TabPage("Теги");
            var split = new SplitContainer { Dock = DockStyle.Fill };
            txtTagFilter = new TextBox { Dock = DockStyle.Top};
            lbAvailableTags = new ListBox { Dock = DockStyle.Fill };
            var btnRefresh = new Button { Text = "Обновить список", Dock = DockStyle.Bottom, Height = 30 };
            btnRefresh.Click += (s, e) =>
            {
                Cursor = Cursors.WaitCursor;
                try { 
                    opcService.Connect(txtOpcServer.Text); 
                    tagManager.RefreshServerTags(opcService); 
                    tagManager.FilterSourceList(lbAvailableTags, "");
                    txtLog.AppendText($"{DateTime.Now:HH:mm:ss}: Обнаружено тегов на сервере: {lbAvailableTags.Items.Count}" + Environment.NewLine);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
                Cursor = Cursors.Default;
            };
            split.Panel1.Controls.AddRange(new Control[] { lbAvailableTags, txtTagFilter, btnRefresh });

            var dgv = new DataGridView { Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = true };

            var typeColumn = new DataGridViewComboBoxColumn
            {
                DataPropertyName = "DataType",
                HeaderText = "DataType",
                DataSource = new[] { "float", "bool", "text" },
                Name = "DataType"
            };

            tagBindingSource.DataSource = config.Tags;
            dgv.DataSource = tagBindingSource;
            dgv.Columns.Add(typeColumn);

            var menu = new ContextMenuStrip();
            var deleteItem = new ToolStripMenuItem("Удалить строку");

            deleteItem.Click += (s, e) =>
            {
                if (dgv.CurrentRow != null && !dgv.CurrentRow.IsNewRow)
                {
                    tagBindingSource.RemoveCurrent();
                    ReindexTags();
                }
            };
            menu.Items.Add(deleteItem);
            dgv.ContextMenuStrip = menu;

            split.Panel2.Controls.Add(dgv);
            tabTags.Controls.Add(split);

            dgv.RowsRemoved += (s, e) => ReindexTags();
            void ReindexTags()
            {
                for (int i = 0; i < config.Tags.Count; i++)
                {
                    config.Tags[i].Id = i + 1; // Устанавливаем ID по порядку (1, 2, 3...)
                }
                dgv.Refresh(); // Обновляем отображение в таблице
            }

            // Менеджер тегов
            tagManager = new TagManager(tagBindingSource, config.Tags);
            txtTagFilter.TextChanged += (s, e) => tagManager.FilterSourceList(lbAvailableTags, txtTagFilter.Text);
            lbAvailableTags.DoubleClick += (s, e) => { if (lbAvailableTags.SelectedItem != null) tagManager.AddTagToConfig(lbAvailableTags.SelectedItem.ToString()); };

            // --- ВКЛАДКА ЗАПУСК ---
            var tabRun = new TabPage("Диагностика");

            txtLog = new TextBox
            {
                Location = new Point(30, 120),
                Size = new Size(500, 300),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f) // Моноширинный шрифт для JSON
            };


            var chkEnableLog = new CheckBox
            {
                Text = "Включить логирование",
                Location = new Point(150, 30), // Размещаем рядом с кнопками
                AutoSize = true,
                Checked = isLoggingEnabled
            };
            // Подписываемся на изменение состояния
            chkEnableLog.CheckedChanged += (s, e) =>
            {
                isLoggingEnabled = chkEnableLog.Checked;
            };
            tabRun.Controls.AddRange(new Control[] { txtLog, chkEnableLog });

            btnStartWebOnly = new Button
            {
                Text = "Только Web",
                Location = new Point(150, 80),
                Size = new Size(100, 40)
            };
            btnStartWebOnly.Click += (s, e) => StartWebOnly();
            tabRun.Controls.Add(btnStartWebOnly);

            var btnOpenBrowser = new Button
            {
                Text = "Открыть в браузере",
                Location = new Point(270, 80),
                Size = new Size(130, 40)
            };

            btnOpenBrowser.Click += (s, e) =>
            {
                try
                {
                    // Предполагается, что переменная порта называется 'port' или берется из конфига
                    string url = $"http://127.0.0.1:{config.WebSettings.Port}/index.html";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при открытии браузера: {ex.Message}");
                }
            };

            tabRun.Controls.Add(btnOpenBrowser);

            tabs.TabPages.AddRange(new[] { tabTags, tabSettings, tabRun });
            this.Controls.Add(tabs);
            this.Controls.Add(panelTop);

            UpdateUiFromConfig();
            UpdateServerStatusUI();
        }
        private void StartWebOnly()
        {
            if (isRunning) return; // Чтобы не запускать дважды

            try
            {
                // Останавливаем старый, если он был (для чистоты памяти)
                _webService?.Stop();

                // Запускаем веб-сервер, передавая null вместо службы опроса
                _webService = new WebService(config.WebSettings, null);
                _webService.Start();

                isRunning = true;
                UpdateServerStatusUI();
                trayService.UpdateStatus(true);

                txtLog.AppendText($"{DateTime.Now}: Веб-сервер запущен автономно (без OPC)" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска веб-сервера: {ex.Message}");
            }
        }

        private void ToggleServer()
        {
            isRunning = !isRunning;

            if (isRunning)
            {
                try
                {
                    opcService.Connect(config.OpcSettings.ServerId);
                    _udpService = new UdpService(config.UdpSettings);

                    // 1. Инициализируем опрос
                    _polling = new DataPollingService(opcService, config.Tags, config.OpcSettings.RefreshRateMs);

                    // 2. Инициализируем и запускаем Web-сервер (передаем настройки и ссылку на опрос)
                    _webService = new WebService(config.WebSettings, _polling);
                    _webService.Start();

                    _polling.DataUpdated += () =>
                    {
                        if (txtLog.InvokeRequired)
                        {
                            _udpService.Send(_polling.LastBinaryData);
                            txtLog.Invoke(new Action(() => UpdateLogView()));
                        }
                        else
                        {
                            UpdateLogView();
                        }
                    };

                    _polling.Start();
                }
                catch (Exception ex)
                {
                    isRunning = false;
                    MessageBox.Show($"Ошибка запуска: {ex.Message}");
                    return;
                }
            }
            else
            {
                // Остановка веб-сервера
                _webService?.Stop();
                _polling?.Stop();
                _udpService?.Dispose();
                opcService.Disconnect();

                _webService = null;
                _polling = null;
                _udpService = null;
            }
            txtLog.AppendText($"{DateTime.Now}: Статус OPC, Web, UDP Запущен? - {isRunning}" + Environment.NewLine);
            trayService.UpdateStatus(isRunning);
            UpdateServerStatusUI();
        }

        private void UpdateServerStatusUI()
        {
            btnStart.BackColor = isRunning ? Color.LightGreen : Color.Gray;
            btnStop.BackColor = isRunning ? Color.Gray : Color.LightCoral;
            btnStart.Enabled = !isRunning;
            btnStop.Enabled = isRunning;
        }
        private void UpdateLogView()
        {
            if (_polling == null) return;

            if (isLoggingEnabled)
            {
                // Выводим JSON и размер бинарного пакета для контроля
                txtLog.Text = $"Последнее обновление: {DateTime.Now:HH:mm:ss}" + Environment.NewLine +
                            $"UDP Пакет: {_polling.LastBinaryData.Length} байт" + Environment.NewLine +
                            "--------------------------------" + Environment.NewLine +
                            _polling.LastJsonData;
            }
        }
        private void ShowWindow() { this.Show(); this.WindowState = FormWindowState.Normal; this.Activate(); }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            var servers = opcService.GetLocalServers();
            Form form = new Form { Text = "Выбор", Size = new Size(300, 400) };
            var lb = new ListBox { Dock = DockStyle.Fill }; lb.Items.AddRange(servers.ToArray());
            var btn = new Button { Text = "OK", Dock = DockStyle.Bottom }; btn.Click += (s, ev) => form.DialogResult = DialogResult.OK;
            form.Controls.AddRange(new Control[] { lb, btn });
            if (form.ShowDialog() == DialogResult.OK && lb.SelectedItem != null)
            {
                txtOpcServer.Text = lb.SelectedItem.ToString();
                ((IDisposable)form).Dispose();
            }
        }

        private void SyncConfigFromUi()
        {
            config.OpcSettings.AppName = txtAppName.Text;
            config.OpcSettings.ServerId = txtOpcServer.Text;
            config.OpcSettings.RefreshRateMs = (int)numRefresh.Value;
            config.WebSettings.Port = (int)numPort.Value;
            config.WebSettings.Enabled = cbWebEnabled.Checked;
            config.UdpSettings.RemoteIp = txtUdpIp.Text;
            config.UdpSettings.RemotePort = (int)numUdpPort.Value;
            config.UdpSettings.Enabled = cbUdpEnabled.Checked;
        }

        private void UpdateUiFromConfig()
        {
            // 1. Обновляем текстовые поля и настройки из объекта config
            txtAppName.Text = config.OpcSettings.AppName;
            txtOpcServer.Text = config.OpcSettings.ServerId;
            numRefresh.Value = config.OpcSettings.RefreshRateMs;
            numPort.Value = config.WebSettings.Port;
            cbWebEnabled.Checked = config.WebSettings.Enabled;
            txtUdpIp.Text = config.UdpSettings.RemoteIp;
            numUdpPort.Value = config.UdpSettings.RemotePort;
            cbUdpEnabled.Checked = config.UdpSettings.Enabled;
            // 2. Привязываем новый список тегов к источнику данных таблицы
            tagBindingSource.DataSource = config.Tags;
            // 3. ОБНОВЛЯЕМ ссылки внутри TagManager, чтобы он работал с НОВЫМ списком
            tagManager.UpdateReferences(tagBindingSource, config.Tags);
            // 4. Уведомляем интерфейс об обновлении данных
            tagBindingSource.ResetBindings(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { trayService?.Dispose(); opcService?.Dispose(); }
            base.Dispose(disposing);
        }
    }

    static class Program
    {
        [STAThread] static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false); 
            Application.Run(new MainForm()); 
        }
    }
}
