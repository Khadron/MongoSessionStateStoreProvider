using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Configuration.Provider;
using System.Globalization;
using System.IO;
using System.Web;
using System.Web.Configuration;
using System.Web.SessionState;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoSessionStateStore
{
    public class MongoSessionStateStore : SessionStateStoreProviderBase
    {
        private SessionStateSection _cfgSection;
        private ConnectionStringSettings _connectionStringSettings;
        private string _applicationName;
        private string _connectionString;
        private bool _recordExceptions;
        private const string ExceptionMessage = "";
        private WriteConcern _writeConcern;
        private MongoClient _client;

        public string ApplicationName
        {
            get { return _applicationName; }
        }

        public bool RecordException
        {
            get { return _recordExceptions; }
        }

        public MongoSessionStateStore()
        {
            Logger.CreateImpl();
        }

        /// <summary>
        /// 获取MongoDb中SessionStae数据库中Session数据
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        private IMongoCollection<BsonDocument> GetSessionCollection()
        {
            _client = new MongoClient(_connectionString);
            return _client.GetDatabase("SessionState")
                .GetCollection<BsonDocument>("Sessions")
                .WithWriteConcern(_writeConcern);
        }


        #region Override

        public override void Initialize(string name, NameValueCollection config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (name.Length == 0)
                name = "MongoSessionStateStore";
            if (string.IsNullOrEmpty(config["description"]))
            {
                config["description"] = "MongoDb Session State Store provider";
            }

            base.Initialize(name, config);

            _applicationName = System.Web.Hosting.HostingEnvironment.ApplicationVirtualPath;

            Configuration cfg = WebConfigurationManager.OpenWebConfiguration(_applicationName);
            _cfgSection = (SessionStateSection)cfg.GetSection("system.web/sessionState");
            _connectionStringSettings = ConfigurationManager.ConnectionStrings[config["connectionStringName"]];

            if (_connectionStringSettings == null || string.IsNullOrWhiteSpace(_connectionStringSettings.ConnectionString))
                throw new ProviderException("Connection string cannot be empty");

            _connectionString = _connectionStringSettings.ConnectionString;

            _recordExceptions = config["recordExceptions"] != null && config["recordExceptions"].ToUpper() == "TRUE";


            bool fsync = false;
            if (config["fsync"] != null)
            {
                if (config["fsync"].ToUpper() == "TRUE")
                    fsync = true;
            }

            int writeConcernLevel = 1;
            if (config["writeConcernLevel"] != null)
            {
                if (int.TryParse(config["writeConcernLevel"], out writeConcernLevel))
                    throw new ProviderException("writeConcernLevel must be a valid integer");
            }

            //MongoDB默认级别为-1-error ignored 
            //在这里默认级别为1-acknowledged
            //关于WriteConcern请看这篇文章 http://kyfxbl.iteye.com/blog/1952941
            string wValue = "1";
            if (writeConcernLevel > 0)
                wValue = (writeConcernLevel + 1).ToString(CultureInfo.InvariantCulture);
            _writeConcern = new WriteConcern(Optional.Create(WriteConcern.WValue.Parse(wValue)),
                fsync: Optional.Create<bool?>(fsync), journal: Optional.Create<bool?>(true));

        }

        public override void Dispose()
        {

        }

        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return false;
        }

        public override void InitializeRequest(HttpContext context)
        {

        }

        public override SessionStateStoreData GetItem(HttpContext context,
            string id, out bool locked, out TimeSpan lockAge, out object lockId,
            out SessionStateActions actions)
        {
            return GetSessionStoreItem(false, context, id, out locked, out lockAge, out lockId, out actions);
        }

        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge,
            out object lockId, out SessionStateActions actions)
        {
            return GetSessionStoreItem(true, context, id, out locked, out lockAge, out lockId, out actions);
        }

        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
        {

            IMongoCollection<BsonDocument> sessionCollection = GetSessionCollection();

            //todo:
        }

        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
        {

            //释放锁
            string sessionItems = Serialize((SessionStateItemCollection)item.Items);

            IMongoCollection<BsonDocument> sessionCollection = GetSessionCollection();

            try
            {
                if (newItem)
                {
                    var insertDoc = new BsonDocument
                    {
                        {"_id",id},
                        {"ApplicationName",_applicationName},
                        {"Created",DateTime.Now.ToUniversalTime()},
                        {"Expires",DateTime.Now.AddMinutes(item.Timeout).ToUniversalTime()},
                        {"LockDate",DateTime.Now.ToUniversalTime()},
                        {"LockId",0},
                        {"Timeout",item.Timeout},
                        {"Locked",false},
                        {"SessionItems",sessionItems},
                        {"Flags",0}
                    };
                    //todo:写入操作
                    sessionCollection.InsertOne(insertDoc);

                }
                else
                {
                    var update = Builders<BsonDocument>.Update.Set("Expires",
                        DateTime.Now.AddMinutes(item.Timeout).ToUniversalTime())
                        .Set("SessionItems", sessionItems)
                        .Set("Locked", false);
                    sessionCollection.UpdateOne(GetIdentity(id, Builders<BsonDocument>.Filter), update);

                }
            }
            catch (Exception ex)
            {

                if (RecordException)
                {
                    Logger.Instance.Write(ex.Message, MessageType.Error);
                }
                throw;
            }
        }

        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
        {
            throw new NotImplementedException();
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 创建用于当前请求的新的SessionStateStoreData
        /// </summary>
        /// <param name="context"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
        {
            return new SessionStateStoreData(new SessionStateItemCollection(), SessionStateUtility.GetSessionStaticObjects(context), timeout);
        }

        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
            throw new NotImplementedException();
        }

        public override void EndRequest(HttpContext context)
        {
            throw new NotImplementedException();
        }

        #endregion


        private string Serialize(SessionStateItemCollection items)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                if (items != null)
                    items.Serialize(writer);
                writer.Close();

                return Convert.ToBase64String(ms.ToArray());
            }
        }

        private SessionStateStoreData Deserialize(HttpContext context, string serializedItems, int timeout)
        {
            using (var ms = new MemoryStream(Convert.FromBase64String(serializedItems)))
            {
                var sessionItems = new SessionStateItemCollection();

                if (ms.Length > 0)
                {
                    using (var reader = new BinaryReader(ms))
                    {
                        sessionItems = SessionStateItemCollection.Deserialize(reader);
                    }
                }

                return new SessionStateStoreData(sessionItems, SessionStateUtility.GetSessionStaticObjects(context), timeout);
            }
        }


        private SessionStateStoreData GetSessionStoreItem(bool lockRecord, HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actionFlags)
        {
            //添加锁
            SessionStateStoreData item = null;
            lockAge = TimeSpan.Zero;
            lockId = null;
            locked = false;
            actionFlags = SessionStateActions.None;

            IMongoCollection<BsonDocument> sessionCollection = GetSessionCollection();

            string serializedItems = string.Empty;
            bool foundRecord = false;
            bool deleteData = false;

            int timeout = 0;

            var filter = Builders<BsonDocument>.Filter;
            /*
             * 调用GetItemExclusive时 locked设为true
             * 调用GetItem时locked设为false
             * 如果数据存储区中未找到任何会话项数据，则 GetItemExclusive 方法将 locked 输出参数设置为 false，并返回 null。
             * 这将导致 SessionStateModule 调用 CreateNewStoreData 方法来为请求创建一个新的 SessionStateStoreData 对象。
             * 如果在数据存储区中找到会话项数据但该数据已锁定，则 GetItemExclusive 方法将 locked 输出参数设置为 true，
             * 将 lockAge 输出参数设置为当前日期和时间与该项锁定日期和时间的差，将 lockId 输出参数设置为从数据存储区中检索的锁定标识符，并返回 null。
             * 这将导致 SessionStateModule 隔半秒后再次调用 GetItemExclusive 方法，以尝试检索会话项信息和获取对数据的锁定。
             * 如果lockAge输出参数的设置值超过 ExecutionTimeout 值，SessionStateModule 将调用 ReleaseItemExclusive方法以清除对会话项数据的锁定，
             * 然后再次调用 GetItemExclusive 方法。 
             */
            try
            {

                if (_cfgSection.RegenerateExpiredSessionId)
                {
                    //todo:Cookieless 
                }
                else
                {

                    var conditions = GetIdentity(id, filter);
                    var first = sessionCollection.Find(conditions).FirstOrDefault();

                    if (first != null)
                    {
                        DateTime expires = first["Expires"].ToUniversalTime();

                        if (expires < DateTime.Now.ToUniversalTime())
                        {
                            sessionCollection.DeleteOne(conditions);
                        }
                        else
                        {
                            bool isLocked = first["Locked"].AsBoolean;

                            if (lockRecord && isLocked)
                            {
                                locked = true;
                                return null;
                            }

                            lockAge = DateTime.Now.ToUniversalTime().Subtract(first["LockDate"].ToUniversalTime());
                            lockId = first["LockId"].AsInt32;
                            serializedItems = first["SessionItems"].AsString;
                            actionFlags = (SessionStateActions)first["Flags"].AsInt32;
                            item = Deserialize(context, serializedItems, timeout);


                            lockId = (int)lockId + 1;


                            var update = Builders<BsonDocument>.Update
                                .Set("LockId", (int)lockId)
                                .Set("Flags", 0);
                            if (lockRecord)
                            {
                                update = update
                                   .Set("Locked", true)
                                   .Set("LockDate", DateTime.Now.ToUniversalTime());
                            }

                            sessionCollection.UpdateOne(conditions, update);
                        }
                    }
                }

            }
            catch (Exception ex)
            {

                if (_recordExceptions)
                {
                    Logger.Instance.Write(ex.Message, MessageType.Error);
                }
                throw;
            }

            return item;
        }

        private FilterDefinition<BsonDocument> GetIdentity(string id, FilterDefinitionBuilder<BsonDocument> filter)
        {
            return filter.Eq("_id", id) & filter.Eq("ApplicationName", ApplicationName);
        }
    }
}
