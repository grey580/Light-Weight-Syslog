using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LightWeightSyslog
{
    internal sealed class MainForm : Form
    {
        private const int DefaultPort = 5514;
        private const int StandardPort = 514;
        private const int MaxMessages = 20000;

         private readonly ConcurrentQueue<SyslogMessage> _pendingMessages = new ConcurrentQueue<SyslogMessage>();
         private readonly List<SyslogMessage> _allMessages = new List<SyslogMessage>();
         private readonly BindingList<SyslogMessage> _visibleMessages = new BindingList<SyslogMessage>();
         private readonly System.Windows.Forms.Timer _uiTimer;
        private readonly object _logFileSync = new object();
        private readonly string _startupDirectory;
        private readonly string _sessionLogPath;

        private UdpClient _udpClient;
        private CancellationTokenSource _listenerCancellation;
        private Task _listenerTask;
        private StreamWriter _logWriter;
        private int _sequence;
        private bool _isListening;
        private bool _filtersDirty;
        private bool _trimmedMessages;
        private string _lastError = string.Empty;
        private DateTime? _lastPacketAt;

        private Button _startButton;
        private Button _stopButton;
        private Button _clearButton;
        private Button _exportButton;
        private Button _copyTargetButton;
        private Button _copySelectedButton;
        private Button _openLogFileButton;
        private Button _use514Button;
        private Button _pauseButton;
        private NumericUpDown _portSelector;
        private CheckBox _autoScrollCheckBox;
        private TextBox _textFilterTextBox;
        private ComboBox _severityFilterComboBox;
        private TextBox _sourceFilterTextBox;
        private Label _localIpLabel;
        private DataGridView _messageGrid;
        private RichTextBox _detailTextBox;
        private ToolStripStatusLabel _listenerStatusLabel;
        private ToolStripStatusLabel _messageCountStatusLabel;
        private ToolStripStatusLabel _sourceCountStatusLabel;
        private ToolStripStatusLabel _lastPacketStatusLabel;
        private ToolStripStatusLabel _bufferStatusLabel;

        public MainForm()
        {
            _startupDirectory = Environment.CurrentDirectory;
            _sessionLogPath = Path.Combine(
                _startupDirectory,
                "LightWeightSyslog-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");

            Text = "Light Weight Syslog";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1100, 700);
            Size = new Size(1280, 820);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            EnsureLogWriter();
            WriteSystemLogLine("Session opened.");

            InitializeLayout();
            PopulateLocalIps();
            RefreshStatusLabels();

            _uiTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _uiTimer.Tick += UiTimerOnTick;
            _uiTimer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            WriteSystemLogLine("Session closing.");
            StopListening();
            CloseLogWriter();
            base.OnFormClosing(e);
        }

        private void InitializeLayout()
        {
            var topPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8)
            };

            var commandRow = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                WrapContents = true
            };

            _startButton = CreateButton("Start", StartButtonOnClick);
            _stopButton = CreateButton("Stop", StopButtonOnClick);
            _stopButton.Enabled = false;
            _clearButton = CreateButton("Clear", ClearButtonOnClick);
            _exportButton = CreateButton("Export", ExportButtonOnClick);
            _copyTargetButton = CreateButton("Copy target", CopyTargetButtonOnClick);
            _copySelectedButton = CreateButton("Copy selected", CopySelectedButtonOnClick);
            _openLogFileButton = CreateButton("Open log file", OpenLogFileButtonOnClick);
            _use514Button = CreateButton("Use 514", Use514ButtonOnClick);
            _pauseButton = CreateButton("Pause", PauseButtonOnClick);

            _portSelector = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 65535,
                Value = DefaultPort,
                Width = 90
            };

            _autoScrollCheckBox = new CheckBox
            {
                Text = "Auto-scroll",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(12, 8, 3, 3)
            };

            _localIpLabel = new Label
            {
                AutoSize = true,
                Margin = new Padding(16, 9, 3, 3)
            };

            commandRow.Controls.AddRange(new Control[]
            {
                _startButton,
                _stopButton,
                _pauseButton,
                _clearButton,
                _exportButton,
                _openLogFileButton,
                _copySelectedButton,
                new Label { Text = "Port:", AutoSize = true, Margin = new Padding(16, 9, 3, 3) },
                _portSelector,
                _use514Button,
                _autoScrollCheckBox,
                _copyTargetButton,
                _localIpLabel
            });

            var filterRow = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                WrapContents = true,
                Padding = new Padding(0, 6, 0, 0)
            };

            _textFilterTextBox = new TextBox { Width = 260 };
            _sourceFilterTextBox = new TextBox { Width = 140 };
            _severityFilterComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 170
            };
            _severityFilterComboBox.Items.AddRange(new object[]
            {
                "All severities",
                "Warning and above",
                "Error and above",
                "Critical and above",
                "Informational and above",
                "Debug only"
            });
            _severityFilterComboBox.SelectedIndex = 0;

            _textFilterTextBox.TextChanged += FilterChanged;
            _sourceFilterTextBox.TextChanged += FilterChanged;
            _severityFilterComboBox.SelectedIndexChanged += FilterChanged;

            filterRow.Controls.AddRange(new Control[]
            {
                new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(3, 9, 3, 3) },
                _textFilterTextBox,
                new Label { Text = "Severity:", AutoSize = true, Margin = new Padding(16, 9, 3, 3) },
                _severityFilterComboBox,
                new Label { Text = "Source IP:", AutoSize = true, Margin = new Padding(16, 9, 3, 3) },
                _sourceFilterTextBox
            });

            topPanel.Controls.Add(commandRow, 0, 0);
            topPanel.Controls.Add(filterRow, 0, 1);

            _messageGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                Font = new Font("Consolas", 9.0f),
                DataSource = _visibleMessages,
                BackgroundColor = SystemColors.Window
            };

            _messageGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", DataPropertyName = nameof(SyslogMessage.Sequence), Width = 55 });
            _messageGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Received", DataPropertyName = nameof(SyslogMessage.ReceivedAtDisplay), Width = 145 });
            _messageGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sender Time", DataPropertyName = nameof(SyslogMessage.SenderTimestamp), Width = 140 });
            _messageGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Source IP", DataPropertyName = nameof(SyslogMessage.SourceIp), Width = 125 });
            _messageGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Host", DataPropertyName = nameof(SyslogMessage.HostName), Width = 140 });
            _messageGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Severity", DataPropertyName = nameof(SyslogMessage.SeverityName), Width = 95 });
            _messageGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Facility", DataPropertyName = nameof(SyslogMessage.FacilityName), Width = 95 });
            _messageGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "App", DataPropertyName = nameof(SyslogMessage.AppName), Width = 120 });
            _messageGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Message", DataPropertyName = nameof(SyslogMessage.Summary), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _messageGrid.SelectionChanged += MessageGridOnSelectionChanged;
            _messageGrid.CellFormatting += MessageGridOnCellFormatting;

            _detailTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 9.0f),
                WordWrap = true,
                BackColor = SystemColors.Window
            };

            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 470
            };
            splitContainer.Panel1.Controls.Add(_messageGrid);
            splitContainer.Panel2.Controls.Add(_detailTextBox);

            var statusStrip = new StatusStrip();
            _listenerStatusLabel = new ToolStripStatusLabel();
            _messageCountStatusLabel = new ToolStripStatusLabel();
            _sourceCountStatusLabel = new ToolStripStatusLabel();
            _lastPacketStatusLabel = new ToolStripStatusLabel();
            _bufferStatusLabel = new ToolStripStatusLabel();
            statusStrip.Items.AddRange(new ToolStripItem[]
            {
                _listenerStatusLabel,
                new ToolStripStatusLabel { Spring = true },
                _messageCountStatusLabel,
                new ToolStripStatusLabel { Text = " | " },
                _sourceCountStatusLabel,
                new ToolStripStatusLabel { Text = " | " },
                _lastPacketStatusLabel,
                new ToolStripStatusLabel { Text = " | " },
                _bufferStatusLabel
            });

            Controls.Add(splitContainer);
            Controls.Add(statusStrip);
            Controls.Add(topPanel);
        }

        private static Button CreateButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(3)
            };
            button.Click += onClick;
            return button;
        }

        private void StartButtonOnClick(object sender, EventArgs e)
        {
            StartListening();
        }

        private void StopButtonOnClick(object sender, EventArgs e)
        {
            StopListening();
        }

        private void PauseButtonOnClick(object sender, EventArgs e)
        {
            if (!_isListening)
            {
                return;
            }

            if (_pauseButton.Text == "Pause")
            {
                _pauseButton.Text = "Resume";
            }
            else
            {
                _pauseButton.Text = "Pause";
            }

            RefreshStatusLabels();
        }

        private void ClearButtonOnClick(object sender, EventArgs e)
        {
            _allMessages.Clear();
            _visibleMessages.Clear();
            _detailTextBox.Clear();
            while (_pendingMessages.TryDequeue(out _))
            {
            }

            _trimmedMessages = false;
            RefreshStatusLabels();
        }

        private void ExportButtonOnClick(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt";
                dialog.FileName = "syslog-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                if (dialog.FilterIndex == 1 || string.Equals(Path.GetExtension(dialog.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
                {
                    ExportCsv(dialog.FileName);
                }
                else
                {
                    ExportText(dialog.FileName);
                }
            }
        }

        private void CopyTargetButtonOnClick(object sender, EventArgs e)
        {
            var preferredIp = GetLocalIpAddresses().FirstOrDefault() ?? "127.0.0.1";
            Clipboard.SetText(preferredIp + ":" + _portSelector.Value.ToString(CultureInfo.InvariantCulture));
        }

        private void CopySelectedButtonOnClick(object sender, EventArgs e)
        {
            var selected = GetSelectedMessage();
            if (selected == null)
            {
                return;
            }

            Clipboard.SetText(selected.DetailText);
        }

        private void OpenLogFileButtonOnClick(object sender, EventArgs e)
        {
            try
            {
                EnsureLogWriter();
                _logWriter.Flush();
                System.Diagnostics.Process.Start(_sessionLogPath);
            }
            catch (Exception ex)
            {
                _lastError = "Failed to open log file: " + ex.Message;
                RefreshStatusLabels();
            }
        }

        private void Use514ButtonOnClick(object sender, EventArgs e)
        {
            if (_isListening)
            {
                return;
            }

            _portSelector.Value = StandardPort;
        }

        private void FilterChanged(object sender, EventArgs e)
        {
            _filtersDirty = true;
        }

        private void MessageGridOnSelectionChanged(object sender, EventArgs e)
        {
            var selected = GetSelectedMessage();
            _detailTextBox.Text = selected?.DetailText ?? string.Empty;
        }

        private void MessageGridOnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (_messageGrid.Columns[e.ColumnIndex].DataPropertyName != nameof(SyslogMessage.SeverityName))
            {
                return;
            }

            if (!(_messageGrid.Rows[e.RowIndex].DataBoundItem is SyslogMessage item))
            {
                return;
            }

            e.CellStyle.BackColor = GetSeverityColor(item.Severity);
            e.CellStyle.SelectionBackColor = GetSeverityColor(item.Severity);
            e.CellStyle.SelectionForeColor = Color.Black;
        }

        private void UiTimerOnTick(object sender, EventArgs e)
        {
            if (!IsPaused())
            {
                FlushPendingMessages();
            }

            if (_filtersDirty)
            {
                RebuildVisibleMessages();
            }

            if (!string.IsNullOrWhiteSpace(_lastError))
            {
                _listenerStatusLabel.Text = _lastError;
            }
            else
            {
                RefreshStatusLabels();
            }
        }

        private void StartListening()
        {
            if (_isListening)
            {
                return;
            }

            var port = Decimal.ToInt32(_portSelector.Value);
            _lastError = string.Empty;

            try
            {
                EnsureLogWriter();
                _listenerCancellation = new CancellationTokenSource();
                _udpClient = new UdpClient(AddressFamily.InterNetwork);
                _udpClient.Client.ReceiveBufferSize = 1024 * 1024;
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                _listenerTask = Task.Run(() => ReceiveLoop(_listenerCancellation.Token));
                _isListening = true;
                _startButton.Enabled = false;
                _stopButton.Enabled = true;
                _portSelector.Enabled = false;
                _use514Button.Enabled = false;
                WriteSystemLogLine("Listening started on UDP " + port + ".");
                RefreshStatusLabels();
            }
            catch (Exception ex)
            {
                _lastError = "Failed to listen on UDP " + port + ": " + ex.Message;
                StopListening();
                RefreshStatusLabels();
            }
        }

        private void StopListening()
        {
            _isListening = false;
            _listenerCancellation?.Cancel();

            try
            {
                _udpClient?.Close();
            }
            catch
            {
            }

            _udpClient = null;
            _listenerTask = null;
            _listenerCancellation?.Dispose();
            _listenerCancellation = null;
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _portSelector.Enabled = true;
            _use514Button.Enabled = true;
            _pauseButton.Text = "Pause";
            WriteSystemLogLine("Listening stopped.");
            RefreshStatusLabels();
        }

        private void ReceiveLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    IPEndPoint remoteEndPoint = null;
                    var bytes = _udpClient.Receive(ref remoteEndPoint);
                    var text = DecodeMessage(bytes);
                    var message = SyslogParser.Parse(text, remoteEndPoint?.Address.ToString() ?? string.Empty, DateTime.Now, Interlocked.Increment(ref _sequence));
                    WriteMessageToLog(message);
                    _pendingMessages.Enqueue(message);
                    _lastPacketAt = DateTime.Now;
                    _lastError = string.Empty;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _lastError = "Listener error: " + ex.Message;
                    }

                    return;
                }
                catch (Exception ex)
                {
                    _lastError = "Receive failed: " + ex.Message;
                }
            }
        }

        private void FlushPendingMessages()
        {
            var filtersDirty = _filtersDirty;
            var trimmed = false;
            var addedVisibleRow = false;

            while (_pendingMessages.TryDequeue(out var message))
            {
                _allMessages.Add(message);

                if (_allMessages.Count > MaxMessages)
                {
                    var removeCount = _allMessages.Count - MaxMessages;
                    _allMessages.RemoveRange(0, removeCount);
                    trimmed = true;
                    _trimmedMessages = true;
                }

                if (!filtersDirty && !trimmed && MessageMatchesFilters(message))
                {
                    _visibleMessages.Add(message);
                    addedVisibleRow = true;
                }
            }

            if (trimmed || filtersDirty)
            {
                RebuildVisibleMessages();
            }
            else if (addedVisibleRow && _autoScrollCheckBox.Checked && _visibleMessages.Count > 0)
            {
                _messageGrid.FirstDisplayedScrollingRowIndex = _visibleMessages.Count - 1;
            }

            RefreshStatusLabels();
        }

        private void RebuildVisibleMessages()
        {
            var selectedSequence = GetSelectedMessage()?.Sequence ?? -1;
            _visibleMessages.RaiseListChangedEvents = false;
            _visibleMessages.Clear();

            foreach (var message in _allMessages.Where(MessageMatchesFilters))
            {
                _visibleMessages.Add(message);
            }

            _visibleMessages.RaiseListChangedEvents = true;
            _visibleMessages.ResetBindings();
            _filtersDirty = false;

            if (selectedSequence > 0)
            {
                foreach (DataGridViewRow row in _messageGrid.Rows)
                {
                    if (row.DataBoundItem is SyslogMessage item && item.Sequence == selectedSequence)
                    {
                        row.Selected = true;
                        break;
                    }
                }
            }

            if (_autoScrollCheckBox.Checked && _visibleMessages.Count > 0)
            {
                _messageGrid.FirstDisplayedScrollingRowIndex = _visibleMessages.Count - 1;
            }

            RefreshStatusLabels();
        }

        private bool MessageMatchesFilters(SyslogMessage message)
        {
            var textFilter = (_textFilterTextBox.Text ?? string.Empty).Trim();
            if (textFilter.Length > 0)
            {
                var haystack = string.Join(" ", new[]
                {
                    message.SourceIp,
                    message.HostName,
                    message.AppName,
                    message.MessageText,
                    message.RawMessage,
                    message.FacilityName,
                    message.SeverityName
                }).ToLowerInvariant();

                if (!haystack.Contains(textFilter.ToLowerInvariant()))
                {
                    return false;
                }
            }

            var sourceFilter = (_sourceFilterTextBox.Text ?? string.Empty).Trim();
            if (sourceFilter.Length > 0 && !message.SourceIp.StartsWith(sourceFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var severityFilter = _severityFilterComboBox.SelectedItem as string ?? "All severities";
            if (severityFilter == "Debug only")
            {
                return message.Severity == 7;
            }

            var minimumSeverity = SyslogParser.GetMinimumSeverityForFilter(severityFilter);
            if (minimumSeverity != int.MaxValue && message.Severity > minimumSeverity)
            {
                return false;
            }

            return true;
        }

        private void PopulateLocalIps()
        {
            _localIpLabel.Text = "This PC: " + string.Join(", ", GetLocalIpAddresses());
        }

        private IEnumerable<string> GetLocalIpAddresses()
        {
            try
            {
                return Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    .Select(ip => ip.ToString())
                    .Distinct()
                    .OrderBy(ip => ip)
                    .ToArray();
            }
            catch
            {
                return new[] { "127.0.0.1" };
            }
        }

        private void RefreshStatusLabels()
        {
            var portText = _portSelector.Value.ToString(CultureInfo.InvariantCulture);
            var pauseState = IsPaused() ? "Paused" : (_isListening ? "Listening" : "Stopped");
            var bufferedCount = _pendingMessages.Count;

            _listenerStatusLabel.Text = _lastError.Length > 0
                ? _lastError
                : pauseState + " on UDP " + portText;
            _messageCountStatusLabel.Text = _allMessages.Count.ToString("N0", CultureInfo.InvariantCulture) + " total";
            _sourceCountStatusLabel.Text = _allMessages.Select(m => m.SourceIp).Where(ip => ip.Length > 0).Distinct().Count().ToString(CultureInfo.InvariantCulture) + " sources";
            _lastPacketStatusLabel.Text = _lastPacketAt.HasValue
                ? "Last packet " + _lastPacketAt.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : "No packets yet";
            _bufferStatusLabel.Text = bufferedCount > 0
                ? bufferedCount.ToString("N0", CultureInfo.InvariantCulture) + " buffered"
                : (_trimmedMessages ? "Display capped at " + MaxMessages.ToString("N0", CultureInfo.InvariantCulture) : "Log: " + Path.GetFileName(_sessionLogPath));
        }

        private SyslogMessage GetSelectedMessage()
        {
            if (_messageGrid.SelectedRows.Count == 0)
            {
                return null;
            }

            return _messageGrid.SelectedRows[0].DataBoundItem as SyslogMessage;
        }

        private void ExportCsv(string path)
        {
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine("Sequence,ReceivedAt,SenderTimestamp,SourceIp,Host,Facility,Severity,App,ProcId,MsgId,Message");

                foreach (var item in _visibleMessages)
                {
                    writer.WriteLine(string.Join(",",
                        EscapeCsv(item.Sequence.ToString(CultureInfo.InvariantCulture)),
                        EscapeCsv(item.ReceivedAtDisplay),
                        EscapeCsv(item.SenderTimestamp),
                        EscapeCsv(item.SourceIp),
                        EscapeCsv(item.HostName),
                        EscapeCsv(item.FacilityName),
                        EscapeCsv(item.SeverityName),
                        EscapeCsv(item.AppName),
                        EscapeCsv(item.ProcId),
                        EscapeCsv(item.MsgId),
                        EscapeCsv(item.Summary)));
                }
            }
        }

        private void ExportText(string path)
        {
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                foreach (var item in _visibleMessages)
                {
                    writer.WriteLine(item.DetailText);
                    writer.WriteLine(new string('-', 80));
                }
            }
        }

        private static string EscapeCsv(string value)
        {
            var safe = value ?? string.Empty;
            if (safe.Contains("\"") || safe.Contains(",") || safe.Contains(Environment.NewLine))
            {
                return "\"" + safe.Replace("\"", "\"\"") + "\"";
            }

            return safe;
        }

        private bool IsPaused()
        {
            return string.Equals(_pauseButton.Text, "Resume", StringComparison.Ordinal);
        }

        private static Color GetSeverityColor(int severity)
        {
            switch (severity)
            {
                case 0:
                case 1:
                case 2:
                    return Color.FromArgb(255, 198, 198);
                case 3:
                    return Color.FromArgb(255, 220, 180);
                case 4:
                    return Color.FromArgb(255, 244, 180);
                case 7:
                    return Color.FromArgb(230, 230, 230);
                default:
                    return Color.FromArgb(232, 248, 232);
            }
        }

        private static string DecodeMessage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            var utf8 = Encoding.UTF8.GetString(bytes);
            if (!utf8.Contains("\uFFFD"))
            {
                return utf8;
            }

            return Encoding.GetEncoding(28591).GetString(bytes);
        }

        private void EnsureLogWriter()
        {
            lock (_logFileSync)
            {
                if (_logWriter != null)
                {
                    return;
                }

                Directory.CreateDirectory(_startupDirectory);
                _logWriter = new StreamWriter(new FileStream(_sessionLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
                {
                    AutoFlush = true
                };

                if (new FileInfo(_sessionLogPath).Length == 0)
                {
                    _logWriter.WriteLine("# Light Weight Syslog session log");
                    _logWriter.WriteLine("# Started: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    _logWriter.WriteLine("# Directory: " + _startupDirectory);
                    _logWriter.WriteLine();
                }
            }
        }

        private void CloseLogWriter()
        {
            lock (_logFileSync)
            {
                if (_logWriter == null)
                {
                    return;
                }

                _logWriter.Dispose();
                _logWriter = null;
            }
        }

        private void WriteMessageToLog(SyslogMessage message)
        {
            lock (_logFileSync)
            {
                if (_logWriter == null)
                {
                    return;
                }

                _logWriter.WriteLine(
                    "[{0}] [{1}] [{2}] [{3}] {4}",
                    message.ReceivedAtDisplay,
                    message.SourceIp,
                    message.FacilityName,
                    message.SeverityName,
                    message.RawMessage);
            }
        }

        private void WriteSystemLogLine(string text)
        {
            lock (_logFileSync)
            {
                if (_logWriter == null)
                {
                    return;
                }

                _logWriter.WriteLine(
                    "[{0}] [SYSTEM] {1}",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    text);
            }
        }
    }
}
