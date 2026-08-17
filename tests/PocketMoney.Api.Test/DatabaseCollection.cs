using Xunit;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace PocketMoney.Api.Test;

[CollectionDefinition("database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
}
