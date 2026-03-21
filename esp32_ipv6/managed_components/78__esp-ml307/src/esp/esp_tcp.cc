#include "esp_tcp.h"

#include <esp_log.h>
#include <unistd.h>
#include <cstring>
#include <arpa/inet.h>
#include <sys/socket.h>
#include <netdb.h>
#include <errno.h>

static const char *TAG = "EspTcp";

EspTcp::EspTcp() {
    event_group_ = xEventGroupCreate();
}

EspTcp::~EspTcp() {
    Disconnect();

    if (event_group_ != nullptr) {
        vEventGroupDelete(event_group_);
        event_group_ = nullptr;
    }
}

bool EspTcp::Connect(const std::string& host, int port) {
    if (connected_) {
        Disconnect();
    }

    // 使用 getaddrinfo 替代 gethostbyname
    struct addrinfo hints = {};
    struct addrinfo* results = nullptr;
    
    hints.ai_family = AF_UNSPEC;      // 支持 IPv4 和 IPv6
    hints.ai_socktype = SOCK_STREAM;  // TCP
    hints.ai_protocol = IPPROTO_TCP;
    
    char port_str[6];
    snprintf(port_str, sizeof(port_str), "%d", port);
    
    ESP_LOGI(TAG, "Resolving host: %s:%s", host.c_str(), port_str);

    int ret = getaddrinfo(host.c_str(), port_str, &hints, &results);
    if (ret != 0 || results == nullptr) {
        last_error_ = ret;
        ESP_LOGE(TAG, "Failed to resolve host: %s, error=%d", host.c_str(), ret);
        return false;
    }
    
    // 遍历地址列表，尝试连接
    struct addrinfo* addr = results;
    bool connected = false;
    
    while (addr != nullptr && !connected) {
        // 创建 socket
        tcp_fd_ = socket(addr->ai_family, addr->ai_socktype, addr->ai_protocol);
        if (tcp_fd_ < 0) {
            last_error_ = errno;
            ESP_LOGD(TAG, "Failed to create socket for address family %d: %d", 
                     addr->ai_family, last_error_);
            addr = addr->ai_next;
            continue;
        }
        
        // 尝试连接
        ret = connect(tcp_fd_, addr->ai_addr, addr->ai_addrlen);
        if (ret == 0) {
            connected = true;
            break;
        }
        
        last_error_ = errno;
        close(tcp_fd_);
        tcp_fd_ = -1;
        addr = addr->ai_next;
    }
    
    freeaddrinfo(results);
    
    if (!connected) {
        ESP_LOGE(TAG, "Failed to connect to %s:%d, last error=0x%x", 
                 host.c_str(), port, last_error_);
        return false;
    }
    
    // 获取并打印实际使用的地址类型
    struct sockaddr_storage actual_addr;
    socklen_t addr_len = sizeof(actual_addr);
    getsockname(tcp_fd_, (struct sockaddr*)&actual_addr, &addr_len);
    ESP_LOGI(TAG, "Connected to %s:%d via %s", host.c_str(), port,
             actual_addr.ss_family == AF_INET6 ? "IPv6" : "IPv4");
    
    connected_ = true;
    
    xEventGroupClearBits(event_group_, ESP_TCP_EVENT_RECEIVE_TASK_EXIT);
    xTaskCreate([](void* arg) {
        EspTcp* tcp = (EspTcp*)arg;
        tcp->ReceiveTask();
        xEventGroupSetBits(tcp->event_group_, ESP_TCP_EVENT_RECEIVE_TASK_EXIT);
        vTaskDelete(NULL);
    }, "tcp_receive", 4096, this, 1, &receive_task_handle_);
    
    return true;
}

void EspTcp::Disconnect() {
    // 如果已经断开，直接返回
    if (!connected_) {
        return;
    }

    // 主动断开，需要等待接收任务退出
    DoDisconnect(true);
}

void EspTcp::DoDisconnect(bool wait_for_task) {
    connected_ = false;

    if (tcp_fd_ != -1) {
        close(tcp_fd_);
        tcp_fd_ = -1;

        // 只有主动断开时才需要等待接收任务退出
        // 被动断开时，当前就是接收任务，不需要等待
        if (wait_for_task) {
            auto bits = xEventGroupWaitBits(event_group_, ESP_TCP_EVENT_RECEIVE_TASK_EXIT, pdFALSE, pdFALSE, pdMS_TO_TICKS(10000));
            if (!(bits & ESP_TCP_EVENT_RECEIVE_TASK_EXIT)) {
                ESP_LOGE(TAG, "Failed to wait for receive task exit");
            }
        }
    }

    // 断开连接时触发断开回调
    if (disconnect_callback_) {
        disconnect_callback_();
    }
}

int EspTcp::Send(const std::string& data) {
    if (!connected_) {
        ESP_LOGE(TAG, "Not connected");
        return -1;
    }

    size_t total_sent = 0;
    size_t data_size = data.size();
    const char* data_ptr = data.data();

    while (total_sent < data_size) {
        int ret = send(tcp_fd_, data_ptr + total_sent, data_size - total_sent, 0);

        if (ret <= 0) {
            ESP_LOGE(TAG, "Send failed: ret=%d, errno=%d", ret, errno);
            return ret;
        }

        total_sent += ret;
    }

    return total_sent;
}

void EspTcp::ReceiveTask() {
    std::string data;
    while (connected_) {
        data.resize(1500);
        int ret = recv(tcp_fd_, data.data(), data.size(), 0);
        if (ret <= 0) {
            if (ret < 0) {
                ESP_LOGE(TAG, "TCP receive failed: %d", ret);
            }
            // 被动断开，不需要等待接收任务退出（当前就是接收任务）
            DoDisconnect(false);
            break;
        }

        if (stream_callback_) {
            data.resize(ret);
            stream_callback_(data);
        }
    }
}

int EspTcp::GetLastError() {
    return last_error_;
}
