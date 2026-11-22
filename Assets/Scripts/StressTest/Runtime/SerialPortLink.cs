using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Ports;
using System.Threading;
using StressTest.Core;

namespace StressTest.Runtime
{
    /// <summary>
    /// ISerialLink 的正式實作：在背景執行緒逐行讀取，主執行緒以 TryReadLine 取出。
    /// </summary>
    public sealed class SerialPortLink : ISerialLink, IDisposable
    {
        public const int BaudRate = 115200;

        private readonly SerialPort _port;
        private readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();
        private readonly Thread _reader;
        private volatile bool _running;
        private volatile bool _faulted;

        private SerialPortLink(SerialPort port)
        {
            _port = port;
            _running = true;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "StressTest serial reader" };
            _reader.Start();
        }

        /// <summary>讀寫發生 I/O 錯誤（例如拔線）後即視為關閉，由介面層提示重新連線。</summary>
        public bool IsOpen => !_faulted && _port.IsOpen;

        /// <summary>
        /// 開啟序列埠。失敗時回傳 null，並以繁體中文說明原因。
        /// </summary>
        public static SerialPortLink TryOpen(string portName, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(portName))
            {
                error = "請先選擇 COM port。";
                return null;
            }

            var port = new SerialPort(portName, BaudRate)
            {
                NewLine = "\n",
                ReadTimeout = 100,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true
            };

            try
            {
                port.Open();
                return new SerialPortLink(port);
            }
            catch (UnauthorizedAccessException)
            {
                error = $"{portName} 被其他程式占用。請關閉 Arduino IDE 的序列埠監控視窗後再試。";
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                error = $"找不到 {portName}。請確認 USB 線已接上，再按「重新整理」。";
            }
            catch (Exception e)
            {
                error = $"無法開啟 {portName}：{e.Message}";
            }

            port.Dispose();
            return null;
        }

        public void WriteLine(string line)
        {
            if (!IsOpen) return;
            try
            {
                _port.Write(line + "\n");
            }
            catch (Exception e) when (e is IOException || e is TimeoutException || e is InvalidOperationException)
            {
                _faulted = true;
            }
        }

        public bool TryReadLine(out string line) => _incoming.TryDequeue(out line);

        private void ReadLoop()
        {
            while (_running)
            {
                try
                {
                    var line = _port.ReadLine().TrimEnd('\r');
                    if (line.Length > 0) _incoming.Enqueue(line);
                }
                catch (TimeoutException)
                {
                }
                catch (Exception)
                {
                    // 埠被拔除或關閉：標記故障後離開，IsOpen 會回傳 false
                    _faulted = true;
                    return;
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            try
            {
                if (_port.IsOpen) _port.Close();
            }
            catch (IOException)
            {
            }
            _reader.Join(500);
            _port.Dispose();
        }
    }
}
