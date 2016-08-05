using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using MongoDB.Bson;

namespace MongoSessionStateStore.Serialization
{
    public class SerializationProxy : ISerialization
    {
        public enum SerializerType
        {
            BsonSerialization,
            RawSerialization
        }

        private ISerialization _serializer;

        public SerializationProxy(SerializerType serializerType)
        {
            if (serializerType == SerializerType.RawSerialization)
                _serializer = new RawSerialization();
            else
                _serializer = new BsonSerialization();
        }


        public BsonArray Serialize(SessionStateStoreData sessionData)
        {
            return _serializer.Serialize(sessionData);
        }

        public SessionStateStoreData Deserialize(HttpContext context, BsonArray bsonSerializedItems, int timeout)
        {
            return _serializer.Deserialize(context, bsonSerializedItems, timeout);
        }

    }
}
