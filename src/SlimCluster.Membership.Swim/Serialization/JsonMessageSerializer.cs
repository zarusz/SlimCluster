namespace SlimCluster.Membership.Swim.Serialization
{
    using Newtonsoft.Json;
    using System;
    using System.Text;

    public class JsonMessageSerializer : ISerializer
    {
        private readonly Encoding encoding;

        public JsonMessageSerializer(Encoding encoding) => this.encoding = encoding;

        public JsonMessageSerializer() : this(Encoding.ASCII)
        {
        }

        public T Deserialize<T>(byte[] paylad) where T : class
        {
            var json = encoding.GetString(paylad);
            return JsonConvert.DeserializeObject<T>(json) ?? throw new ArgumentNullException(nameof(json));
        }

        public byte[] Serialize<T>(T msg) where T : class
        {
            var json = JsonConvert.SerializeObject(msg);
            return encoding.GetBytes(json);
        }
    }

}
