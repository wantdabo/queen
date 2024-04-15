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
        /// 日志队列
        /// </summary>
        private Queue<LogInfo> logInfos = new();

        private StreamWriter writer;

        protected override void OnCreate()
        {
            base.OnCreate();

            var logFilePath = $"{AppDomain.CurrentDomain.BaseDirectory}/Logs/{DateTime.Now.ToLongDateString()}{DateTime.Now.ToLongTimeString().Replace(':', '.')}.txt";
            var fs = File.Open(logFilePath, FileMode.OpenOrCreate);
            writer = new StreamWriter(fs);

            AppDomain.CurrentDomain.FirstChanceException += (object sender, FirstChanceExceptionEventArgs e) =>
            {
                Log(e.Exception.Message);
            };

            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                Log(e.ExceptionObject.ToString());
                while (logInfos.Count > 0) Log(logInfos.Dequeue());
                writer.Flush();
                writer.Close();
            };

            engine.ticker.Timing((t) =>
            {
                while (logInfos.Count > 0) Log(logInfos.Dequeue());
            }, 2000, -1);
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
        public void Log(string message)
        {
            logInfos.Enqueue(new LogInfo { time = $"{DateTime.Now.ToShortDateString()} - {DateTime.Now.ToLongTimeString()}", message = message });
        }

        /// <summary>
        /// 打印日志
        /// </summary>
        /// <param name="log">日志</param>
        private void Log(LogInfo log)
        {
            var logStr = $"{log.time} --- {log.message}";
            writer.WriteLine(logStr);
            writer.Flush();
            Console.WriteLine(logStr);
        }

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
        }
    }
}
