namespace XiaoZhi.Net.Server.Abstractions.Common.Dtos
{
    /// <summary>
    /// api响应
    /// </summary>
    /// <typeparam name="TData"></typeparam>
    public class ApiResponse<TData>
    {
        public ApiResponse() { }
        /// <summary>
        /// 响应码
        /// </summary>
        public int Code { get; set; }
        /// <summary>
        ///  响应信息
        /// </summary>
        public string Msg { get; set; } = null!;
        /// <summary>
        /// 响应数据
        /// </summary>
        public TData? Data { get; set; }
        /// <summary>
        /// 响应成功
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static ApiResponse<TData> Success(TData data)
        {
            return new ApiResponse<TData>
            {
                Code = 0,
                Msg = "success",
                Data = data
            };
        }
        /// <summary>
        /// 响应失败
        /// </summary>
        /// <param name="code"></param>
        /// <param name="msg"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public static ApiResponse<TData> Failure(int code, string msg, TData? data = default)
        {
            return new ApiResponse<TData>
            {
                Code = code,
                Msg = msg,
                Data = data
            };
        }
    }
}
