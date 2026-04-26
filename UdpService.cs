using System;
using System.Net;
using System.Net.Sockets;

namespace OPCWebServer
{
    public class UdpService : IDisposable
    {
        private readonly UdpClient _udpClient;
        private readonly IPEndPoint _remoteEndPoint;
        private readonly bool _enabled;
        private bool _disposed = false;

        public UdpService(UdpSettings settings)
        {
            _enabled = settings.Enabled;
            _udpClient = new UdpClient();
            
            // Парсим IP и порт из конфига
            _remoteEndPoint = new IPEndPoint(
                IPAddress.Parse(settings.RemoteIp), 
                settings.RemotePort
            );
        }

        public void Send(byte[] data)
        {
            if (_disposed || !_enabled || data == null || data.Length == 0) return;

            try
            {
                // Отправка данных асинхронно, чтобы не тормозить цикл опроса OPC
                _udpClient.BeginSend(data, data.Length, _remoteEndPoint, null, null);
            }
            catch (Exception)
            {
                // Ошибки сети не должны «вешать» основную программу
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    _udpClient?.Close();
                    _udpClient?.Dispose();
                }
                catch { }
            }
        }
    }
}
