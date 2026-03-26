本项目对[XiaoZhi.net](https://github.com/mm7h/XiaoZhi.Net)项目的二次开发，在此感谢XiaoZhi.net项目的开源大佬。

在原基础上增加了mqtt+udp通讯模式，增加了三方mcp注册，但是由于还未开发数据库部分，三方mcp绑定的终端token是写死的，暂时只支持一台终端注册。

整个项目按XiaoZhi.net项目说明部署好，再将我的代码覆盖原项目代码部分即可，我上传的就是修改的部分代码。

WebAPI文件夹为xiaozhi端ota的webapi（没有数据库都是临时的），appsettings.json配置ota请求时下发的消息，"mqttxs": true为下发mqtt服务器配置、false为不下发，小智端收到mqtt配置会优先连接mqtt，没有下发就会连接websocket。

mqtt+udp和修改的websocket都支持了IPv4和v6双栈,所以附带了修改xiaozhi-esp32的代码，修改xiaozhi-esp32的udp音频上传格式和增加了ipv6。

三方mcp连接：http://ip可以使用v6和v4+port/mcp/?token=AAAFPzL146bfSelCIxiGaYP73orWydK4ZOuDCajDn4bMPNXeIzYhp8y3ScGAQt0Xa

XiaoZhi.Net.Sample.Server项目里的configs文件夹里的configs.json文件添加配置
```
 "MqttConfig": {
   "Port": 1883,
   "UseTls": false,
   "GlobalUsername": "yangai_mqtt",
   "GlobalPassword": "yangai@123456",
   "ConnectionBacklog": 1000,
   "EnablePersistentSessions": true,
   "KeepAliveMonitorInterval": 30,
   "MaxPendingMessagesPerClient": 1000
 },
 "UdpConfig": {
   "Server": "192.168.1.37", // UDP服务绑定地址（0.0.0.0监听所有网卡）
   "Port": 8888, // UDP监听端口（可自定义，避免与MQTT端口冲突）
   "Key": "0123456789ABCDEF0123456789ABCDEF", // 128位AES密钥（32位纯十六进制，无分隔符）
   "NonceSalt": "UDP_AUDIO_NONCE_SALT_2026", // 生成nonce的盐值（自定义，建议保留）
   "WorkerCount": 2, // 可选, 表示使用 CPU 核心数
   "QueueSize": 1000 // 可选，每个 Worker 的队列容量
 },
 "McpServerEndpointconfig": {
   "Port": 8080,//三方mcp服务连接端口
   "Path": "mcp"//
 },
```

对接此服务端xiaozhi-esp32的源码需要修改mqtt_protocol.cc文件两个类
修改SendAudio函数为统一上传和接收的音频格式
```
bool MqttProtocol::SendAudio(std::unique_ptr<AudioStreamPacket> packet) {
    std::lock_guard<std::mutex> lock(channel_mutex_);
       if (udp_ == nullptr) {
        return false;
    }

    // 协议要求的17字节包头结构体
    struct AudioUdpHeader {
        uint8_t type;          // 1字节，固定0x01
        uint8_t flags;         // 1字节，未使用设为0
        uint16_t payload_len;  // 2字节，网络字节序
        uint32_t ssrc;         // 4字节，使用服务端分配的SSRC
        uint32_t timestamp;    // 4字节，网络字节序
        uint32_t sequence;     // 4字节，网络字节序
    } __attribute__((packed));

    AudioUdpHeader header = {0};
    header.type = 0x01;                
    header.flags = 0x00;               
    header.payload_len = htons(packet->payload.size()); 
    // 核心修改：替换为服务端分配的SSRC（而非手动生成）
    header.ssrc = htonl(server_udp_ssrc_); // 注意：转网络字节序！
    audio_ts += 60; // 每帧+60ms
    header.timestamp = htonl(audio_ts);
    //header.timestamp = htonl(packet->timestamp);       
    header.sequence = htonl(++local_sequence_);        

    // 后续组装/加密逻辑不变
    std::string encrypted_packet;
    encrypted_packet.resize(sizeof(AudioUdpHeader) + packet->payload.size());
    memcpy(encrypted_packet.data(), &header, sizeof(AudioUdpHeader));

    std::string nonce(aes_nonce_);
    *(uint16_t*)&nonce[2] = header.payload_len;
    *(uint32_t*)&nonce[8] = header.timestamp;
    *(uint32_t*)&nonce[12] = header.sequence;

    size_t nc_off = 0;
    uint8_t stream_block[16] = {0};
    if (mbedtls_aes_crypt_ctr(&aes_ctx_, packet->payload.size(), &nc_off, (uint8_t*)nonce.c_str(), stream_block,
        (uint8_t*)packet->payload.data(), (uint8_t*)&encrypted_packet[sizeof(AudioUdpHeader)]) != 0) {
        ESP_LOGE(TAG, "Failed to encrypt audio data");
        return false;
    }

    return udp_->Send(encrypted_packet) > 0;
}
```
修改ParseServerHello函数是增加一个ssrc用于udp音频数据
```
void MqttProtocol::ParseServerHello(const cJSON* root) {
    auto transport = cJSON_GetObjectItem(root, "transport");
    if (transport == nullptr || strcmp(transport->valuestring, "udp") != 0) {
        ESP_LOGE(TAG, "Unsupported transport: %s", transport->valuestring);
        return;
    }

    auto session_id = cJSON_GetObjectItem(root, "session_id");
    if (cJSON_IsString(session_id)) {
        session_id_ = session_id->valuestring;
        ESP_LOGI(TAG, "Session ID: %s", session_id_.c_str());
    }

    // Get sample rate from hello message
    auto audio_params = cJSON_GetObjectItem(root, "audio_params");
    if (cJSON_IsObject(audio_params)) {
        auto sample_rate = cJSON_GetObjectItem(audio_params, "sample_rate");
        if (cJSON_IsNumber(sample_rate)) {
            server_sample_rate_ = sample_rate->valueint;
        }
        auto frame_duration = cJSON_GetObjectItem(audio_params, "frame_duration");
        if (cJSON_IsNumber(frame_duration)) {
            server_frame_duration_ = frame_duration->valueint;
        }
    }

    auto udp = cJSON_GetObjectItem(root, "udp");
    if (!cJSON_IsObject(udp)) {
        ESP_LOGE(TAG, "UDP is not specified");
        return;
    }
    udp_server_ = cJSON_GetObjectItem(udp, "server")->valuestring;
    udp_port_ = cJSON_GetObjectItem(udp, "port")->valueint;
    auto key = cJSON_GetObjectItem(udp, "key")->valuestring;
    auto nonce = cJSON_GetObjectItem(udp, "nonce")->valuestring;
    //加了这部分
    auto assigned_ssrc = cJSON_GetObjectItem(udp, "assigned_ssrc");
    if (!cJSON_IsNumber(assigned_ssrc)) {
        ESP_LOGE(TAG, "assigned_ssrc is missing or invalid (not a number)");
        return;
    }
    server_udp_ssrc_ = assigned_ssrc->valueint;
    //到此
    aes_nonce_ = DecodeHexString(nonce);
    mbedtls_aes_init(&aes_ctx_);
    mbedtls_aes_setkey_enc(&aes_ctx_, (const unsigned char*)DecodeHexString(key).c_str(), 128);
    local_sequence_ = 0;
    remote_sequence_ = 0;
    xEventGroupSetBits(event_group_handle_, MQTT_PROTOCOL_SERVER_HELLO_EVENT);
}
```
