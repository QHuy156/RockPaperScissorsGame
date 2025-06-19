using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;

namespace RockPaperScissorsClient
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var loginForm = new LoginForm();
            if (loginForm.ShowDialog() == DialogResult.OK)
            {
                var mainForm = new GameForm(loginForm.PlayerName);
                Application.Run(mainForm);
            }
        }
    }

    public class LoginForm : Form
    {
        public string PlayerName { get; private set; }
        private TextBox nameTextBox;
        private Button submitButton;

        public LoginForm()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "Login";
            this.Size = new System.Drawing.Size(300, 200);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;

            var label = new Label
            {
                Text = "Enter your name:",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(250, 20)
            };

            nameTextBox = new TextBox
            {
                Location = new System.Drawing.Point(20, 50),
                Size = new System.Drawing.Size(240, 20)
            };

            submitButton = new Button
            {
                Text = "Start",
                Location = new System.Drawing.Point(20, 90),
                Size = new System.Drawing.Size(240, 30)
            };
            submitButton.Click += SubmitButton_Click;

            this.Controls.Add(label);
            this.Controls.Add(nameTextBox);
            this.Controls.Add(submitButton);
        }

        private void SubmitButton_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(nameTextBox.Text))
            {
                PlayerName = nameTextBox.Text;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show("Please enter your name!");
            }
        }
    }

    public class GameForm : Form
    {
        private TcpClient client;
        private NetworkStream stream;
        private readonly string playerName;
        private Thread receiveThread;
        private Label statusLabel;
        private Label opponentLabel;
        private Button rockButton;
        private Button paperButton;
        private Button scissorsButton;

        public GameForm(string name)
        {
            this.playerName = name;
            InitializeComponents();
            ConnectToServer();
        }

        private void InitializeComponents()
        {
            this.Text = $"Rock-Paper-Scissors - {playerName}";
            this.Size = new System.Drawing.Size(400, 300);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;

            statusLabel = new Label
            {
                Text = "Connecting to server...",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(350, 30),
                Font = new System.Drawing.Font("Arial", 12, System.Drawing.FontStyle.Bold)
            };

            opponentLabel = new Label
            {
                Text = "Opponent: None",
                Location = new System.Drawing.Point(20, 60),
                Size = new System.Drawing.Size(350, 20)
            };

            rockButton = new Button
            {
                Text = "✊ Rock",
                Tag = "Rock",
                Location = new System.Drawing.Point(20, 100),
                Size = new System.Drawing.Size(100, 50),
                Font = new System.Drawing.Font("Arial", 12),
                Enabled = false
            };
            rockButton.Click += ChoiceButton_Click;

            paperButton = new Button
            {
                Text = "✋ Paper",
                Tag = "Paper",
                Location = new System.Drawing.Point(140, 100),
                Size = new System.Drawing.Size(100, 50),
                Font = new System.Drawing.Font("Arial", 12),
                Enabled = false
            };
            paperButton.Click += ChoiceButton_Click;

            scissorsButton = new Button
            {
                Text = "✌ Scissors",
                Tag = "Scissors",
                Location = new System.Drawing.Point(260, 100),
                Size = new System.Drawing.Size(100, 50),
                Font = new System.Drawing.Font("Arial", 12),
                Enabled = false
            };
            scissorsButton.Click += ChoiceButton_Click;

            this.Controls.Add(statusLabel);
            this.Controls.Add(opponentLabel);
            this.Controls.Add(rockButton);
            this.Controls.Add(paperButton);
            this.Controls.Add(scissorsButton);

            this.FormClosing += GameForm_FormClosing;
        }

        private void ConnectToServer()
        {
            try
            {
                client = new TcpClient();
                client.Connect("127.0.0.1", 8888);
                stream = client.GetStream();

                byte[] nameData = Encoding.ASCII.GetBytes(playerName);
                stream.Write(nameData, 0, nameData.Length);

                receiveThread = new Thread(ReceiveMessages);
                receiveThread.IsBackground = true;
                receiveThread.Start();

                statusLabel.Text = "Connected. Waiting for opponent...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection error: {ex.Message}");
                this.Close();
            }
        }

        private void ReceiveMessages()
        {
            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    string message = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    ProcessServerMessage(message);
                }
            }
            catch (Exception ex)
            {
                this.Invoke((MethodInvoker)delegate {
                    statusLabel.Text = "Disconnected from server";
                    rockButton.Enabled = false;
                    paperButton.Enabled = false;
                    scissorsButton.Enabled = false;
                });
            }
        }

        private void ProcessServerMessage(string message)
        {
            string[] parts = message.Split('|');
            string command = parts[0];
            string content = parts.Length > 1 ? parts[1] : "";

            this.Invoke((MethodInvoker)delegate {
                switch (command)
                {
                    case "MATCHED":
                        opponentLabel.Text = $"Opponent: {content}";
                        statusLabel.Text = "Game started!";
                        rockButton.Enabled = true;
                        paperButton.Enabled = true;
                        scissorsButton.Enabled = true;
                        break;
                    case "CHOOSE":
                        statusLabel.Text = content;
                        break;
                    case "WIN":
                        statusLabel.Text = $"🎉 {content}";
                        statusLabel.ForeColor = Color.Green;
                        MessageBox.Show(content, "You Win!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        break;
                    case "LOSE":
                        statusLabel.Text = $"😞 {content}";
                        statusLabel.ForeColor = Color.Red;
                        MessageBox.Show(content, "You Lose", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        break;
                    case "DRAW":
                        statusLabel.Text = $"🤝 {content}";
                        statusLabel.ForeColor = Color.Blue;
                        MessageBox.Show(content, "Draw", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        break;
                    case "OPPONENT_DISCONNECTED":
                        statusLabel.Text = "⚠ " + content;
                        opponentLabel.Text = "Opponent: None";
                        rockButton.Enabled = false;
                        paperButton.Enabled = false;
                        scissorsButton.Enabled = false;
                        MessageBox.Show(content, "Opponent Disconnected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        break;
                }
            });
        }

        private void ChoiceButton_Click(object sender, EventArgs e)
        {
            var button = (Button)sender;
            string choice = button.Tag.ToString();

            try
            {
                byte[] data = Encoding.ASCII.GetBytes(choice);
                stream.Write(data, 0, data.Length);
                statusLabel.Text = $"You chose: {choice}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending choice: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GameForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            receiveThread?.Interrupt();
            stream?.Close();
            client?.Close();
        }
    }
}