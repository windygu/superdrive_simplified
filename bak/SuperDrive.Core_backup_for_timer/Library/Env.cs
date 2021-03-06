﻿using System;
using ConnectTo.Foundation.Core;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Threading;
using NLog;
using ConnectTo.Foundation.Helper;
using ConnectTo.Foundation.Messages;
using Newtonsoft.Json;
using System.IO;
using System.Net;
using NLog.Config;
using NLog.Targets;
using System.Text;
using System.Threading.Tasks;
using ConnectTo.Foundation.Business;
using SuperDrive.Core.Library;
using SuperDrive.Util;

namespace SuperDrive.Library
{
    public abstract class Env
    {
        public abstract DeviceType DeviceType { get; protected set; }
        
        public event Action<string> NetworkChanged = delegate { };
        public void OnNetworkChanged(string ips)
        {
            NetworkChanged?.Invoke(ips);
        }

        public abstract bool CanDiscover { get;}

        
        public Config Config { get; set; }
        public virtual string AppWorkingPath { get; internal protected set; }
        public abstract string DefaultConfigPath { get; }

        public abstract string[] GetIPs();

        //NLog的config是不是通过配置文件(app.config)做更好一些？ 
        //但那样有个问题，就是不知道怎么把DefaultConfigPath传进去。而且不知道Andriod一端怎么弄
        //或者能不能通过配置文件配置大部分，在加载代码的时候修改其中一部分？
        //foreach(LoggingRule rule in LogManager.Configuration.LoggingRules)
        //{
        //    foreach(Target target in rule.Targets)
        //    {
        //        if(target is FileTarget)
        //        {
        //            var ft = target as FileTarget;
        //            ft.FileName = Util.CombinePath(DefaultConfigPath, "${shortdate}.log");
        //        }
        //    }
        //}
        Logger _logger = null;
        public Logger Logger => _logger ?? (_logger = LogManager.GetLogger("any"));
        public string CurrentSSID { get; set; }
        public JsonSerializerSettings JsonSetting { get; set; }
        
        private const string LogLayout =
            "${longdate}" +
            "|${threadid}" +
            "|${level:uppercase=true}" +
            "|${logger:shortName=true}" +
            "|${message}" +
            "${onexception:inner=${newline}${exception:format=tostring,stacktrace}}";

        public static void InitializeLogManager(string LogDirectory)
        {
            var logConfig = new LoggingConfiguration();
#if DEBUG
            var minLevel = LogLevel.Trace;
#else
            var minLevel = LogLevel.Trace;
#endif

#if DEBUG
            var debug = new DebuggerTarget { Layout = LogLayout };
            var debugRule = new LoggingRule("*", LogLevel.Debug, debug);
            logConfig.LoggingRules.Add(debugRule);
            logConfig.AddTarget("debug", debug);
#endif

            var file = new FileTarget
            {
                Layout = LogLayout,
                FileName = Path.Combine(LogDirectory, "${shortdate}.log"),
                Encoding = Encoding.UTF8
            };
            logConfig.AddTarget("file", file);
            //路由规则。loggerNamePattern且minLevel符合,输出到target
            var fileRule = new LoggingRule("*", minLevel, file);
            logConfig.LoggingRules.Add(fileRule);

            LogManager.Configuration = logConfig;
        }

        protected void CommonInit()
        {
            //Log和App逻辑没有关系，所以放在Env中是合适。
            //Config呢？和逻辑有关系，但其实只是一个永久性存储。是不是也适合放在Env里面？
            //Env最先初始化，然后应该初始化Config,最后初始化Log.其间有顺序依赖。因为未来Log有可能依赖Config.
            if (_instance != null) throw new Exception("Env只应创建一个实例。有其他方法可重构");

            _instance = this;
            JsonSetting = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            Config = Config.Load(this, Util.CombinePath(DefaultConfigPath, Config.CONFIG_FILENAME));
        }

        internal void CheckFileDataMessage(FileDataMessage message, FileItem fileItem)
        {

            var logger = Logger;
            if (message.Length < FileDataMessage.BUFFER_SIZE)
            {
                if (message.Length + fileItem.TransferredLength != fileItem.Length)
                {
                    var msg = "可能的接收文件错误。message.length=" + message.Length + " data.length=" + message.Data.Length + " file.length=" + fileItem.Length + " fileItem.TransferredLength=" + fileItem.TransferredLength + " file.name" + fileItem.Name;
                    //ShowMessage(msg);文件读取的时候，存不在在读取到的数据，在中间过程中，与给定长度不一致的时候？
                    logger.Debug(msg);
                }
            }
        }

        ~Env()
        {
            //TODO 这样在析构函数里面做有用不？如果有用，还要实现IDispose接口做什么？
            Dispose();
        }

        Queue<Action> taskList = new Queue<Action>(32);
        AutoResetEvent taskWaiter = new AutoResetEvent(false);
        /// <summary>
        /// 注意！！！这个不适合在UI线程调用。因为当任务队列满之后，会阻塞。
        /// </summary>
        /// <param name="task"></param>
        public void PostTask(Action task)
        {
            
        }


        public abstract bool InitLocalDeviceIPAdress(Device localDeviceInfo);
        /// <summary>
        /// 将Image,Music等分类目录转换为真正的路径。这个实现不好。这些路径应该作为常量定义在Container（Environment）里面。
        /// 但是有个问题，在BrowseRequest的时候，需要传递的是一个Constant, Type之类的，在真正的打开文件的时候又需要真实的路径。
        /// </summary>
        /// <param name="libraryName"></param>
        /// <returns>返回翻译好的路径，如果不知道如何翻译，就原样返回</returns>
        protected abstract string GetRealPathForLibrary(string libraryName);

        public string GetRealPath(string pathString)
        {
            var definedLibrary = "";
            var relativePath = "";
            var index = pathString.IndexOf("/");
            if (index > 0)
            {
                definedLibrary = pathString.Substring(0, index);
                relativePath = pathString.Substring(index);
            }
            else
            {
                definedLibrary = pathString;
            }

            return GetRealPathForLibrary(definedLibrary) + relativePath;
        }

        public abstract byte[] GetThumbnailStream(string relativePath, Item item);

        public abstract List<Item> GetBrowseList(string path);
        public abstract void ShowMessage(string v);

        public abstract string GetUserName();
        public abstract string GetDeviceName();

        internal static T Create<T>()
        {
            throw new NotImplementedException();
        }

        static Env _instance = null;
        public static Env Instance
        {
            get
            {
                if (_instance == null) throw new Exception("Env需要至少一次实例。如果有更好的方法可以重构。");
                return _instance;
            }
        }

        
        public void Dispose()
        {
        
        }

        
        public const string Image = "__IMAGE__";
        public const string Music = "__MUSIC__";
        public const string Video = "__VIDEO__";
        public const string Others = "__OTHERS__";
        internal static object DefaultBroadCastPort;

        public static async Task<List<AbstractFileItem>> GetFiles(DirItem dir)
        {
            return await _instance.GetFilesImpl(dir);
        }

        public static async Task<Stream> GetThumbnailStream(string path)
        {
            return await _instance.GetThumbnailStreamImpl(path);
        }

        protected abstract Task<Stream> GetThumbnailStreamImpl(string path);

        protected abstract Task<List<AbstractFileItem>> GetFilesImpl(DirItem dir);

        public static void Init(Env envImpl) => _instance = envImpl;
        public static void PostTask(MTask task) => _instance.TaskPool.PostTask(task);

        public static void PostTask(Action task, Func<bool> isValid, Func<string> toStringImpl = null)
        {
            _instance.TaskPool.PostTask(task, isValid, toStringImpl);

        }
        /// <summary>
        /// 若想取消，设置task的isValid为false就行了。或者为Mtask定义一个Cancel方法。
        /// </summary>
        /// <param name="task"></param>
        /// <param name="time"></param>
        public static Timer DoLater(Action task, TimeSpan time)
        {
            Contract.Requires(task != null);
            Timer timer = new Timer(time,task);
            return timer;
        }


        protected Env()
        {
            TaskPool = new TaskPool();
            TaskPool.Start();
        }



        public TaskPool TaskPool { get; private set; }

        internal static void SendUdpMessage(byte[] data, int length, string _defaultIP, object defaultBroadCastPort)
        {
            throw new NotImplementedException();
        }
    }
}