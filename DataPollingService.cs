using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using System.Text.Json;

namespace OPCWebServer
{
    public class DataPollingService
    {
        private readonly OpcService _opc;
        private readonly List<TagConfig> _tags;
        private readonly int _interval;
        private System.Timers.Timer _timer;

        public byte[] LastBinaryData { get; private set; } = Array.Empty<byte>();
        public string LastJsonData { get; private set; } = "[]";

        public event Action DataUpdated;

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
        }

        public void Stop()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
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

                for (int i = 0; i < _tags.Count; i++)
                {
                    if (i >= results.Length || results[i].Value == null) 
                        continue;

                    var tag = _tags[i];
                    object processed = ApplyLogic(results[i].Value, tag);

                    // В JSON улетит либо число, либо bool, либо строка
                    jsonList.Add(new { id = tag.Id, addr = tag.Address, v = processed });

                    // В UDP (Binary) отправляем только если это число (float)
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
                }

                LastJsonData = JsonSerializer.Serialize(jsonList);
                LastBinaryData = binaryList.SelectMany(BitConverter.GetBytes).ToArray();

                DataUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                // Рекомендуется добавить: Console.WriteLine($"Error: {ex.Message}");
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
    }
}
