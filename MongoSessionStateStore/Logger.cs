﻿/******************************************************************* 
 * FileName: Logger.cs
 * Author   : Qiang Kong
 * Date : 2015-09-09 09:55:28
 * Desc : 
 * 
 * 
 * *******************************************************************/

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MongoSessionStateStore
{
    public class Logger : IDisposable
    {

        private static readonly object InitLock = new object();
        //日志消息队列
        private Queue<LogMessage> _logMessages;

        //日志保存路径
        private static string _logDirectory;

        //写入文件的情况
        private static bool _state;

        //日志类型
        private static LogType _logType;

        //日志时间
        private static DateTime _timeSign;

        //写日志文件流
        private StreamWriter _writer;

        /// <summary>
        /// 写日志消息的信号量
        /// </summary>
        private Semaphore _semaphore;

        /// <summary>
        /// logger单例
        /// </summary>
        private static Logger _log;

        /// <summary>
        /// 返回Mylog单例
        /// </summary>
        public static Logger Instance
        {
            get
            {
                Logger log;
                if (_log == null)
                {
                    lock (InitLock)
                    {
                        if (_log == null)
                        {
                            log = new Logger();
                            _log = log;
                        }
                    }

                }
                return _log;
            }
        }

        public static bool CreateImpl()
        {
            return Instance != null;
        }

        private static object _lockObjeck;

        /// <summary>
        /// 初始化Mylog实例
        /// </summary>
        private void Initialize()
        {
            if (_logMessages == null)
            {
                _state = true;
                string logPath = GetSpecifyConfigValue("LogDirectory", string.Format(@"{0}Logger.config", AppDomain.CurrentDomain.BaseDirectory));
                _logDirectory = string.IsNullOrEmpty(logPath) ? string.Format(@"{0}\\Logs\", AppDomain.CurrentDomain.BaseDirectory.TrimEnd('/', '\\')) : logPath;
                if (!Directory.Exists(_logDirectory)) Directory.CreateDirectory(_logDirectory);
                _logType = LogType.Daily;
                _lockObjeck = new object();
                _semaphore = new Semaphore(0, 50);
                _logMessages = new Queue<LogMessage>();
                var thread = new Thread(Work) { IsBackground = true };
                thread.Start();
            }
        }

        /// <summary>
        ///创建一个Mylog实例
        /// </summary>
        private Logger()
        {
            Initialize();
        }

        /// <summary>
        /// log类型
        /// </summary>
        public LogType LogType
        {
            get { return _logType; }
            set { _logType = value; }
        }

        /// <summary>
        /// 写log
        /// </summary>
        private void Work()
        {
            while (true)
            {
                //Determine log queue have record need wirte
                if (_logMessages.Count > 0)
                {
                    FileWriteMessage();
                }
                else
                    if (WaitLogMessage()) break;
            }
        }

        /// <summary>
        /// 写log方法
        /// </summary>
        private void FileWriteMessage()
        {
            LogMessage logMessage = null;
            lock (_lockObjeck)
            {
                if (_logMessages.Count > 0)
                    logMessage = _logMessages.Dequeue();
            }
            if (logMessage != null)
            {
                FileWrite(logMessage);
            }
        }

        /// <summary>
        ///等待信息
        /// </summary>
        private bool WaitLogMessage()
        {
            //determine log life time is true or false
            if (_state)
            {
                WaitHandle.WaitAny(new WaitHandle[] { _semaphore }, -1, false);
                return false;
            }
            FileClose();
            return true;
        }

        /// <summary>
        /// 根据logType获取log文件名称
        /// </summary>
        private string GetFilename()
        {
            DateTime now = DateTime.Now;
            string format = "";
            try
            {
                switch (_logType)
                {
                    case LogType.Daily:
                        _timeSign = new DateTime(now.Year, now.Month, now.Day);
                        _timeSign = _timeSign.AddDays(1);
                        format = "yyyyMMdd'.log'";
                        break;
                    case LogType.Weekly:
                        _timeSign = new DateTime(now.Year, now.Month, now.Day);
                        _timeSign = _timeSign.AddDays(7);
                        format = "yyyyMMdd'.log'";
                        break;
                    case LogType.Monthly:
                        _timeSign = new DateTime(now.Year, now.Month, 1);
                        _timeSign = _timeSign.AddMonths(1);
                        format = "yyyyMM'.log'";
                        break;
                    case LogType.Annually:
                        _timeSign = new DateTime(now.Year, 1, 1);
                        _timeSign = _timeSign.AddYears(1);
                        format = "yyyy'.log'";
                        break;
                }
                return now.ToString(format);
            }
            catch (Exception ex)
            {

                throw;
            }

        }

        private void FileWrite(LogMessage msg)
        {
            try
            {
                if (_writer == null)
                {
                    FileOpen();
                }
                else
                {
                    //determine the log file is time sign
                    if (DateTime.Now >= _timeSign)
                    {
                        FileClose();
                        FileOpen();
                    }
                }

                var strArr = msg.Text.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var s in strArr)
                {
                    _writer.WriteLine(s);
                }
                _writer.Flush();
            }
            catch (Exception e)
            {
                Console.Out.Write(e);
            }
        }

        private void FileOpen()
        {
            var filePath = Path.Combine(_logDirectory, GetFilename());
            _writer = new StreamWriter(filePath, true, Encoding.UTF8);
        }

        private void FileClose()
        {
            if (_writer != null)
            {
                _writer.Flush();
                _writer.Close();
                _writer.Dispose();
                _writer = null;
            }
        }

        /// <summary>
        /// 入队
        /// </summary>
        public void Write(LogMessage msg)
        {
            if (msg != null)
            {
                lock (_lockObjeck)
                {
                    _logMessages.Enqueue(msg);
                    _semaphore.Release();
                }
            }
        }

        /// <summary>
        /// Write message by message content and type
        /// </summary>
        /// <param name="text">log message</param>
        /// <param name="type">message type</param>
        public void Write(string text, MessageType type)
        {
            Write(new LogMessage(text, type));
        }

        /// <summary>
        /// Write Message by datetime and message content and type
        /// </summary>
        /// <param name="dateTime">datetime</param>
        /// <param name="text">message content</param>
        /// <param name="type">message type</param>
        public void Write(DateTime dateTime, string text, MessageType type)
        {
            Write(new LogMessage(dateTime, text, type));
        }

        /// <summary>
        /// Write message ty exception and message type 
        /// </summary>
        /// <param name="e">exception</param>
        /// <param name="type">message type</param>
        public void Write(Exception e, MessageType type)
        {
            Write(new LogMessage(e.Message, type));
        }

        #region IDisposable member

        /// <summary>
        /// Dispose log
        /// </summary>
        public void Dispose()
        {
            _state = false;
        }

        #endregion

        public static string GetSpecifyConfigValue(string key, string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    //Console.Out.Write("配置文件不存在：{0}", Path.GetFileName(filePath));
                    return null;
                }
                var ecf = new ExeConfigurationFileMap { ExeConfigFilename = filePath };
                Configuration configuration = ConfigurationManager.OpenMappedExeConfiguration(ecf, ConfigurationUserLevel.None);
                return configuration.AppSettings.Settings[key].Value;
            }
            catch (Exception ex)
            {
                Console.Out.Write(ex.Message);
                return null;
            }
        }

    }

    /// <summary>
    /// 日志类型
    /// </summary>
    public enum LogType
    {
        /// <summary>
        /// Create log by daily
        /// </summary>
        Daily,

        /// <summary>
        /// Create log by weekly
        /// </summary>
        Weekly,

        /// <summary>
        /// Create log by monthly
        /// </summary>
        Monthly,

        /// <summary>
        /// Create log by annually
        /// </summary>
        Annually
    }

    /// <summary>
    /// 日志消息
    /// </summary>
    public class LogMessage
    {
        public LogMessage()
            : this("", MessageType.Unknown)
        {
        }

        public LogMessage(string text, MessageType messageType)
            : this(DateTime.Now, text, messageType)
        {
        }

        public LogMessage(DateTime dateTime, string text, MessageType messageType)
        {
            Datetime = dateTime;
            Type = messageType;
            Text = text;
        }

        /// <summary>
        /// Gets or sets datetime
        /// </summary>
        public DateTime Datetime { get; set; }

        /// <summary>
        /// Gets or sets message content
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Gets or sets message type
        /// </summary>
        public MessageType Type { get; set; }

        /// <summary>
        /// Get Message to string
        /// </summary>
        /// <returns></returns>
        public new string ToString()
        {
            return string.Format("{0}\t {1}\n", Datetime.ToString(CultureInfo.InvariantCulture), Text);
        }
    }

    /// <summary>
    /// 日志消息类型
    /// </summary>
    public enum MessageType
    {
        /// <summary>
        /// 未定义的
        /// </summary>
        Unknown,

        /// <summary>
        /// 信息
        /// </summary>
        Information,

        /// <summary>
        /// 警告
        /// </summary>
        Warning,

        /// <summary>
        /// 错误
        /// </summary>
        Error,

        /// <summary>
        /// OK
        /// </summary>
        Success
    }

}
