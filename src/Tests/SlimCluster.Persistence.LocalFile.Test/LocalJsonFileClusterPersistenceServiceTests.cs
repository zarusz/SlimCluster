namespace SlimCluster.Persistence.LocalFile.Test;

public class LocalJsonFileClusterPersistenceServiceTests
{
    public class SomeDurableComponent : IDurableComponent
    {
        private readonly Fixture _fixture;

        public string StringValue { get; protected set; } = string.Empty;
        public DateTime DateTimeValue { get; protected set; }
        public IDictionary<string, string> DictionaryValue { get; protected set; }
        public IList<string> ListValue { get; protected set; }

        public SomeDurableComponent()
        {
            _fixture = new Fixture();

            StringValue = _fixture.Create<string>();
            DateTimeValue = _fixture.Create<DateTime>().ToUniversalTime();
            DictionaryValue = _fixture.Create<IDictionary<string, string>>();
            ListValue = _fixture.CreateMany<string>(10).ToList();
        }

        public void OnStatePersist(IStateWriter state)
        {
            state.Set(nameof(StringValue), StringValue);
            state.Set(nameof(DateTimeValue), DateTimeValue);
            state.Set(nameof(DictionaryValue), DictionaryValue);
            state.Set(nameof(ListValue), ListValue);
        }

        public void OnStateRestore(IStateReader state)
        {
            StringValue = state.Get<string>(nameof(StringValue))!;
            DateTimeValue = state.Get<DateTime>(nameof(DateTimeValue));
            DictionaryValue = state.Get<IDictionary<string, string>>(nameof(DictionaryValue))!;
            ListValue = state.Get<IList<string>>(nameof(ListValue))!;
        }
    }

    [Fact]
    public async Task When_Restore_Given_DurableComponentInContainer_Then_RestoresState()
    {
        // arrange
        var services = new ServiceCollection();

        var tempFilePath = Path.Combine(Path.GetTempPath(), "cluster-state.json");
        services.AddSlimCluster(cfg =>
        {
            cfg.AddPersistenceUsingLocalFile(tempFilePath, Newtonsoft.Json.Formatting.Indented);
        });

        services.AddScoped<SomeDurableComponent>();
        services.AddTransient<IDurableComponent>(svp => svp.GetRequiredService<SomeDurableComponent>());

        using var serviceProvider = services.BuildServiceProvider();

        using var scopeA = serviceProvider.CreateScope();
        var clusterPeristenceServiceA = scopeA.ServiceProvider.GetRequiredService<IClusterPersistenceService>();
        var durableComponentA = scopeA.ServiceProvider.GetRequiredService<SomeDurableComponent>();

        using var scopeB = serviceProvider.CreateScope();
        var clusterPeristenceServiceB = scopeB.ServiceProvider.GetRequiredService<IClusterPersistenceService>();
        var durableComponentB = scopeB.ServiceProvider.GetRequiredService<SomeDurableComponent>();

        // act
        await clusterPeristenceServiceA.Persist(default);
        await clusterPeristenceServiceB.Restore(default);

        // assert
        durableComponentB.Should().NotBeSameAs(durableComponentA);

        durableComponentB.StringValue.Should().Be(durableComponentA.StringValue);
        durableComponentB.DateTimeValue.Should().Be(durableComponentA.DateTimeValue);
        durableComponentB.DictionaryValue.Should().BeEquivalentTo(durableComponentA.DictionaryValue);
        durableComponentB.ListValue.Should().BeEquivalentTo(durableComponentA.ListValue);
    }
}
