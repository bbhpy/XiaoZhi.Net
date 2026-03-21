#ifndef ESP_NOW_HANDLER_H
#define ESP_NOW_HANDLER_H

#include <stdint.h>
#include <stdbool.h>
#include <esp_err.h>
#include "esp_netif.h"
#include "lwip/ip6_addr.h"
#include "lwip/netdb.h"

#ifdef __cplusplus
extern "C" {
#endif
     // 15秒无通信视为断开
// 设备类型定义
typedef enum {
    DEVICE_TYPE_SERVO = 0,    // 舵机控制设备
    DEVICE_TYPE_MOTOR = 1,    // 电机控制设备
    DEVICE_TYPE_LED   = 2     // LED控制设备
} device_type_t;

// 控制命令定义
typedef enum {
    CMD_RAISE_HAND = 0,       // 举手
    CMD_LOWER_HAND = 1,       // 放下手
    CMD_MOVE_FORWARD = 2,     // 前进
    CMD_TURN_LEFT = 3,        // 左转
    CMD_TURN_RIGHT = 4,       // 右转
    CMD_STOP = 5,             // 停止
    CMD_SET_ANGLE = 6,        // 设置角度
    CMD_SET_SPEED = 7         // 设置速度
} command_type_t;

// ESP-NOW控制消息结构体（最大250字节）
typedef struct {
    uint8_t target_mac[6];    // 目标设备MAC地址
    uint8_t command_type;     // 命令类型
    uint8_t device_type;      // 设备类型
    uint8_t actuator_id;      // 执行器ID（0-255）
    int16_t value;            // 命令值（角度、速度等）
    uint16_t duration;        // 持续时间（ms）
    uint32_t timestamp;       // 时间戳
    uint8_t checksum;         // 校验和
} esp_now_control_msg_t;

// 设备信息结构体
typedef struct {
    char device_name[32];     // 设备名称
    uint8_t mac_addr[6];      // MAC地址
    device_type_t type;       // 设备类型
    bool is_registered;       // 是否已注册
    bool is_connected;      // 连接状态
    unsigned long last_seen; // 最后通信时间
    int id;                 // 设备ID（可选）
} device_info_t;

// 支持的语音指令映射
typedef struct {
    const char* voice_command;    // 语音指令
    command_type_t command;       // 对应命令
    device_type_t device_type;    // 设备类型
    const char* description;      // 描述
} voice_command_mapping_t;

// 函数声明
esp_err_t esp_now_handler_init(void);
esp_err_t esp_now_handler_deinit(void);
esp_err_t register_device(const char* device_name, const uint8_t* mac_addr, device_type_t type);
esp_err_t send_control_command(const char* device_name, command_type_t cmd, int16_t value, uint16_t duration);
esp_err_t send_broadcast_command(command_type_t cmd, int16_t value, uint16_t duration);
const char* parse_voice_command(const char* voice_text);
void print_registered_devices(void);
bool is_esp_now_initialized(void);
void update_device_last_seen(const uint8_t* mac_addr, device_info_t* device_list, int device_count);
void check_and_reconnect_all_devices();
int auto_check_and_reconnect_all_devices(device_info_t* device_list, int device_count, uint32_t timeout_ms);
esp_err_t esp_now_optimize_communication(void);
void print_esp_now_version(void);

#ifdef __cplusplus
}
#endif

#endif // ESP_NOW_HANDLER_H


// esp_now_handler.h
// #ifndef ESP_NOW_HANDLER_H
// #define ESP_NOW_HANDLER_H

// #include "esp_err.h"

// #ifdef __cplusplus
// extern "C" {
// #endif

// // 【修改内容】：统一控制指令数据结构（合并新旧字段）
// typedef struct {
//     char target[32];          // 目标设备名称
//     uint8_t target_device;    // 目标设备编号
//     uint8_t command_type;     // 命令类型：0=舵机控制，1=电机控制
//     uint8_t actuator_id;      // 执行器ID：0=中立，1=左舵机，2=右舵机
//     int value;               // 控制值（角度或速度）
//     uint8_t action;          // 动作类型：0=停止，1=执行
//     uint8_t command;         // 具体命令编号
//     uint32_t timestamp;      // 时间戳（可选）
//     int duration;           // 持续时间（毫秒）- 这是您需要的成员
// } control_message_t;

// // 【新增内容】：函数原型声明
// void init_esp_now(void);
// esp_err_t send_to_device(const char *device_name, const control_message_t *message);
// esp_err_t broadcast_to_all(const control_message_t *message);
// control_message_t create_control_message(const char* action, const char* device);

// // 【新增内容】：辅助函数声明
// bool validate_action(const char* action);
// bool validate_device(const char* device);
// bool is_device_connected(const char* device_name);
// esp_err_t send_with_retry(const char* device_name, const control_message_t* msg, int max_retries);

// uint8_t* find_device_mac(const char* device_name);
// void normalize_action_string(char* normalized, const char* action, size_t max_len);
// #ifdef __cplusplus
// }
// #endif

// #endif // ESP_NOW_HANDLER_H
