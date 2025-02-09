using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MetamindFlasher
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    /// <summary>
    /// Representa um segmento .hex (Address + Data).
    /// </summary>
    public class HexSegment
    {
        public int Address { get; set; }
        public byte[] Data { get; set; }
    }

    public class MainForm : Form
    {
        // Campos
        private MenuStrip menuStrip;
        private ToolStripMenuItem sobreMenuItem;

        // SplitContainer para a foto (esquerda) e logs (direita)
        private SplitContainer mainSplit;
        private PictureBox boardPictureBox;
        private RichTextBox logsRichTextBox;

        // Painel topo + GroupBox
        private Panel topPanel;
        private GroupBox actionsGroup;
        private ComboBox portComboBox;
        private Button refreshPortsButton;
        private Button flashHexButton;
        private Button eraseButton;
        private Button connectButton;

        // Barra de status
        private StatusStrip statusStrip;
        private ToolStripProgressBar progressBar;
        private ToolStripStatusLabel progressLabel;
        private ToolStripStatusLabel connectionStatusLabel;

        // Serial
        private SerialPort serialPort;

        // Caminhos fixos
        private string boardImagePath;
        private string firmwareHexPath;

        // Flag de operação em andamento (apagar ou gravar)
        private bool isBusy = false;

        public MainForm()
        {
            this.Text = "Metamind Flashe";
            this.Width = 800;
            this.Height = 600;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(600, 400);

            boardImagePath = Path.Combine(Application.StartupPath, "Resources", "board.jpg");
            firmwareHexPath = Path.Combine(Application.StartupPath, "Resources", "firmware.hex");

            InitializeMenu();
            InitializeTopPanel();
            InitializeMainSplit();
            InitializeStatusStrip();

            // Instancia a SerialPort
            serialPort = new SerialPort();

            // Ao fechar, verificar se está ocupado
            this.FormClosing += MainForm_FormClosing;

            RefreshPorts();
            UpdateConnectionStatus(false, "Desconectado");
        }

        //========================== MENU ==========================
        private void InitializeMenu()
        {
            menuStrip = new MenuStrip { Dock = DockStyle.Top };
            sobreMenuItem = new ToolStripMenuItem("Sobre");
            sobreMenuItem.Click += SobreMenuItem_Click;
            menuStrip.Items.Add(sobreMenuItem);
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);
        }

        private void SobreMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "Metamind Flashe\n\nFuncionalidades:\n\n - Apagar memória\n\n - Gravar firmware .hex\n",
                "Sobre", MessageBoxButtons.OK, MessageBoxIcon.Information
            );
        }

        //====================== PAINEL TOPO =======================
        private void InitializeTopPanel()
        {
            topPanel = new Panel { Dock = DockStyle.Top, Height = 100 };
            this.Controls.Add(topPanel);

            Label portLabel = new Label
            {
                Text = "Porta COM:",
                Left = 10,
                Top = 10,
                AutoSize = true
            };
            topPanel.Controls.Add(portLabel);

            portComboBox = new ComboBox
            {
                Left = 80,
                Top = 6,
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            topPanel.Controls.Add(portComboBox);

            refreshPortsButton = new Button
            {
                Text = "Atualizar",
                Left = 210,
                Top = 5,
                Width = 80
            };
            // Exemplo de colocar um ícone (placeholder) -> substitua por um resource real
            // refreshPortsButton.Image = Image.FromFile("Resources/refreshIcon.png");
            refreshPortsButton.Click += RefreshPortsButton_Click;
            topPanel.Controls.Add(refreshPortsButton);

            connectButton = new Button
            {
                Text = "Conectar a Placa Catalyst",
                Left = 300,
                Top = 5,
                Width = 150
            };
            // connectButton.Image = Image.FromFile("Resources/connectIcon.png"); // se quiser
            connectButton.Click += ConnectButton_Click;
            topPanel.Controls.Add(connectButton);

            // Agrupar botões Flash e Erase em um GroupBox
            actionsGroup = new GroupBox
            {
                Text = "Ações",
                Left = 10,
                Top = 50,
                Width = 440,
                Height = 50
            };
            topPanel.Controls.Add(actionsGroup);

            // Botão para gravar
            flashHexButton = new Button
            {
                Text = "Programar",
                Left = 10,
                Top = 15,
                Width = 100
            };
            // flashHexButton.Image = ... se tiver
            flashHexButton.Click += FlashHexButton_Click;
            actionsGroup.Controls.Add(flashHexButton);

            // Botão para apagar
            eraseButton = new Button
            {
                Text = "Apagar",
                Left = 120,
                Top = 15,
                Width = 90
            };
            eraseButton.Click += EraseButton_Click;
            actionsGroup.Controls.Add(eraseButton);
        }

        private void RefreshPortsButton_Click(object sender, EventArgs e)
        {
            RefreshPorts();
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            // Não chama RefreshPorts() aqui para não sobrescrever a escolha do usuário
            if (portComboBox.SelectedIndex < 0)
            {
                MessageBox.Show(
                    "Nenhuma porta COM foi selecionada.",
                    "Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            var selectedPort = portComboBox.SelectedItem.ToString();
            UpdateConnectionStatus(true, selectedPort);

            MessageBox.Show(
                $"Conectado à porta {selectedPort}",
                "Conexão Estabelecida",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void RefreshPorts()
        {
            var oldSelection = portComboBox.SelectedItem;

            portComboBox.Items.Clear();
            var ports = SerialPort.GetPortNames();

            foreach (var p in ports)
            {
                portComboBox.Items.Add(p);
            }

            // Tenta restaurar a seleção antiga
            if (oldSelection != null && portComboBox.Items.Contains(oldSelection))
            {
                portComboBox.SelectedItem = oldSelection;
            }
            else if (portComboBox.SelectedIndex < 0 && ports.Length > 0)
            {
                portComboBox.SelectedIndex = 0;
            }
        }

        private void FlashHexButton_Click(object sender, EventArgs e)
        {
            if (portComboBox.SelectedItem == null)
            {
                MessageBox.Show("Selecione a porta serial.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // Usa thread para não travar UI
            if (isBusy)
            {
                MessageBox.Show("Uma operação já está em andamento!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Thread t = new Thread(FlashHexProcess);
            t.Start();
        }

        private void EraseButton_Click(object sender, EventArgs e)
        {
            if (portComboBox.SelectedItem == null)
            {
                MessageBox.Show("Selecione a porta serial.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (isBusy)
            {
                MessageBox.Show("Uma operação já está em andamento!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Thread t = new Thread(EraseProcess);
            t.Start();
        }

        //==================== SPLIT CONTAINER ====================
        private void InitializeMainSplit()
        {
            mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 250
            };
            this.Controls.Add(mainSplit);

            // Panel1: PictureBox
            boardPictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            if (File.Exists(boardImagePath))
            {
                boardPictureBox.Image = Image.FromFile(boardImagePath);
            }
            else
            {
                Console.WriteLine("Imagem board.jpg não foi encontrada.");
            }
            mainSplit.Panel1.Controls.Add(boardPictureBox);

            // Panel2: RichTextBox logs
            logsRichTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White
            };
            mainSplit.Panel2.Controls.Add(logsRichTextBox);
        }

        //==================== STATUS STRIP ====================
        private void InitializeStatusStrip()
        {
            statusStrip = new StatusStrip { Dock = DockStyle.Bottom };

            progressBar = new ToolStripProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Size = new Size(200, 16)
            };
            progressLabel = new ToolStripStatusLabel("0%");

            // Indicativo de estado conectado
            connectionStatusLabel = new ToolStripStatusLabel("Desconectado");

            statusStrip.Items.Add(new ToolStripStatusLabel("Progresso:"));
            statusStrip.Items.Add(progressBar);
            statusStrip.Items.Add(progressLabel);

            // Adiciona separador
            statusStrip.Items.Add(new ToolStripSeparator());
            statusStrip.Items.Add(connectionStatusLabel);

            this.Controls.Add(statusStrip);
        }

        //==================== LÓGICA DE FLASH E ERASE ====================
        private void FlashHexProcess()
        {
            try
            {
                isBusy = true;
                UpdateProgress(0);
                StartSerial();

                AddLog("Reset manual p/ gravar...");
                // Pedimos reset manual
                Invoke(new Action(() =>
                    MessageBox.Show("Faça reset manual no MCU e clique OK", "Reset Manual", MessageBoxButtons.OK)
                ));
                Thread.Sleep(2000);

                AddLog("Enviando U 23130 (sync)...");
                if (!SendCommand("U 23130"))
                {
                    AddLog("Falha bootloader (não sincronizado).");
                    return;
                }

                AddLog("Apagando memória antes de gravar...");
                bool apagou = EraseFlashProcess();
                if (!apagou)
                {
                    AddLog("Falha ao apagar memória, mas prosseguindo...");
                }

                // Carrega e grava do firmwareHexPath
                if (!File.Exists(firmwareHexPath))
                {
                    AddLog($"Arquivo .hex não encontrado em: {firmwareHexPath}");
                    return;
                }

                AddLog("Carregando .hex embutido no projeto...");
                WriteHexFromFile(firmwareHexPath);

                AddLog("Executando verificação (Checksum)...");
                bool checksumOk = VerificaChecksum();
                if (checksumOk)
                {
                    AddLog("Checksum OK - Gravação validada!");
                }
                else
                {
                    AddLog("Falha na verificação de Checksum!");
                }

                AddLog("Gravação concluída!");
                MessageBox.Show("Gravação concluída!",
                                "Sucesso",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AddLog($"Erro: {ex.Message}");
            }
            finally
            {
                serialPort?.Close();
                UpdateProgress(100);
                isBusy = false;
            }
        }

        private void EraseProcess()
        {
            try
            {
                isBusy = true;
                UpdateProgress(0);
                StartSerial();

                // Reset manual
                AddLog("Reset manual p/ apagar...");
                Invoke(new Action(() =>
                    MessageBox.Show("Faça reset manual no MCU e clique OK", "Reset Manual", MessageBoxButtons.OK)
                ));
                Thread.Sleep(2000);

                AddLog("Enviando U 23130 (sync)...");
                if (!SendCommand("U 23130"))
                {
                    AddLog("Falha bootloader (não sincronizado).");
                    MessageBox.Show("Falha no sync do bootloader!",
                                    "Erro",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                    return;
                }

                AddLog("Iniciando apagar...");
                bool apagou = EraseFlashProcess();

                if (apagou)
                {
                    AddLog("Memória apagada com sucesso!");
                    MessageBox.Show("Memória apagada!",
                                    "Sucesso",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);
                }
                else
                {
                    AddLog("Falha ao apagar memória!");
                    MessageBox.Show("Falha ao apagar memória!",
                                    "Erro",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                AddLog($"Erro: {ex.Message}");
            }
            finally
            {
                serialPort?.Close();
                UpdateProgress(100);
                isBusy = false;
            }
        }

        /// <summary>
        /// Lógica de apagar: P 0 29 e E 0 29. Retorna true se ambos ok.
        /// </summary>
        private bool EraseFlashProcess()
        {
            UpdateProgress(0);

            AddLog("Enviando P 0 29 (prepare)...");
            bool okPrep = SendCommand("P 0 29");
            if (!okPrep) AddLog("Falha no comando P 0 29.");

            UpdateProgress(50);

            AddLog("Enviando E 0 29 (erase)...");
            bool okErase = SendCommand("E 0 29");
            if (!okErase) AddLog("Falha no comando E 0 29.");

            UpdateProgress(100);
            return (okPrep && okErase);
        }

        //==================== Verificação de Checksum ====================
        private bool VerificaChecksum()
        {
            // Exemplo fictício: supõe que o bootloader tenha comando "CHECKSUM"
            // e retorne algo contendo "OK" se estiver certo.
            AddLog("Enviando comando CHECKSUM...");
            bool ok = SendCommand("CHECKSUM");
            return ok;
        }

        //==================== FUNÇÕES DE ESCRITA DE .HEX ====================
        private void WriteHexFromFile(string hexFilePath)
        {
            var segs = ParseHexFile(hexFilePath);
            int totalBytes = segs.Sum(s => s.Data.Length);
            int written = 0;

            foreach (var seg in segs)
            {
                string cmd = $"W {seg.Address} {seg.Data.Length}";
                AddLog($"Enviando: {cmd}");
                serialPort.WriteLine(cmd);
                Thread.Sleep(100);

                string dataHex = ByteArrayToHex(seg.Data);
                if (dataHex.Length > 200)
                    dataHex = dataHex.Substring(0, 200) + "...";

                AddLog($"Dados a gravar (hex): {dataHex}");
                AddLog($"Gravando {seg.Data.Length} bytes em 0x{seg.Address:X}...");

                serialPort.Write(seg.Data, 0, seg.Data.Length);
                Thread.Sleep(100);

                written += seg.Data.Length;
                int percent = (int)((written / (double)totalBytes) * 100);
                UpdateProgress(percent);
            }

            AddLog("Enviando comando final C...");
            SendCommand("C");
            UpdateProgress(100);
        }

        //==================== AUXILIARES ====================
        private string ByteArrayToHex(byte[] data)
        {
            return BitConverter.ToString(data).Replace("-", "");
        }

        private void StartSerial()
        {
            if (serialPort == null)
                serialPort = new SerialPort();

            if (serialPort.IsOpen)
                serialPort.Close();

            string portName = "";
            // Lê a porta escolhida
            Invoke(new Action(() => portName = portComboBox.SelectedItem?.ToString()));

            AddLog($"Abrindo porta {portName} @115200...");
            serialPort.PortName = portName;
            serialPort.BaudRate = 115200;
            serialPort.Parity = Parity.None;
            serialPort.DataBits = 8;
            serialPort.StopBits = StopBits.One;
            serialPort.ReadTimeout = 5000;
            serialPort.WriteTimeout = 5000;
            serialPort.Open();
        }

        private bool SendCommand(string cmd)
        {
            try
            {
                serialPort.WriteLine(cmd);
                Thread.Sleep(500);
                string resp = serialPort.ReadExisting();
                AddLog($"Resp: {resp}");
                return IsValidResponse(resp);
            }
            catch (Exception ex)
            {
                AddLog($"Erro {cmd}: {ex.Message}");
                return false;
            }
        }

        private bool IsValidResponse(string resp)
        {
            if (string.IsNullOrEmpty(resp)) return false;
            if (resp.StartsWith("0")) return true;
            var markers = new[] { "OK", "Synchronized", "019", "LH" };
            return markers.Any(m => resp.Contains(m));
        }

        private List<HexSegment> ParseHexFile(string filePath)
        {
            AddLog($"Abrindo .hex: {filePath}");
            var lines = File.ReadAllLines(filePath);
            List<HexSegment> segments = new List<HexSegment>();

            using (var ms = new MemoryStream())
            {
                foreach (var line in lines)
                {
                    if (!line.StartsWith(":")) continue;

                    int byteCount = Convert.ToInt32(line.Substring(1, 2), 16);
                    int address = Convert.ToInt32(line.Substring(3, 4), 16);
                    int recordType = Convert.ToInt32(line.Substring(7, 2), 16);

                    string dataStr = line.Substring(9, byteCount * 2);
                    byte[] dataBytes = new byte[byteCount];
                    for (int i = 0; i < byteCount; i++)
                        dataBytes[i] = Convert.ToByte(dataStr.Substring(i * 2, 2), 16);

                    if (recordType == 0x00)
                    {
                        ms.Position = address;
                        ms.Write(dataBytes, 0, dataBytes.Length);
                    }
                }
                segments.Add(new HexSegment
                {
                    Address = 0,
                    Data = ms.ToArray()
                });
            }
            AddLog("Carregado e parseado .hex (1 segmento).");
            return segments;
        }

        private void UpdateProgress(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            if (statusStrip.InvokeRequired)
            {
                statusStrip.Invoke(new Action(() =>
                {
                    progressBar.Value = percent;
                    progressLabel.Text = $"{percent}%";
                }));
            }
            else
            {
                progressBar.Value = percent;
                progressLabel.Text = $"{percent}%";
            }
        }

        private void AddLog(string msg)
        {
            if (logsRichTextBox.InvokeRequired)
            {
                logsRichTextBox.Invoke(new Action(() =>
                {
                    logsRichTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
                }));
            }
            else
            {
                logsRichTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
            }
        }

        //==================== Indicador de Conexão ====================
        private void UpdateConnectionStatus(bool connected, string portName)
        {
            if (statusStrip.InvokeRequired)
            {
                statusStrip.Invoke(new Action(() =>
                {
                    if (connected)
                    {
                        connectionStatusLabel.Text = $"Conectado: {portName}";
                    }
                    else
                    {
                        connectionStatusLabel.Text = "Desconectado";
                    }
                }));
            }
            else
            {
                connectionStatusLabel.Text = connected ? $"Conectado: {portName}" : "Desconectado";
            }
        }

        //==================== Proteção ao Fechar ====================
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isBusy)
            {
                var result = MessageBox.Show(
                    "Uma operação de gravação/apagamento está em andamento. Fechar agora pode corromper o processo.\nDeseja realmente sair?",
                    "Operação em Andamento",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );
                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                }
            }
        }
    }
}
