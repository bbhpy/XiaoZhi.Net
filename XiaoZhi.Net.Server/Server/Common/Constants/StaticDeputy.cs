using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Common.Constants
{
    internal class StaticDeputy
    {
        /// <summary>
        /// 验证工具名称是否只包含英文字母、下划线、点号
        /// </summary>
        public static bool IsValidToolName(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return false;

            // 正则表达式：只允许英文字母、下划线、点号   [^a-zA-Z_.]
            return Regex.IsMatch(toolName, @"^[a-zA-Z_.]{1,60}$");
        }
    }
}
