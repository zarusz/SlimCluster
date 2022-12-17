namespace SlimCluster.Test;

using SlimCluster.Host.Common;

public class TaskLoopTests
{
    private readonly Mock<TaskLoop> subjectMock;

    public TaskLoopTests()
    {
        subjectMock = new Mock<TaskLoop>(NullLogger.Instance) { CallBase = true };
    }

    [Fact]
    public void Given_NewTaskLoop_Then_NothingHappens()
    {
        // act

        // assert
        subjectMock.Verify(x => x.OnLoopRun(It.IsAny<CancellationToken>()), Times.Never);

        subjectMock.Verify(x => x.OnStarting(), Times.Never);
        subjectMock.Verify(x => x.OnStarted(), Times.Never);

        subjectMock.Verify(x => x.OnStopping(), Times.Never);
        subjectMock.Verify(x => x.OnStopped(), Times.Never);
    }

    [Fact]
    public async Task When_Start_Then_OnStaring_And_OnStarted_And_OnLoopRun_IsCalled()
    {
        // arrange

        // act
        await subjectMock.Object.Start();

        // assert
        subjectMock.Verify(x => x.OnLoopRun(It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        subjectMock.Verify(x => x.OnStarting(), Times.Once);
        subjectMock.Verify(x => x.OnStarted(), Times.Once);
    }

    [Fact]
    public async Task When_Stop_Then_OnStopping_And_OnStopped_And_OnLoopRun_IsNotCalled()
    {
        // arrange
        await subjectMock.Object.Start();

        await Task.Delay(200);

        // act
        await subjectMock.Object.Stop();
        subjectMock.Verify(x => x.OnLoopRun(It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // assert        
        subjectMock.Verify(x => x.OnStopping(), Times.Once);
        subjectMock.Verify(x => x.OnStopped(), Times.Once);
    }
}
