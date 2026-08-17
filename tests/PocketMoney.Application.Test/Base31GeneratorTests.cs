using FluentAssertions;
using PocketMoney.Application;
using PocketMoney.Global;
using Xunit;

namespace PocketMoney.Application.Test;

public class Base31GeneratorTests
{
    [Fact]
    public void GenerateAccountId_produces_5_valid_characters()
    {
        for (var i = 0; i < 200; i++)
        {
            var id = Base31Generator.GenerateAccountId();
            id.Should().HaveLength(5);
            Base31Generator.IsValid(id).Should().BeTrue();
        }
    }

    [Fact]
    public void Generated_ids_never_contain_ambiguous_characters()
    {
        for (var i = 0; i < 500; i++)
        {
            Base31Generator.GenerateAccountId()
                .Should().NotContainAny("O", "I", "S", "U", "Q");
        }
    }

    [Theory]
    [InlineData("MJ74K", true)]
    [InlineData("mj74k", false)]   // lowercase invalid (normalized upstream)
    [InlineData("MJ74", false)]     // too short
    [InlineData("MJ74KS", false)]   // too long
    [InlineData("MJ7O5", false)]    // O excluded
    [InlineData("", false)]
    public void IsValid_matches_spec(string input, bool expected)
    {
        Base31Generator.IsValid(input).Should().Be(expected);
    }

    [Fact]
    public void IsValid_rejects_null()
    {
        Base31Generator.IsValid(null).Should().BeFalse();
    }
}
