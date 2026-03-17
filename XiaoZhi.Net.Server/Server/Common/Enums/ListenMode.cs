using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace XiaoZhi.Net.Server.Common.Enums
{
    internal enum ListenMode
    {
        /// <summary>
        /// 自动
        /// </summary>
        [Description("Auto")]
        Auto,
        /// <summary>
        /// 手动
        /// </summary>
        [Description("Manual")]
        Manual,
        /// <summary>
        /// 实时
        /// </summary>
        [Description("Realtime")]
        Realtime
    }
}
