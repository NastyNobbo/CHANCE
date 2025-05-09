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

        static void Main(string[] args)
        {
            const int port = 5000;
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"Сервер запущен на порту {port}");

            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(ClientHandler, client);
            }
        }

        static void ClientHandler(object obj)
        {
            TcpClient tcpClient = (TcpClient)obj;
            NetworkStream stream = tcpClient.GetStream();
            string userName = null;

            try
            {
                // Ждем сообщение login
                byte[] buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) throw new Exception("Пустое имя пользователя");
                string loginMsg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var loginObj = JsonSerializer.Deserialize<Message>(loginMsg);
                if (loginObj == null || loginObj.Type != "login" || string.IsNullOrWhiteSpace(loginObj.Name))
                    throw new Exception("Некорректное имя пользователя");

                userName = loginObj.Name.Trim();

                if (!clients.TryAdd(userName, new ClientInfo { Name = userName, Client = tcpClient, Stream = stream }))
                {
                    // Имя уже занято
                    SendMessage(stream, new Message { Type = "error", Text = "Пользователь с таким именем уже подключен." });
                    tcpClient.Close();
                    return;
                }

                Console.WriteLine($"Пользователь {userName} подключился");

                // Отправляем обновленный список пользователей всем клиентам
                BroadcastUserList();

                // Отправляем клиенту историю диалогов по запросу (если был такой тип)
                // Но пока нет запроса на это, можем отправлять диалоги по запросу клиента при расширении функционала

                // Обрабатываем входящие сообщения от клиента
                while (true)
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break; // отключение

                    string msgJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var message = JsonSerializer.Deserialize<Message>(msgJson);
                    if (message == null) continue;

                    if (message.Type == "message")
                    {
                        // Сохраняем сообщение в диалогах
                        string dialogKey = GetDialogKey(userName, message.To);
                        string formattedMsg = $"{userName}: {message.Text}";

                        dialogs.AddOrUpdate(dialogKey,
                            new List<string>() { formattedMsg },
                            (key, oldList) =>
                            {
                                oldList.Add(formattedMsg);
                                return oldList;
                            });

                        // Получаем получателя и отправляем ему сообщение, если есть
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
                        // Клиент запросил диалог с пользователем message.To
                        string dialogKey = GetDialogKey(userName, message.To);
                        if (dialogs.TryGetValue(dialogKey, out var dialogMessages))
                        {
                            // Отправляем клиенту все сообщения диалога как одно сообщение с типом dialog
                            SendMessage(stream, new Message
                            {
                                Type = "dialog",
                                From = message.To,
                                To = userName,
                                DialogMessages = dialogMessages
                            });
                        }
                        else
                        {
                            // Если диалог пустой, отправляем пустой список
                            SendMessage(stream, new Message
                            {
                                Type = "dialog",
                                From = message.To,
                                To = userName,
                                DialogMessages = new List<string>()
                            });
                        }
                    }
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
            var userListMessage = new Message
            {
                Type = "userlist",
                Users = new List<string>(clients.Keys)
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
        public string Type { get; set; } // login, message, userlist, error, dialog, getdialog
        public string Name { get; set; } // для login
        public string From { get; set; } // для message
        public string To { get; set; } // для message или getdialog
        public string Text { get; set; } // для message, error
        public List<string> Users { get; set; } // для userlist
        public List<string> DialogMessages { get; set; } // для dialog
    }
}