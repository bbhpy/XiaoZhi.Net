#include "esp_udp.h"

#include <esp_log.h>
#include <unistd.h>
#include <cstring>
#include <arpa/inet.h>
#include <sys/socket.h>
#include <netdb.h>
#include <errno.h>

static const char *TAG = "EspUdp";

EspUdp::EspUdp() : udp_fd_(-1) {
    event_group_ = xEventGroupCreate();
}

EspUdp::~EspUdp() {
    Disconnect();

    if (event_group_ != nullptr) {
        vEventGroupDelete(event_group_);
        event_group_ = nullptr;
    }
}

bool EspUdp::Connect(const std::string& host, int port) {
   if (connected_) {
        Disconnect();
    }
    
    struct addrinfo hints = {};
    struct addrinfo* results = nullptr;
    
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = SOCK_DGRAM;
    hints.ai_protocol = IPPROTO_UDP;
    
    char port_str[6];
    snprintf(port_str, sizeof(port_str), "%d", port);
    
    int ret = getaddrinfo(host.c_str(), port_str, &hints, &results);
    if (ret != 0 || results == nullptr) {
        last_error_ = ret;
        ESP_LOGE(TAG, "Failed to resolve host: %s", host.c_str());
        return false;
    }
    
    struct addrinfo* addr = results;
    bool connected = false;
    
    while (addr != nullptr && !connected) {
        udp_fd_ = socket(addr->ai_family, addr->ai_socktype, addr->ai_protocol);
        if (udp_fd_ < 0) {
            last_error_ = errno;
            addr = addr->ai_next;
            continue;
        }
        
        ret = connect(udp_fd_, addr->ai_addr, addr->ai_addrlen);
        if (ret == 0) {
            connected = true;
            break;
        }
        
        last_error_ = errno;
        close(udp_fd_);
        udp_fd_ = -1;
        addr = addr->ai_next;
    }
    
    freeaddrinfo(results);
    
    if (!connected) {
        ESP_LOGE(TAG, "Failed to connect to %s:%d", host.c_str(), port);
        return false;
    }
    
    connected_ = true;
    
    xEventGroupClearBits(event_group_, ESP_UDP_EVENT_RECEIVE_TASK_EXIT);
    xTaskCreate([](void* arg) {
        EspUdp* udp = (EspUdp*)arg;
        udp->ReceiveTask();
        xEventGroupSetBits(udp->event_group_, ESP_UDP_EVENT_RECEIVE_TASK_EXIT);
        vTaskDelete(NULL);
    }, "udp_receive", 2048, this, 1, &receive_task_handle_);
    
    return true;
}

void EspUdp::Disconnect() {
    connected_ = false;

    if (udp_fd_ != -1) {
        close(udp_fd_);
        udp_fd_ = -1;

        auto bits = xEventGroupWaitBits(event_group_, ESP_UDP_EVENT_RECEIVE_TASK_EXIT, pdFALSE, pdFALSE, pdMS_TO_TICKS(10000));
        if (!(bits & ESP_UDP_EVENT_RECEIVE_TASK_EXIT)) {
            ESP_LOGE(TAG, "Failed to wait for receive task exit");
        }
    }
}

int EspUdp::Send(const std::string& data) {
    if (!connected_) {
        ESP_LOGE(TAG, "Not connected");
        return -1;
    }

    int ret = send(udp_fd_, data.data(), data.size(), 0);
    if (ret <= 0) {
        ESP_LOGE(TAG, "Send failed: ret=%d, errno=%d", ret, errno);
    }
    return ret;
}

void EspUdp::ReceiveTask() {
    std::string data;
    while (connected_) {
        data.resize(1500);
        int ret = recv(udp_fd_, data.data(), data.size(), 0);
        if (ret <= 0) {
            connected_ = false;
            break;
        }
        
        if (message_callback_) {
            data.resize(ret);
            message_callback_(data);
        }
    }
}

int EspUdp::GetLastError() {
    return last_error_;
}
