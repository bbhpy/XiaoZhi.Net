using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    /// <summary>
    /// 处理器基类，实现了IHandler接口，提供基础的处理器功能
    /// </summary>
    internal abstract class BaseHandler : IHandler
    {
        private CancellationTokenSource? _handlerCts;
        private CancellationTokenRegistration? _tokenRegistration;

        /// <summary>
        /// 初始化BaseHandler实例
        /// </summary>
        /// <param name="config">小智配置对象</param>
        /// <param name="logger">日志记录器</param>
        public BaseHandler(XiaoZhiConfig config, ILogger logger)
        {
            this.Config = config;
            this.Logger = logger;
        }

        /// <summary>
        /// 获取配置对象
        /// </summary>
        public XiaoZhiConfig Config { get; }

        /// <summary>
        /// 获取日志记录器
        /// </summary>
        public ILogger Logger { get; }

        /// <summary>
        /// 当处理器被中止时触发的事件
        /// </summary>
        public event Action<string, string, string>? OnAbort;

        /// <summary>
        /// 获取或设置处理器是否已构建完成
        /// </summary>
        public bool Builded { get; protected set; }

        /// <summary>
        /// 获取处理器名称（抽象属性，子类必须实现）
        /// </summary>
        public abstract string HandlerName { get; }

        /// <summary>
        /// 获取或设置业务发送外部接口
        /// </summary>
        public IBizSendOutter SendOutter { get; set; } = null!;

        /// <summary>
        /// 获取处理器的取消令牌
        /// </summary>
        protected CancellationToken HandlerToken { get; private set; }

        /// <summary>
        /// 构建处理器
        /// </summary>
        /// <param name="privateProvider">私有提供者</param>
        /// <returns>构建是否成功</returns>
        public abstract bool Build(PrivateProvider privateProvider);

        /// <summary>
        /// 注册取消令牌，建立与会话取消令牌的关联
        /// </summary>
        protected void RegisterCancellationToken()
        {
            Session session = this.SendOutter.GetSession();
            this._handlerCts = CancellationTokenSource.CreateLinkedTokenSource(session.SessionCtsToken);
            this.HandlerToken = this._handlerCts.Token;
            this._tokenRegistration = this.HandlerToken.Register(this.OnTokenCanceled);
            session.SessionCtsTokenChanged += this.OnSessionCtsTokenChanged;
        }

        /// <summary>
        /// 检查工作流是否有效，防止处理过期的工作流数据
        /// </summary>
        /// <typeparam name="T">工作流数据类型</typeparam>
        /// <param name="workflow">要检查的工作流</param>
        /// <returns>工作流是否有效</returns>
        protected bool CheckWorkflowValid<T>(Workflow<T> workflow)
        {
            // 为避免在触发Abort后，Channel中残留的旧Workflow被继续处理
            long sessionTurnId = this.SendOutter.GetSession().TurnId;
            if (workflow.TurnId != sessionTurnId)
            {
                this.Logger.LogDebug(Lang.BaseHandler_CheckWorkflowValid_StaleWorkflow, this.HandlerName, workflow.TurnId, sessionTurnId);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 当处理器令牌发生变化时的虚方法，供子类重写
        /// </summary>
        protected virtual void OnHandlerTokenChanged()
        {
        }

        /// <summary>
        /// 当会话取消令牌发生变化时的回调方法
        /// </summary>
        /// <param name="newToken">新的取消令牌</param>
        private void OnSessionCtsTokenChanged(CancellationToken newToken)
        {
            this._tokenRegistration?.Dispose();
            var oldCts = this._handlerCts;
            try
            {
                oldCts?.Cancel();
                oldCts?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                this.Logger.LogDebug(Lang.BaseHandler_OnSessionCtsTokenChanged_CtsAlreadyDisposed, this.HandlerName);
            }

            this._handlerCts = CancellationTokenSource.CreateLinkedTokenSource(newToken);
            this.HandlerToken = this._handlerCts.Token;
            this._tokenRegistration = this.HandlerToken.Register(this.OnTokenCanceled);
            this.OnHandlerTokenChanged();
        }

        /// <summary>
        /// 当令牌被取消时的回调方法
        /// </summary>
        private void OnTokenCanceled()
        {
            this.Logger.LogDebug(Lang.BaseHandler_OnTokenCanceled_TokenCanceled, this.HandlerName);
            this.OnHandlerTokenChanged();
            Session session = this.SendOutter.GetSession();
            this.OnAbort?.Invoke(session.DeviceId, session.SessionId, this.HandlerName);
        }

        /// <summary>
        /// 释放资源，清理取消令牌注册和相关资源
        /// </summary>
        public virtual void Dispose()
        {
            Session session = this.SendOutter.GetSession();
            session.SessionCtsTokenChanged -= this.OnSessionCtsTokenChanged;
            this._tokenRegistration?.Dispose();
            this._handlerCts?.Dispose();
        }
    }
}
