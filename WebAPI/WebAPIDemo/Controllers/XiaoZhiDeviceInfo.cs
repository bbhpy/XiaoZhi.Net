namespace WebAPIDemo.Controllers
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// XiaoZhi-ESP32 设备信息根对象
    /// </summary>
    public class XiaoZhiDeviceInfo
    {
        /// <summary>
        /// 版本号
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; }

        /// <summary>
        /// 语言（如 zh-CN）
        /// </summary>
        [JsonPropertyName("language")]
        public string Language { get; set; }

        /// <summary>
        /// Flash 大小（字节）
        /// </summary>
        [JsonPropertyName("flash_size")]
        public long FlashSize { get; set; }

        /// <summary>
        /// 最小可用堆内存大小
        /// </summary>
        [JsonPropertyName("minimum_free_heap_size")]
        public string MinimumFreeHeapSize { get; set; }

        /// <summary>
        /// MAC 地址
        /// </summary>
        [JsonPropertyName("mac_address")]
        public string MacAddress { get; set; }

        /// <summary>
        /// 设备唯一标识
        /// </summary>
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; }

        /// <summary>
        /// 芯片型号名称（如 esp32s3）
        /// </summary>
        [JsonPropertyName("chip_model_name")]
        public string ChipModelName { get; set; }

        /// <summary>
        /// 芯片详细信息
        /// </summary>
        [JsonPropertyName("chip_info")]
        public ChipInfo ChipInfo { get; set; }

        /// <summary>
        /// 应用程序信息
        /// </summary>
        [JsonPropertyName("application")]
        public ApplicationInfo Application { get; set; }

        /// <summary>
        /// 分区表信息
        /// </summary>
        [JsonPropertyName("partition_table")]
        public List<PartitionTableItem> PartitionTable { get; set; }

        /// <summary>
        /// OTA 分区信息
        /// </summary>
        [JsonPropertyName("ota")]
        public OtaInfo Ota { get; set; }

        /// <summary>
        /// 显示设备信息
        /// </summary>
        [JsonPropertyName("display")]
        public DisplayInfo Display { get; set; }

        /// <summary>
        /// 主板/网络信息
        /// </summary>
        [JsonPropertyName("board")]
        public BoardInfo Board { get; set; }
    }

    /// <summary>
    /// 芯片详细信息
    /// </summary>
    public class ChipInfo
    {
        /// <summary>
        /// 芯片型号（数字标识）
        /// </summary>
        [JsonPropertyName("model")]
        public int Model { get; set; }

        /// <summary>
        /// 核心数
        /// </summary>
        [JsonPropertyName("cores")]
        public int Cores { get; set; }

        /// <summary>
        /// 芯片版本
        /// </summary>
        [JsonPropertyName("revision")]
        public int Revision { get; set; }

        /// <summary>
        /// 芯片功能标识
        /// </summary>
        [JsonPropertyName("features")]
        public int Features { get; set; }
    }

    /// <summary>
    /// 应用程序信息
    /// </summary>
    public class ApplicationInfo
    {
        /// <summary>
        /// 应用名称（如 xiaozhi）
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// 应用版本（如 2.1.0）
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// 编译时间
        /// </summary>
        [JsonPropertyName("compile_time")]
        public string CompileTime { get; set; }

        /// <summary>
        /// IDF 版本
        /// </summary>
        [JsonPropertyName("idf_version")]
        public string IdfVersion { get; set; }

        /// <summary>
        /// ELF 文件 SHA256 哈希
        /// </summary>
        [JsonPropertyName("elf_sha256")]
        public string ElfSha256 { get; set; }
    }

    /// <summary>
    /// 分区表项
    /// </summary>
    public class PartitionTableItem
    {
        /// <summary>
        /// 分区标签（如 nvs/otadata）
        /// </summary>
        [JsonPropertyName("label")]
        public string Label { get; set; }

        /// <summary>
        /// 分区类型（数字标识）
        /// </summary>
        [JsonPropertyName("type")]
        public int Type { get; set; }

        /// <summary>
        /// 分区子类型
        /// </summary>
        [JsonPropertyName("subtype")]
        public int Subtype { get; set; }

        /// <summary>
        /// 分区起始地址
        /// </summary>
        [JsonPropertyName("address")]
        public int Address { get; set; }

        /// <summary>
        /// 分区大小（字节）
        /// </summary>
        [JsonPropertyName("size")]
        public int Size { get; set; }
    }

    /// <summary>
    /// OTA 分区信息
    /// </summary>
    public class OtaInfo
    {
        /// <summary>
        /// 当前 OTA 分区标签（如 ota_0）
        /// </summary>
        [JsonPropertyName("label")]
        public string Label { get; set; }
    }

    /// <summary>
    /// 显示设备信息
    /// </summary>
    public class DisplayInfo
    {
        /// <summary>
        /// 是否单色屏
        /// </summary>
        [JsonPropertyName("monochrome")]
        public bool Monochrome { get; set; }

        /// <summary>
        /// 屏幕宽度
        /// </summary>
        [JsonPropertyName("width")]
        public int Width { get; set; }

        /// <summary>
        /// 屏幕高度
        /// </summary>
        [JsonPropertyName("height")]
        public int Height { get; set; }
    }

    /// <summary>
    /// 主板/网络信息
    /// </summary>
    public class BoardInfo
    {
        /// <summary>
        /// 主板类型
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// 主板名称
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// 连接的 WiFi SSID
        /// </summary>
        [JsonPropertyName("ssid")]
        public string Ssid { get; set; }

        /// <summary>
        /// WiFi 信号强度（RSSI）
        /// </summary>
        [JsonPropertyName("rssi")]
        public int Rssi { get; set; }

        /// <summary>
        /// WiFi 信道
        /// </summary>
        [JsonPropertyName("channel")]
        public int Channel { get; set; }

        /// <summary>
        /// 设备 IP 地址
        /// </summary>
        [JsonPropertyName("ip")]
        public string Ip { get; set; }

        /// <summary>
        /// 主板 MAC 地址
        /// </summary>
        [JsonPropertyName("mac")]
        public string Mac { get; set; }
    }
}
