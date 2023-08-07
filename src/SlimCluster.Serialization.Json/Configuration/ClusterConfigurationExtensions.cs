namespace SlimCluster.Serialization.Json;

using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SlimCluster.Serialization;

public static class ClusterConfigurationExtensions
{
    public static ClusterConfiguration AddJsonSerialization(this ClusterConfiguration cfg, Encoding? encoding = null)
    {
        encoding ??= Encoding.ASCII;

        cfg.PostConfigurationActions.Add(services =>
        {
            services.TryAddSingleton<ISerializer>(svp => new AliasedJsonMessageSerializer(encoding, svp.GetRequiredService<IEnumerable<ISerializationTypeAliasProvider>>()));
        });

        return cfg;
    }
}
