using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace WpfApp1
{
    public partial class ConnectionSettingsWindow : Window
    {
        public string IpAddress { get; private set; }
        public int Port { get; private set; }

        private static string IpStoreFile = "ip_store.txt";
        private static string PortStoreFile = "port_store.txt";

        public ConnectionSettingsWindow(string currentIp, int currentPort)
        {
            InitializeComponent();

            var ipSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var portSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Загрузим список IP из файла (если есть)

            ipAdder(ipSet, currentIp);
            portAdder(portSet, currentPort.ToString());

            cbIp.Text = currentIp;
            txtPort.Text = currentPort.ToString();
        }
        public void ipAdder(HashSet<string> ipSet, string currentIp)
        {
            if (File.Exists(IpStoreFile))
            {
                var lines = File.ReadAllLines(IpStoreFile)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line));
                foreach (var ip in lines)
                    ipSet.Add(ip);
            }

            // Добавим текущий IP, если его ещё нет
            if (!ipSet.Contains(currentIp))
            {
                ipSet.Add(currentIp);
                File.WriteAllLines(IpStoreFile, ipSet); // обновим файл
            }

            // Заполним ComboBox
            foreach (var ip in ipSet)
                cbIp.Items.Add(ip);
        }
        public void portAdder(HashSet<string> portSet, string currentPort)
        {
            if (File.Exists(PortStoreFile))
            {
                var lines = File.ReadAllLines(PortStoreFile)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line));
                foreach (var port in lines)
                    portSet.Add(port);
            }

            // Добавим текущий IP, если его ещё нет
            if (!portSet.Contains(currentPort))
            {
                portSet.Add(currentPort);
                File.WriteAllLines(PortStoreFile, portSet); // обновим файл
            }

            // Заполним ComboBox
            foreach (var port in portSet)
                txtPort.Items.Add(port);
        }



        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string ip = cbIp.Text.Trim();
            if (string.IsNullOrEmpty(ip))
            {
                MessageBox.Show("Введите IP адрес.");
                return;
            }

            if (!int.TryParse(txtPort.Text.Trim(), out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Введите корректный порт (1–65535).");
                return;
            }

            string ipStorePath = "ip_store.txt";
            string defaultPath = "default_connection.txt";
            HashSet<string> ipSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(ipStorePath))
            {
                foreach (var line in File.ReadAllLines(ipStorePath))
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        ipSet.Add(trimmed);
                }
            }

            if (!ipSet.Contains(ip))
                ipSet.Add(ip);

            File.WriteAllLines(ipStorePath, ipSet);

            // Если галочка стоит — сохраняем как дефолтный
            if (chkUseAlways.IsChecked == true)
            {
                File.WriteAllText(defaultPath, $"{ip}:{port}");
            }

            IpAddress = ip;
            Port = port;
            DialogResult = true;
            Close();
        }
    }
}
