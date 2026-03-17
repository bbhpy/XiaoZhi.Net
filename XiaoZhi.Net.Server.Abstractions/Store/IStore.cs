namespace XiaoZhi.Net.Server.Abstractions.Store
{
    /// <summary>
    /// 定义一个存储接口，提供键值对数据的增删改查操作
    /// </summary>
    public interface IStore : IDisposable
    {
        /// <summary>
        /// 向存储中添加键值对
        /// </summary>
        /// <typeparam name="T">值的数据类型</typeparam>
        /// <param name="key">要添加的键</param>
        /// <param name="value">要添加的值</param>
        /// <returns>如果添加成功返回true，否则返回false</returns>
        bool Add<T>(string key, T value);

        /// <summary>
        /// 检查存储中是否包含指定的键
        /// </summary>
        /// <param name="key">要检查的键</param>
        /// <returns>如果包含该键返回true，否则返回false</returns>
        bool Contains(string key);

        /// <summary>
        /// 根据键获取对应的值
        /// </summary>
        /// <typeparam name="T">值的数据类型</typeparam>
        /// <param name="key">要获取值的键</param>
        /// <returns>与键对应的值</returns>
        T Get<T>(string key);

        /// <summary>
        /// 获取存储中的所有项的数量
        /// </summary>
        /// <returns>存储中项的总数量</returns>
        int GetAllCount();

        /// <summary>
        /// 获取存储中的所有键值对
        /// </summary>
        /// <typeparam name="T">值的数据类型</typeparam>
        /// <returns>包含所有键值对的字典</returns>
        IDictionary<string, T> GetAll<T>();

        /// <summary>
        /// 根据条件获取匹配的值集合
        /// </summary>
        /// <typeparam name="T">值的数据类型</typeparam>
        /// <param name="criteria">用于筛选值的谓词条件</param>
        /// <returns>满足条件的值的枚举集合</returns>
        IEnumerable<T> Get<T>(Predicate<T> criteria);

        /// <summary>
        /// 根据键移除单个项
        /// </summary>
        /// <param name="key">要移除的键</param>
        /// <returns>实际移除的项的数量</returns>
        int Remove(string key);

        /// <summary>
        /// 根据多个键批量移除项
        /// </summary>
        /// <param name="keys">要移除的键数组</param>
        /// <returns>实际移除的项的总数量</returns>
        int Remove(params string[] keys);

        /// <summary>
        /// 更新指定键对应的值
        /// </summary>
        /// <typeparam name="T">值的数据类型</typeparam>
        /// <param name="key">要更新的键</param>
        /// <param name="value">新的值</param>
        /// <returns>如果更新成功返回true，否则返回false</returns>
        bool Update<T>(string key, T value);

        /// <summary>
        /// 清空存储中的所有项
        /// </summary>
        void Clear();
    }
}