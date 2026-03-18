using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class Session
    {
        /// <summary>
        /// 是否正在处理音频
        /// </summary>
        private long _isAudioProcessing;
        /// <summary>
        /// 音频处理任务取消令牌
        /// </summary>
        private CancellationTokenSource _sessionCts = null!;
        /// <summary>
        /// 会话管理类，用于管理用户连接会话的状态和行为
        /// </summary>
        private readonly object _lock = new object();
        /// <summary>
        /// 重置状态标志，volatile确保多线程访问的一致性
        /// </summary>
        private volatile bool _isReseting = false;
        /// <summary>
        /// 转换ID，用于标识会话中的不同转换
        /// </summary>
        private long _turnId = 0;

        /// <summary>
        /// 初始化Session实例 websocket服务使用
        /// </summary>
        /// <param name="sessionId">会话ID</param>
        /// <param name="deviceId">设备ID</param>
        /// <param name="authToken">认证令牌</param>
        /// <param name="userEndPoint">用户端点信息</param>
        /// <param name="sendOutter">业务发送接口</param>
        public Session(string sessionId, string deviceId, string authToken, IPEndPoint userEndPoint, ProtocolType protocolType, IBizSendOutter sendOutter)
        {
            this.SessionId = sessionId;
            this.DeviceId = deviceId;
            this.AuthToken = authToken;
            this.EndPoint = userEndPoint;
            this.SendOutter = sendOutter;
            this.protocolType = protocolType;
            this.AudioSetting = new AudioSetting();
            this.AudioPacket = new AudioPacket();
            this.HandlerPipeline = new HandlerPipeline();
            this.PrivateProvider = new PrivateProvider();
            this.CreateCancellationTokenSource();
        }
        /// <summary>
        /// 初始化Session实例 mqtt服务器
        /// </summary>
        /// <param name="sessionId">会话ID</param>
        /// <param name="clientid">设备唯一标识符</param>
        /// <param name="sendOutter">业务发送接口</param>
        public Session(string sessionId, string clientid, IBizSendOutter sendOutter)
        {
            this.SessionId = sessionId;
            this.DeviceId = clientid;
            this.protocolType = ProtocolType.mqtt;
            this.AudioSetting = new AudioSetting();
            this.AudioPacket = new AudioPacket();
            this.HandlerPipeline = new HandlerPipeline();
            this.PrivateProvider = new PrivateProvider();
            this.CreateCancellationTokenSource();
        }
        public ProtocolType protocolType { get; }
        /// <summary>
        /// 当会话取消令牌变更时触发的事件
        /// </summary>
        public event Action<CancellationToken>? SessionCtsTokenChanged;
        public string SessionId { get; }
        public string DeviceId { get; }
        public string AuthToken { get; }
        /// <summary>
        /// 音频设置，用于描述音频数据的属性和参数
        /// </summary>
        public AudioSetting AudioSetting { get; }
        /// <summary>
        ///  ip地址和端口信息，标识用户的网络位置，用于网络通信和数据传输
        /// </summary>
        public IPEndPoint EndPoint { get; }
        /// <summary>
        /// 监听模式，标识用户当前的录音模式
        /// </summary>
        public ListenMode ListenMode { get; set; }
        /// <summary>
        /// 音频数据包，用于存储和传输音频数据
        /// </summary>
        public AudioPacket AudioPacket { get; set; }
        /// <summary>
        ///  会话取消令牌，用于取消会话中的任务
        /// </summary>
        public CancellationToken SessionCtsToken => this._sessionCts.Token;
        /// <summary>
        ///  处理管道，用于管理会话中的处理步骤
        /// </summary>
        public HandlerPipeline HandlerPipeline { get; }
        /// <summary>
        ///  业务发送接口，用于发送业务消息
        /// </summary>
        public IBizSendOutter SendOutter { get; }
        /// <summary>
        ///  私有提供者，用于提供私有功能 
        /// </summary>
        public PrivateProvider PrivateProvider { get; }
        /// <summary>
        /// 设备是否已绑定
        /// </summary>
        public bool IsDeviceBinded { get; set; }
        /// <summary>
        ///  绑定码，用于标识设备
        /// </summary>
        public string? BindCode { get; set; }
        /// <summary>
        ///  最后活动时间，用于跟踪会话的活跃状态
        /// </summary>
        public DateTime LastActivityTime { get; private set; }
        /// <summary>
        ///  会话关闭后是否自动聊天
        /// </summary>
        public bool CloseAfterChat { get; set; }
        /// <summary>
        ///  转换ID，用于标识会话中的转换
        /// </summary>
        public long TurnId => Interlocked.Read(ref _turnId);
        /// <summary>
        /// 是否正在处理音频
        /// </summary>
        public bool IsAudioProcessing => Interlocked.Read(ref _isAudioProcessing) == 1;
        /// <summary>
        /// 检查是否应该忽略当前操作（当会话正在重置时）
        /// </summary>
        /// <returns>如果正在重置则返回true，否则返回false</returns>
        public bool ShouldIgnore() => this._isReseting;
        /// <summary>
        /// 是否超时关闭
        /// </summary>
        public bool timeoutClose { get; set; } = false;
        /// <summary>
        /// 设置监听模式
        /// </summary>
        /// <param name="mode">监听模式，支持 "auto"、"manual"、"realtime"</param>
        public void SetListenMode(string mode)
        {
            switch (mode.ToLower())
            {
                default:
                case "auto":
                    this.ListenMode = ListenMode.Auto;
                    break;
                case "manual":
                    this.ListenMode = ListenMode.Manual;
                    break;
                case "realtime":
                    this.ListenMode = ListenMode.Realtime;
                    break;
            }
        }
        /// <summary>
        /// 手动开始录音
        /// </summary>
        public void ManualStart()
        {
            this.Reset();
            this.AudioPacket.HaveVoice = true;
            this.AudioPacket.VoiceStop = false;
        }
        /// <summary>
        /// 手动结束录音
        /// </summary>
        public void ManualStop()
        {
            this.AudioPacket.HaveVoice = true;
            this.AudioPacket.VoiceStop = true;
        }

        /// <summary>
        /// 拒绝接收音频数据
        /// </summary>
        public void RejectIncomingAudio()
        {
            Interlocked.Exchange(ref this._isAudioProcessing, 0);
        }
        /// <summary>
        /// 接收音频数据
        /// </summary>
        public void AcceptIncomingAudio()
        {
            Interlocked.Exchange(ref this._isAudioProcessing, 1);
        }

        /// <summary>
        /// 重置会话状态
        /// </summary>
        public void Reset()
        {
            Interlocked.Increment(ref this._turnId);
            //this.AcceptIncomingAudio();
            // 默认关闭门禁，等待第一个音频包触发
            this.RejectIncomingAudio(); // 改为关闭门禁
            this.AudioPacket.Reset();
        }
        /// <summary>
        /// 中止会话
        /// </summary>
        public void Abort()
        {
            lock (_lock)
            {
                if (this._isReseting)
                {
                    return;
                }
                this._isReseting = true;
            }
            try
            {
                this._sessionCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                lock (_lock)
                {
                    this.Reset();
                    this._isReseting = false;
                    this.CreateCancellationTokenSource();
                    this.SessionCtsTokenChanged?.Invoke(this._sessionCts.Token);
                }
            }
        }

        /// <summary>
        /// 刷新最后活动时间
        /// </summary>
        public void RefreshLastActivityTime()
        {
            this.LastActivityTime = DateTime.Now;
        }

        /// <summary>
        /// 释放会话资源
        /// </summary>
        public void Release()
        {
            this.Reset();
            this.AudioPacket.Release();
            this._sessionCts.Cancel();
            this.HandlerPipeline.Release();
            this.PrivateProvider.Release();
            this._sessionCts.Dispose();
        }

        /// <summary>
        /// 创建新的取消令牌源
        /// </summary>
        private void CreateCancellationTokenSource()
        {
            this._sessionCts = new CancellationTokenSource();
            this._sessionCts.Token.Register(async () =>
            {
                await Task.Yield();

                lock (_lock)
                {
                    this.Reset();
                    this._isReseting = false;

                    var oldCts = this._sessionCts;
                    this._sessionCts = new CancellationTokenSource();
                    var newToken = this._sessionCts.Token;

                    try
                    {
                        oldCts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {

                    }

                    this.SessionCtsTokenChanged?.Invoke(newToken);
                }
            });
        }
        /// <summary>
        /// 返回会话的设备ID和会话ID信息
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"DeviceId: {this.DeviceId}, SessionId: {this.SessionId}";
        }
        /// <summary>
        /// 通讯类型 mqtt或者websocket
        /// </summary>
        public enum ProtocolType
        {
            websocket,
            mqtt
        }
    }
}
