using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        TcpClient client;
        NetworkStream stream;
        Thread listenThread;
        string userName; // сохраняем имя пользователя
        string selectedChatUser;
        string ipAddress = ConnectionData.GetCorrectLocalIPv4().ToString();
        int port = 56000;


        Dictionary<string, List<string>> chats = new Dictionary<string, List<string>>();
        List<string> groupChats = new List<string>();
        string selectedGroupChat = null;

        public MainWindow()
        {
            InitializeComponent();
            defaultConnection();
            usersList.SelectionChanged += UsersList_SelectionChanged;
            groupList.SelectionChanged += GroupList_SelectionChanged;
        }

        private void btnOpenRegister_Click(object sender, RoutedEventArgs e)
        {
            var regWindow = new RegistrationWindow();
            regWindow.Owner = this;
            regWindow.ShowDialog();
        }
        public void defaultConnection()
        {
            string defaultPath = "default_connection.txt";
            if (File.Exists(defaultPath))
            {
                var line = File.ReadAllText(defaultPath).Trim();
                var parts = line.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out int savedPort))
                {
                    ipAddress = parts[0];
                    port = savedPort;
                }
            }
        }
        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetCredentials(out string login, out string password)) return;

            try
            {
                try 
                { 
                    client = new TcpClient(ipAddress, port);
                    stream = client.GetStream();
                
                }
                catch (SocketException ex)
                {
                    MessageBox.Show($"Ошибка подключения к серверу: {ex.Message}");
                    return;
                }
                var loginMsg = new Message
                {
                    Type = "login",
                    Login = login,
                    Password = password
                };
                SendMessage(loginMsg);

                byte[] buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                string responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var response = JsonSerializer.Deserialize<Message>(responseJson);

                if (response.Type == "login_success")
                {
                    userName = response.Name ?? login;
                    LoginPanel.Visibility = Visibility.Collapsed;
                    ChatGrid.Visibility = Visibility.Visible;
                    Title = $"Чат - {userName}";
                    if (response.Users != null)
                    {
                        groupChats.Clear();
                        groupList.Items.Clear();
                        foreach (var g in response.Users)
                        {
                            groupChats.Add(g);
                            groupList.Items.Add(g);
                        }
                    }
                    listenThread = new Thread(Listen) { IsBackground = true };
                    listenThread.Start();

                    txtMessage.IsEnabled = true;
                    btnSend.IsEnabled = true;
                }
                else
                {
                    MessageBox.Show($"Ошибка входа: {response.Text}");
                    stream.Close();
                    client.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка входа: {ex.Message}");
            }
        }

        private void btnResetPassword_Click(object sender, RoutedEventArgs e)
        {
            var resetWindow = new ResetPasswordWindow();
            resetWindow.Owner = this;
            resetWindow.ShowDialog();
        }

        private bool TryGetCredentials(out string login, out string password)
        {
            login = txtLogin.Text.Trim();
            password = txtPassword.Password;
            if (string.IsNullOrEmpty(login))
            {
                MessageBox.Show("Введите логин.");
                return false;
            }
            if (string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Введите пароль.");
                return false;
            }
            return true;
        }

        private void Listen()
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (true)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var message = JsonSerializer.Deserialize<Message>(json);

                    if (message == null) continue;

                    Dispatcher.Invoke(() =>
                    {
                        switch (message.Type)
                        {
                            case "userlist":
                                UpdateUserList(message.Users);
                                break;
                            case "message":
                                ReceiveChatMessage(message);
                                break;
                            case "group_message":
                                ReceiveGroupMessage(message);
                                break;
                            case "group_history":
                                ReceiveGroupHistory(message);
                                break;
                            case "group_list":
                                groupChats.Clear();
                                groupList.Items.Clear();
                                foreach (var g in message.Users.Distinct()) // на всякий случай
                                {
                                    groupChats.Add(g);
                                    groupList.Items.Add(g);
                                }
                                break;
                            case "group_invited":
                                string newGroup = message.Text;
                                if (!groupChats.Contains(newGroup))
                                {
                                    groupChats.Add(newGroup);
                                    groupList.Items.Add(newGroup);
                                }
                                MessageBox.Show($"Вы были приглашены в группу: {newGroup}");
                                break;
                            case "group_created":
                                // Добавление новой группы в список
                                string newGroupName = message.Text;
                                if (!groupChats.Contains(newGroupName))
                                {
                                    groupChats.Add(newGroupName);
                                    groupList.Items.Add(newGroupName);
                                }
                                break;
                            case "dialog":
                                ReceiveDialogMessages(message);
                                break;
                            case "error":
                                MessageBox.Show(message.Text);
                                break;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show("Ошибка связи с сервером: " + ex.Message));
                Dispatcher.Invoke(() => Close());
            }
        }
        private void ReceiveGroupMessage(Message message)
        {
            string text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message.From}: {message.Text}";
            if (!chats.ContainsKey(message.To))
                chats[message.To] = new List<string>();
            chats[message.To].Add(text);
            if (selectedGroupChat == message.To)
                chatList.Items.Add(text);
        }

        private void ReceiveGroupHistory(Message message)
        {
            if (!chats.ContainsKey(message.To))
                chats[message.To] = new List<string>();
            else
                chats[message.To].Clear();
            chats[message.To].AddRange(message.DialogMessages);
            if (selectedGroupChat == message.To)
            {
                chatList.Items.Clear();
                foreach (var msg in chats[message.To])
                    chatList.Items.Add(msg);
            }
        }
        private void UpdateUserList(List<string> users)
        {
            usersList.Items.Clear();
            foreach (var user in users)
            {
                if (user != userName) // Исключаем самого себя
                    usersList.Items.Add(user);
            }

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
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string displayMsg = $"{timestamp} {fromUser}: {text}";

            if (!chats.ContainsKey(fromUser))
                chats[fromUser] = new List<string>();

            chats[fromUser].Add(displayMsg);

            if (fromUser == selectedChatUser)
                chatList.Items.Add(displayMsg);
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
            selectedGroupChat = null;

            selectedChatUser = usersList.SelectedItem.ToString();
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
            chatList.Items.Clear();
        }

        private void btnSend_Click(object sender, RoutedEventArgs e)
        {
            string text = txtMessage.Text.Trim();
            if (string.IsNullOrEmpty(text))
                return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string displayMsg = $"{timestamp} {userName}: {text}";

            // Если выбран групповой чат
            if (!string.IsNullOrEmpty(selectedGroupChat))
            {
                if (!chats.ContainsKey(selectedGroupChat))
                    chats[selectedGroupChat] = new List<string>();

                var groupMessage = new Message
                {
                    Type = "group_message",
                    From = userName,
                    To = selectedGroupChat,
                    Text = text
                };

                SendMessage(groupMessage);
                txtMessage.Clear();
                return;
            }

            // Если выбран личный чат
            if (string.IsNullOrEmpty(selectedChatUser))
            {
                MessageBox.Show("Выберите пользователя или группу для отправки сообщения.");
                return;
            }

            if (!chats.ContainsKey(selectedChatUser))
                chats[selectedChatUser] = new List<string>();

            chats[selectedChatUser].Add(displayMsg);
            chatList.Items.Add(displayMsg);

            var privateMessage = new Message
            {
                Type = "message",
                From = userName,
                To = selectedChatUser,
                Text = text
            };

            SendMessage(privateMessage);
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

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                if (stream != null && stream.CanWrite)
                {
                    SendMessage(new Message { Type = "logout", From = userName });
                    Thread.Sleep(100); // подождать, чтобы сообщение успело уйти
                }
            }
            catch { /* игнорируем */ }

            try { listenThread?.Abort(); } catch { }
            stream?.Close();
            client?.Close();
        }

        private void btnCreateGroup_Click(object sender, RoutedEventArgs e)
        {
            string groupName = Microsoft.VisualBasic.Interaction.InputBox("Введите название группы", "Новая группа");
            if (!string.IsNullOrWhiteSpace(groupName))
            {
                SendMessage(new Message { Type = "create_group", Text = groupName });
                groupChats.Add(groupName);
                groupList.Items.Add(groupName);
            }
        }
        private void GroupList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (groupList.SelectedItem == null) 
                return;
            selectedChatUser = null;
            selectedGroupChat = groupList.SelectedItem.ToString();
            RequestGroupHistory(selectedGroupChat);
        }

        private void RequestGroupHistory(string groupName)
        {
            SendMessage(new Message { Type = "group_history", To = groupName });
            chatList.Items.Clear();
        }

        private void btnInviteToGroup_Click(object sender, RoutedEventArgs e)
        {
            if (groupList.SelectedItem == null)
            {
                MessageBox.Show("Выберите группу.");
                return;
            }
            if (usersList.SelectedItem == null)
            {
                MessageBox.Show("Выберите пользователя для приглашения.");
                return;
            }

            string groupName = groupList.SelectedItem.ToString();
            string targetUser = usersList.SelectedItem.ToString();

            var inviteMsg = new Message
            {
                Type = "invite_to_group",
                To = groupName,
                Text = targetUser
            };
            SendMessage(inviteMsg);
            MessageBox.Show($"Приглашение отправлено пользователю {targetUser} в группу {groupName}.");
        }

        private void btnConnectionSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new ConnectionSettingsWindow(ipAddress, port);
            if (settingsWindow.ShowDialog() == true)
            {
                ipAddress = settingsWindow.IpAddress;
                port = settingsWindow.Port;

                MessageBox.Show($"Подключение изменено на {ipAddress}:{port}");

                // Если пользователь уже авторизован — переподключаем
                if (!string.IsNullOrEmpty(userName))
                {
                    try
                    {
                        stream?.Close();
                        client?.Close();
                        listenThread?.Abort();
                    }
                    catch { }

                    try
                    {
                        client = new TcpClient(ipAddress, port);
                        stream = client.GetStream();

                        var loginMsg = new Message
                        {
                            Type = "login",
                            Login = userName,
                            Password = txtPassword.Password // Убедитесь, что это поле всё ещё содержит пароль
                        };
                        SendMessage(loginMsg);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка переподключения: {ex.Message}");
                    }
                }
            }
        }
    }

    public class Message
    {
        public string Type { get; set; }
        public string Login { get; set; }
        public string Name { get; set; }
        public string Password { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Text { get; set; }
        public List<string> Users { get; set; }
        public List<string> DialogMessages { get; set; }
    }
}
