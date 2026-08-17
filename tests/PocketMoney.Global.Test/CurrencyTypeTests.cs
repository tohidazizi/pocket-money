using Xunit;

namespace PocketMoney.Global.Test;

public sealed class CurrencyTypeTests
{
    [Fact]
    public void CurrencyType_Test()
    {
        // Arrange
        const string usDollarKey = "USD";

        // Act
        var currencyType = CurrencyType.Parse(usDollarKey);

        // Assert
        Assert.NotNull(currencyType);
        Assert.Equal(usDollarKey, currencyType.Key);
        Assert.Equal("US", currencyType.Country);
        Assert.Equal(2, currencyType.DecimalDigits);
    }
}
