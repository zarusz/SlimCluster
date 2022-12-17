namespace SlimCluster.Persistence.LocalFile;

using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class LocalJsonFileClusterPersistenceService : IClusterPersistenceService
{
    private readonly IEnumerable<IDurableComponent> _components;
    private readonly string _filePath;
    private readonly Encoding _encoding = Encoding.UTF8;
    private readonly JsonSerializerSettings _jsonSettings;

    public LocalJsonFileClusterPersistenceService(IEnumerable<IDurableComponent> components, string filePath, Formatting formatting)
    {
        _components = components;
        _filePath = filePath;
        _jsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = formatting
        };
    }

    public async Task Persist(CancellationToken cancellationToken)
    {
        try
        {
            var state = new JObject();
            foreach (var component in _components)
            {
                var componentState = new JObject();
                component.OnStatePersist(new JsonStateWriter(componentState));

                var name = component.GetType().Name;
                state[name] = componentState;
            }

            var json = JsonConvert.SerializeObject(state, _jsonSettings);
            await File.WriteAllTextAsync(_filePath, json, _encoding, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new ClusterException($"Could not persist cluster state into local file {_filePath}: {ex.Message}", ex);
        }
    }

    public async Task Restore(CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, _encoding, cancellationToken);

            var state = JsonConvert.DeserializeObject<JObject>(json, _jsonSettings)!;
            foreach (var component in _components)
            {
                var name = component.GetType().Name;

                var componentState = state[name] as JObject;
                if (componentState != null)
                {
                    component.OnStateRestore(new JsonStateReader(componentState));
                }
            }
        }
        catch (Exception ex)
        {
            throw new ClusterException($"Could not restore cluster state into local file {_filePath}: {ex.Message}", ex);
        }
    }
}


internal class JsonStateWriter : IStateWriter
{
    private readonly JObject _current;

    public JsonStateWriter(JObject current)
    {
        _current = current;
    }

    public void Set<T>(string key, T value)
    {
        _current[key] = value != null ? JToken.FromObject(value) : null;
    }

    public IStateWriter SubComponent(string key)
    {
        var component = new JObject();
        _current[key] = component;
        return new JsonStateWriter(component);
    }
}

internal class JsonStateReader : IStateReader
{
    private readonly JObject _current;

    public JsonStateReader(JObject current)
    {
        _current = current;
    }

    public T? Get<T>(string key)
    {
        var v = _current[key];
        if (v == null)
        {
            return default;
        }
        return v.ToObject<T>();
    }

    IStateReader IStateReader.SubComponent(string key)
    {
        var component = _current[key] as JObject ?? new JObject();
        return new JsonStateReader(component);
    }
}
