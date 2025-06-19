using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RockPaperScissorsServer
{
    public class GameServer
    {
        private readonly List<Player> players = new List<Player>();
        private readonly object lockObject = new object();
        private int gameIdCounter = 1;
        private TcpListener? listener;
        public Action<string>? LogAction { get; set; }

        public void Start()
        {
            IPAddress ip = IPAddress.Parse("127.0.0.1");
            listener = new TcpListener(ip, 8888);
            listener.Start();
            LogAction?.Invoke("Server started on port 8888");

            while (true)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    LogAction?.Invoke("New client connected");
                    Thread clientThread = new Thread(HandleClient);
                    clientThread.Start(client);
                }
                catch (Exception ex)
                {
                    LogAction?.Invoke($"Server error: {ex.Message}");
                    break;
                }
            }
        }

        public void Stop()
        {
            listener?.Stop();
        }

        private void HandleClient(object? obj)
        {
            if (obj is not TcpClient client) return;

            NetworkStream? stream = client.GetStream();
            Player? player = null;

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
                        player = new Player(client, stream, message);
                        lock (lockObject)
                        {
                            players.Add(player);
                        }
                        SendMessage(stream, $"Welcome, {player.Name}! Waiting for opponent...");
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
                LogAction?.Invoke($"Client error: {ex.Message}");
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
                LogAction?.Invoke("Client disconnected");
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

        private void DetermineWinner(Player player1, Player player2)
        {
            if (player1.Choice == null || player2.Choice == null) return;

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

            LogAction?.Invoke($"Game result: {player1.Name} ({player1.Choice}) vs {player2.Name} ({player2.Choice}) - " +
                            $"{result1.Split('|')[0]}/{result2.Split('|')[0]}");

            player1.HasMadeChoice = false;
            player2.HasMadeChoice = false;
            player1.Choice = null;
            player2.Choice = null;
        }

        private void NotifyOpponentDisconnected(Player disconnectedPlayer)
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
                    LogAction?.Invoke($"Notified {opponent.Name} about disconnection");
                }
            }
        }

        private static void SendMessage(NetworkStream stream, string message)
        {
            try
            {
                byte[] data = Encoding.ASCII.GetBytes(message);
                stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message: {ex.Message}");
            }
        }
    }

    public class Player
    {
        public TcpClient Client { get; }
        public NetworkStream Stream { get; }
        public string Name { get; }
        public int GameId { get; set; }
        public bool HasMadeChoice { get; set; }
        public string? Choice { get; set; }

        public Player(TcpClient client, NetworkStream stream, string name)
        {
            Client = client;
            Stream = stream;
            Name = name;
            GameId = 0;
            HasMadeChoice = false;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Rock-Paper-Scissors Server";
            var server = new GameServer();
            server.LogAction = Console.WriteLine;
            
            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal server error: {ex}");
            }
        }
    }
}