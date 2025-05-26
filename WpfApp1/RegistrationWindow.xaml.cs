using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WpfApp1
{
    /// <summary>
    /// Логика взаимодействия для RegistrationWindow.xaml
    /// </summary>
    public partial class RegistrationWindow : Window
    {
        string ipAddress = ConnectionData.GetCorrectLocalIPv4().ToString();
        int port = 56000;
        public RegistrationWindow()
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
        private void btnRegister_Click(object sender, RoutedEventArgs e)
        {
            string name = txtRegName.Text.Trim();
            string login = txtRegLogin.Text.Trim();
            string password = txtRegPassword.Password;
            string repeatPassword = txtRepeatRegPassword.Password;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(login) ||
            string.IsNullOrEmpty(password) || string.IsNullOrEmpty(repeatPassword))
            {
                MessageBox.Show("Заполните все поля!");
                return;
            }
            if (password != repeatPassword)
            {
                MessageBox.Show("Пароли не совпадают!");
                return;
            }
            try
            {
                using var client = new TcpClient(ipAddress, port);
                using var stream = client.GetStream();
                var registerMessage = new Message
                {
                    Type = "register",
                    Name = name,
                    Login = login,
                    Password = password
                };
                string json = JsonSerializer.Serialize(registerMessage);
                byte[] data = Encoding.UTF8.GetBytes(json);
                stream.Write(data, 0, data.Length);
                byte[] buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                string responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var response = JsonSerializer.Deserialize<Message>(responseJson);
                if (response.Type == "register_success")
                {
                    MessageBox.Show("Регистрация успешна! Теперь войдите.");
                    Close();
                }
                else
                {
                    MessageBox.Show($"Ошибка регистрации: {response.Text}");
                }
                stream.Close();
                client.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка регистрации: {ex.Message}");
            }
        }
    }
}
