本项目对[XiaoZhi.net](https://github.com/mm7h/XiaoZhi.Net)项目的二次开发，在此感谢XiaoZhi.net项目的开源大佬。

在原基础上增加了mqtt+udp通讯模式，增加了三方mcp注册，但是由于还未开发数据库部分，三方mcp绑定的终端token是写死的，暂时只支持一台终端注册。

整个项目按XiaoZhi.net项目说明部署好，再将我的代码覆盖原项目代码部分即可，我上传的就是修改的部分代码。

mqtt+udp和修改的websocket都支持了IPv4和v6双栈,所以附带了修改xiaozhi-esp32的代码，修改xiaozhi-esp32的udp音频上传格式和增加了ipv6。
