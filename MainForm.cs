using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows.Forms;

namespace SerialPortHelper
{
    public partial class MainForm : Form
    {
        private readonly SerialPort _serialPort = new();
        private readonly System.Windows.Forms.Timer _autoSendTimer = new();
        private bool _isHexReceive = false;
        private bool _isHexSend = false;
        private bool _showTimestamp = true;
        private bool _isReceiving = false;

        // ==== 控件声明 ====
        private ComboBox cboPort = null!;
        private ComboBox cboBaud = null!;
        private ComboBox cboDataBits = null!;
        private ComboBox cboStopBits = null!;
        private ComboBox cboParity = null!;
        private Button btnRefresh = null!;
        private Button btnOpenClose = null!;
        private TextBox txtReceive = null!;
        private CheckBox chkHexRecv = null!;
        private CheckBox chkTimestamp = null!;
        private Button btnClearRecv = null!;
        private Button btnSaveRecv = null!;
        private TextBox txtSend = null!;
        private CheckBox chkHexSend = null!;
        private CheckBox chkAutoSend = null!;
        private NumericUpDown numInterval = null!;
        private Button btnSend = null!;
        private Button btnClearSend = null!;
        private StatusStrip statusStrip = null!;
        private ToolStripStatusLabel lblStatus = null!;
        private ToolStripStatusLabel lblRxCount = null!;
        private ToolStripStatusLabel lblTxCount = null!;

        private long _rxBytes = 0;
        private long _txBytes = 0;

        public MainForm()
        {
            InitializeComponent();
            TeamUpdaterBootstrap.Attach(this);
            SetupSerialPort();
            LoadPorts();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // 窗体
            this.Text = "串口助手";
            this.Size = new System.Drawing.Size(820, 640);
            this.MinimumSize = new System.Drawing.Size(640, 480);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new System.Drawing.Font("微软雅黑", 9F);

            // ===== 顶部配置面板 =====
            var panelConfig = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                Padding = new Padding(8)
            };

            var lblPort = new Label { Text = "串  口:", Location = new System.Drawing.Point(8, 12), AutoSize = true };
            cboPort = new ComboBox { Location = new System.Drawing.Point(68, 8), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            btnRefresh = new Button { Text = "刷新", Location = new System.Drawing.Point(162, 7), Width = 56, Height = 25 };
            btnRefresh.Click += BtnRefresh_Click;

            var lblBaud = new Label { Text = "波特率:", Location = new System.Drawing.Point(230, 12), AutoSize = true };
            cboBaud = new ComboBox { Location = new System.Drawing.Point(290, 8), Width = 90 };
            cboBaud.Items.AddRange(new object[] { "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600" });
            cboBaud.Text = "115200";

            var lblData = new Label { Text = "数据位:", Location = new System.Drawing.Point(392, 12), AutoSize = true };
            cboDataBits = new ComboBox { Location = new System.Drawing.Point(452, 8), Width = 55, DropDownStyle = ComboBoxStyle.DropDownList };
            cboDataBits.Items.AddRange(new object[] { "5", "6", "7", "8" });
            cboDataBits.Text = "8";

            var lblStop = new Label { Text = "停止位:", Location = new System.Drawing.Point(8, 44), AutoSize = true };
            cboStopBits = new ComboBox { Location = new System.Drawing.Point(68, 40), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            cboStopBits.Items.AddRange(new object[] { "1", "1.5", "2" });
            cboStopBits.Text = "1";

            var lblParity = new Label { Text = "校验位:", Location = new System.Drawing.Point(170, 44), AutoSize = true };
            cboParity = new ComboBox { Location = new System.Drawing.Point(230, 40), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            cboParity.Items.AddRange(new object[] { "None", "Odd", "Even", "Mark", "Space" });
            cboParity.Text = "None";

            btnOpenClose = new Button
            {
                Text = "打开串口",
                Location = new System.Drawing.Point(520, 8),
                Width = 110,
                Height = 56,
                BackColor = System.Drawing.Color.FromArgb(0, 150, 80),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold)
            };
            btnOpenClose.FlatAppearance.BorderSize = 0;
            btnOpenClose.Click += BtnOpenClose_Click;

            panelConfig.Controls.AddRange(new Control[] {
                lblPort, cboPort, btnRefresh, lblBaud, cboBaud,
                lblData, cboDataBits, lblStop, cboStopBits,
                lblParity, cboParity, btnOpenClose
            });

            // ===== 接收区 =====
            var groupRecv = new GroupBox
            {
                Text = "接收区",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            txtReceive = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = System.Drawing.Color.FromArgb(245, 245, 245),
                Font = new System.Drawing.Font("Consolas", 10F)
            };

            var panelRecvCtrl = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36
            };

            chkHexRecv = new CheckBox { Text = "HEX显示", Location = new System.Drawing.Point(8, 8), AutoSize = true };
            chkHexRecv.CheckedChanged += (s, e) => _isHexReceive = chkHexRecv.Checked;

            chkTimestamp = new CheckBox { Text = "时间戳", Location = new System.Drawing.Point(100, 8), AutoSize = true, Checked = true };
            chkTimestamp.CheckedChanged += (s, e) => _showTimestamp = chkTimestamp.Checked;

            btnClearRecv = new Button { Text = "清空接收", Location = new System.Drawing.Point(190, 4), Width = 80, Height = 26 };
            btnClearRecv.Click += (s, e) => { txtReceive.Clear(); _rxBytes = 0; UpdateStatus(); };

            btnSaveRecv = new Button { Text = "保存日志", Location = new System.Drawing.Point(280, 4), Width = 80, Height = 26 };
            btnSaveRecv.Click += BtnSaveRecv_Click;

            panelRecvCtrl.Controls.AddRange(new Control[] { chkHexRecv, chkTimestamp, btnClearRecv, btnSaveRecv });
            groupRecv.Controls.Add(txtReceive);
            groupRecv.Controls.Add(panelRecvCtrl);

            // ===== 发送区 =====
            var groupSend = new GroupBox
            {
                Text = "发送区",
                Dock = DockStyle.Bottom,
                Height = 160,
                Padding = new Padding(8)
            };

            txtSend = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Consolas", 10F)
            };

            var panelSendCtrl = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36
            };

            chkHexSend = new CheckBox { Text = "HEX发送", Location = new System.Drawing.Point(8, 8), AutoSize = true };
            chkHexSend.CheckedChanged += (s, e) => _isHexSend = chkHexSend.Checked;

            chkAutoSend = new CheckBox { Text = "定时发送", Location = new System.Drawing.Point(100, 8), AutoSize = true };
            chkAutoSend.CheckedChanged += ChkAutoSend_CheckedChanged;

            var lblInterval = new Label { Text = "间隔(ms):", Location = new System.Drawing.Point(190, 10), AutoSize = true };
            numInterval = new NumericUpDown
            {
                Location = new System.Drawing.Point(258, 6),
                Width = 70,
                Minimum = 10,
                Maximum = 60000,
                Value = 1000,
                Increment = 100
            };

            btnClearSend = new Button { Text = "清空发送", Location = new System.Drawing.Point(345, 4), Width = 80, Height = 26 };
            btnClearSend.Click += (s, e) => txtSend.Clear();

            btnSend = new Button
            {
                Text = "发  送",
                Location = new System.Drawing.Point(620, 3),
                Width = 120,
                Height = 28,
                BackColor = System.Drawing.Color.FromArgb(0, 120, 215),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Bold)
            };
            btnSend.FlatAppearance.BorderSize = 0;
            btnSend.Click += BtnSend_Click;

            panelSendCtrl.Controls.AddRange(new Control[] {
                chkHexSend, chkAutoSend, lblInterval, numInterval, btnClearSend, btnSend
            });
            groupSend.Controls.Add(txtSend);
            groupSend.Controls.Add(panelSendCtrl);

            // ===== 状态栏 =====
            statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            lblRxCount = new ToolStripStatusLabel("RX: 0 B");
            lblTxCount = new ToolStripStatusLabel("TX: 0 B");
            statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus, lblRxCount, lblTxCount });

            // 组装
            this.Controls.Add(groupRecv);
            this.Controls.Add(groupSend);
            this.Controls.Add(panelConfig);
            this.Controls.Add(statusStrip);

            // 确保接收区在发送区之上（Dock顺序）
            groupRecv.BringToFront();

            // 定时发送定时器
            _autoSendTimer.Tick += (s, e) => DoSend();

            this.ResumeLayout(false);
        }

        private void SetupSerialPort()
        {
            _serialPort.DataReceived += SerialPort_DataReceived;
            _serialPort.ErrorReceived += SerialPort_ErrorReceived;
        }

        private void LoadPorts()
        {
            cboPort.Items.Clear();
            string[] ports = SerialPort.GetPortNames();
            foreach (string p in ports)
                cboPort.Items.Add(p);
            if (cboPort.Items.Count > 0)
                cboPort.SelectedIndex = 0;
        }

        private void BtnRefresh_Click(object? sender, EventArgs e)
        {
            LoadPorts();
            lblStatus.Text = $"已刷新，共 {cboPort.Items.Count} 个串口";
        }

        private void BtnOpenClose_Click(object? sender, EventArgs e)
        {
            if (_serialPort.IsOpen)
            {
                ClosePort();
            }
            else
            {
                OpenPort();
            }
        }

        private void OpenPort()
        {
            if (cboPort.SelectedItem == null)
            {
                MessageBox.Show("请选择串口", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _serialPort.PortName = cboPort.SelectedItem.ToString()!;
                _serialPort.BaudRate = int.Parse(cboBaud.Text);
                _serialPort.DataBits = int.Parse(cboDataBits.Text);
                _serialPort.StopBits = cboStopBits.Text switch
                {
                    "1" => StopBits.One,
                    "1.5" => StopBits.OnePointFive,
                    "2" => StopBits.Two,
                    _ => StopBits.One
                };
                _serialPort.Parity = cboParity.Text switch
                {
                    "Odd" => Parity.Odd,
                    "Even" => Parity.Even,
                    "Mark" => Parity.Mark,
                    "Space" => Parity.Space,
                    _ => Parity.None
                };
                _serialPort.ReadTimeout = 500;
                _serialPort.WriteTimeout = 500;
                _serialPort.Encoding = Encoding.UTF8;

                _serialPort.Open();
                _isReceiving = true;

                btnOpenClose.Text = "关闭串口";
                btnOpenClose.BackColor = System.Drawing.Color.FromArgb(200, 60, 60);
                SetConfigEnabled(false);
                lblStatus.Text = $"已连接 {_serialPort.PortName} @ {_serialPort.BaudRate}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开串口失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "打开失败";
            }
        }

        private void ClosePort()
        {
            try
            {
                _isReceiving = false;
                _autoSendTimer.Stop();
                chkAutoSend.Checked = false;
                if (_serialPort.IsOpen)
                    _serialPort.Close();
            }
            catch { }

            btnOpenClose.Text = "打开串口";
            btnOpenClose.BackColor = System.Drawing.Color.FromArgb(0, 150, 80);
            SetConfigEnabled(true);
            lblStatus.Text = "已断开";
        }

        private void SetConfigEnabled(bool enabled)
        {
            cboPort.Enabled = enabled;
            cboBaud.Enabled = enabled;
            cboDataBits.Enabled = enabled;
            cboStopBits.Enabled = enabled;
            cboParity.Enabled = enabled;
            btnRefresh.Enabled = enabled;
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (!_isReceiving) return;
            try
            {
                int len = _serialPort.BytesToRead;
                if (len == 0) return;
                byte[] buffer = new byte[len];
                _serialPort.Read(buffer, 0, len);
                _rxBytes += len;

                this.BeginInvoke(new Action(() =>
                {
                    AppendReceiveData(buffer);
                    UpdateStatus();
                }));
            }
            catch { }
        }

        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            this.BeginInvoke(new Action(() =>
            {
                lblStatus.Text = $"串口错误: {e.EventType}";
            }));
        }

        private void AppendReceiveData(byte[] data)
        {
            string line;
            if (_isHexReceive)
            {
                line = BitConverter.ToString(data).Replace("-", " ") + " ";
            }
            else
            {
                line = Encoding.UTF8.GetString(data);
            }

            if (_showTimestamp)
            {
                string ts = DateTime.Now.ToString("HH:mm:ss.fff");
                txtReceive.AppendText($"[{ts}] {line}");
            }
            else
            {
                txtReceive.AppendText(line);
            }

            // 限制最大长度，防止内存溢出
            if (txtReceive.TextLength > 500000)
            {
                txtReceive.Text = txtReceive.Text.Substring(400000);
                txtReceive.SelectionStart = txtReceive.TextLength;
                txtReceive.ScrollToCaret();
            }
        }

        private void BtnSend_Click(object? sender, EventArgs e)
        {
            DoSend();
        }

        private void DoSend()
        {
            if (!_serialPort.IsOpen)
            {
                MessageBox.Show("请先打开串口", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                chkAutoSend.Checked = false;
                return;
            }

            string text = txtSend.Text;
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                byte[] data;
                if (_isHexSend)
                {
                    data = HexStringToBytes(text);
                }
                else
                {
                    data = Encoding.UTF8.GetBytes(text);
                }

                _serialPort.Write(data, 0, data.Length);
                _txBytes += data.Length;
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"发送失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static byte[] HexStringToBytes(string hex)
        {
            // 去除空格、换行、0x前缀等
            string clean = System.Text.RegularExpressions.Regex.Replace(hex, @"[\s\r\n]|0x|0X", "");
            if (clean.Length % 2 != 0)
                throw new ArgumentException("HEX格式错误：字符数必须为偶数");

            byte[] bytes = new byte[clean.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        private void ChkAutoSend_CheckedChanged(object? sender, EventArgs e)
        {
            if (chkAutoSend.Checked)
            {
                if (!_serialPort.IsOpen)
                {
                    MessageBox.Show("请先打开串口", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    chkAutoSend.Checked = false;
                    return;
                }
                _autoSendTimer.Interval = (int)numInterval.Value;
                _autoSendTimer.Start();
                lblStatus.Text = "定时发送中...";
            }
            else
            {
                _autoSendTimer.Stop();
                lblStatus.Text = _serialPort.IsOpen ? "已连接" : "就绪";
            }
        }

        private void BtnSaveRecv_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtReceive.Text))
            {
                MessageBox.Show("没有可保存的数据", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new SaveFileDialog
            {
                Filter = "文本文件|*.txt|所有文件|*.*",
                FileName = $"SerialLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = "保存接收日志"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    File.WriteAllText(dlg.FileName, txtReceive.Text, Encoding.UTF8);
                    lblStatus.Text = $"已保存到 {dlg.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void UpdateStatus()
        {
            lblRxCount.Text = $"RX: {FormatBytes(_rxBytes)}";
            lblTxCount.Text = $"TX: {FormatBytes(_txBytes)}";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _autoSendTimer.Stop();
            _isReceiving = false;
            if (_serialPort.IsOpen)
            {
                try { _serialPort.Close(); } catch { }
            }
            _serialPort.Dispose();
            base.OnFormClosing(e);
        }
    }
}
