using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using MongoDB.Bson;

namespace MongoSessionStateStore.Serialization
{
    internal class RawSerialization : ISerialization
    {
        public BsonArray Serialize(SessionStateStoreData sessionData)
        {
            BsonArray bsonArray = new BsonArray();
            for (int i = 0; i < sessionData.Items.Count; i++)
            {
                string key = sessionData.Items.Keys[i];
                var sessionObj = sessionData.Items[key];

                if (sessionObj == null)
                {
                    bsonArray.Add(new BsonDocument(key, BsonNull.Value));
                }
                else
                {
                    using (var ms = new MemoryStream())
                    {
                        IFormatter formatter = new BinaryFormatter();
                        string serializedItem;
                        if (sessionObj is UnSerializedItem)
                        {
                            serializedItem = ((UnSerializedItem)sessionObj).SerializedString;
                        }
                        else
                        {
                            formatter.Serialize(ms, sessionObj);
                            serializedItem = Convert.ToBase64String(ms.ToArray());
                        }
                        bsonArray.Add(new BsonDocument(key, serializedItem));
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
                    var value = document[name];

                    if (value == BsonNull.Value)
                    {
                        sessionItems[name] = null;
                    }
                    else
                    {
                        string valueSerialized = value.AsString;
                        try
                        {
                            using (var ms = new MemoryStream(Convert.FromBase64String(valueSerialized)))
                            {
                                IFormatter formatter = new BinaryFormatter();
                                var item = formatter.Deserialize(ms);
                                sessionItems[name] = item;
                            }
                        }
                        catch (SerializationException)
                        {
                            sessionItems[name] = new UnSerializedItem { SerializedString = valueSerialized };
                        }
                    }
                }
            }

            return new SessionStateStoreData(sessionItems, SessionStateUtility.GetSessionStaticObjects(context), timeout);
        }

    }
}
