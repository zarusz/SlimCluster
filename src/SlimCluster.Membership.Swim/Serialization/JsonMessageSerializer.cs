namespace SlimCluster.Membership.Swim.Serialization
{
    using Newtonsoft.Json;
    using System;
    using System.Text;

    public class JsonMessageSerializer : ISerializer
    {
        private readonly Encoding encoding;
        private readonly JsonSerializerSettings settings;

        public JsonMessageSerializer(Encoding encoding)
        {
            this.encoding = encoding;
            this.settings = new JsonSerializerSettings
            {
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            };
        }

        public JsonMessageSerializer() : this(Encoding.ASCII)
        {
        }

        public T Deserialize<T>(byte[] paylad) where T : class
        {
            var json = encoding.GetString(paylad);
            return JsonConvert.DeserializeObject<T>(json, settings)
                ?? throw new ArgumentNullException(nameof(json));
        }

        public byte[] Serialize<T>(T msg) where T : class
        {
            var json = JsonConvert.SerializeObject(msg, settings);
            return encoding.GetBytes(json);
        }
    }
}
