namespace SlimCluster.Serialization
{
    public interface ISerializer
    {
        T Deserialize<T>(byte[] paylad) where T : class;
        byte[] Serialize<T>(T msg) where T : class;
    }
}
