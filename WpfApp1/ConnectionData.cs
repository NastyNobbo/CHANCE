using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace WpfApp1
{
    internal class ConnectionData
    {
        public static IPAddress GetCorrectLocalIPv4()
        {
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up) // Только активные
                .Where(network => network.NetworkInterfaceType != NetworkInterfaceType.Loopback) // Не localhost
                .Where(network => network.NetworkInterfaceType != NetworkInterfaceType.Tunnel) // Не VPN/туннели
                .Where(network =>
                    !network.Name.Contains("Virtual") &&
                    !network.Name.Contains("VMware") &&
                    !network.Name.Contains("Radmin VPN") &&
                    !network.Name.Contains("VirtualBox")) // Фильтр виртуальных адаптеров по имени
                .Where(network =>
                    network.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                    network.NetworkInterfaceType == NetworkInterfaceType.Wireless80211); // Только Ethernet/Wi-Fi

            foreach (var networkInterface in networkInterfaces)
            {
                var ipProperties = networkInterface.GetIPProperties();
                var unicastAddresses = ipProperties.UnicastAddresses;

                foreach (var ip in unicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork) // IPv4
                    {
                        return ip.Address;
                    }
                }
            }

            throw new Exception("Не удалось найти IPv4 (Ethernet/Wi-Fi)!");
        }


        // Использование
        public static int PortFinder()
        {
            Random random = new Random();
            int randomPort = 0;
            bool portFound = false;

            // Генерируем случайный порт, пока не найдем открытый
            while (!portFound)
            {
                randomPort = random.Next(49152, 65535); // Генерируем порт в диапазоне от 49152 до 65535
                if (IsPortOpen(randomPort))
                {
                    portFound = true;
                }

            }
            return randomPort;

        }
        private static bool IsPortOpen(int port)
        {
            try
            {
                using (TcpListener listener = new TcpListener(IPAddress.Any, port))
                {
                    listener.Start();
                    listener.Stop();
                    return true; // Порт открыт
                }
            }
            catch (SocketException)
            {
                return false; // Порт закрыт
            }
        }
    }
}
