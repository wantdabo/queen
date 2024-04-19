using Queen.Core;
using System;
using System.Runtime.ExceptionServices;

namespace Queen.Common
{
    /// <summary>
    /// 日志系统
    /// </summary>
    public class Logger : Comp
    {
        /// <summary>
        /// 日志数据结构
        /// </summary>
        private struct LogInfo
        {
            /// <summary>
            /// 日志时间
            /// </summary>
            public string time;
            /// <summary>
            /// 日志内容
            /// </summary>
            public string message;
            /// <summary>
            /// 日志颜色
            /// </summary>
            public ConsoleColor color;
        }

        /// <summary>
        /// 日志队列
        /// </summary>
        private Queue<LogInfo> logInfos = new();

        private StreamWriter writer;

        protected override void OnCreate()
        {
            base.OnCreate();

            Thread thread = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(100);
                    while (logInfos.Count > 0) Log(logInfos.Dequeue());
                }
            });
            thread.IsBackground = true;
            thread.Start();

            var logFilePath = $"{AppDomain.CurrentDomain.BaseDirectory}/Logs/{DateTime.Now.ToLongDateString()}{DateTime.Now.ToLongTimeString().Replace(':', '.')}.txt";
            var fs = File.Open(logFilePath, FileMode.OpenOrCreate);
            writer = new StreamWriter(fs);

            AppDomain.CurrentDomain.FirstChanceException += (object sender, FirstChanceExceptionEventArgs e) =>
            {
                Log(e.Exception.Message);
            };

            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                while (logInfos.Count > 0) Log(logInfos.Dequeue());
                writer.Flush();
                writer.Close();
            };
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            writer.Flush();
            writer.Close();
        }

        /// <summary>
        /// 打印日志
        /// </summary>
        /// <param name="message">日志内容</param>
        public void Log(string message, ConsoleColor color = ConsoleColor.White)
        {
            logInfos.Enqueue(new LogInfo { time = $"{DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()}", message = message, color = color });
        }

        /// <summary>
        /// 打印日志
        /// </summary>
        /// <param name="log">日志</param>
        private void Log(LogInfo log)
        {
            var logStr = $"{log.time} {log.message}";
            writer.WriteLine(logStr);
            writer.Flush();
            Console.ForegroundColor = log.color;
            Console.WriteLine(logStr);
        }
    }
}
