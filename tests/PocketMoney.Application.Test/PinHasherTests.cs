using FluentAssertions;
using PocketMoney.Application;
using Xunit;

namespace PocketMoney.Application.Test;

public class PinHasherTests
{
    [Fact]
    public void Hash_then_Verify_roundtrips()
    {
        var hash = PinHasher.Hash("1234");
        PinHasher.Verify("1234", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_rejects_wrong_pin()
    {
        var hash = PinHasher.Hash("1234");
        PinHasher.Verify("9999", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_is_salted_every_time()
    {
        PinHasher.Hash("1234").Should().NotBe(PinHasher.Hash("1234"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("PBKDF2-SHA256$abc")]
    [InlineData("BCRYPT$310000$x$y")]
    [InlineData(null)]
    public void Verify_rejects_malformed_stored_hash(string? stored)
    {
        PinHasher.Verify("1234", stored!).Should().BeFalse();
    }

    [Fact]
    public void Hash_uses_documented_scheme_format()
    {
        var hash = PinHasher.Hash("0000");
        hash.Split('$').Should().HaveCount(4);
        hash.Should().StartWith("PBKDF2-SHA256$310000$");
    }
}
