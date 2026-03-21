#ifndef WIFI_BOARD_H
#define WIFI_BOARD_H

#include "board.h"
#include <freertos/FreeRTOS.h>
#include <freertos/event_groups.h>
#include <esp_timer.h>
#include <esp_netif.h>  

class WifiBoard : public Board {
protected:
    bool enable_ipv6_ = true; 
    esp_timer_handle_t connect_timer_ = nullptr;
    bool in_config_mode_ = false;
    NetworkEventCallback network_event_callback_ = nullptr;

    esp_timer_handle_t ipv6_check_timer_ = nullptr;  // IPv6 检查定时器
    bool ipv6_global_obtained_ = false;  
    bool ipv6_link_local_printed_ = false;        

    virtual std::string GetBoardJson() override;

    /**
     * Handle network event (called from WiFi manager callbacks)
     * @param event The network event type
     * @param data Additional data (e.g., SSID for Connecting/Connected events)
     */
    void OnNetworkEvent(NetworkEvent event, const std::string& data = "");

    /**
     * Start WiFi connection attempt
     */
    void TryWifiConnect();

    /**
     * Enter WiFi configuration mode
     */
    void StartWifiConfigMode();

    /**
     * WiFi connection timeout callback
     */
    static void OnWifiConnectTimeout(void* arg);
    static void OnIPv6CheckTimer(void* arg);  // 添加IPv6检查定时器回调
    void StartIPv6Monitoring();                // 启动IPv6监控
    void StopIPv6Monitoring();                 // 停止IPv6监控
    void CheckIPv6Address();                   // 检查IPv6地址
    void PrintIPv6Address(esp_ip6_addr_t& ip6_addr, const char* type);  // 打印IPv6地址
public:
    WifiBoard();
    virtual ~WifiBoard();
    
    virtual std::string GetBoardType() override;
    void ConfigureIPv6();
    /**
     * Start network connection asynchronously
     * This function returns immediately. Network events are notified through the callback set by SetNetworkEventCallback().
     */
    virtual void StartNetwork() override;
    
    virtual NetworkInterface* GetNetwork() override;
    virtual void SetNetworkEventCallback(NetworkEventCallback callback) override;
    virtual const char* GetNetworkStateIcon() override;
    virtual void SetPowerSaveLevel(PowerSaveLevel level) override;
    virtual AudioCodec* GetAudioCodec() override { return nullptr; }
    virtual std::string GetDeviceStatusJson() override;
    
    /**
     * Enter WiFi configuration mode (thread-safe, can be called from any task)
     */
    void EnterWifiConfigMode();
    
    /**
     * Check if in WiFi config mode
     */
    bool IsInWifiConfigMode() const;
};

#endif // WIFI_BOARD_H
