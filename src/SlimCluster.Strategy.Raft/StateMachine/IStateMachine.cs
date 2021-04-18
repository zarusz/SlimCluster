using System.Collections.Generic;
using System.Threading.Tasks;

namespace SlimCluster.Strategy.Raft
{
    public interface IStateMachine
    {
        Task Apply(IEnumerable<object> commands);

        // ToDo: Rebuild from snapshot
    }
}
