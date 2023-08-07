namespace SlimCluster.Serialization.Json;

using System.Text;

using Newtonsoft.Json;

using SlimCluster.Serialization;

public class AliasedJsonMessageSerializer : ISerializer
{
    private readonly Encoding _encoding;
    private readonly JsonSerializerSettings _settings;
    private readonly IDictionary<string, Type> _typeByAlias;
    private readonly IDictionary<Type, string> _aliasByType;

    public AliasedJsonMessageSerializer(Encoding encoding, IEnumerable<ISerializationTypeAliasProvider>? typeAliasProviders = null)
    {
        _encoding = encoding;
        _settings = new JsonSerializerSettings
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor
        };

        _typeByAlias = new Dictionary<string, Type>();
        _aliasByType = new Dictionary<Type, string>();
        if (typeAliasProviders != null)
        {
            foreach (var typeAliasProvider in typeAliasProviders)
            {
                foreach (var (alias, type) in typeAliasProvider.GetTypeAliases())
                {
                    _typeByAlias.Add(alias, type);
                    _aliasByType.Add(type, alias);
                }
            }
        }
    }

    public AliasedJsonMessageSerializer(IEnumerable<ISerializationTypeAliasProvider>? typeAliasProviders = null)
        : this(Encoding.ASCII, typeAliasProviders)
    {
    }

    public object Deserialize(byte[] payload)
    {
        var aliasCount = payload[0];
        var alias = _encoding.GetString(payload.AsSpan(1, aliasCount));

        var messageType = _typeByAlias[alias];

        var json = _encoding.GetString(payload.AsSpan(1 + aliasCount));
        return JsonConvert.DeserializeObject(json, messageType, _settings)
            ?? throw new ArgumentNullException(nameof(json));
    }

    public byte[] Serialize(object msg)
    {
        string alias;
        for (var messageType = msg.GetType(); !_aliasByType.TryGetValue(messageType, out alias) && messageType != typeof(object); messageType = messageType.BaseType)
        {
        }

        if (alias == null)
        {
            throw new InvalidOperationException($"Cannot serialize message of type {msg.GetType()}");
        }

        var aliasCount = _encoding.GetByteCount(alias);

        var json = JsonConvert.SerializeObject(msg, _settings);
        var jsonCount = _encoding.GetByteCount(json);

        var payload = new byte[1 + aliasCount + jsonCount];

        payload[0] = (byte)aliasCount;
        _encoding.GetBytes(alias, payload.AsSpan(1, aliasCount));
        _encoding.GetBytes(json, payload.AsSpan(1 + aliasCount));

        return payload;
    }
}
