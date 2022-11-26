namespace SlimCluster.Membership.Swim;

using SlimCluster.Membership.Swim.Messages;

public class SwimFailureDetector : IAsyncDisposable
{
    private readonly ILogger<SwimFailureDetector> _logger;
    private readonly SwimClusterMembershipOptions _options;
    private readonly IMessageSender _messageSender;
    private readonly IReadOnlyList<SwimMember> _otherMembers;
    private readonly ITime _time;
    private readonly Random _random = new();

    /// <summary>
    /// The current protocol period sequence number.
    /// </summary>
    private long _periodSequenceNumber;

    /// <summary>
    /// The time that the current protocol period ends.
    /// </summary>
    private DateTimeOffset periodTimeout;

    /// <summary>
    /// Timer that performs the checks for the expected timeouts.
    /// </summary>
    private Timer? _periodTimer;

    private SwimMember? pingNode;
    private DateTimeOffset? pingAckTimeout;

    public long PeriodSequenceNumber => Interlocked.Read(ref _periodSequenceNumber);

    private bool _timerMethodRunning;

    public SwimFailureDetector(
        ILogger<SwimFailureDetector> logger,
        SwimClusterMembershipOptions options,
        IMessageSender messageSender,
        IReadOnlyList<SwimMember> otherMembers,
        ITime time)
    {
        _logger = logger;
        _options = options;
        _messageSender = messageSender;
        _otherMembers = otherMembers;
        _time = time;
        AdvancePeriod(time.Now);
        _periodTimer = new Timer((o) => _ = OnTimer(), null, 0, (int)options.PeriodTimerInterval.TotalMilliseconds);
    }

    public async ValueTask DisposeAsync()
    {
        if (_periodTimer != null)
        {
            _periodTimer.Change(Timeout.Infinite, Timeout.Infinite);

            await _periodTimer.DisposeAsync();
            _periodTimer = null;
        }

        GC.SuppressFinalize(this);
    }

    public async Task OnTimer()
    {
        if (_timerMethodRunning)
        {
            // another run is happening at the moment - prevent from rentering
            return;
        }

        _timerMethodRunning = true;
        try
        {
            await DoRun();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error occured at protocol period timer loop");
        }
        finally
        {
            _timerMethodRunning = false;
        }
    }

    private async Task DoRun()
    {
        var now = _time.Now;
        if (now >= periodTimeout)
        {
            // Start a new period
            await OnNewPeriod();
        }
        else
        {
            if (pingAckTimeout != null && now >= pingAckTimeout.Value)
            {
                pingAckTimeout = null;
                await OnPingTimeout();
            }
        }
    }

    private void AdvancePeriod(DateTimeOffset now)
    {
        periodTimeout = now.Add(_options.ProtocolPeriod);
        Interlocked.Increment(ref _periodSequenceNumber);

        _logger.LogDebug("Started period {PeriodSequenceNumber} and timeout on {PeriodTimeout:s}", PeriodSequenceNumber, periodTimeout);
    }

    protected IList<SwimMember> ActiveMembers => _otherMembers.Where(x => x.Status == SwimMemberStatus.Active).ToList();

    private Task OnPingTimeout()
    {
        // get the active members only
        var activeMembers = ActiveMembers;
        if (activeMembers.Count == 0)
        {
            // no active members
            return Task.CompletedTask;
        }

        // choose a subgroup of active members
        var selectedMembers = new List<SwimMember>(_options.FailureDetectionSubgroupSize);
        for (var i = _options.FailureDetectionSubgroupSize; i > 0 && activeMembers.Count > 0; i--)
        {
            var selectedMemberIndex = _random.Next(activeMembers.Count);
            var selectedMember = activeMembers[selectedMemberIndex];

            activeMembers.RemoveAt(selectedMemberIndex);
        }

        if (pingNode == null)
        {
            // in case this would have changed
            return Task.CompletedTask;
        }

        var targetNodeAddress = pingNode.Address.ToString();

        Task SendPingReq(SwimMember member)
        {
            var message = new NodeMessage
            {
                PingReq = new PingReqMessage
                {
                    PeriodSequenceNumber = PeriodSequenceNumber,
                    NodeAddress = targetNodeAddress,
                }
            };
            return _messageSender.SendMessage(message, member.Address.EndPoint);
        }

        // send the ping-req to them (in pararell)
        return Task.WhenAll(selectedMembers.Select(m => SendPingReq(m)));
    }

    private async Task OnNewPeriod()
    {
        var now = _time.Now;

        // Declare node that did not recieve an Ack for the Ping as Failed
        if (pingNode != null)
        {
            if (pingNode.Status == SwimMemberStatus.Confirming)
            {
                // When the node Ack did not arrive (via direct ping or via inderect ping-req) then declare this node as unhealty
                pingNode.OnFaulted();

                _logger.LogInformation("Node {NodeId} was declared as {NodeStatus} - ack message for ping (direct or indirect) did not arrive in time for period {PeriodSequenceNumber}", pingNode.Id, pingNode.Status, PeriodSequenceNumber);
            }
        }

        AdvancePeriod(now);

        await SelectMemberForPing(now);
    }

    private Task SelectMemberForPing(DateTimeOffset now)
    {
        var activeMembers = ActiveMembers;
        if (activeMembers.Count == 0)
        {
            // nothing to available to select
            pingNode = null;
            pingAckTimeout = null;
            return Task.CompletedTask;
        }

        // select random node for ping
        var i = _random.Next(activeMembers.Count);

        // store the selected node ID
        pingNode = activeMembers[i];
        // expect an ack after the specified timeout
        pingAckTimeout = now.Add(_options.PingAckTimeout);

        pingNode.OnConfirming(periodTimeout);

        _logger.LogDebug("Node {NodeId} was selected for failure detecton at period {PeriodSequenceNumber} - ping message will be sent", pingNode.Id, PeriodSequenceNumber);

        var message = new NodeMessage
        {
            Ping = new PingMessage
            {
                PeriodSequenceNumber = _periodSequenceNumber,
            }
        };
        return _messageSender.SendMessage(message, pingNode.Address.EndPoint);
    }

    public Task OnPingAckArrived(AckMessage m, IPEndPoint senderEndPoint)
    {
        var node = _otherMembers.SingleOrDefault(x => x.Id == m.NodeId);
        if (node != null)
        {
            if (PeriodSequenceNumber != m.PeriodSequenceNumber)
            {
                _logger.LogDebug("Ack arrived too late for the node {NodeId}, period {PeriodSequenceNumber}, while the Ack message was for period {AckPeriodSequenceNumber}", m.NodeId, PeriodSequenceNumber, m.PeriodSequenceNumber);
            }
            else
            {
                _logger.LogDebug("Ack arrived for the node {NodeId}, period {PeriodSequenceNumber}, node status {NodeStatus}", m.NodeId, PeriodSequenceNumber, node.Status);
                node.OnActive(_time);
            }
        }

        return Task.CompletedTask;
    }
}
