#include <esp_log.h>
#include <esp_err.h>
#include <nvs.h>
#include <nvs_flash.h>
#include <driver/gpio.h>
#include <esp_event.h>
#include <freertos/FreeRTOS.h>
#include <freertos/task.h>
#include <esp_netif.h> 

#include "application.h"
#include "system_info.h"

#define TAG "main"

extern "C" void app_main(void)
{
    // Initialize NVS flash for WiFi configuration
    esp_err_t ret = nvs_flash_init();
    if (ret == ESP_ERR_NVS_NO_FREE_PAGES || ret == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_LOGW(TAG, "Erasing NVS flash to fix corruption");
        ESP_ERROR_CHECK(nvs_flash_erase());
        ret = nvs_flash_init();
    }
    ESP_ERROR_CHECK(ret);

  // 初始化网络栈
    ESP_ERROR_CHECK(esp_netif_init());
    ESP_ERROR_CHECK(esp_event_loop_create_default());
    
    // 注意：不要手动创建 WiFi 网络接口
    // WifiBoard 和 WifiManager 组件会自动创建和管理网络接口
    // 如果手动创建可能会导致重复初始化的问题
    
    // 只需要确保 IPv6 在 sdkconfig 中已启用即可
    ESP_LOGI(TAG, "System initialized, IPv6 support is %s", 
             CONFIG_LWIP_IPV6 ? "enabled" : "disabled");
    // Initialize and run the application
    auto& app = Application::GetInstance();
    app.Initialize();
    app.Run();  // This function runs the main event loop and never returns
}
