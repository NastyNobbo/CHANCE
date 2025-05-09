using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;
using System.Reflection.Metadata;
using System.Security.Claims;
using System.Windows;
using System.Xml.Linq;
using System.Net.NetworkInformation;
using System.Data.SQLite;
using System.Security.Cryptography;

namespace ConsoleApp1
{
    class Program
    {
        static TcpListener listener;
        // userName -> ClientInfo
        static ConcurrentDictionary<string, ClientInfo> clients = new ConcurrentDictionary<string, ClientInfo>();

        // Сохранять диалоги: ключ - пара пользователей в отсортированном виде, value - список сообщений
        // Сообщения хранятся как строки с форматом: "Отправитель: Текст"
        static ConcurrentDictionary<string, List<string>> dialogs = new ConcurrentDictionary<string, List<string>>();

        const string DbFile = "chat_users.db";
        static string ConnectionString = $"Data Source={DbFile}";

        static void Main(string[] args)
        {
            Console.WriteLine("Сервер запускается...");

            InitializeDatabase();

            const int port = 56000;
            IPAddress myIp = ConnectionData.GetCorrectLocalIPv4();
            listener = new TcpListener(myIp, port);
            listener.Start();
            Console.WriteLine($"Сервер запущен на порту {port}");
            

            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(ClientHandler, client);
            }
        }
        static void InitializeDatabase()
        {
            using var connection = new SQLiteConnection(ConnectionString);
            connection.Open();
            string createUsersTableSql = @"
                CREATE TABLE IF NOT EXISTS users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT UNIQUE NOT NULL,
                    password_hash TEXT NOT NULL
                );";
            string createDialogsTableSql = @"
                CREATE TABLE IF NOT EXISTS dialogs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    dialog_key TEXT NOT NULL,
                    sender TEXT NOT NULL,
                    message TEXT NOT NULL,
                    sent_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );";
            using var cmd = new SQLiteCommand(createUsersTableSql, connection);
            cmd.ExecuteNonQuery();
            cmd.CommandText = createDialogsTableSql;
            cmd.ExecuteNonQuery();
        }

        static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hashBytes);
        }
        static bool UserExists(string username)
        {
            using var connection = new SQLiteConnection(ConnectionString);
            connection.Open();
            string sql = "SELECT COUNT(*) FROM users WHERE username = @username;";
            using var cmd = new SQLiteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@username", username);
            long count = (long)(cmd.ExecuteScalar() ?? 0);
            return count > 0;
        }

        static bool VerifyUserPassword(string username, string password)
        {
            using var connection = new SQLiteConnection(ConnectionString);
            connection.Open();
            string sql = "SELECT password_hash FROM users WHERE username = @username;";
            using var cmd = new SQLiteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@username", username);
            var result = cmd.ExecuteScalar();
            if (result == null) return false;
            string storedHash = (string)result;
            string inputHash = ComputeHash(password);
            return storedHash == inputHash;
        }

        static bool AddUser(string username, string password)
        {
            try
            {
                using var connection = new SQLiteConnection(ConnectionString);
                connection.Open();
                string insertSql = "INSERT INTO users (username, password_hash) VALUES (@username, @password_hash);";
                using var cmd = new SQLiteCommand(insertSql, connection);
                cmd.Parameters.AddWithValue("@username", username);
                string hash = ComputeHash(password);
                cmd.Parameters.AddWithValue("@password_hash", hash);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка добавления пользователя: {ex.Message}");
                return false;
            }
        }
        static void UpdateUserPassword(string username, string newHash)
        {
            try
            {
                using var connection = new SQLiteConnection(ConnectionString);
                connection.Open();
                string updateSql = "UPDATE users SET password_hash = @password_hash WHERE username = @username;";
                using var cmd = new SQLiteCommand(updateSql, connection);
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@password_hash", newHash);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обновления пароля: {ex.Message}");
            }
        }

        static bool AddUserIfNotExists(string username)
        {
            try
            {
                using var connection = new SQLiteConnection(ConnectionString);
                connection.Open();
                // Проверяем есть ли пользователь
                string checkSql = "SELECT COUNT(*) FROM users WHERE username = @username;";
                using var checkCmd = connection.CreateCommand();
                checkCmd.CommandText = checkSql;
                checkCmd.Parameters.AddWithValue("@username", username);
                long count = (long)checkCmd.ExecuteScalar();
                if (count > 0)
                {
                    return true; // Уже есть
                }
                // Вставляем нового пользователя
                string insertSql = "INSERT INTO users (username) VALUES (@username);";
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = insertSql;
                insertCmd.Parameters.AddWithValue("@username", username);
                insertCmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при работе с БД: {ex.Message}");
                return false;
            }
        }

        static void SaveDialogMessage(string dialogKey, string sender, string message)
        {
            try
            {
                using var connection = new SQLiteConnection(ConnectionString);
                connection.Open();
                string insertSql = "INSERT INTO dialogs (dialog_key, sender, message) VALUES (@dialog_key, @sender, @message);";
                using var cmd = new SQLiteCommand(insertSql, connection);
                cmd.Parameters.AddWithValue("@dialog_key", dialogKey);
                cmd.Parameters.AddWithValue("@sender", sender);
                cmd.Parameters.AddWithValue("@message", message);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения сообщения чата: {ex.Message}");
            }
        }
        static List<string> LoadDialogMessages(string dialogKey)
        {
            try
            {
                using var connection = new SQLiteConnection(ConnectionString);
                connection.Open();
                string selectSql = "SELECT sender, message, sent_at FROM dialogs WHERE dialog_key = @dialog_key ORDER BY sent_at ASC;";
                using var cmd = new SQLiteCommand(selectSql, connection);
                cmd.Parameters.AddWithValue("@dialog_key", dialogKey);
                List<string> messages = new List<string>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string sender = reader.GetString(0);
                    string message = reader.GetString(1);
                    DateTime sentAt = reader.GetDateTime(2);
                    messages.Add($"{sentAt:yyyy-MM-dd HH:mm:ss} {sender}: {message}");
                }
                return messages;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки сообщений чата: {ex.Message}");
                return new List<string>();
            }
        }
        static List<string> GetAllUsersFromDatabase()
        {
            var users = new List<string>();
            try
            {
                using var connection = new SQLiteConnection(ConnectionString);
                connection.Open();
                string selectSql = "SELECT username FROM users ORDER BY username;";
                using var cmd = new SQLiteCommand(selectSql, connection);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    users.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении списка пользователей из БД: {ex.Message}");
            }
            return users;
        }
        static void ClientHandler(object obj)
        {
            TcpClient tcpClient = (TcpClient)obj;
            NetworkStream stream = tcpClient.GetStream();
            string userName = null;
            try
            {
                byte[] buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) throw new Exception("Пустой запрос");
                string requestJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var request = JsonSerializer.Deserialize<Message>(requestJson);
                if (request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrEmpty(request.Password))
                    throw new Exception("Некорректный запрос");
                userName = request.Name.Trim();
                string password = request.Password;

                if (request.Type == "register")
                {
                    if (UserExists(userName))
                    {
                        SendMessage(stream, new Message { Type = "register_fail", Text = "Пользователь с таким именем уже существует." });
                        tcpClient.Close();
                        return;
                    }
                    else
                    {
                        if (!AddUser(userName, password))
                        {
                            SendMessage(stream, new Message { Type = "register_fail", Text = "Ошибка при регистрации." });
                            tcpClient.Close();
                            return;
                        }
                        SendMessage(stream, new Message { Type = "register_success", Text = "Регистрация успешна." });
                    }
                    tcpClient.Close();
                    return;
                }

                else if (request.Type == "login")
                {
                    if (!UserExists(userName))
                    {
                        SendMessage(stream, new Message { Type = "login_fail", Text = "Пользователь не найден." });
                        tcpClient.Close();
                        return;
                    }
                    if (!VerifyUserPassword(userName, password))
                    {
                        SendMessage(stream, new Message { Type = "login_fail", Text = "Неверный пароль." });
                        tcpClient.Close();
                        return;
                    }
                    if (!clients.TryAdd(userName, new ClientInfo { Name = userName, Client = tcpClient, Stream = stream }))
                    {
                        SendMessage(stream, new Message { Type = "login_fail", Text = "Пользователь уже подключен." });
                        tcpClient.Close();
                        return;
                    }
                    SendMessage(stream, new Message { Type = "login_success", Text = "Вход успешен." });
                    Console.WriteLine($"Пользователь {userName} вошел");



                    // Отправляем обновленный список пользователей всем клиентам
                    BroadcastUserList();

                    // Отправляем клиенту историю диалогов по запросу (если был такой тип)
                    // Но пока нет запроса на это, можем отправлять диалоги по запросу клиента при расширении функционала

                    // Обрабатываем входящие сообщения от клиента
                    while (true)
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;
                        string msgJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        var message = JsonSerializer.Deserialize<Message>(msgJson);
                        if (message == null) continue;
                        if (message.Type == "message")
                        {
                            string dialogKey = GetDialogKey(userName, message.To);
                            string formattedMsg = $"{userName}: {message.Text}";
                            SaveDialogMessage(dialogKey, userName, message.Text);
                            if (!string.IsNullOrWhiteSpace(message.To) && clients.TryGetValue(message.To, out var toClient))
                            {
                                SendMessage(toClient.Stream, new Message
                                {
                                    Type = "message",
                                    From = userName,
                                    To = message.To,
                                    Text = message.Text
                                });
                            }
                        }
                        else if (message.Type == "getdialog")
                        {
                            string dialogKey = GetDialogKey(userName, message.To);
                            var dialogMessages = LoadDialogMessages(dialogKey);
                            SendMessage(stream, new Message
                            {
                                Type = "dialog",
                                From = message.To,
                                To = userName,
                                DialogMessages = dialogMessages
                            });
                        }
                        else if (request.Type == "reset_password")
                        {
                            if (!UserExists(userName))
                            {
                                SendMessage(stream, new Message { Type = "reset_password_fail", Text = "Пользователь не найден." });
                                continue;
                            }
                            string newHash = ComputeHash(password);
                            UpdateUserPassword(userName, newHash);
                            SendMessage(stream, new Message { Type = "reset_password_success", Text = "Пароль успешно сброшен." });
                        }
                    }
                }
                else if (request.Type == "reset_password")
                {
                    if (!UserExists(userName))
                    {
                        SendMessage(stream, new Message { Type = "reset_password_fail", Text = "Пользователь не найден." });
                        tcpClient.Close();
                        return;
                    }
                    string newHash = ComputeHash(password);
                    UpdateUserPassword(userName, newHash);
                    SendMessage(stream, new Message { Type = "reset_password_success", Text = "Пароль успешно сброшен." });
                    tcpClient.Close();
                    return;
                }
                else
                {
                    SendMessage(stream, new Message { Type = "error", Text = "Неверный тип запроса" });
                    tcpClient.Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка клиента {userName}: {ex.Message}");
            }
            finally
            {
                if (userName != null)
                {
                    clients.TryRemove(userName, out _);
                    BroadcastUserList();
                    Console.WriteLine($"Пользователь {userName} отключился");
                }
                tcpClient.Close();
            }
        }

        static void BroadcastUserList()
        {
            var allUsers = GetAllUsersFromDatabase();
            var userListMessage = new Message
            {
                Type = "userlist",
                Users = allUsers
            };
            string json = JsonSerializer.Serialize(userListMessage);
            byte[] data = Encoding.UTF8.GetBytes(json);
            foreach (var kvp in clients)
            {
                try
                {
                    kvp.Value.Stream.Write(data, 0, data.Length);
                }
                catch
                {
                    // Игнорируем ошибки отправки
                }
            }
        }

        static void SendMessage(NetworkStream stream, Message message)
        {
            string json = JsonSerializer.Serialize(message);
            byte[] data = Encoding.UTF8.GetBytes(json);
            stream.Write(data, 0, data.Length);
        }

        // Генерация ключа для диалога - "UserA|UserB" в алфавитном порядке
        static string GetDialogKey(string user1, string user2)
        {
            var arr = new string[] { user1, user2 };
            Array.Sort(arr, StringComparer.Ordinal);
            return $"{arr[0]}|{arr[1]}";
        }
    }

    class ClientInfo
    {
        public string Name { get; set; }
        public TcpClient Client { get; set; }
        public NetworkStream Stream { get; set; }
    }

    class Message
    {
        public string Type { get; set; } // register, register_success, register_fail, login, login_success, login_fail, message, userlist, getdialog, dialog, error
        public string Name { get; set; } // Для login/register
        public string Password { get; set; }
        public string From { get; set; } // для message
        public string To { get; set; } // для message или getdialog
        public string Text { get; set; } // для message, error
        public List<string> Users { get; set; } // для userlist
        public List<string> DialogMessages { get; set; } // для dialog
    }
}