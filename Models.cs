using System.Collections.Generic;

namespace OPCWebServer
{
    public class AppConfig
    {
        public OpcSettings OpcSettings { get; set; } = new OpcSettings();
        public WebSettings WebSettings { get; set; } = new WebSettings();
        public UdpSettings UdpSettings { get; set; } = new UdpSettings();
        public List<TagConfig> Tags { get; set; } = new List<TagConfig>();
    }

    public class OpcSettings
    {
        public string AppName { get; set; } = "Graybox";
        public string ServerId { get; set; } = "Graybox.Simulator";
        public int RefreshRateMs { get; set; } = 2000;
    }

    public class WebSettings
    {
        public bool Enabled { get; set; } = true;
        public int Port { get; set; } = 8085;
        public string StaticFolder { get; set; } = "wwwroot";
    }

    public class UdpSettings
    {
        public bool Enabled { get; set; } = true;
        public string RemoteIp { get; set; } = "127.0.0.1";
        public int RemotePort { get; set; } = 3310;
    }

    public class TagConfig
    {
        public int Id { get; set; }
        public string Address { get; set; } = "";
        public string DataType { get; set; } = "float";
        public double Multiplier { get; set; }
        public double Offset { get; set; }
        public bool Invert { get; set; }
        public bool UdpSend { get; set; }
    }

    public class TagViewItem
    {
        public int Id { get; set; }
        public string Address { get; set; } = "";
        public string DataType { get; set; } = "float";
        public bool Invert { get; set; } = false;
        public bool UdpSend { get; set; } = false;
    }

}
