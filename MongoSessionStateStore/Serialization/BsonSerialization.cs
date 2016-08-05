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
    internal class BsonSerialization : ISerialization
    {
        public BsonArray Serialize(SessionStateStoreData sessionData)
        {
            BsonArray bsonArray = new BsonArray();
            for (int i = 0; i < sessionData.Items.Count; i++)
            {
                string key = sessionData.Items.Keys[i];
                var sessionObj = sessionData.Items[key];
                if (sessionObj is BsonArray)
                {
                    bsonArray.Add(new BsonDocument(key, sessionObj as BsonValue));
                }
                else
                {
                    BsonValue outValue;

                    if (BsonTypeMapper.TryMapToBsonValue(sessionObj, out outValue))
                    {
                        bsonArray.Add(new BsonDocument(key, outValue));
                    }
                    else
                    {
                        bsonArray.Add(new BsonDocument(key, sessionObj.ToBsonDocument()));
                    }
                }

            }
            return bsonArray;
        }

        public SessionStateStoreData Deserialize(HttpContext context, BsonArray bsonSerializedItems, int timeout)
        {

            var sessionItems = new SessionStateItemCollection();

            foreach (var bsonValue in bsonSerializedItems.Values)
            {
                var document = bsonValue as BsonDocument;
                foreach (var name in document.Names)
                {
                    sessionItems[name] = document[name];
                }
            }

            return new SessionStateStoreData(sessionItems, SessionStateUtility.GetSessionStaticObjects(context), timeout);
        }
    }
}
