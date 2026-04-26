using System;
using System.Collections.Generic;
using System.Linq;
using Opc;
using Opc.Da;

namespace OPCWebServer
{
    public class OpcService : IDisposable
    {
        private Opc.Da.Server _server;
        private readonly OpcCom.Factory _factory = new OpcCom.Factory();
        private Opc.Da.Subscription _group;

        public List<string> GetLocalServers()
        {
            var serverNames = new List<string>();
            try
            {
                // Используем OpcCom.ServerEnumerator как в вашем рабочем коде
                OpcCom.ServerEnumerator discovery = new OpcCom.ServerEnumerator();
                Opc.Server[] servers = discovery.GetAvailableServers(Specification.COM_DA_20, "localhost", null);
                
                if (servers != null)
                {
                    foreach (var s in servers) serverNames.Add(s.Name);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Ошибка поиска OPC: " + ex.Message);
            }
            return serverNames;
        }

        public void Connect(string serverName)
        {
            Disconnect();
            // Получаем объект сервера через перечислитель для корректного приведения типов
            OpcCom.ServerEnumerator discovery = new OpcCom.ServerEnumerator();
            Opc.Server[] servers = discovery.GetAvailableServers(Specification.COM_DA_20, "localhost", null);
            
            foreach(var s in servers) {
                if (s.Name == serverName) {
                    _server = (Opc.Da.Server)s;
                    break;
                }
            }

            if (_server == null) throw new Exception("Сервер не найден");
            _server.Connect();
        }

        public List<BrowseElement> Browse(ItemIdentifier parentId = null)
        {
            if (_server == null || !_server.IsConnected) return new List<BrowseElement>();

            BrowseFilters filters = new BrowseFilters { BrowseFilter = browseFilter.all };
            BrowsePosition position;
            // Получаем элементы (папки и теги)
            BrowseElement[] elements = _server.Browse(parentId, filters, out position);
            
            return elements != null ? new List<BrowseElement>(elements) : new List<BrowseElement>();
        }

        public Opc.Da.ItemValueResult[] ReadTags(string[] addresses)
        {
            if (_server == null || !_server.IsConnected) return Array.Empty<Opc.Da.ItemValueResult>();
            
            // Создаем объекты Item для каждого адреса
            var items = addresses.Select(addr => new Opc.Da.Item { ItemName = addr }).ToArray();
            // Синхронное чтение с сервера
            return _server.Read(items);
        }

 public void PrepareSubscription(string[] addresses, int refreshRate)
{
    if (_server == null || !_server.IsConnected) 
        throw new Exception("OPC сервер не подключен");

    // 1. Очистка старых подписок
    try
    {
        if (_group != null)
        {
            _server.CancelSubscription(_group);
            _group.Dispose();
            _group = null;
        }
        
        // На всякий случай удаляем вообще все подписки, если сервер их "забыл"
        foreach (Opc.Da.Subscription sub in _server.Subscriptions)
        {
            _server.CancelSubscription(sub);
        }
    }
    catch { /* Игнорируем ошибки при очистке */ }

    // 2. Создание новой группы
    var state = new Opc.Da.SubscriptionState {
        Name = "DataPollingGroup_" + DateTime.Now.Ticks, // Уникальное имя
        UpdateRate = refreshRate,
        Active = true
    };

    _group = (Opc.Da.Subscription)_server.CreateSubscription(state);

    var items = addresses.Select(addr => new Opc.Da.Item { 
        ItemName = addr, 
        Active = true 
    }).ToArray();

    _group.AddItems(items);
}


public Opc.Da.ItemValueResult[] ReadActiveTags()
{
    try 
    {
        if (_server == null || !_server.IsConnected || _group == null) 
            return Array.Empty<Opc.Da.ItemValueResult>();

        return _group.Read(_group.Items);
    }
    catch (Exception ex)
    {
        // Если подписка "протухла", возвращаем пустой массив
        // Это предотвратит вылет DataPollingService
        return Array.Empty<Opc.Da.ItemValueResult>();
    }
}


        public void Disconnect()
        {
            if (_server != null && _server.IsConnected)
            {
                _server.Disconnect();
                _server = null;
            }
        }

        public void Dispose() => Disconnect();
    }
}
