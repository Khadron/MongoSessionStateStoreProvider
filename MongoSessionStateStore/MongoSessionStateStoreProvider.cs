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
using MongoSessionStateStore.Serialization;

namespace MongoSessionStateStore
{
    public class MongoSessionStateStoreProvider : SessionStateStoreProviderBase
    {
        private string _applicationName;
        private string _connectionString;
        private bool _recordExceptions;
        private SessionStateSection _cfgSection;
        private ConnectionStringSettings _connectionStringSettings;
        private MongoDatabaseSettings _databaseSettings;
        private SerializationProxy _serializer;

        public string ApplicationName
        {
            get { return _applicationName; }
        }

        public bool RecordException
        {
            get { return _recordExceptions; }
        }

        public MongoSessionStateStoreProvider()
        {
            Logger.CreateImpl();
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
                config.Remove("description");
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

            _recordExceptions = !string.IsNullOrEmpty(config["recordExceptions"]) && config["recordExceptions"].ToUpper() == "TRUE";


            bool journal = false;
            if (!string.IsNullOrEmpty(config["journal"]))
            {
                if (config["journal"].ToUpper() == "TRUE")
                    journal = true;
            }

            int writeConcernLevel = 1;
            string writeConcerStr = config["writeConcernLevel"];
            WriteConcern wc;
            if (!string.IsNullOrEmpty(writeConcerStr))
            {
                writeConcerStr = writeConcerStr.ToUpper();
                switch (writeConcerStr)
                {
                    case "W1":
                        wc = new WriteConcern(journal: journal, w: 1);
                        break;
                    case "W2":
                        wc = new WriteConcern(journal: journal, w: 1);
                        break;
                    case "W3":
                        wc = new WriteConcern(journal: journal, w: 1);
                        break;
                    case "WMAJORITY":
                        WriteConcern.WValue aux = "majority";
                        wc = new WriteConcern(aux, journal: journal);
                        break;
                    default:
                        throw new InvalidOperationException("unknown writeConcernLevel");
                }
                _databaseSettings = new MongoDatabaseSettings
                {
                    WriteConcern = wc
                };
            }

            string serializeTypeStr = config["SerializationType"];
            SerializationProxy.SerializerType serializerType = SerializationProxy.SerializerType.BsonSerialization;
            if (!string.IsNullOrEmpty(serializeTypeStr))
            {
                serializeTypeStr = serializeTypeStr.ToUpper();
                switch (serializeTypeStr)
                {
                    case "raw":
                        serializerType = SerializationProxy.SerializerType.RawSerialization;
                        break;
                    case "bson":
                        serializerType = SerializationProxy.SerializerType.BsonSerialization;
                        break;
                    default:
                        throw new InvalidOperationException("");
                }

            }
            Serializer(serializerType);

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

            try
            {
                //Release locked
                var client = GetConnection();
                IMongoCollection<BsonDocument> sessionCollection = GetSessionCollection(client);

                var filterBuilder = Builders<BsonDocument>.Filter;
                var filter = filterBuilder.And(
                    filterBuilder.Eq("_id", id),
                    filterBuilder.Eq("LockId", (Int32)lockId));

                var update = Builders<BsonDocument>.Update
                    .Set("Locked", false)
                    .Set("Expires", DateTime.Now.AddMinutes(_cfgSection.Timeout.TotalMinutes).ToUniversalTime());

                sessionCollection.UpdateOne(filter, update);
            }
            catch (MongoBulkWriteException ex)
            {
                if (RecordException)
                {
                    Logger.Instance.Write(ex.Message, MessageType.Error);
                }

                throw new ProviderException(ex.Message);
            }
        }

        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
        {
            BsonArray bsonArray = Serialize(item);
            MongoClient client = GetConnection();
            IMongoCollection<BsonDocument> sessionCollection = GetSessionCollection(client);
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
                        {"SessionItems",bsonArray},
                        {"Flags",0}
                    };
                    sessionCollection.InsertOneAsync(insertDoc);
                }
                else
                {
                    var update = Builders<BsonDocument>.Update
                        .Set("Expires", DateTime.Now.AddMinutes(item.Timeout).ToUniversalTime())
                        .Set("SessionItems", bsonArray)
                        .Set("Locked", false);
                    var filterBuilder = Builders<BsonDocument>.Filter;
                    var filter = filterBuilder.And(filterBuilder.Eq("_id", id), filterBuilder.Eq("LockId", lockId));
                    sessionCollection.UpdateOneAsync(filter, update);

                }
            }
            catch (MongoBulkWriteException ex)
            {

                if (RecordException)
                {
                    Logger.Instance.Write(ex.Message, MessageType.Error);
                }
                throw new ProviderException(ex.Message);
            }
        }

        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
        {
            try
            {
                var client = GetConnection();
                IMongoCollection<BsonDocument> sessionCollection = GetSessionCollection(client);

                var filter = Builders<BsonDocument>.Filter;
                var query = filter.Eq("_id", id)
                    & filter.Eq("ApplicationName", _applicationName)
                    & filter.Eq("LockId", (Int32)lockId);

                sessionCollection.DeleteOne(query);
            }
            catch (MongoBulkWriteException ex)
            {
                if (RecordException)
                {
                    Logger.Instance.Write(ex.Message, MessageType.Error);
                }
                throw new ProviderException(ex.Message);
            }
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            try
            {
                var client = GetConnection();
                IMongoCollection<BsonDocument> sessionCollection = GetSessionCollection(client);

                var filterBuilder = Builders<BsonDocument>.Filter;
                var query = filterBuilder.And(
                    filterBuilder.Eq("_id", id));

                var update = Builders<BsonDocument>.Update
                    .Set("Expires", DateTime.Now.AddMinutes(_cfgSection.Timeout.TotalMinutes)
                    .ToUniversalTime());

                sessionCollection.UpdateOneAsync(query, update);
            }
            catch (MongoBulkWriteException ex)
            {
                if (RecordException)
                {
                    Logger.Instance.Write(ex.Message, MessageType.Error);
                }

                throw new ProviderException(ex.Message);
            }

        }

        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
        {
            return new SessionStateStoreData(new SessionStateItemCollection(),
                SessionStateUtility.GetSessionStaticObjects(context), timeout);
        }

        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
            var client = GetConnection();
            IMongoCollection<BsonDocument> sessionCollection = GetSessionCollection(client);

            try
            {
                var doc = new BsonDocument{
                        { "_id",id},
                        { "ApplicationName",_applicationName},
                        { "Created",DateTime.Now.ToUniversalTime()},
                        { "Expires",DateTime.Now.ToUniversalTime()},
                        { "LockDate",DateTime.Now.ToUniversalTime()},
                        { "LockId",0},{"Timeout",timeout},{"Locked",false},
                        { "SessionItems",new BsonArray()},
                        { "Flags",1}
                    };

                sessionCollection.InsertOne(doc);
            }
            catch (MongoBulkWriteException ex)
            {
                if (RecordException)
                {
                    Logger.Instance.Write(ex.Message, MessageType.Error);
                }
                throw new ProviderException(ex.Message);
            }
        }

        public override void EndRequest(HttpContext context)
        {

        }

        #endregion

        #region Tools Method

        private MongoClient GetConnection()
        {
            var client = new MongoClient(_connectionString);
            return client;
        }

        private IMongoCollection<BsonDocument> GetSessionCollection(MongoClient client)
        {
            return client.GetDatabase("SessionState", _databaseSettings)
                .GetCollection<BsonDocument>("Sessions");
        }

        private SessionStateStoreData GetSessionStoreItem(bool lockRecord, HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actionFlags)
        {
            SessionStateStoreData item = null;
            lockAge = TimeSpan.Zero;
            lockId = null;
            locked = false;
            actionFlags = SessionStateActions.None;

            try
            {
                var client = GetConnection();
                IMongoCollection<BsonDocument> sessionCollection = GetSessionCollection(client);

                var filterBuilder = Builders<BsonDocument>.Filter;
                if (lockRecord)
                {
                    var filter = filterBuilder.And(
                        filterBuilder.Eq("_id", id),
                        filterBuilder.Eq("Locked", false),
                        filterBuilder.Gt("Expires", DateTime.Now.ToUniversalTime()));
                    var update = Builders<BsonDocument>.Update
                        .Set("Locked", true)
                        .Set("LockDate", DateTime.Now.ToUniversalTime());
                    var result = sessionCollection.UpdateOne(filter, update);
                    bool isLocked = result.ModifiedCount == 1;

                    locked = !isLocked;
                }

                var getFilter = filterBuilder.And(filterBuilder.Eq("_id", id));
                var curSessionItem = sessionCollection.Find(getFilter).FirstOrDefault();
                if (curSessionItem != null)
                {
                    DateTime expires = curSessionItem["Expires"].ToUniversalTime();
                    if (expires < DateTime.Now.ToUniversalTime())
                    {
                        //delete session 
                        sessionCollection.DeleteOne(getFilter);
                        locked = false;
                    }
                    else
                    {
                        if (!locked)
                        {

                            var serializedItems = curSessionItem["SessionItems"].AsBsonArray;
                            lockId = curSessionItem["LockId"].AsInt32;
                            lockAge = DateTime.Now.ToUniversalTime().Subtract(curSessionItem["LockDate"].ToUniversalTime());
                            actionFlags = (SessionStateActions)curSessionItem["Flags"].AsInt32;
                            var timeout = curSessionItem["Timeout"].AsInt32;

                            lockId = (int)lockId + 1;
                            var update = Builders<BsonDocument>
                                .Update
                                .Set("LockId", lockId)
                                .Set("Flags", 0);
                            sessionCollection.UpdateOneAsync(getFilter, update);

                            item = actionFlags == SessionStateActions.InitializeItem
                                ? CreateNewStoreData(context, (int)_cfgSection.Timeout.TotalMinutes)
                                : _serializer.Deserialize(context, serializedItems, timeout);
                        }
                    }
                }

            }
            catch (MongoBulkWriteException ex)
            {

                if (_recordExceptions)
                {
                    Logger.Instance.Write(ex.Message, MessageType.Error);
                }
                throw;
            }

            return item;
        }

        private BsonArray Serialize(SessionStateStoreData item)
        {
            return _serializer.Serialize(item);
        }

        private void Serializer(SerializationProxy.SerializerType serializerType)
        {
            _serializer = new SerializationProxy(serializerType);
        }

        #endregion

    }
}
