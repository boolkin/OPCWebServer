using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using System.Text.Json;
using System.Threading;

namespace OPCWebServer
{
    public class DataPollingService
    {
        private readonly OpcService _opc;
        private readonly List<TagConfig> _tags;
        private readonly int _interval;
        private System.Timers.Timer _timer;
        private readonly object _lock = new object();
        private byte[] _lastBinaryData = Array.Empty<byte>();
        private string _lastJsonData = "[]";
        
        // Настройки для БД и UDP
        private bool _udpEnabled;
        private UdpService _udpService;
        private DbService _dbService;
        private DatabaseSettings _dbSettings;

        public byte[] LastBinaryData 
        { 
            get 
            { 
                lock (_lock) return _lastBinaryData.ToArray(); 
            } 
        }
        
        public string LastJsonData 
        { 
            get 
            { 
                lock (_lock) return _lastJsonData ?? "[]"; 
            } 
        }

        public event Action DataUpdated;
        public event Action<string> LogMessage;

        public void SetServices(UdpService udp, DbService db, DatabaseSettings dbSettings, bool udpEnabled)
        {
            _udpService = udp;
            _dbService = db;
            _dbSettings = dbSettings;
            _udpEnabled = udpEnabled;
        }

        public DataPollingService(OpcService opc, List<TagConfig> tags, int intervalMs)
        {
            _opc = opc;
            _tags = tags;
            _interval = intervalMs;
        }

        public void Start()
        {
            Stop();
            var addresses = _tags.Select(t => t.Address).ToArray();
            _opc.PrepareSubscription(addresses, _interval);

            _timer = new System.Timers.Timer(_interval);
            _timer.Elapsed += ProcessTick;
            _timer.AutoReset = true;
            _timer.Enabled = true;
            
            LogMessage?.Invoke($"{DateTime.Now:HH:mm:ss}: Опрос тегов запущен (интервал {_interval}мс)");
        }

        public void Stop()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
                LogMessage?.Invoke($"{DateTime.Now:HH:mm:ss}: Опрос тегов остановлен");
            }
        }

        private void ProcessTick(object sender, ElapsedEventArgs e)
        {
            try
            {
                var results = _opc.ReadActiveTags();
                if (results == null) return;

                var jsonList = new List<object>();
                var binaryList = new List<float>();
                var tagsForArchive = new List<(int Index, float Value)>();

                for (int i = 0; i < _tags.Count; i++)
                {
                    if (i >= results.Length || results[i].Value == null) 
                        continue;

                    var tag = _tags[i];
                    object processed = ApplyLogic(results[i].Value, tag);

                    // В JSON улетит либо число, либо bool, либо строка
                    jsonList.Add(new { id = tag.Id, addr = tag.Address, v = processed });

                    // Логика UDP - только числа и bool (как float)
                    if (tag.UdpSend)
                    {
                        if (processed is float f)
                        {
                            binaryList.Add(f);
                        }
                        else if (processed is bool b)
                        {
                            binaryList.Add(b ? 1f : 0f);
                        }
                        // Текстовые данные игнорируем для бинарного UDP пакета
                    }

                    // Логика Архива (БД) - аналогично UDP, только числа
                    if (_dbSettings?.Enabled == true && tag.Archive)
                    {
                        if (processed is float valFloat)
                        {
                            tagsForArchive.Add((i, valFloat));
                        }
                        else if (processed is bool b)
                        {
                            tagsForArchive.Add((i, b ? 1f : 0f));
                        }
                        // Строки в архив не пишем
                    }
                }

                string jsonData = JsonSerializer.Serialize(jsonList);
                byte[] binaryData = binaryList.SelectMany(BitConverter.GetBytes).ToArray();

                lock (_lock)
                {
                    _lastJsonData = jsonData;
                    _lastBinaryData = binaryData;
                }

                // Отправка в UDP
                if (binaryList.Count > 0 && _udpEnabled && _udpService != null)
                {
                    _udpService.Send(binaryList);
                }

                // Запись в БД (пакетная)
                if (tagsForArchive.Count > 0 && _dbService != null)
                {
                    long timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
                    foreach (var item in tagsForArchive)
                    {
                        _dbService.EnqueueRecord(timestamp, item.Index, item.Value);
                    }
                }

                DataUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"{DateTime.Now:HH:mm:ss}: Ошибка опроса: {ex.Message}");
            }
        }

        private object ApplyLogic(object rawVal, TagConfig tag)
        {
            if (tag.DataType == "text")
            {
                return rawVal?.ToString() ?? "";
            }

            try 
            {
                float v = Convert.ToSingle(rawVal);
                
                if (tag.DataType == "bool")
                {
                    bool boolVal = v > 0;
                    if (tag.Invert) boolVal = !boolVal;
                    return boolVal; // Вернет true/false в JSON
                }

                // Математика для обычных чисел (float/int)
                return (float)((v * tag.Multiplier) + tag.Offset);
            }
            catch 
            {
                return 0f; 
            }
        }

        public object GetCurrentValue(int tagIndex)
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(_lastJsonData) || _lastJsonData == "[]")
                    return null;

                try
                {
                    var jsonList = JsonSerializer.Deserialize<List<JsonElement>>(_lastJsonData);
                    if (jsonList == null || tagIndex >= jsonList.Count)
                        return null;

                    var item = jsonList[tagIndex];
                    if (item.TryGetProperty("v", out var valueProp))
                    {
                        switch (valueProp.ValueKind)
                        {
                            case JsonValueKind.Number:
                                return valueProp.GetDouble();
                            case JsonValueKind.True:
                                return true;
                            case JsonValueKind.False:
                                return false;
                            case JsonValueKind.String:
                                return valueProp.GetString();
                            default:
                                return null;
                        }
                    }
                }
                catch
                {
                    // Ignore parsing errors
                }
                
                return null;
            }
        }
    }
}
