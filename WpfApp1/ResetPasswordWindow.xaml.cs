using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace WpfApp1
{
    public partial class ResetPasswordWindow : Window
    {
        string ipAddress = ConnectionData.GetCorrectLocalIPv4().ToString();
        int port = 56000;
        public ResetPasswordWindow()
        {
            InitializeComponent();
            defaultConnection();
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
        private void btnResetPassword_Click(object sender, RoutedEventArgs e)
        {
            string login = txtLogin.Text.Trim();
            string newPassword = txtNewPassword.Password;

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(newPassword))
            {
                MessageBox.Show("Пожалуйста, заполните все поля.");
                return;
            }

            try
            {
                using var client = new TcpClient(ipAddress, port);
                using var stream = client.GetStream();

                var resetMsg = new Message
                {
                    Type = "reset_password",
                    Login = login,
                    Password = newPassword
                };

                string json = JsonSerializer.Serialize(resetMsg);
                byte[] data = Encoding.UTF8.GetBytes(json);
                stream.Write(data, 0, data.Length);

                byte[] buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                string responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var response = JsonSerializer.Deserialize<Message>(responseJson);

                if (response.Type == "reset_password_success")
                {
                    MessageBox.Show("Пароль успешно сброшен.");
                    Close();
                }
                else
                {
                    MessageBox.Show($"Ошибка: {response.Text}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }
    }
}
