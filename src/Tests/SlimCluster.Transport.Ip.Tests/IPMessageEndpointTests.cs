namespace SlimCluster.Transport.Ip.Test;

public class IPMessageEndpointTests
{
    private readonly IpTransportOptions _options;
    private readonly Mock<ISerializer> _serializerMock;
    private readonly Mock<IMessageSendingHandler> _messageSendingHandlerMock;
    private readonly Mock<IMessageArrivedHandler> _messageArrivedHandlerMock;
    private readonly TestSocketClient _socketClient;

    public IPMessageEndpointTests()
    {
        _options = new IpTransportOptions();
        _serializerMock = new Mock<ISerializer>();
        _messageSendingHandlerMock = new Mock<IMessageSendingHandler>();
        _messageArrivedHandlerMock = new Mock<IMessageArrivedHandler>();
        _socketClient = new TestSocketClient();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task When_SendRequest_And_ResponseArrives_Then_ResponseIsReturned(bool responseWithMatchingRequestIdArrived)
    {
        var request = new SomeRequest();
        var response = new SomeResponse
        {
            RequestId = responseWithMatchingRequestIdArrived
                ? request.RequestId
                : Guid.NewGuid()
        };

        var requestPayload = new byte[] { 1 };
        var responsePayload = new byte[] { 2 };

        _serializerMock.Setup(x => x.Serialize(request)).Returns(requestPayload);
        _serializerMock.Setup(x => x.Deserialize(requestPayload)).Returns(request);

        _serializerMock.Setup(x => x.Serialize(response)).Returns(responsePayload);
        _serializerMock.Setup(x => x.Deserialize(responsePayload)).Returns(response);

        var remoteAddress = IPEndPointAddress.Parse("192.168.1.2:9999");
        var selfEndPoint = IPEndPointAddress.Parse("192.168.1.1:9999");

        await using var subject = new IPMessageEndpoint(
            NullLogger<IPMessageEndpoint>.Instance,
            Options.Create(_options),
            _serializerMock.Object,
            new[] { _messageSendingHandlerMock.Object },
            new[] { _messageArrivedHandlerMock.Object },
            () => _socketClient);

        await subject.Start();

        var timeout = TimeSpan.FromMilliseconds(250);

        // response arrived from remote endpoint
        _socketClient.OnMessageSend(async (endPoint, payload) =>
        {
            // Send response for request            
            if (IPEndPointAddress.From(endPoint).Equals(remoteAddress) && payload == requestPayload)
            {
                await _socketClient.OnMessageArrived(endPoint, responsePayload);
            }
        });

        // act
        var responseTask = subject.SendRequest(request, remoteAddress, timeout);

        // assert

        // wait 2x more than the timeout just in case
        var finishedTask = await Task.WhenAny(Task.Delay(timeout.Multiply(2)), responseTask);
        finishedTask.Should().Be(responseTask);

        if (responseWithMatchingRequestIdArrived)
        {
            // completed
            responseTask.IsCompleted.Should().BeTrue();
            responseTask.IsCanceled.Should().BeFalse();
            responseTask.IsFaulted.Should().BeFalse();
            responseTask.Result.Should().Be(response);
        }
        else
        {
            // canceled
            responseTask.IsCompleted.Should().BeTrue();
            responseTask.IsCanceled.Should().BeTrue();
        }

        subject.RequestCount.Should().Be(0);
    }

    internal class SomeRequest : IRequest<SomeResponse>
    {
        public Guid RequestId { get; set; } = Guid.NewGuid();
    }

    internal class SomeResponse : IResponse
    {
        public Guid RequestId { get; set; } = Guid.NewGuid();
    }
}


