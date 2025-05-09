using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace WpfApp1
{
    public partial class ResetPasswordWindow : Window
    {
        string ipAddress = ConnectionData.GetCorrectLocalIPv4().ToString();
        public ResetPasswordWindow()
        {
            InitializeComponent();
        }

        private void btnResetPassword_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string newPassword = txtNewPassword.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(newPassword))
            {
                MessageBox.Show("Пожалуйста, заполните все поля.");
                return;
            }

            try
            {
                using var client = new TcpClient(ipAddress, 56000);
                using var stream = client.GetStream();

                var resetMsg = new Message
                {
                    Type = "reset_password",
                    Name = username,
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
