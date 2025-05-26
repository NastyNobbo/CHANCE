using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ChatServer
{
    public partial class MainWindow : Window
    {
        private TcpListener listener;
        private readonly ConcurrentDictionary<string, ClientInfo> clients = new();
        private const string DbFile = "chat_users.db";
        private readonly string ConnectionString = $"Data Source={DbFile}";

        public MainWindow()
        {
            InitializeComponent();
            InitializeDatabase();
            StartServer();
        }

        private void StartServer()
        {
            Task.Run(() =>
            {
                try
                {
                    int port = 56000;
                    IPAddress ip = GetLocalIPAddress();
                    listener = new TcpListener(ip, port);
                    listener.Start();
                    Log($"Сервер запущен на {ip}:{port}");

                    while (true)
                    {
                        TcpClient client = listener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(ClientHandler, client);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Ошибка запуска сервера: {ex.Message}");
                }
            });
        }

        private void ClientHandler(object obj)
        {
            TcpClient tcpClient = (TcpClient)obj;
            NetworkStream stream = tcpClient.GetStream();
            string login = null;
            string userName = null;

            try
            {
                byte[] buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) return;

                string requestJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var request = JsonSerializer.Deserialize<Message>(requestJson);
                if (request == null || string.IsNullOrEmpty(request.Login) || string.IsNullOrEmpty(request.Password)) return;

                login = request.Login.Trim();
                string password = request.Password;

                if (request.Type == "register")
                {
                    userName = request.Name.Trim();
                    if (UserExists(login))
                    {
                        SendMessage(stream, new Message { Type = "register_fail", Text = "Логин уже существует." });
                    }
                    else
                    {
                        if (!AddUser(login, userName, password))
                        {
                            SendMessage(stream, new Message { Type = "register_fail", Text = "Ошибка при регистрации." });
                        }
                        else
                        {
                            SendMessage(stream, new Message { Type = "register_success", Text = "Регистрация успешна." });
                        }
                    }
                    tcpClient.Close();
                    return;
                }

                if (request.Type == "login")
                {
                    if (!UserExists(login))
                    {
                        SendMessage(stream, new Message { Type = "login_fail", Text = "Пользователь не найден." });
                        tcpClient.Close();
                        return;
                    }
                    if (!VerifyUserPassword(login, password))
                    {
                        SendMessage(stream, new Message { Type = "login_fail", Text = "Неверный пароль." });
                        tcpClient.Close();
                        return;
                    }

                    userName = GetUsernameByLogin(login);
                    if (!clients.TryAdd(userName, new ClientInfo { Name = userName, Client = tcpClient, Stream = stream }))
                    {
                        SendMessage(stream, new Message { Type = "login_fail", Text = "Пользователь уже подключен." });
                        tcpClient.Close();
                        return;
                    }

                    Log($"{userName} вошёл в систему.");
                    UpdateClientList();

                    while (true)
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;
                        string msgJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        var message = JsonSerializer.Deserialize<Message>(msgJson);
                        if (message?.Type == "logout") break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка клиента {userName}: {ex.Message}");
            }
            finally
            {
                if (userName != null)
                {
                    clients.TryRemove(userName, out _);
                    UpdateClientList();
                    Log($"Пользователь {userName} отключён.");
                }
                tcpClient.Close();
            }
        }

        private void SendMessage(NetworkStream stream, Message message)
        {
            string json = JsonSerializer.Serialize(message);
            byte[] data = Encoding.UTF8.GetBytes(json);
            stream.Write(data, 0, data.Length);
        }

        private void UpdateClientList()
        {
            Dispatcher.Invoke(() =>
            {
                ClientList.ItemsSource = null;
                ClientList.ItemsSource = clients.Values.ToList();
            });
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                LogTextBox.ScrollToEnd();
            });
        }

        private void InitializeDatabase()
        {
            using var connection = new SQLiteConnection(ConnectionString);
            connection.Open();
            string sql = @"
                CREATE TABLE IF NOT EXISTS users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT NOT NULL,
                    login TEXT UNIQUE NOT NULL,
                    password_hash TEXT NOT NULL
                );";
            using var cmd = new SQLiteCommand(sql, connection);
            cmd.ExecuteNonQuery();
        }

        private bool UserExists(string login)
        {
            using var connection = new SQLiteConnection(ConnectionString);
            connection.Open();
            var cmd = new SQLiteCommand("SELECT COUNT(*) FROM users WHERE login = @login", connection);
            cmd.Parameters.AddWithValue("@login", login);
            return (long)(cmd.ExecuteScalar() ?? 0) > 0;
        }

        private bool VerifyUserPassword(string login, string password)
        {
            using var connection = new SQLiteConnection(ConnectionString);
            connection.Open();
            var cmd = new SQLiteCommand("SELECT password_hash FROM users WHERE login = @login", connection);
            cmd.Parameters.AddWithValue("@login", login);
            var result = cmd.ExecuteScalar();
            return result != null && (string)result == ComputeHash(password);
        }

        private string GetUsernameByLogin(string login)
        {
            using var connection = new SQLiteConnection(ConnectionString);
            connection.Open();
            var cmd = new SQLiteCommand("SELECT username FROM users WHERE login = @login", connection);
            cmd.Parameters.AddWithValue("@login", login);
            var result = cmd.ExecuteScalar();
            return result?.ToString() ?? login;
        }

        private bool AddUser(string login, string username, string password)
        {
            try
            {
                using var connection = new SQLiteConnection(ConnectionString);
                connection.Open();
                var cmd = new SQLiteCommand("INSERT INTO users (login, username, password_hash) VALUES (@l, @u, @p)", connection);
                cmd.Parameters.AddWithValue("@l", login);
                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@p", ComputeHash(password));
                cmd.ExecuteNonQuery();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string ComputeHash(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private IPAddress GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ip;
            }
            return IPAddress.Loopback;
        }
    }

    public class ClientInfo
    {
        public string Name { get; set; }
        public TcpClient Client { get; set; }
        public NetworkStream Stream { get; set; }
    }

    public class Message
    {
        public string Type { get; set; }
        public string Login { get; set; }
        public string Password { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }
    }
}
