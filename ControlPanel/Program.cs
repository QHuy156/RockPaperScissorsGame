using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.IO; 
namespace RockPaperScissorsControlPanel
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainControlForm());
        }
    }

    public class GamePlayer
    {
        public TcpClient Client { get; }
        public NetworkStream Stream { get; }
        public string Name { get; }
        public int GameId { get; set; }
        public bool HasMadeChoice { get; set; }
        public string Choice { get; set; }

        public GamePlayer(TcpClient client, NetworkStream stream, string name)
        {
            Client = client;
            Stream = stream;
            Name = name;
            GameId = 0;
            HasMadeChoice = false;
            Choice = null;
        }
    }

    public class GameServer
    {
        private readonly List<GamePlayer> players = new List<GamePlayer>();
        private readonly object lockObject = new object();
        private int gameIdCounter = 1;
        private TcpListener listener;
        private bool isRunning;

        public Action<string> LogAction { get; set; }

        public void Start()
        {
            isRunning = true;
            IPAddress ip = IPAddress.Parse("127.0.0.1");
            listener = new TcpListener(ip, 8888);
            listener.Start();
            
            LogAction?.Invoke("Server started. Waiting for connections...");

            while (isRunning)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    Thread clientThread = new Thread(() => HandleClient(client));
                    clientThread.Start();
                    LogAction?.Invoke("New client connected");
                }
                catch (SocketException)
                {
                    // Server stopped
                }
            }
        }

        public void Stop()
        {
            isRunning = false;
            listener?.Stop();
        }

        private void HandleClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = client.GetStream();
            GamePlayer player = null;

            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    string message = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    LogAction?.Invoke($"Received: {message}");

                    if (player == null)
                    {
                        player = new GamePlayer(client, stream, message);
                        lock (lockObject)
                        {
                            players.Add(player);
                        }
                        SendMessage(player.Stream, $"Welcome, {player.Name}! Waiting for opponent...");
                        MatchPlayers();
                    }
                    else
                    {
                        player.Choice = message;
                        player.HasMadeChoice = true;
                        CheckGameStatus(player.GameId);
                    }
                }
            }
            catch (Exception ex)
            {
                LogAction?.Invoke($"Error: {ex.Message}");
            }
            finally
            {
                if (player != null)
                {
                    lock (lockObject)
                    {
                        players.Remove(player);
                    }
                    if (player.GameId != 0)
                    {
                        NotifyOpponentDisconnected(player);
                    }
                }
                client.Close();
                LogAction?.Invoke("Client disconnected.");
            }
        }

        private void MatchPlayers()
        {
            lock (lockObject)
            {
                var waitingPlayers = players.Where(p => p.GameId == 0).ToList();
                if (waitingPlayers.Count >= 2)
                {
                    int gameId = gameIdCounter++;
                    waitingPlayers[0].GameId = gameId;
                    waitingPlayers[1].GameId = gameId;

                    SendMessage(waitingPlayers[0].Stream, $"MATCHED|Game started! Your opponent is {waitingPlayers[1].Name}");
                    SendMessage(waitingPlayers[1].Stream, $"MATCHED|Game started! Your opponent is {waitingPlayers[0].Name}");

                    SendMessage(waitingPlayers[0].Stream, "CHOOSE|Rock, Paper, or Scissors?");
                    SendMessage(waitingPlayers[1].Stream, "CHOOSE|Rock, Paper, or Scissors?");
                    
                    LogAction?.Invoke($"Matched players: {waitingPlayers[0].Name} vs {waitingPlayers[1].Name}");
                }
            }
        }

        private void CheckGameStatus(int gameId)
        {
            lock (lockObject)
            {
                var gamePlayers = players.Where(p => p.GameId == gameId).ToList();
                if (gamePlayers.Count == 2 && gamePlayers.All(p => p.HasMadeChoice))
                {
                    DetermineWinner(gamePlayers[0], gamePlayers[1]);
                }
            }
        }

        private void DetermineWinner(GamePlayer player1, GamePlayer player2)
        {
            string result1, result2;

            if (player1.Choice == player2.Choice)
            {
                result1 = result2 = "DRAW|It's a draw!";
            }
            else if ((player1.Choice == "Rock" && player2.Choice == "Scissors") ||
                     (player1.Choice == "Paper" && player2.Choice == "Rock") ||
                     (player1.Choice == "Scissors" && player2.Choice == "Paper"))
            {
                result1 = $"WIN|You win! {player1.Choice} beats {player2.Choice}";
                result2 = $"LOSE|You lose! {player1.Choice} beats {player2.Choice}";
            }
            else
            {
                result1 = $"LOSE|You lose! {player2.Choice} beats {player1.Choice}";
                result2 = $"WIN|You win! {player2.Choice} beats {player1.Choice}";
            }

            SendMessage(player1.Stream, result1);
            SendMessage(player2.Stream, result2);

            player1.HasMadeChoice = false;
            player2.HasMadeChoice = false;
            player1.Choice = null;
            player2.Choice = null;

            SendMessage(player1.Stream, "CHOOSE|Rock, Paper, or Scissors?");
            SendMessage(player2.Stream, "CHOOSE|Rock, Paper, or Scissors?");
            
            LogAction?.Invoke($"Game result: {player1.Name} ({player1.Choice}) vs {player2.Name} ({player2.Choice})");
        }

        private void NotifyOpponentDisconnected(GamePlayer disconnectedPlayer)
        {
            lock (lockObject)
            {
                var opponent = players.FirstOrDefault(p => p.GameId == disconnectedPlayer.GameId && p != disconnectedPlayer);
                if (opponent != null)
                {
                    SendMessage(opponent.Stream, "OPPONENT_DISCONNECTED|Your opponent has disconnected.");
                    opponent.GameId = 0;
                    opponent.HasMadeChoice = false;
                    opponent.Choice = null;
                }
            }
        }

        private void SendMessage(NetworkStream stream, string message)
        {
            byte[] data = Encoding.ASCII.GetBytes(message);
            stream.Write(data, 0, data.Length);
        }
    }

    public class MainControlForm : Form
    {
        private Button startServerButton;
        private Button startClientButton;
        private TextBox logTextBox;
        private GameServer server;
        private Thread serverThread;

        public MainControlForm()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "Game Control Panel";
            this.Size = new System.Drawing.Size(600, 400);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.StartPosition = FormStartPosition.CenterScreen;

            startServerButton = new Button
            {
                Text = "Start Server",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(150, 40),
                Font = new System.Drawing.Font("Arial", 10)
            };
            startServerButton.Click += StartServerButton_Click;

            startClientButton = new Button
            {
                Text = "Add Player",
                Location = new System.Drawing.Point(190, 20),
                Size = new System.Drawing.Size(150, 40),
                Font = new System.Drawing.Font("Arial", 10),
                Enabled = false
            };
            startClientButton.Click += StartClientButton_Click;

            logTextBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new System.Drawing.Point(20, 80),
                Size = new System.Drawing.Size(540, 260),
                Font = new System.Drawing.Font("Arial", 10),
                ReadOnly = true
            };

            this.Controls.Add(startServerButton);
            this.Controls.Add(startClientButton);
            this.Controls.Add(logTextBox);

            this.FormClosing += MainControlForm_FormClosing;
        }

        private void StartServerButton_Click(object sender, EventArgs e)
        {
            if (server == null)
            {
                server = new GameServer();
                server.LogAction = LogMessage;
                
                serverThread = new Thread(() => 
                {
                    try 
                    {
                        server.Start();
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Server error: {ex.Message}");
                    }
                });
                serverThread.IsBackground = true;
                serverThread.Start();

                startServerButton.Text = "Stop Server";
                startClientButton.Enabled = true;
                LogMessage("Server started on port 8888");
            }
            else
            {
                server.Stop();
                server = null;
                startServerButton.Text = "Start Server";
                startClientButton.Enabled = false;
                LogMessage("Server stopped");
            }
        }

       private void StartClientButton_Click(object sender, EventArgs e)
{
    try
    {
        // Sửa đường dẫn tuyệt đối
        string clientProjectPath = Path.Combine(
            Directory.GetCurrentDirectory(), 
            "Client", 
            "Client.csproj");
        
        // Kiểm tra file tồn tại
        if (!File.Exists(clientProjectPath))
        {
            MessageBox.Show($"Không tìm thấy file: {clientProjectPath}");
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{clientProjectPath}\"",
            UseShellExecute = true,
            CreateNoWindow = false
        };
        
        Process.Start(startInfo);
    }
    catch (Exception ex)
    {
        LogMessage($"Lỗi khi mở client: {ex.Message}");
    }
}
        private void LogMessage(string message)
        {
            this.Invoke((MethodInvoker)delegate {
                logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            });
        }

        private void MainControlForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (server != null)
            {
                server.Stop();
            }
        }
    }
}