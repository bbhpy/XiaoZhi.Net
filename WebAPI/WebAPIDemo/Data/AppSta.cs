using WebAPIDemo.Controllers;

namespace WebAPIDemo.Data
{
    public class AppSta
    {
        static IConfiguration? configuration { get; set; }
        public AppSta() { }

        public static bool MqttEnabled { get; set; } = false;
        public static mqtt Mqtt { get; set; } = new();

        public static bool WebSocketEnabled { get; set; } = false;
        public static websocket WebSocket { get; set; }=new();

        /// <summary>
        /// 启动时 干活
        /// </summary>
        public static void AddServices(IServiceCollection services, IConfiguration _configuration) {
            configuration = _configuration;
            MqttEnabled = ConfigHelper.GetConfigToBool("mqttxs");
            Mqtt.endpoint = ConfigHelper.GetConfigToString("mqtt:endpoint");
            Mqtt.client_id = ConfigHelper.GetConfigToString("mqtt:client_id");
            Mqtt.username = ConfigHelper.GetConfigToString("mqtt:username");
            Mqtt.password = ConfigHelper.GetConfigToString("mqtt:password");
            Mqtt.subscribe_topic= ConfigHelper.GetConfigToString("mqtt:subscribe_topic");
            Mqtt.publish_topic = ConfigHelper.GetConfigToString("mqtt:publish_topic");

            WebSocketEnabled = ConfigHelper.GetConfigToBool("websocketxs");
            WebSocket.url= ConfigHelper.GetConfigToString("websocket:url");
            WebSocket.token= ConfigHelper.GetConfigToString("websocket:token");

        }
    }
    public class ConfigHelper
    {
        /// <summary>
        /// The configuration
        /// </summary>
        private static IConfigurationRoot? _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigHelper"/> class.
        /// </summary>
        public ConfigHelper()
        {
            if (_configuration == null)
            {
                IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
                _configuration = builder.Build();
            }
        }

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        public string GetConfig(string key)
        {
            string ret = string.Empty;
            if (_configuration != null)
            {
                ret = _configuration[key];
            }
            return ret;
        }

        /// <summary>
        ///获取字符串配置
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        public static string GetConfigToString(string key)
        {
            try
            {
                return new ConfigHelper().GetConfig(key);
            }
            catch
            {
                return "";
            }
        }
        /// <summary>
        /// 查询配置，返回整数
        /// </summary>
        /// <param name="key">键名</param>
        /// <returns></returns>
        public static int GetConfigToInt(string key)
        {
            try
            {
                string configValue = new ConfigHelper().GetConfig(key);
                if (configValue == null)
                {
                    return int.MinValue;
                }
                return Convert.ToInt32(configValue);
            }
            catch
            {
                return int.MinValue;
            }
        }
        /// <summary>
        /// 查询配置，返回整数
        /// </summary>
        /// <param name="key">键名</param>
        /// <returns></returns>
        public static bool GetConfigToBool(string key)
        {
            try
            {
                string configValue = new ConfigHelper().GetConfig(key);
                if (configValue == null)
                {
                    return false;
                }
                return Convert.ToBoolean(configValue);
            }
            catch
            {
                return false;
            }
        }
    }
}
