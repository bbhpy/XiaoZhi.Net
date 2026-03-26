using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using WebAPIDemo.Attributes;
using WebAPIDemo.Data;
using WebAPIDemo.Filters.AuthFilters;

namespace WebAPIDemo.Controllers
{
    [ApiVersion("1.0")]
    [ApiExplorerSettings(GroupName = "v1")]
    [ApiController]
    [Route("yangai")]
    public class XiaoZhiController : ControllerBase
    {
        public XiaoZhiController() { }


        [HttpGet]
        [Route("ota")]
        [RequiredClaim("read", "true")]
        public IActionResult GetShirts()
        {
            var result = new
            {
                message = "Hello, this is a GET request to /yangai/v1/ota"
            };
            Console.WriteLine(Request.HttpContext.Connection.RemoteIpAddress);
            return Ok(result);
        }



        [HttpPost]
        [Route("ota")]
        [RequiredClaim("read", "true")]
        public async Task<IActionResult> PostShirts()
        {
            string ret=string.Empty;
            Dictionary<string, string> dict = new Dictionary<string, string>();
            foreach (var header in Request.Headers)
            {
                //Console.WriteLine($"{header.Key}: {header.Value}");
                dict[header.Key] = header.Value;
            }
            int len=dict.Count;

            // 2. 获取请求体（原始字符串）
            string rawBody = await new StreamReader(Request.Body).ReadToEndAsync();
            //Request.Body.Position = 0; // 重置流位置
            string he= System.Text.Json.JsonSerializer.Serialize(dict, options);

            string gid = "GID_yangai@@@" + dict["Device-Id"].Replace(":","_") + "@@@" + dict["Client-Id"];

            dynamic requestBody = null;
            if (!string.IsNullOrEmpty(rawBody))
            {
                try
                {
                    requestBody = JsonConvert.DeserializeObject(rawBody);
                }
                catch
                {
                    return BadRequest("请求体不是合法的JSON格式");
                }
            }

            var remoteIp = Request.HttpContext.Connection.RemoteIpAddress;

            Console.WriteLine("客户端IP:{0},Device-Id:{1},Client-Id:{2}请求体:{3}", remoteIp, dict["Device-Id"].Replace(":", "_"), dict["Client-Id"], rawBody);
            string ipVersion = "Unknown";

            if (remoteIp != null)
            {
                if (remoteIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    ipVersion = "ws://v4.tghis.com:4530/yangai/v1/";
                }
                else if (remoteIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    ipVersion = "ws://v6.tghis.com:4530/yangai/v1/";
                }
            }
            DateTime localTime = DateTime.Now; 
            long timestamp1 = (long)(localTime - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

            // 修正后（含时区偏移的本地时间戳）
            // 步骤1：获取本地时区与UTC的偏移量
            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(localTime);
            // 步骤2：在UTC时间戳基础上加时区偏移
            long localTimestamp = timestamp1 - (long)offset.TotalMilliseconds;
            //var result = new { };
            if (AppSta.MqttEnabled)
            {
               var result = new
                {
                    mqtt = new
                    {
                        endpoint = AppSta.Mqtt.endpoint,
                        client_id = gid,
                        username = AppSta.Mqtt.username,
                        password = AppSta.Mqtt.password,
                        publish_topic = AppSta.Mqtt.publish_topic,
                        subscribe_topic = AppSta.Mqtt.subscribe_topic
                    },
                    websocket = new
                    {
                        url = AppSta.WebSocket.url,
                        token = AppSta.WebSocket.token
                    },
                    server_time = new
                    {
                        timestamp = localTimestamp,
                        timezone_offset = 480
                    },
                    firmware = new
                    {
                        version = "2.1.0",
                        url = ""
                    }
                };
                return Ok(result);
            }
            else
            {
                var result = new
                {
                    websocket = new
                    {
                        url = AppSta.WebSocket.url,
                        token = AppSta.WebSocket.token
                    },
                    server_time = new
                    {
                        timestamp = timestamp1,
                        timezone_offset = 480
                    },
                    firmware = new
                    {
                        version = "2.1.0",
                        url = ""
                    }
                };
                return Ok(result);
            }
            //return ret;
        }

        JsonSerializerOptions options = new JsonSerializerOptions
        {
            WriteIndented = true, // 格式化（换行+缩进），生产环境可设为 false 减小体积
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 支持中文/特殊字符
        };
    }
    public class mqtt {         
        public string endpoint { get; set; }
        public string client_id { get; set; }
        public string username { get; set; }
        public string password { get; set; }
        public string publish_topic { get; set; }
        public string subscribe_topic { get; set; }
    }
    public class websocket
    {
        public string url { get; set; }
        public string token { get; set; }
    }
    public class server_time
    {
        public long timestamp { get; set; }
        public int timezone_offset { get; set; } = 480;
    }
    public class firmware
    {
        public string version { get; set; } = "2.1.0";
        public string url { get; set; } = string.Empty;
    }

    public class OtaRequest
    {
        public string DeviceId { get; set; } // 设备ID
        public string Version { get; set; }  // 固件版本
        public string Cmd { get; set; }      // 指令
    }

    /*
     {
	"version": 2,
	"language": "zh-CN",
	"flash_size": 16777216,
	"minimum_free_heap_size": "7162016",
	"mac_address": "1c:db:d4:75:14:24",
	"uuid": "6ab6f976-5e02-40b4-a0cf-cc9a85ae551a",
	"chip_model_name": "esp32s3",
	"chip_info": {
		"model": 9,
		"cores": 2,
		"revision": 2,
		"features": 18
	},
	"application": {
		"name": "xiaozhi",
		"version": "2.1.0",
		"compile_time": "Feb 12 2026T14:34:52Z",
		"idf_version": "v5.5.2",
		"elf_sha256": "b13d1b2802f2da7f5084b270065d8144718eb4aa6b3f11548a01a795beb1572c"
	},
	"partition_table": [{
		"label": "nvs",
		"type": 1,
		"subtype": 2,
		"address": 36864,
		"size": 16384
	}, {
		"label": "otadata",
		"type": 1,
		"subtype": 0,
		"address": 53248,
		"size": 8192
	}, {
		"label": "phy_init",
		"type": 1,
		"subtype": 1,
		"address": 61440,
		"size": 4096
	}, {
		"label": "ota_0",
		"type": 0,
		"subtype": 16,
		"address": 131072,
		"size": 4128768
	}, {
		"label": "ota_1",
		"type": 0,
		"subtype": 17,
		"address": 4259840,
		"size": 4128768
	}, {
		"label": "assets",
		"type": 1,
		"subtype": 130,
		"address": 8388608,
		"size": 8388608
	}],
	"ota": {
		"label": "ota_0"
	},
	"display": {
		"monochrome": false,
		"width": 320,
		"height": 240
	},
	"board": {
		"type": "bread-compact-wifi-s3cam",
		"name": "bread-compact-wifi-s3cam",
		"ssid": "dashijie",
		"rssi": -55,
		"channel": 6,
		"ip": "192.168.0.244",
		"mac": "1c:db:d4:75:14:24"
	}
}

{
  "Connection": "close",
  "Host": "192.168.1.37:5250",
  "User-Agent": "bread-compact-wifi-s3cam/2.1.0",
  "Accept-Language": "zh-CN",
  "Content-Type": "application/json",
  "Content-Length": "1139",
  "Activation-Version": "1",
  "Client-Id": "6ab6f976-5e02-40b4-a0cf-cc9a85ae551a",
  "Device-Id": "d4:75:14:1c:db:24"
}
     */
}
