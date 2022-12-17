namespace SlimCluster.Consensus.Raft.Test;

using SlimCluster.Serialization;

public class SerializerMock
{
    private readonly Mock<ISerializer> _mock = new();

    public ISerializer Object => _mock.Object;

    public void SetupSerDes(object obj, byte[]? payload = null)
    {
        payload ??= new byte[] { 1 };

        _mock
            .Setup(x => x.Serialize(obj))
            .Returns(payload);

        _mock
            .Setup(x => x.Deserialize(payload))
            .Returns(obj);
    }
}
