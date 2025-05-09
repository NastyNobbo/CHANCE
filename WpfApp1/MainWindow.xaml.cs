using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net;
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
        string ipAddress = ConnectionData.GetCorrectLocalIPv4().ToString();


        // Словарь чатов: имя пользователя -> список сообщений
        Dictionary<string, List<string>> chats = new Dictionary<string, List<string>>();

        public MainWindow()
        {
            InitializeComponent();
            usersList.SelectionChanged += UsersList_SelectionChanged;
        }

        private void btnOpenRegister_Click(object sender, RoutedEventArgs e)
        {
            RegistrationWindow regWindow = new RegistrationWindow();
            regWindow.Owner = this;
            regWindow.ShowDialog();
        }

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetCredentials(out string name, out string password)) return;
            try
            {
                client = new TcpClient(ipAddress, 56000);
                stream = client.GetStream();
                var loginMsg = new Message
                {
                    Type = "login",
                    Name = name,
                    Password = password
                };
                SendMessage(loginMsg);
                byte[] buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                string responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var response = JsonSerializer.Deserialize<Message>(responseJson);
                if (response.Type == "login_success")
                {
                    userName = name;
                    LoginPanel.Visibility = Visibility.Collapsed;
                    ChatGrid.Visibility = Visibility.Visible;
                    Title = $"Чат - {userName}";
                    listenThread = new Thread(Listen);
                    listenThread.IsBackground = true;
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

        private bool TryGetCredentials(out string name, out string password)
        {
            name = txtName.Text.Trim();
            password = txtPassword.Password;
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Введите имя пользователя.");
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
                        Dispatcher.Invoke(() => MessageBox.Show(message.Text));
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show("Ошибка связи с сервером: " + ex.Message));
                Dispatcher.Invoke(() => Close());
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
            {
                chatList.Items.Add(displayMsg);
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
            if (string.IsNullOrEmpty(selectedChatUser))
            {
                MessageBox.Show("Выберите пользователя.");
                return;
            }
            string text = txtMessage.Text.Trim();
            if (string.IsNullOrEmpty(text))
                return;
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string displayMsg = $"{timestamp} {userName}: {text}";
            if (!chats.ContainsKey(selectedChatUser))
                chats[selectedChatUser] = new List<string>();
            chats[selectedChatUser].Add(displayMsg);
            chatList.Items.Add(displayMsg);
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

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                listenThread?.Abort();
            }
            catch { }
            stream?.Close();
            client?.Close();
        }


        
    }


    public class Message
    {
        public string Type { get; set; } // login, message, userlist, error, dialog, getdialog
        public string Name { get; set; } // для login
        public string Password { get; set; }
        public string From { get; set; } // для сообщения
        public string To { get; set; } // для сообщения
        public string Text { get; set; } // для сообщения, ошибки
        public List<string> Users { get; set; } // для userlist
        public List<string> DialogMessages { get; set; } // для dialog
    }
}