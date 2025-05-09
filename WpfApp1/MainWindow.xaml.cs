using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Xml.Linq;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        TcpClient client;
        NetworkStream stream;
        Thread listenThread;
        string userName;
        string selectedChatUser;

        // Словарь чатов: имя пользователя -> список сообщений
        Dictionary<string, List<string>> chats = new Dictionary<string, List<string>>();

        public MainWindow()
        {
            InitializeComponent();
            usersList.SelectionChanged += UsersList_SelectionChanged;
        }

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            userName = txtName.Text.Trim();
            if (string.IsNullOrEmpty(userName))
            {
                MessageBox.Show("Введите имя пользователя.");
                return;
            }

            try
            {
                client = new TcpClient("127.0.0.1", 5000);
                stream = client.GetStream();

                var loginMsg = new Message
                {
                    Type = "login",
                    Name = userName
                };
                SendMessage(loginMsg);

                listenThread = new Thread(Listen);
                listenThread.IsBackground = true;
                listenThread.Start();

                btnLogin.IsEnabled = false;
                txtName.IsEnabled = false;
                txtMessage.IsEnabled = true;
                btnSend.IsEnabled = true;

                Title = $"Демо Чат - {userName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка подключения: " + ex.Message);
            }
        }

        private void Listen()
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (true)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                        break;

                    string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var message = JsonSerializer.Deserialize<Message>(json);

                    if (message.Type == "userlist")
                    {
                        Dispatcher.Invoke(() => UpdateUserList(message.Users));
                    }
                    else if (message.Type == "message")
                    {
                        Dispatcher.Invoke(() => ReceiveChatMessage(message));
                    }
                    else if (message.Type == "dialog")
                    {
                        Dispatcher.Invoke(() => ReceiveDialogMessages(message));
                    }
                    else if (message.Type == "error")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(message.Text);
                            Disconnect();
                        });
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show("Ошибка соединения: " + ex.Message));
            }
            finally
            {
                Dispatcher.Invoke(() => Disconnect());
            }
        }

        private void UpdateUserList(List<string> users)
        {
            usersList.Items.Clear();
            foreach (var user in users)
            {
                if (user != userName)
                    usersList.Items.Add(user);
            }
            // Если выбранный пользователь в списке отсутствует — очистить
            if (selectedChatUser != null && !usersList.Items.Contains(selectedChatUser))
            {
                selectedChatUser = null;
                chatList.Items.Clear();
            }
        }

        private void ReceiveChatMessage(Message message)
        {
            string fromUser = message.From;
            string text = message.Text;

            if (!chats.ContainsKey(fromUser))
                chats[fromUser] = new List<string>();

            chats[fromUser].Add($"{fromUser}: {text}");

            // Если открыт чат с этим пользователем, показать сообщение
            if (fromUser == selectedChatUser)
            {
                chatList.Items.Add($"{fromUser}: {text}");
            }
        }

        private void ReceiveDialogMessages(Message message)
        {
            string dialogWithUser = message.From;
            if (!chats.ContainsKey(dialogWithUser))
                chats[dialogWithUser] = new List<string>();
            else
                chats[dialogWithUser].Clear();

            chats[dialogWithUser].AddRange(message.DialogMessages);

            if (dialogWithUser == selectedChatUser)
            {
                chatList.Items.Clear();
                foreach (var msg in chats[dialogWithUser])
                {
                    chatList.Items.Add(msg);
                }
            }
        }

        private void UsersList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (usersList.SelectedItem == null)
                return;

            selectedChatUser = usersList.SelectedItem.ToString();

            // Запрашиваем у сервера диалог с этим пользователем
            RequestDialog(selectedChatUser);
        }

        private void RequestDialog(string user)
        {
            var msg = new Message
            {
                Type = "getdialog",
                From = userName,
                To = user
            };
            SendMessage(msg);
        }

        private void btnSend_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(selectedChatUser))
            {
                MessageBox.Show("Выберите пользователя для чата.");
                return;
            }

            string text = txtMessage.Text.Trim();
            if (string.IsNullOrEmpty(text))
                return;

            // Добавляем сообщение в локальный чат
            if (!chats.ContainsKey(selectedChatUser))
                chats[selectedChatUser] = new List<string>();

            chats[selectedChatUser].Add($"Я: {text}");
            chatList.Items.Add($"Я: {text}");

            var message = new Message
            {
                Type = "message",
                From = userName,
                To = selectedChatUser,
                Text = text
            };
            SendMessage(message);

            txtMessage.Clear();
        }

        private void SendMessage(Message message)
        {
            try
            {
                string json = JsonSerializer.Serialize(message);
                byte[] data = Encoding.UTF8.GetBytes(json);
                stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show("Ошибка отправки: " + ex.Message));
            }
        }

        private void Disconnect()
        {
            try
            {
                stream?.Close();
                client?.Close();
            }
            catch { }

            Dispatcher.Invoke(() =>
            {
                btnLogin.IsEnabled = true;
                txtName.IsEnabled = true;
                txtMessage.IsEnabled = false;
                btnSend.IsEnabled = false;
                usersList.Items.Clear();
                chatList.Items.Clear();
                Title = "Демо Чат";
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                listenThread?.Abort();
            }
            catch { }
            Disconnect();
        }
    }

    public class Message
    {
        public string Type { get; set; } // login, message, userlist, error, dialog, getdialog
        public string Name { get; set; } // для login
        public string From { get; set; } // для сообщения
        public string To { get; set; } // для сообщения
        public string Text { get; set; } // для сообщения, ошибки
        public List<string> Users { get; set; } // для userlist
        public List<string> DialogMessages { get; set; } // для dialog
    }
}