namespace SlimCluster.Serialization;

public interface ISerializer
{
    object Deserialize(byte[] paylad);
    byte[] Serialize(object msg);
}
