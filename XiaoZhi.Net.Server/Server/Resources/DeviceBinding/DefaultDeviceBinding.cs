using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Resources.DeviceBinding
{
    /// <summary>
    /// 默认设备绑定实现类，继承自BaseResource并实现IDeviceBinding接口
    /// 负责加载和管理设备绑定相关的音频文件资源
    /// </summary>
    internal class DefaultDeviceBinding : BaseResource<DefaultDeviceBinding, DeviceBindSetting>, IDeviceBinding
    {
        /// <summary>
        /// 绑定码提示音频文件的缓存键名
        /// </summary>
        private const string BIND_CODE_PROMPT = "BindCodePrompt";

        /// <summary>
        /// 设备未找到音频文件的缓存键名
        /// </summary>
        private const string BIND_NOT_FOUND = "BindNotFound";

        /// <summary>
        /// 音频文件缓存字典，存储音频文件名与二进制数据的映射关系
        /// </summary>
        private readonly IDictionary<string, byte[]> _audioFilesCache;

        /// <summary>
        /// 标识所有数字音频文件是否都是WAV格式的标志
        /// </summary>
        private bool _isAllWavFormat = true;

        /// <summary>
        /// 初始化DefaultDeviceBinding类的新实例
        /// </summary>
        /// <param name="logger">用于记录日志的ILogger实例</param>
        public DefaultDeviceBinding(ILogger<DefaultDeviceBinding> logger) : base(logger)
        {
            this._audioFilesCache = new Dictionary<string, byte[]>();
        }

        /// <summary>
        /// 获取资源名称
        /// </summary>
        public override string ResourceName => "DeviceBinding";

        /// <summary>
        /// 加载设备绑定设置中的音频文件到缓存中
        /// </summary>
        /// <param name="settings">设备绑定设置对象，包含音频文件路径配置</param>
        /// <returns>加载成功返回true，失败返回false</returns>
        public override bool Load(DeviceBindSetting settings)
        {
            try
            {
                // 加载绑定码提示音频文件
                string bindCodePromptFilePath = Path.Combine(Environment.CurrentDirectory, settings.BindCodePromptFilePath);
                if (File.Exists(bindCodePromptFilePath))
                {
                    byte[] fileData = File.ReadAllBytes(bindCodePromptFilePath);
                    this._audioFilesCache[BIND_CODE_PROMPT] = fileData;
                }
                else
                {
                    this.Logger.LogError(Lang.DefaultDeviceBinding_Load_BindCodePromptNotExist, bindCodePromptFilePath);
                    return false;
                }

                // 加载设备未找到音频文件
                string bindNotFoundFilePath = Path.Combine(Environment.CurrentDirectory, settings.BindNotFoundFilePath);
                if (File.Exists(bindNotFoundFilePath))
                {
                    byte[] fileData = File.ReadAllBytes(bindNotFoundFilePath);
                    this._audioFilesCache[BIND_NOT_FOUND] = fileData;
                }
                else
                {
                    this.Logger.LogError(Lang.DefaultDeviceBinding_Load_BindNotFoundNotExist, bindNotFoundFilePath);
                    return false;
                }

                // 加载数字音频文件（0-9）
                string[] digitFiles = Directory.GetFiles(Path.Combine(Environment.CurrentDirectory, settings.BindCodeDigitFolderPath));
                if (digitFiles.Length != 10)
                {
                    this.Logger.LogError(Lang.DefaultDeviceBinding_Load_DigitFilesCountError);
                    return false;
                }

                // 验证并加载每个数字音频文件
                foreach (string digitFile in digitFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(digitFile);

                    this._isAllWavFormat = this._isAllWavFormat && Path.GetExtension(digitFile).Equals(".wav", StringComparison.OrdinalIgnoreCase);

                    if (int.TryParse(fileName, out int digit) && digit >= 0 && digit <= 9)
                    {
                        byte[] fileData = File.ReadAllBytes(digitFile);
                        this._audioFilesCache[digit.ToString()] = fileData;
                    }
                    else
                    {
                        this.Logger.LogWarning(Lang.DefaultDeviceBinding_Load_InvalidDigitFile, fileName);
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.DefaultDeviceBinding_Load_InvalidResourceLoading, this.ResourceName);
                return false;
            }
        }

        /// <summary>
        /// 获取设备未找到时的音频流
        /// </summary>
        /// <returns>设备未找到音频流，如果音频数据不存在则返回null</returns>
        public Stream? GetDeviceNotFoundAudioStream()
        {
            if (this._audioFilesCache.TryGetValue(BIND_NOT_FOUND, out byte[]? audioData))
            {
                return new CombinedAudioStream([audioData]);
            }
            else
            {
                this.Logger.LogError(Lang.DefaultDeviceBinding_GetDeviceNotFoundAudioStream_NotLoaded);
                return null;
            }
        }

        /// <summary>
        /// 根据绑定码获取对应的音频流，包括提示音和数字音频
        /// </summary>
        /// <param name="bindCode">6位数字绑定码</param>
        /// <returns>组合后的音频流，如果绑定码无效或音频数据缺失则返回null</returns>
        public Stream? GetDeviceBindCodeAudioStream(string bindCode)
        {
            if (string.IsNullOrWhiteSpace(bindCode) || bindCode.Length != 6 || !bindCode.All(char.IsDigit))
            {
                this.Logger.LogError(Lang.DefaultDeviceBinding_GetDeviceBindCodeAudioStream_InvalidBindCode, bindCode);
                return null;
            }

            List<byte[]> audioDataList = new();

            // 添加绑定码提示音频
            if (this._audioFilesCache.TryGetValue(BIND_CODE_PROMPT, out byte[]? promptData))
            {
                audioDataList.Add(promptData);
            }
            else
            {
                this.Logger.LogError(Lang.DefaultDeviceBinding_GetDeviceBindCodeAudioStream_PromptNotLoaded);
                return null;
            }

            // 添加每个数字的音频数据
            foreach (char digit in bindCode)
            {
                if (this._audioFilesCache.TryGetValue(digit.ToString(), out byte[]? digitData))
                {
                    audioDataList.Add(digitData);
                }
                else
                {
                    this.Logger.LogError(Lang.DefaultDeviceBinding_GetDeviceBindCodeAudioStream_DigitNotLoaded, digit);
                    return null;
                }
            }

            // 根据音频格式选择合适的流类型
            if (this._isAllWavFormat)
            {
                return new CombinedWavStream(audioDataList);
            }
            else
            {
                return new CombinedAudioStream(audioDataList);
            }
        }

        /// <summary>
        /// 释放资源，清空音频文件缓存
        /// </summary>
        public override void Dispose()
        {
            this._audioFilesCache.Clear();
        }
    }

    /// <summary>
    /// 用于合并WAV文件的流类，正确处理WAV文件头和数据部分
    /// </summary>
    file class CombinedWavStream : Stream
    {
        private readonly byte[] _combinedWavData;
        private long _position;

        public CombinedWavStream(List<byte[]> wavFiles)
        {
            if (wavFiles == null || wavFiles.Count == 0)
                throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_ListEmpty);

            _combinedWavData = CombineWavFiles(wavFiles);
            _position = 0;
        }

        private byte[] CombineWavFiles(List<byte[]> wavFiles)
        {
            if (wavFiles.Count == 1)
                return wavFiles[0];

            // 获取第一个文件作为基础
            var firstFile = wavFiles[0];

            // 验证是否为有效的WAV文件
            if (firstFile.Length < 44 ||
                !firstFile.Take(4).SequenceEqual(new byte[] { 0x52, 0x49, 0x46, 0x46 })) // "RIFF"
            {
                throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_FirstFileInvalid);
            }

            // 提取第一个文件的头部信息（前44字节）
            var header = new byte[44];
            Array.Copy(firstFile, 0, header, 0, 44);

            // 计算所有文件的PCM数据总长度
            long totalPcmDataLength = 0;
            var pcmDataChunks = new List<byte[]>();

            foreach (var wavFile in wavFiles)
            {
                // 验证WAV文件格式
                if (wavFile.Length < 44 ||
                    !wavFile.Take(4).SequenceEqual(new byte[] { 0x52, 0x49, 0x46, 0x46 }))
                {
                    throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_FileInvalid);
                }

                // 提取PCM数据（跳过44字节头部）
                var pcmData = new byte[wavFile.Length - 44];
                Array.Copy(wavFile, 44, pcmData, 0, pcmData.Length);
                pcmDataChunks.Add(pcmData);
                totalPcmDataLength += pcmData.Length;
            }

            // 创建新的WAV文件
            var totalFileSize = 44 + totalPcmDataLength;
            var result = new byte[totalFileSize];

            // 复制头部
            Array.Copy(header, 0, result, 0, 44);

            // 更新文件大小信息（RIFF chunk size = 总文件大小 - 8）
            var riffChunkSize = (uint)(totalFileSize - 8);
            var riffChunkSizeBytes = BitConverter.GetBytes(riffChunkSize);
            Array.Copy(riffChunkSizeBytes, 0, result, 4, 4);

            // 更新数据块大小信息（位置40-43是data chunk size）
            var dataChunkSize = (uint)totalPcmDataLength;
            var dataChunkSizeBytes = BitConverter.GetBytes(dataChunkSize);
            Array.Copy(dataChunkSizeBytes, 0, result, 40, 4);

            // 合并所有PCM数据
            long offset = 44;
            foreach (var pcmData in pcmDataChunks)
            {
                Array.Copy(pcmData, 0, result, offset, pcmData.Length);
                offset += pcmData.Length;
            }

            return result;
        }

        /// <summary>
        /// 获取一个值，该值指示当前流是否支持读取操作
        /// </summary>
        public override bool CanRead => true;

        /// <summary>
        /// 获取一个值，该值指示当前流是否支持查找操作
        /// </summary>
        public override bool CanSeek => true;

        /// <summary>
        /// 获取一个值，该值指示当前流是否支持写入操作
        /// </summary>
        public override bool CanWrite => false;

        /// <summary>
        /// 获取流的长度（以字节为单位）
        /// </summary>
        public override long Length => _combinedWavData.Length;

        /// <summary>
        /// 获取或设置流中的当前位置
        /// </summary>
        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        /// <summary>
        /// 从当前流中读取指定数量的字节到缓冲区中
        /// </summary>
        /// <param name="buffer">存储读取数据的目标缓冲区</param>
        /// <param name="offset">缓冲区中的字节偏移量</param>
        /// <param name="count">最多要读取的字节数</param>
        /// <returns>实际读取的字节数</returns>
        /// <exception cref="ArgumentNullException">当buffer为null时抛出</exception>
        /// <exception cref="ArgumentOutOfRangeException">当offset或count小于0时抛出</exception>
        /// <exception cref="ArgumentException">当offset + count大于buffer长度时抛出</exception>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (offset + count > buffer.Length)
                throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_BufferOverflow);

            // 检查是否已到达流末尾
            if (_position >= _combinedWavData.Length)
                return 0;

            // 计算实际可读取的字节数
            var bytesToRead = (int)Math.Min(count, _combinedWavData.Length - _position);
            Array.Copy(_combinedWavData, _position, buffer, offset, bytesToRead);
            _position += bytesToRead;

            return bytesToRead;
        }

        /// <summary>
        /// 设置当前流中的位置
        /// </summary>
        /// <param name="offset">相对于origin参数的偏移量</param>
        /// <param name="origin">开始计算偏移量的位置</param>
        /// <returns>当前流位置</returns>
        /// <exception cref="ArgumentException">当origin参数无效时抛出</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            // 根据不同的查找原点计算新位置
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _combinedWavData.Length + offset,
                _ => throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_InvalidOrigin, nameof(origin))
            };

            // 确保新位置在有效范围内
            if (newPosition < 0)
                newPosition = 0;
            else if (newPosition > _combinedWavData.Length)
                newPosition = _combinedWavData.Length;

            _position = newPosition;
            return _position;
        }

        /// <summary>
        /// 设置流的长度（不支持此操作）
        /// </summary>
        /// <param name="value">新的长度值</param>
        /// <exception cref="NotSupportedException">始终抛出此异常</exception>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// 将字节写入当前流（不支持此操作）
        /// </summary>
        /// <param name="buffer">包含要写入数据的缓冲区</param>
        /// <param name="offset">缓冲区中的字节偏移量</param>
        /// <param name="count">要写入的字节数</param>
        /// <exception cref="NotSupportedException">始终抛出此异常</exception>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// 清空流的缓冲区（不需要实现，因为这是只读流）
        /// </summary>
        public override void Flush()
        {
            // 不需要实现，因为这是只读流
        }
    }

    /// <summary>
    /// 原有的合并音频流类（保留以供参考，但不推荐用于WAV文件）
    /// </summary>
    file class CombinedAudioStream : Stream
    {
        /// <summary>
        /// 存储音频数据列表的只读集合
        /// </summary>
        private readonly List<byte[]> _audioDataList;
        /// <summary>
        /// 当前正在读取的音频数据流索引
        /// </summary>
        private int _currentStreamIndex;
        /// <summary>
        /// 当前在合并流中的位置
        /// </summary>
        private long _currentPosition;
        /// <summary>
        /// 合并后音频数据的总长度
        /// </summary>
        private long _totalLength;

        /// <summary>
        /// 初始化CombinedAudioStream的新实例
        /// </summary>
        /// <param name="audioDataList">音频数据字节数组列表</param>
        public CombinedAudioStream(List<byte[]> audioDataList)
        {
            _audioDataList = audioDataList ?? throw new ArgumentNullException(nameof(audioDataList));
            _currentStreamIndex = 0;
            _currentPosition = 0;
            _totalLength = audioDataList.Sum(data => (long)data.Length);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _totalLength;

        public override long Position
        {
            get => _currentPosition;
            set => Seek(value, SeekOrigin.Begin);
        }

        /// <summary>
        /// 从合并的音频流中读取指定数量的字节到缓冲区
        /// </summary>
        /// <param name="buffer">目标缓冲区</param>
        /// <param name="offset">缓冲区中的起始偏移量</param>
        /// <param name="count">要读取的字节数</param>
        /// <returns>实际读取的字节数</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (offset + count > buffer.Length)
                throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_BufferOverflow);

            int totalBytesRead = 0;
            int remainingBytes = count;

            while (remainingBytes > 0 && _currentStreamIndex < _audioDataList.Count)
            {
                var currentAudioData = _audioDataList[_currentStreamIndex];
                long positionInCurrentStream = _currentPosition - GetStreamStartPosition(_currentStreamIndex);

                // 如果当前流已经读完，移动到下一个流
                if (positionInCurrentStream >= currentAudioData.Length)
                {
                    _currentStreamIndex++;
                    continue;
                }

                // 计算当前流中可读取的字节数
                int availableBytesInCurrentStream = (int)(currentAudioData.Length - positionInCurrentStream);
                int bytesToRead = Math.Min(remainingBytes, availableBytesInCurrentStream);

                // 从当前流中读取数据
                Array.Copy(currentAudioData, positionInCurrentStream, buffer, offset + totalBytesRead, bytesToRead);

                totalBytesRead += bytesToRead;
                remainingBytes -= bytesToRead;
                _currentPosition += bytesToRead;
            }

            return totalBytesRead;
        }

        /// <summary>
        /// 设置当前流位置到指定位置
        /// </summary>
        /// <param name="offset">相对于origin的偏移量</param>
        /// <param name="origin">搜索位置的起始点</param>
        /// <returns>新的流位置</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _currentPosition + offset,
                SeekOrigin.End => _totalLength + offset,
                _ => throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_InvalidOrigin, nameof(origin))
            };

            if (newPosition < 0)
                newPosition = 0;
            else if (newPosition > _totalLength)
                newPosition = _totalLength;

            _currentPosition = newPosition;

            // 更新当前流索引
            _currentStreamIndex = 0;
            long accumulatedLength = 0;

            for (int i = 0; i < _audioDataList.Count; i++)
            {
                if (newPosition <= accumulatedLength + _audioDataList[i].Length)
                {
                    _currentStreamIndex = i;
                    break;
                }
                accumulatedLength += _audioDataList[i].Length;
            }

            return _currentPosition;
        }

        /// <summary>
        /// 获取指定流索引在合并流中的起始位置
        /// </summary>
        /// <param name="streamIndex">流索引</param>
        /// <returns>该流在合并流中的起始位置</returns>
        private long GetStreamStartPosition(int streamIndex)
        {
            long position = 0;
            for (int i = 0; i < streamIndex && i < _audioDataList.Count; i++)
            {
                position += _audioDataList[i].Length;
            }
            return position;
        }

        public override void SetLength(long value)
        {
            return;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            return;
        }

        public override void Flush()
        {
            return;
        }
    }
}
