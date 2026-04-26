using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace OPCWebServer
{
    public class TagManager
    {
        // Все теги, полученные с сервера (плоский список)
        private List<string> _allServerTags = new List<string>();
        private BindingSource _tableBinding;
        private List<TagConfig> _configTags;

        public TagManager(BindingSource tableBinding, List<TagConfig> configTags)
        {
            _tableBinding = tableBinding;
            _configTags = configTags;
        }

        // Заполнение плоского списка тегов (рекурсивный обход при подключении)
        public void RefreshServerTags(OpcService service)
        {
            _allServerTags.Clear();
            GetAllTagsRecursive(service, null);
        }

        private void GetAllTagsRecursive(OpcService service, Opc.ItemIdentifier parent)
        {
            var elements = service.Browse(parent);
            foreach (var el in elements)
            {
                if (el.IsItem) _allServerTags.Add(el.ItemName);
                if (el.HasChildren) 
                {
                    var childId = new Opc.ItemIdentifier(el.ItemPath, el.ItemName);
                    GetAllTagsRecursive(service, childId);
                }
            }
        }

        // Фильтрация списка для отображения в ListBox
        public void FilterSourceList(ListBox listBox, string filter)
        {
            listBox.BeginUpdate();
            listBox.Items.Clear();
            
            var filtered = string.IsNullOrWhiteSpace(filter) 
                ? _allServerTags 
                : _allServerTags.Where(t => t.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >=0);

            foreach (var tag in filtered) listBox.Items.Add(tag);
            listBox.EndUpdate();
        }

        // Добавление тега из списка в таблицу конфигурации
        public void AddTagToConfig(string tagName)
        {
            if (_configTags.Any(t => t.Address == tagName)) return;

            _configTags.Add(new TagConfig {
                Id = _configTags.Count > 0 ? _configTags.Max(t => t.Id) + 1 : 0,
                Address = tagName,
                DataType = "float",
                Multiplier = 1,
                Offset = 0,
                Invert = false,
                UdpSend = false
            });

            _tableBinding.ResetBindings(false);
        }

        public void UpdateReferences(BindingSource tableBinding, List<TagConfig> configTags)
        {
            _tableBinding = tableBinding;
            _configTags = configTags;
        }
    }
}
