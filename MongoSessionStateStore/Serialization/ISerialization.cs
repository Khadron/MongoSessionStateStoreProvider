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
    public interface ISerialization
    {
        BsonArray Serialize(SessionStateStoreData sessionData);
        SessionStateStoreData Deserialize(HttpContext context, BsonArray bsonSerializedItems, int timeout);
    }
}
