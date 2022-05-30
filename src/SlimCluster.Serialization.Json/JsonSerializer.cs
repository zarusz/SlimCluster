namespace SlimCluster.Serialization.Json;

using Newtonsoft.Json;
using SlimCluster.Serialization;
using System.Text;

public class JsonSerializer : ISerializer
{
    private readonly Encoding _encoding;
    private readonly JsonSerializerSettings _settings;

    public JsonSerializer(Encoding encoding)
    {
        _encoding = encoding;
        _settings = new JsonSerializerSettings
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc
        };
    }

    public JsonSerializer() : this(Encoding.ASCII)
    {
    }

    public T Deserialize<T>(byte[] paylad) where T : class
    {
        var json = _encoding.GetString(paylad);
        return JsonConvert.DeserializeObject<T>(json, _settings)
            ?? throw new ArgumentNullException(nameof(json));
    }

    public byte[] Serialize<T>(T msg) where T : class
    {
        var json = JsonConvert.SerializeObject(msg, _settings);
        return _encoding.GetBytes(json);
    }
}
