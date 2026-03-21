#include "esp_now_handler.h"
#include <esp_now.h>
#include <esp_wifi.h>
#include <esp_log.h>
#include <esp_timer.h>
#include <string.h>
#include <stdio.h>
#include <ctype.h>
#include "esp_timer.h"

static const char* TAG = "ESP_NOW_HANDLER";

// 预定义的从设备列表（最多支持20个设备）
#define MAX_DEVICES 20
static device_info_t device_list[MAX_DEVICES] = {
    {"Servo_Controller", {0x08, 0xD1, 0xF9, 0x99, 0xC6, 0xB8}, DEVICE_TYPE_SERVO, false,false,0,1}
    ,{"Motor_Controller", {0x11, 0x22, 0x33, 0x44, 0x55, 0x66}, DEVICE_TYPE_MOTOR, false,false,0,2}
    ,{"LED_Controller",   {0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF}, DEVICE_TYPE_LED, false,false,0,3}
};

static int registered_device_count = 3;
static bool esp_now_initialized = false;

// 语音指令到控制命令的映射表
static const voice_command_mapping_t voice_command_map[] = {
    // 舵机控制指令
    {"举手", CMD_RAISE_HAND, DEVICE_TYPE_SERVO, "控制舵机举手"},
    {"把手举起来", CMD_RAISE_HAND, DEVICE_TYPE_SERVO, "控制舵机举手"},
    {"raise hand", CMD_RAISE_HAND, DEVICE_TYPE_SERVO, "控制舵机举手"},
    {"放下手", CMD_LOWER_HAND, DEVICE_TYPE_SERVO, "控制舵机放下手"},
    {"把手放下", CMD_LOWER_HAND, DEVICE_TYPE_SERVO, "控制舵机放下手"},
    {"lower hand", CMD_LOWER_HAND, DEVICE_TYPE_SERVO, "控制舵机放下手"},
    
    // 电机控制指令
    {"前进", CMD_MOVE_FORWARD, DEVICE_TYPE_MOTOR, "控制电机前进"},
    {"向前移动", CMD_MOVE_FORWARD, DEVICE_TYPE_MOTOR, "控制电机前进"},
    {"move forward", CMD_MOVE_FORWARD, DEVICE_TYPE_MOTOR, "控制电机前进"},
    {"左转", CMD_TURN_LEFT, DEVICE_TYPE_MOTOR, "控制电机左转"},
    {"向左转", CMD_TURN_LEFT, DEVICE_TYPE_MOTOR, "控制电机左转"},
    {"turn left", CMD_TURN_LEFT, DEVICE_TYPE_MOTOR, "控制电机左转"},
    {"右转", CMD_TURN_RIGHT, DEVICE_TYPE_MOTOR, "控制电机右转"},
    {"向右转", CMD_TURN_RIGHT, DEVICE_TYPE_MOTOR, "控制电机右转"},
    {"turn right", CMD_TURN_RIGHT, DEVICE_TYPE_MOTOR, "控制电机右转"},
    {"停止", CMD_STOP, DEVICE_TYPE_MOTOR, "控制电机停止"},
    {"停下", CMD_STOP, DEVICE_TYPE_MOTOR, "控制电机停止"},
    {"stop", CMD_STOP, DEVICE_TYPE_MOTOR, "控制电机停止"},
    
    // 通用指令
    {"设置角度", CMD_SET_ANGLE, DEVICE_TYPE_SERVO, "设置舵机角度"},
    {"设置速度", CMD_SET_SPEED, DEVICE_TYPE_MOTOR, "设置电机速度"}
};

static const int voice_command_count = sizeof(voice_command_map) / sizeof(voice_command_mapping_t);

// ESP-NOW发送回调函数（ESP-IDF 5.5版本）
static void esp_now_send_cb(const wifi_tx_info_t *info, esp_now_send_status_t status) {
    const uint8_t *mac_addr = info->des_addr;
    if (status == ESP_NOW_SEND_SUCCESS) {
        ESP_LOGI(TAG, "✅ 指令发送成功到 %02X:%02X:%02X:%02X:%02X:%02X",
                mac_addr[0], mac_addr[1], mac_addr[2],
                mac_addr[3], mac_addr[4], mac_addr[5]);
    } else {
        ESP_LOGE(TAG, "❌ 指令发送失败到 %02X:%02X:%02X:%02X:%02X:%02X",
                mac_addr[0], mac_addr[1], mac_addr[2],
                mac_addr[3], mac_addr[4], mac_addr[5]);
    }
}

// 计算校验和
static uint8_t calculate_checksum(const esp_now_control_msg_t* msg) {
    uint8_t sum = 0;
    const uint8_t* data = (const uint8_t*)msg;
    
    for (size_t i = 0; i < sizeof(esp_now_control_msg_t) - 1; i++) {
        sum += data[i];
    }
    
    return ~sum + 1;  // 补码校验
}

// 检查ESP-NOW是否已初始化
bool is_esp_now_initialized(void) {
    return esp_now_initialized;
}

// 初始化ESP-NOW处理器（确保在WiFi连接成功后调用）
esp_err_t esp_now_handler_init(void) {

    if (esp_now_initialized) {
        ESP_LOGW(TAG, "ESP-NOW已初始化");
        return ESP_OK;
    }
    
    ESP_LOGI(TAG, "开始初始化ESP-NOW处理器...");
    
    // 检查WiFi是否已启动
    wifi_mode_t mode;
    if (esp_wifi_get_mode(&mode) != ESP_OK) {
        ESP_LOGE(TAG, "WiFi未初始化，请先初始化WiFi");
        return ESP_ERR_WIFI_NOT_INIT;
    }
    
    // 获取当前WiFi信道（确保ESP-NOW使用相同信道）
    uint8_t channel = 6;
    wifi_second_chan_t second_chan = WIFI_SECOND_CHAN_NONE;
    esp_err_t ret = esp_wifi_get_channel(&channel, &second_chan);
    if (ret == ESP_OK) {
        ESP_LOGI(TAG, "当前WiFi信道: %d", channel);
    } else {
        channel = 1; // 默认信道
        ESP_LOGW(TAG, "无法获取WiFi信道，使用默认信道: %d", channel);
    }
    
    // 初始化ESP-NOW
    ret = esp_now_init();
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "ESP-NOW初始化失败: %s", esp_err_to_name(ret));
        return ret;
    }
    print_esp_now_version();
    
    // 注册发送回调（ESP-IDF 5.5版本）
    ret = esp_now_register_send_cb(esp_now_send_cb);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "注册发送回调失败: %s", esp_err_to_name(ret));
        esp_now_deinit();
        return ret;
    }
    
    // 注册所有预定义设备
    int success_count = 0;
    for (int i = 0; i < registered_device_count; i++) {
        if (!device_list[i].is_registered) {
            // ESP-IDF 5.5版本的结构体初始化方式
            esp_now_peer_info_t peer_info = {};
            memcpy(peer_info.peer_addr, device_list[i].mac_addr, 6);
            peer_info.channel = channel;
            peer_info.ifidx = WIFI_IF_STA;
            peer_info.encrypt = false;
            memset(peer_info.lmk, 0, ESP_NOW_KEY_LEN);
            peer_info.priv = NULL;
            
            ret = esp_now_add_peer(&peer_info);
            if (ret == ESP_OK) {
                device_list[i].is_registered = true;
                success_count++;
                ESP_LOGI(TAG, "✅ 设备 %s 注册成功", device_list[i].device_name);
            } else {
                ESP_LOGE(TAG, "设备 %s 注册失败: %s", 
                        device_list[i].device_name, esp_err_to_name(ret));
            }
        }
    }
    
    esp_now_initialized = true;
    ESP_LOGI(TAG, "✅ ESP-NOW处理器初始化完成");
    ESP_LOGI(TAG, "  成功注册设备: %d/%d", success_count, registered_device_count);
    ESP_LOGI(TAG, "  当前信道: %d", channel);
    
    return ESP_OK;
}
// 方法1：直接获取版本号
void print_esp_now_version(void)
{
    uint32_t version = 0;
    esp_err_t ret = esp_now_get_version(&version);
    
    if (ret == ESP_OK) {
        // 观察你的输出：0x00000002 -> 版本2.0
        // 说明版本号直接存储在低位字节
        
        // 方法A：按位解析（适用于你的情况）
        uint8_t major = version & 0xFF;      // 主版本
        uint8_t minor = (version >> 8) & 0xFF; // 次版本
        uint8_t patch = (version >> 16) & 0xFF; // 补丁
        uint8_t reserved = (version >> 24) & 0xFF; // 保留
        
        ESP_LOGI(TAG, "════════════════════════════════════════");
        ESP_LOGI(TAG, "ESP-NOW 版本信息 (正确解析)");
        ESP_LOGI(TAG, "════════════════════════════════════════");
        ESP_LOGI(TAG, "原始版本号: 0x%08X", version);
        ESP_LOGI(TAG, "主版本号: %d", major);
        ESP_LOGI(TAG, "次版本号: %d", minor);
        ESP_LOGI(TAG, "补丁版本: %d", patch);
        
        // 根据你的输出，版本是2.0
        ESP_LOGI(TAG, "实际版本: v%d.%d", major, minor);
        
        // 与系统日志对比
        ESP_LOGI(TAG, "系统日志显示: version: %d.%d", major, minor);
        ESP_LOGI(TAG, "════════════════════════════════════════");
    }
}

// "空中波特率设置为9600"实际上是通过配置Wi-Fi底层参数来实现的：

// 协议模式选择：使用WIFI_PROTOCOL_11B | WIFI_PROTOCOL_11G而不是默认的包含802.11n的模式，这降低了最大传输速率但提高了稳定性
// 带宽设置：使用WIFI_BW_HT20（20MHz带宽）而不是40MHz，减少干扰
// 固定信道：选择一个相对干净的信道，避免信道切换带来的不稳定
esp_err_t esp_now_optimize_communication(void){
    esp_err_t ret;
    
    // 1. 设置协议模式为802.11b/g
    ret = esp_wifi_set_protocol(WIFI_IF_STA, WIFI_PROTOCOL_11B | WIFI_PROTOCOL_11G);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "设置协议模式失败: %s", esp_err_to_name(ret));
        return ret;
    }
    
    // 2. 设置20MHz带宽
    ret = esp_wifi_set_bandwidth(WIFI_IF_STA, WIFI_BW_HT20);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "设置带宽失败: %s", esp_err_to_name(ret));
        return ret;
    }
    
    // 3. 设置固定信道
    // ret = esp_wifi_set_channel(6, WIFI_SECOND_CHAN_NONE);
    // if (ret != ESP_OK) {
    //     ESP_LOGE(TAG, "设置信道失败: %s", esp_err_to_name(ret));
    //     return ret;
    // }
    
    ESP_LOGI(TAG, "ESP-NOW通信参数优化完成");
    return ESP_OK;  // 返回 ESP_OK 表示成功
}

// 反初始化ESP-NOW处理器
esp_err_t esp_now_handler_deinit(void) {
    if (!esp_now_initialized) {
        return ESP_OK;
    }
    
    ESP_LOGI(TAG, "开始反初始化ESP-NOW处理器...");
    
    // 注销发送回调
    esp_now_unregister_send_cb();
    
    // 删除所有对等设备
    for (int i = 0; i < registered_device_count; i++) {
        if (device_list[i].is_registered) {
            esp_now_del_peer(device_list[i].mac_addr);
            device_list[i].is_registered = false;
        }
    }
    
    // 反初始化ESP-NOW
    esp_err_t ret = esp_now_deinit();
    if (ret == ESP_OK) {
        esp_now_initialized = false;
        ESP_LOGI(TAG, "✅ ESP-NOW处理器反初始化完成");
    } else {
        ESP_LOGE(TAG, "ESP-NOW反初始化失败: %s", esp_err_to_name(ret));
    }
    
    return ret;
}

// 注册新设备到ESP-NOW对等列表
esp_err_t register_device(const char* device_name, const uint8_t* mac_addr, device_type_t type) {
    if (device_name == NULL || mac_addr == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    
    if (!esp_now_initialized) {
        ESP_LOGE(TAG, "ESP-NOW未初始化，无法注册设备");
        return ESP_ERR_WIFI_NOT_INIT;
    }
    
    // 检查设备是否已存在
    for (int i = 0; i < registered_device_count; i++) {
        if (memcmp(device_list[i].mac_addr, mac_addr, 6) == 0) {
            if (device_list[i].is_registered) {
                ESP_LOGW(TAG, "设备 %s 已注册", device_name);
                return ESP_OK;
            }
        }
    }
    
    // 检查是否达到最大设备数
    if (registered_device_count >= MAX_DEVICES) {
        ESP_LOGE(TAG, "设备数量已达上限 %d", MAX_DEVICES);
        return ESP_ERR_NO_MEM;
    }
    
    // ESP-IDF 5.5版本的结构体初始化方式
    esp_now_peer_info_t peer_info = {};
    memcpy(peer_info.peer_addr, mac_addr, 6);
    peer_info.channel = 0;  // 使用当前信道
    peer_info.ifidx = WIFI_IF_STA;
    peer_info.encrypt = false;
    memset(peer_info.lmk, 0, ESP_NOW_KEY_LEN);
    peer_info.priv = NULL;
    
    // 添加到ESP-NOW对等列表
    esp_err_t ret = esp_now_add_peer(&peer_info);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "添加设备 %s 失败: %s", device_name, esp_err_to_name(ret));
        return ret;
    }
    
    // 保存设备信息
    strncpy(device_list[registered_device_count].device_name, device_name, 31);
    device_list[registered_device_count].device_name[31] = '\0';
    memcpy(device_list[registered_device_count].mac_addr, mac_addr, 6);
    device_list[registered_device_count].type = type;
    device_list[registered_device_count].is_registered = true;
    
    registered_device_count++;
    
    ESP_LOGI(TAG, "✅ 新设备 %s 注册成功", device_name);
    ESP_LOGI(TAG, "  MAC地址: %02X:%02X:%02X:%02X:%02X:%02X",
            mac_addr[0], mac_addr[1], mac_addr[2],
            mac_addr[3], mac_addr[4], mac_addr[5]);
    
    return ESP_OK;
}

// 查找设备信息
static device_info_t* find_device_by_name(const char* device_name) {
    for (int i = 0; i < registered_device_count; i++) {
        if (strcmp(device_list[i].device_name, device_name) == 0) {
            return &device_list[i];
        }
    }
    return NULL;
}

// 发送控制命令到指定设备（带重试机制）
esp_err_t send_control_command(const char* device_name, command_type_t cmd, int16_t value, uint16_t duration) {
    if (device_name == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    
    if (!esp_now_initialized) {
        ESP_LOGE(TAG, "ESP-NOW未初始化，无法发送命令");
        return ESP_ERR_WIFI_NOT_INIT;
    }
    
    // 查找设备
    device_info_t* device = find_device_by_name(device_name);
    if (device == NULL || !device->is_registered) {
        ESP_LOGE(TAG, "设备 %s 未找到或未注册", device_name);
        return ESP_ERR_NOT_FOUND;
    }
    
    // 构建控制消息
    esp_now_control_msg_t control_msg = {0};
    memcpy(control_msg.target_mac, device->mac_addr, 6);
    control_msg.command_type = cmd;
    control_msg.device_type = device->type;
    control_msg.actuator_id = 0;  // 默认执行器ID
    control_msg.value = value;
    control_msg.duration = duration;
    control_msg.timestamp = esp_timer_get_time() / 1000;  // 转换为毫秒
    
    // 计算校验和
    control_msg.checksum = calculate_checksum(&control_msg);
    
    ESP_LOGI(TAG, "🚀 发送控制命令到设备: %s", device_name);
    ESP_LOGI(TAG, "  命令类型: %d, 设备类型: %d", cmd, device->type);
    ESP_LOGI(TAG, "  命令值: %d, 持续时间: %dms", value, duration);
    ESP_LOGI(TAG, "  MAC地址: %02X:%02X:%02X:%02X:%02X:%02X",
            device->mac_addr[0], device->mac_addr[1], device->mac_addr[2],
            device->mac_addr[3], device->mac_addr[4], device->mac_addr[5]);
    
    // 发送ESP-NOW消息（带重试机制）
    esp_err_t ret = ESP_FAIL;
    const int max_retries = 3;
    
    for (int attempt = 0; attempt < max_retries; attempt++) {
        ret = esp_now_send(device->mac_addr, (uint8_t*)&control_msg, sizeof(control_msg));
        
        if (ret == ESP_OK) {
            ESP_LOGI(TAG, "✅ 第%d次尝试发送成功", attempt + 1);
            return ESP_OK;
        } else {
            ESP_LOGW(TAG, "第%d次尝试发送失败: %s", attempt + 1, esp_err_to_name(ret));
            vTaskDelay(pdMS_TO_TICKS(50 * (attempt + 1))); // 递增延迟
        }
    }
    
    ESP_LOGE(TAG, "发送失败，已达到最大重试次数: %d", max_retries);
    return ret;
}

// 发送广播命令到所有设备
esp_err_t send_broadcast_command(command_type_t cmd, int16_t value, uint16_t duration) {
    if (!esp_now_initialized) {
        ESP_LOGE(TAG, "ESP-NOW未初始化，无法发送广播命令");
        return ESP_ERR_WIFI_NOT_INIT;
    }
    
    ESP_LOGI(TAG, "📢 发送广播命令到所有设备");
    
    esp_err_t overall_result = ESP_OK;
    int success_count = 0;
    
    for (int i = 0; i < registered_device_count; i++) {
        if (device_list[i].is_registered) {
            esp_err_t ret = send_control_command(device_list[i].device_name, cmd, value, duration);
            if (ret == ESP_OK) {
                success_count++;
            } else {
                overall_result = ret;
            }
        }
    }
    
    ESP_LOGI(TAG, "广播完成: %d/%d 个设备成功", success_count, registered_device_count);
    return overall_result;
}

// 解析语音指令
const char* parse_voice_command(const char* voice_text) {
    if (voice_text == NULL) {
        return "语音指令为空";
    }
    
    ESP_LOGI(TAG, "解析语音指令: %s", voice_text);
    
    // 转换为小写便于匹配
    char lower_text[256];
    strncpy(lower_text, voice_text, sizeof(lower_text) - 1);
    lower_text[sizeof(lower_text) - 1] = '\0';
    
    for (int i = 0; lower_text[i]; i++) {
        lower_text[i] = tolower(lower_text[i]);
    }
    
    // 查找匹配的语音指令
    for (int i = 0; i < voice_command_count; i++) {
        if (strstr(lower_text, voice_command_map[i].voice_command) != NULL) {
            ESP_LOGI(TAG, "✅ 匹配到指令: %s -> %s", 
                    voice_command_map[i].voice_command,
                    voice_command_map[i].description);
            
            // 根据指令类型执行相应操作
            switch (voice_command_map[i].command) {
                case CMD_RAISE_HAND:
                    send_control_command("Servo_Controller", CMD_RAISE_HAND, 90, 1000);
                    return "正在举手...";
                    
                case CMD_LOWER_HAND:
                    send_control_command("Servo_Controller", CMD_LOWER_HAND, 0, 1000);
                    return "正在放下手...";
                    
                case CMD_MOVE_FORWARD:
                    send_control_command("Motor_Controller", CMD_MOVE_FORWARD, 100, 2000);
                    return "正在前进...";
                    
                case CMD_TURN_LEFT:
                    send_control_command("Motor_Controller", CMD_TURN_LEFT, 50, 1000);
                    return "正在左转...";
                    
                case CMD_TURN_RIGHT:
                    send_control_command("Motor_Controller", CMD_TURN_RIGHT, 50, 1000);
                    return "正在右转...";
                    
                case CMD_STOP:
                    send_broadcast_command(CMD_STOP, 0, 0);
                    return "已停止所有设备";
                    
                default:
                    return "指令已识别，但未实现具体操作";
            }
        }
    }
    
    ESP_LOGW(TAG, "❌ 未识别的语音指令: %s", voice_text);
    return "未识别的语音指令，请重试";
}

// 打印已注册设备列表
void print_registered_devices(void) {
    ESP_LOGI(TAG, "=== 已注册设备列表 ===");
    
    if (registered_device_count == 0) {
        ESP_LOGI(TAG, "暂无注册设备");
        return;
    }
    
    for (int i = 0; i < registered_device_count; i++) {
        const char* type_str = "未知";
        switch (device_list[i].type) {
            case DEVICE_TYPE_SERVO: type_str = "舵机"; break;
            case DEVICE_TYPE_MOTOR: type_str = "电机"; break;
            case DEVICE_TYPE_LED:   type_str = "LED"; break;
        }
        
        ESP_LOGI(TAG, "%d. %s [%s]", i + 1, device_list[i].device_name, type_str);
        ESP_LOGI(TAG, "   MAC: %02X:%02X:%02X:%02X:%02X:%02X",
                device_list[i].mac_addr[0], device_list[i].mac_addr[1],
                device_list[i].mac_addr[2], device_list[i].mac_addr[3],
                device_list[i].mac_addr[4], device_list[i].mac_addr[5]);
        ESP_LOGI(TAG, "   状态: %s", device_list[i].is_registered ? "已注册" : "未注册");
    }
}

void check_and_reconnect_all_devices(void){
    int ret=auto_check_and_reconnect_all_devices(device_list,registered_device_count,30000);
    if(ret>0){
        ESP_LOGI("ESP_NOW_CONN", "有 %s 个设备连接",ret);
    }else{
        ESP_LOGI("ESP_NOW_CONN", "ESP-NOW设备连接错误");
    }
}
// /**
//  * @brief 自动检测并重连所有设备的完整函数
//  * @param device_list 设备信息数组
//  * @param device_count 设备数量
//  * @param check_interval_ms 检查间隔（毫秒），默认30000ms
//  * @return int 返回成功重连的设备数量，负数表示错误
//  */
int auto_check_and_reconnect_all_devices(device_info_t* device_list, int device_count, uint32_t check_interval_ms = 30000) {
    static unsigned long last_check_time = 0;
    unsigned long current_time = esp_timer_get_time() / 1000;
    
    if (current_time - last_check_time < check_interval_ms) {
        return 0;
    }
    
    ESP_LOGI("ESP_NOW_CONN", "开始自动检测设备连接状态...");
    last_check_time = current_time;
    
    int disconnected_count = 0;
    int reconnected_count = 0;
    int total_checked = 0;
    
    for (int i = 0; i < device_count; i++) {
        device_info_t* device = &device_list[i];
        
        if (!device->is_registered) {
            ESP_LOGD("ESP_NOW_CONN", "设备 %s 未注册，跳过检查", device->device_name);
            continue;
        }
        
        total_checked++;
        
        // 检查设备连接状态
        bool is_connected = true;
        if (current_time - device->last_seen > 30000) {
            is_connected = false;
            disconnected_count++;
            ESP_LOGW("ESP_NOW_CONN", "设备 %s (ID:%d) 连接超时，最后通信: %lu秒前", 
                    device->device_name, device->id, 
                    (current_time - device->last_seen) / 1000);
        }
        
        bool was_connected = device->is_connected;
        device->is_connected = is_connected;
        
        if (!is_connected) {
            ESP_LOGI("ESP_NOW_CONN", "尝试重连设备: %s", device->device_name);
            
            bool peer_exists = esp_now_is_peer_exist(device->mac_addr);
            
            if (!peer_exists) {
                esp_now_peer_info_t peer_info = {};
                memset(&peer_info, 0, sizeof(peer_info));
                memcpy(peer_info.peer_addr, device->mac_addr, 6);
                peer_info.channel = 1;
                peer_info.encrypt = false;
                peer_info.ifidx = WIFI_IF_STA;
                
                esp_err_t add_result = esp_now_add_peer(&peer_info); // 使用不同的变量名
                if (add_result == ESP_OK) {
                    ESP_LOGI("ESP_NOW_CONN", "成功添加设备到配对列表: %s", device->device_name);
                    peer_exists = true;
                } else {
                    ESP_LOGW("ESP_NOW_CONN", "添加设备失败，错误码: %d", add_result);
                    continue;
                }
            }
            
            if (peer_exists) {
                uint8_t test_packet[] = {0xAA, 0x55, 0x01};
                esp_err_t send_result = esp_now_send(device->mac_addr, test_packet, sizeof(test_packet));
                
                if (send_result == ESP_OK) {
                    ESP_LOGI("ESP_NOW_CONN", "测试数据包发送成功，设备 %s 重连成功", device->device_name);
                    device->is_connected = true;
                    device->last_seen = current_time;
                    reconnected_count++;
                } else {
                    // 关键修正：使用正确的变量名 send_result
                    ESP_LOGW("ESP_NOW_CONN", "测试数据包发送失败，错误码: %d", send_result);
                    
                    esp_now_del_peer(device->mac_addr);
                    ESP_LOGI("ESP_NOW_CONN", "已从配对列表中删除设备 %s，将在下次检查时重新尝试", device->device_name);
                }
            }
        } else if (!was_connected && is_connected) {
            ESP_LOGI("ESP_NOW_CONN", "设备 %s 连接状态已恢复", device->device_name);
        }
    }
    
    if (disconnected_count > 0) {
        ESP_LOGW("ESP_NOW_CONN", "连接检查完成：检查了 %d 个设备，%d 个断开连接，成功重连 %d 个", 
                total_checked, disconnected_count, reconnected_count);
    } else {
        ESP_LOGI("ESP_NOW_CONN", "连接检查完成：所有 %d 个设备连接正常", total_checked);
    }
    
    return reconnected_count;
}


// /**
//  * @brief 更新设备最后通信时间（在数据接收回调中调用）
//  * @param mac_addr 设备的MAC地址
//  * @param device_list 设备列表
//  * @param device_count 设备数量
//  */
void update_device_last_seen(const uint8_t* mac_addr, device_info_t* device_list, int device_count) {
    for (int i = 0; i < device_count; i++) {
        if (memcmp(device_list[i].mac_addr, mac_addr, 6) == 0) {
            device_list[i].last_seen = esp_timer_get_time() / 1000;
            device_list[i].is_connected = true;
            
            ESP_LOGD(TAG, "更新设备 %s 的最后通信时间", device_list[i].device_name);
            break;
        }
    }
}


// // esp_now_handler.cc - 完整修改版本
// #include "esp_now_handler.h"
// #include "esp_wifi.h"
// #include "esp_now.h"
// #include "esp_log.h"
// #include <string.h>
// #include <algorithm>
// #include <vector>
// #include "freertos/FreeRTOS.h"
// #include "freertos/task.h"
// #include <ctype.h> 
// #include <string>      // 添加这行，解决std::string未定义的问题

// static const char *TAG = "ESP_NOW";

// // 1. 对等设备列表
// #define MAX_PEERS 3
// typedef struct {
//     uint8_t mac_addr[6];
//     char device_name[16];
//     bool is_registered;
// } peer_device_t;

// //ESP32-D0WD-V3 (revision 3)  08:d1:f9:99:c6:b8  4MB
// //ESP32-S3 (QFN56) (revision v0.2)   mac 1c:db:d4:75:14:24   16MB

// // 【修改内容】：预定义的设备列表
// static peer_device_t peer_list[MAX_PEERS] = {
//     {{0x08, 0xD1, 0xF9, 0x99, 0xC6, 0xB8}, "Light_01", false},  // 第一个客户端：舵机控制
//     {{0x11, 0x22, 0x33, 0x44, 0x55, 0x66}, "Fan_01", false}     // 第二个客户端：电机控制
// };

// static const int device_count = sizeof(peer_list) / sizeof(peer_device_t);

// uint8_t* find_device_mac(const char* device_name) {
//     // 参数有效性检查
//     if (device_name == NULL) {
//         ESP_LOGE("ESP_NOW", "设备名称为空指针");
//         return NULL;
//     }
    
//     if (strlen(device_name) == 0) {
//         ESP_LOGE("ESP_NOW", "设备名称长度为0");
//         return NULL;
//     }
    
//     ESP_LOGI("ESP_NOW", "开始查找设备MAC地址: %s", device_name);
    
//     // 遍历预定义的设备列表
//     for (int i = 0; i < device_count; i++) {
//         // 比较设备名称（不区分大小写）
//         if (strcasecmp(peer_list[i].device_name, device_name) == 0) {
//             // 检查设备是否已注册
//             if (peer_list[i].is_registered) {
//                 ESP_LOGI("ESP_NOW", "✅ 找到已注册设备: %s", device_name);
//                 ESP_LOGI("ESP_NOW", "  MAC地址: %02X:%02X:%02X:%02X:%02X:%02X",
//                         peer_list[i].mac_addr[0], peer_list[i].mac_addr[1],
//                         peer_list[i].mac_addr[2], peer_list[i].mac_addr[3],
//                         peer_list[i].mac_addr[4], peer_list[i].mac_addr[5]);
                
//                 return peer_list[i].mac_addr;
//             } else {
//                 ESP_LOGW("ESP_NOW", "设备 %s 存在但未注册", device_name);
//                 return NULL;
//             }
//         }
//     }
    
//     // 未找到设备
//     ESP_LOGW("ESP_NOW", "❌ 未找到设备: %s", device_name);
//     ESP_LOGW("ESP_NOW", "可用设备列表:");
//     for (int i = 0; i < device_count; i++) {
//         ESP_LOGW("ESP_NOW", "  %s [%s]", 
//                 peer_list[i].device_name,
//                 peer_list[i].is_registered ? "已注册" : "未注册");
//     }
    
//     return NULL;
// }

// // 2. 发送回调函数
// static void espnow_send_cb(const esp_now_send_info_t *send_info, esp_now_send_status_t status) {
//     const uint8_t *mac_addr = send_info->src_addr; 
//     char macStr[18];
//     snprintf(macStr, sizeof(macStr), "%02x:%02x:%02x:%02x:%02x:%02x",
//              mac_addr[0], mac_addr[1], mac_addr[2], 
//              mac_addr[3], mac_addr[4], mac_addr[5]);
//     ESP_LOGI(TAG, "数据包发送至: %s, 状态: %s", macStr,
//              status == ESP_NOW_SEND_SUCCESS ? "投递成功" : "投递失败");
// }

// // 3. 接收回调函数
// static void espnow_recv_cb(const esp_now_recv_info_t *recv_info, const uint8_t *data, int len) {
//     const uint8_t *mac_addr = recv_info->src_addr; 
//     control_message_t received_cmd;
//     if (len == sizeof(control_message_t)) {
//         memcpy(&received_cmd, data, sizeof(received_cmd));
//         ESP_LOGI(TAG, "从 %02x:%02x:%02x:%02x:%02x:%02x 收到指令: type=%d, device=%d, actuator=%d",
//                  mac_addr[0], mac_addr[1], mac_addr[2], mac_addr[3], mac_addr[4], mac_addr[5],
//                  received_cmd.command_type, received_cmd.target_device, received_cmd.actuator_id);
//     }
// }

// // 4. 注册所有对等设备
// static esp_err_t register_all_peers(void) {
//     esp_now_peer_info_t peer_info = {
//         .channel = 0,
//         .encrypt = false,
//     };
    
//     for (int i = 0; i < MAX_PEERS; i++) {
//         if (strlen(peer_list[i].device_name) > 0) {
//             memcpy(peer_info.peer_addr, peer_list[i].mac_addr, ESP_NOW_ETH_ALEN);
//             esp_err_t ret = esp_now_add_peer(&peer_info);
//             if (ret == ESP_OK) {
//                 peer_list[i].is_registered = true;
//                 ESP_LOGI(TAG, "成功注册对等设备: %s (MAC: %02x:%02x:%02x:%02x:%02x:%02x)",
//                          peer_list[i].device_name,
//                          peer_list[i].mac_addr[0], peer_list[i].mac_addr[1],
//                          peer_list[i].mac_addr[2], peer_list[i].mac_addr[3],
//                          peer_list[i].mac_addr[4], peer_list[i].mac_addr[5]);
//             } else {
//                 ESP_LOGW(TAG, "注册设备 %s 失败: %s", peer_list[i].device_name, esp_err_to_name(ret));
//                 peer_list[i].is_registered = false;
//             }
//         }
//     }
//     return ESP_OK;
// }

// // 5. 主初始化函数
// void init_esp_now(void) {
//     ESP_LOGI(TAG, "正在初始化ESP-NOW...");
    
//     ESP_ERROR_CHECK(esp_now_init());
//     ESP_ERROR_CHECK(esp_now_register_send_cb(espnow_send_cb));
//     ESP_ERROR_CHECK(esp_now_register_recv_cb(espnow_recv_cb));
    
//     register_all_peers();
    
//     ESP_LOGI(TAG, "ESP-NOW初始化完成");
// }

// // 6. 【新增内容】：创建控制消息的核心函数
// control_message_t create_control_message(const char* action, const char* device) {
//     control_message_t msg = {0};
    
//     // 1. 参数验证
//     if (action == NULL || device == NULL) {
//         ESP_LOGE("CONTROL", "无效参数: action=%p, device=%p", action, device);
//         msg.command = 255;  // 错误命令编号
//         return msg;
//     }
    
//     // 2. 标准化输入
//     char normalized_action[64];
//     char normalized_device[32];
    
//     normalize_action_string(normalized_action, action, sizeof(normalized_action));
//     normalize_action_string(normalized_device, device, sizeof(normalized_device));
    
//     ESP_LOGI("CONTROL", "原始输入: action='%s', device='%s'", action, device);
//     ESP_LOGI("CONTROL", "标准化后: action='%s', device='%s'", normalized_action, normalized_device);
    
//     // 3. 初始化消息结构体
//     memset(&msg, 0, sizeof(control_message_t));
    
//     // 设置目标设备名称
//     strncpy(msg.target, device, sizeof(msg.target) - 1);
//     msg.target[sizeof(msg.target) - 1] = '\0';
    
//     // 设置时间戳
//     msg.timestamp = (uint32_t)(esp_timer_get_time() / 1000);
    
//     // 4. 设备类型映射（基于搜索结果中的设备控制方案[1](@ref)）
//     if (strstr(normalized_device, "light") != NULL || 
//         strstr(normalized_device, "xiao") != NULL ||
//         strstr(normalized_device, "arm") != NULL) {
//         msg.target_device = 0;    // 舵机控制设备
//         msg.command_type = 0;     // 舵机控制命令
//     } 
//     else if (strstr(normalized_device, "fan") != NULL || 
//              strstr(normalized_device, "motor") != NULL) {
//         msg.target_device = 1;    // 电机控制设备
//         msg.command_type = 1;     // 电机控制命令
//     }
//     else if (strstr(normalized_device, "s3") != NULL || 
//              strstr(normalized_device, "server") != NULL) {
//         msg.target_device = 2;    // 服务器自身
//         msg.command_type = 2;     // 系统命令
//     }
//     else {
//         // 默认处理
//         msg.target_device = 0;
//         msg.command_type = 0;
//         ESP_LOGW("CONTROL", "未知设备: %s，使用默认设置", device);
//     }
    
//     // 5. 指令映射逻辑（基于搜索结果中的语音交互系统[4](@ref)）
//     // 使用strstr进行模糊匹配，提高兼容性
    
//     // 举左手/举右手指令
//     if (strstr(normalized_action, "raise") != NULL || 
//         strstr(normalized_action, "举") != NULL ||
//         strstr(normalized_action, "lift") != NULL) {
        
//         if (strstr(normalized_action, "left") != NULL || 
//             strstr(normalized_action, "左") != NULL) {
//             msg.actuator_id = 1;      // 左舵机
//             msg.value = 90;           // 90度位置
//             msg.action = 1;           // 执行动作
//             msg.command = 1;          // 命令编号：举左手
//             ESP_LOGI("CONTROL", "映射到: 举左手 (左舵机90度)");
//         }
//         else if (strstr(normalized_action, "right") != NULL || 
//                 strstr(normalized_action, "右") != NULL) {
//             msg.actuator_id = 2;      // 右舵机
//             msg.value = 90;           // 90度位置
//             msg.action = 1;           // 执行动作
//             msg.command = 3;          // 命令编号：举右手
//             ESP_LOGI("CONTROL", "映射到: 举右手 (右舵机90度)");
//         }
//         else {
//             // 默认举左手
//             msg.actuator_id = 1;
//             msg.value = 90;
//             msg.action = 1;
//             msg.command = 1;
//             ESP_LOGI("CONTROL", "映射到: 默认举左手");
//         }
//     }
    
//     // 放下左手/放下右手指令
//     else if (strstr(normalized_action, "lower") != NULL || 
//              strstr(normalized_action, "放下") != NULL ||
//              strstr(normalized_action, "put_down") != NULL) {
        
//         if (strstr(normalized_action, "left") != NULL || 
//             strstr(normalized_action, "左") != NULL) {
//             msg.actuator_id = 1;
//             msg.value = 0;
//             msg.action = 1;
//             msg.command = 2;
//             ESP_LOGI("CONTROL", "映射到: 放下左手 (左舵机0度)");
//         }
//         else if (strstr(normalized_action, "right") != NULL || 
//                 strstr(normalized_action, "右") != NULL) {
//             msg.actuator_id = 2;
//             msg.value = 0;
//             msg.action = 1;
//             msg.command = 4;
//             ESP_LOGI("CONTROL", "映射到: 放下右手 (右舵机0度)");
//         }
//         else {
//             msg.actuator_id = 1;
//             msg.value = 0;
//             msg.action = 1;
//             msg.command = 2;
//             ESP_LOGI("CONTROL", "映射到: 默认放下左手");
//         }
//     }
    
//     // 前进指令
//     else if (strstr(normalized_action, "forward") != NULL || 
//              strstr(normalized_action, "前进") != NULL ||
//              strstr(normalized_action, "go") != NULL) {
//         msg.actuator_id = 0;
//         msg.value = 200;
//         msg.action = 1;
//         msg.command = 5;
//         ESP_LOGI("CONTROL", "映射到: 前进 (速度200)");
//     }
    
//     // 左转指令
//     else if (strstr(normalized_action, "turn_left") != NULL || 
//              strstr(normalized_action, "左转") != NULL ||
//              strstr(normalized_action, "left") != NULL) {
//         msg.actuator_id = 1;
//         msg.value = 180;
//         msg.action = 1;
//         msg.command = 6;
//         ESP_LOGI("CONTROL", "映射到: 左转 (左舵机180度)");
//     }
    
//     // 右转指令
//     else if (strstr(normalized_action, "turn_right") != NULL || 
//              strstr(normalized_action, "右转") != NULL ||
//              strstr(normalized_action, "right") != NULL) {
//         msg.actuator_id = 2;
//         msg.value = 180;
//         msg.action = 1;
//         msg.command = 7;
//         ESP_LOGI("CONTROL", "映射到: 右转 (右舵机180度)");
//     }
    
//     // 停止指令
//     else if (strstr(normalized_action, "stop") != NULL || 
//              strstr(normalized_action, "停止") != NULL ||
//              strstr(normalized_action, "停下") != NULL ||
//              strstr(normalized_action, "halt") != NULL) {
//         msg.actuator_id = 0;
//         msg.value = 0;
//         msg.action = 0;
//         msg.command = 8;
//         ESP_LOGI("CONTROL", "映射到: 停止");
//     }
    
//     // 返回中立位置指令
//     else if (strstr(normalized_action, "center") != NULL || 
//              strstr(normalized_action, "中立") != NULL ||
//              strstr(normalized_action, "neutral") != NULL ||
//              strstr(normalized_action, "中间") != NULL) {
//         msg.actuator_id = 0;
//         msg.value = 90;
//         msg.action = 1;
//         msg.command = 9;
//         ESP_LOGI("CONTROL", "映射到: 返回中立位置 (90度)");
//     }
    
//     // 测试指令
//     else if (strstr(normalized_action, "test") != NULL || 
//              strstr(normalized_action, "测试") != NULL ||
//              strstr(normalized_action, "自检") != NULL) {
//         msg.actuator_id = 0;
//         msg.value = 255;
//         msg.action = 1;
//         msg.command = 10;
//         ESP_LOGI("CONTROL", "映射到: 测试指令");
//     }
    
//     // 状态查询指令
//     else if (strstr(normalized_action, "status") != NULL || 
//              strstr(normalized_action, "状态") != NULL ||
//              strstr(normalized_action, "查询") != NULL) {
//         msg.actuator_id = 0;
//         msg.value = 0;
//         msg.action = 2;
//         msg.command = 11;
//         ESP_LOGI("CONTROL", "映射到: 状态查询");
//     }
    
//     // 未知指令处理
//     else {
//         msg.actuator_id = 0;
//         msg.value = 0;
//         msg.action = 0;
//         msg.command = 255;
//         ESP_LOGW("CONTROL", "❌ 未知动作指令: %s (标准化后: %s)", action, normalized_action);
        
//         // 尝试提供建议
//         if (strlen(action) > 0) {
//             ESP_LOGW("CONTROL", "建议使用以下指令之一:");
//             ESP_LOGW("CONTROL", "  - raise hand / 举手");
//             ESP_LOGW("CONTROL", "  - lower hand / 放下手");
//             ESP_LOGW("CONTROL", "  - move forward / 前进");
//             ESP_LOGW("CONTROL", "  - turn left / 左转");
//             ESP_LOGW("CONTROL", "  - turn right / 右转");
//             ESP_LOGW("CONTROL", "  - stop / 停止");
//         }
//     }
    
//     // 6. 详细日志记录（基于搜索结果中的性能监控方案[5](@ref)）
//     if (msg.command != 255) {
//         ESP_LOGI("CONTROL", "✅ 成功创建控制消息");
//         ESP_LOGI("CONTROL", "  设备: %s (类型: %d)", msg.target, msg.target_device);
//         ESP_LOGI("CONTROL", "  执行器: %d, 值: %d", msg.actuator_id, msg.value);
//         ESP_LOGI("CONTROL", "  命令: %d, 动作: %d", msg.command, msg.action);
//         ESP_LOGI("CONTROL", "  时间戳: %u ms", msg.timestamp);
//     } else {
//         ESP_LOGE("CONTROL", "❌ 创建控制消息失败");
//     }
    
//     return msg;
// }


// // 7. 发送数据到指定设备
// esp_err_t send_to_device(const char *device_name, const control_message_t *message) {
//     for (int i = 0; i < MAX_PEERS; i++) {
//         if (strcmp(peer_list[i].device_name, device_name) == 0 && peer_list[i].is_registered) {
//             esp_err_t ret = esp_now_send(peer_list[i].mac_addr, (uint8_t *)message, sizeof(control_message_t));
//             if (ret == ESP_OK) {
//                 ESP_LOGI(TAG, "已向 %s 发送指令: type=%d, actuator=%d, value=%d", 
//                          device_name, message->command_type, message->actuator_id, message->value);
//             } else {
//                 ESP_LOGE(TAG, "向 %s 发送失败: %s", device_name, esp_err_to_name(ret));
//             }
//             return ret;
//         }
//     }
//     ESP_LOGW(TAG, "未找到设备: %s 或设备未注册", device_name);
//     return ESP_ERR_NOT_FOUND;
// }

// // 8. 广播发送到所有设备
// esp_err_t broadcast_to_all(const control_message_t *message) {
//     esp_err_t overall_result = ESP_OK;
    
//     for (int i = 0; i < MAX_PEERS; i++) {
//         if (peer_list[i].is_registered) {
//             esp_err_t ret = esp_now_send(peer_list[i].mac_addr, (uint8_t *)message, sizeof(control_message_t));
//             if (ret != ESP_OK) {
//                 overall_result = ret;
//             }
//         }
//     }
//     return overall_result;
// }

// // 11. 【新增内容】：检查设备连接状态
// bool is_device_connected(const char* device_name) {
//     for (int i = 0; i < MAX_PEERS; i++) {
//         if (strcmp(peer_list[i].device_name, device_name) == 0) {
//             return peer_list[i].is_registered;
//         }
//     }
//     return false;
// }

// // 12. 【新增内容】：带重试的发送函数
// esp_err_t send_with_retry(const char* device_name, const control_message_t* msg, int max_retries) {
//     esp_err_t result = ESP_FAIL;
    
//     for (int attempt = 0; attempt < max_retries; attempt++) {
//         // 查找设备MAC地址
//         uint8_t* mac_addr = find_device_mac(device_name);
//         if (mac_addr == nullptr) {
//             ESP_LOGE("ESP_NOW", "设备 %s 未找到", device_name);
//             return ESP_ERR_NOT_FOUND;
//         }
        
//         // 发送消息
//         result = esp_now_send(mac_addr, (uint8_t*)msg, sizeof(control_message_t));
        
//         if (result == ESP_OK) {
//             ESP_LOGI("ESP_NOW", "第%d次尝试发送成功", attempt + 1);
//             return ESP_OK;
//         } else {
//             ESP_LOGW("ESP_NOW", "第%d次尝试发送失败: %s", attempt + 1, esp_err_to_name(result));
//             vTaskDelay(pdMS_TO_TICKS(100)); // 等待100ms后重试
//         }
//     }
    
//     ESP_LOGE("ESP_NOW", "发送失败，已达到最大重试次数: %d", max_retries);
//     return result;
// }


// // 验证动作是否有效
// bool validate_action(const char* action) {
//     // 支持的动作指令列表（中文和英文）
//     static const char* valid_actions[] = {
//         // 中文指令
//         "举左手", "左手举起来", "举起左手", "把手举起来",
//         "放下左手", "左手放下", "放下手",
//         "举右手", "右手举起来", "举起右手",
//         "放下右手", "右手放下",
//         "前进", "往前走", "向前走",
//         "左转", "向左转", "向左转",
//         "右转", "向右转", "向右转",
//         "站住", "停止", "停下", "停",
        
//         // 英文指令
//         "raise hand", "raise left hand", "raise right hand",
//         "lift hand", "lift left hand", "lift right hand",
//         "lower hand", "lower left hand", "lower right hand",
//         "put down hand", "put down left hand", "put down right hand",
//         "move forward", "forward", "go forward",
//         "turn left", "left", "turn to left",
//         "turn right", "right", "turn to right",
//         "stop", "halt", "stand still",
        
//         NULL  // 列表结束标记
//     };
    
//     // 转换为小写进行比较（如果需要）
//     std::string action_str(action);
//     std::transform(action_str.begin(), action_str.end(), action_str.begin(), ::tolower);
    
//     for (int i = 0; valid_actions[i] != NULL; i++) {
//         std::string valid_str(valid_actions[i]);
//         std::transform(valid_str.begin(), valid_str.end(), valid_str.begin(), ::tolower);
        
//         if (action_str == valid_str) {
//             return true;
//         }
//     }
    
//     ESP_LOGW("VALIDATE", "无效的动作指令: %s", action);
//     return false;
// }

// // 验证设备是否有效
// bool validate_device(const char* device) {
//     static const char* valid_devices[] = {
//         "Light_01", "XiaoZhi",      // 舵机控制设备
//         "Fan_01",                   // 电机控制设备
//         "ESP32_S3",                 // 服务器自身
//         "client1", "client2",       // 兼容名称
//         NULL
//     };
    
//     for (int i = 0; valid_devices[i] != NULL; i++) {
//         if (strcmp(device, valid_devices[i]) == 0) {
//             return true;
//         }
//     }
    
//     ESP_LOGW("VALIDATE", "无效的设备名称: %s", device);
//     return false;
// }

// void normalize_action_string(char* normalized, const char* action, size_t max_len) {
//     strncpy(normalized, action, max_len - 1);
//     normalized[max_len - 1] = '\0';
    
//     // 转换为小写
//     for (size_t i = 0; i < strlen(normalized); i++) {
//         normalized[i] = tolower(normalized[i]);
//     }
    
//     // 替换空格为下划线
//     for (size_t i = 0; i < strlen(normalized); i++) {
//         if (normalized[i] == ' ') {
//             normalized[i] = '_';
//         }
//     }
// }