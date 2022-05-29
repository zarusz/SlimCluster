namespace SlimCluster.Serialization.Json
{
    using Newtonsoft.Json;
    using SlimCluster.Serialization;
    using System;
    using System.Text;

    public class JsonSerializer : ISerializer
    {
        private readonly Encoding encoding;
        private readonly JsonSerializerSettings settings;

        public JsonSerializer(Encoding encoding)
        {
            this.encoding = encoding;
            settings = new JsonSerializerSettings
            {
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            };
        }

        public JsonSerializer() : this(Encoding.ASCII)
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
