using Aslan.Naps.Protocol;

namespace Aslan.Naps.Tests;

public class LtvProtocolTests
{
    [Fact]
    public void Tlv_EncodesCorrectly()
    {
        Assert.Equal("001003009", LtvProtocol.Tlv("001", "009"));
        Assert.Equal("002012000000001000", LtvProtocol.Tlv("002", "000000001000"));
        Assert.Equal("01400814052026", LtvProtocol.Tlv("014", "14052026"));
    }

    [Fact]
    public void Tlv_EmptyValue()
    {
        Assert.Equal("010000", LtvProtocol.Tlv("010", ""));
    }

    [Fact]
    public void ParseMessage_BasicMessage()
    {
        var fields = LtvProtocol.ParseMessage("001003109013003000!");
        Assert.Equal("109", fields["001"]);
        Assert.Equal("000", fields["013"]);
    }

    [Fact]
    public void ParseMessage_PaymentResponse()
    {
        var msg = "001003101013003000008006123456009006AB1234007016516794******3315!";
        var fields = LtvProtocol.ParseMessage(msg);
        Assert.Equal("101", fields["001"]);
        Assert.Equal("000", fields["013"]);
        Assert.Equal("123456", fields["008"]);
        Assert.Equal("AB1234", fields["009"]);
        Assert.Equal("516794******3315", fields["007"]);
    }

    [Fact]
    public void ParseMessage_WithoutTerminator()
    {
        var fields = LtvProtocol.ParseMessage("001003109013003000");
        Assert.Equal("109", fields["001"]);
        Assert.Equal("000", fields["013"]);
    }

    [Fact]
    public void ParseMessage_EmptyString()
    {
        var fields = LtvProtocol.ParseMessage("");
        Assert.Empty(fields);
    }

    [Fact]
    public void ParseMessage_TruncatedField()
    {
        // Length says 10 but only 3 chars available
        var fields = LtvProtocol.ParseMessage("001010abc");
        Assert.Empty(fields);
    }

    [Fact]
    public void BuildMessage_NetworkTest_UsesConnectionFraming()
    {
        var msg = LtvProtocol.BuildMessage("009");
        Assert.StartsWith("001003009", msg);
        Assert.False(msg.EndsWith("!", StringComparison.Ordinal));
        Assert.Contains("003007", msg); // NCAI tag
        Assert.Contains("004006", msg); // NS tag
    }

    [Fact]
    public void BuildMessage_Payment()
    {
        var msg = LtvProtocol.BuildMessage("001", amountCentimes: 1000);
        Assert.StartsWith("001003001", msg);
        Assert.Contains("0020041000", msg); // 10.00 MAD in centimes
        Assert.Contains("012003504", msg); // Currency MAD
        Assert.False(msg.EndsWith("!", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildConfirmation_IncludesStan()
    {
        var msg = LtvProtocol.BuildConfirmation("654321");
        Assert.StartsWith("001003002", msg); // TM=002
        Assert.Contains("008006654321", msg); // STAN
        Assert.False(msg.EndsWith("!", StringComparison.Ordinal));
    }

    [Fact]
    public void IsApproved_AcceptsAllCodes()
    {
        Assert.True(LtvProtocol.IsApproved("000"));
        Assert.True(LtvProtocol.IsApproved("001"));
        Assert.True(LtvProtocol.IsApproved("003"));
        Assert.True(LtvProtocol.IsApproved("007"));
    }

    [Fact]
    public void IsApproved_RejectsDeclined()
    {
        Assert.False(LtvProtocol.IsApproved("100"));
        Assert.False(LtvProtocol.IsApproved("116"));
        Assert.False(LtvProtocol.IsApproved("909"));
        Assert.False(LtvProtocol.IsApproved(null));
    }

    [Fact]
    public void MaskPan_StandardCard()
    {
        Assert.Equal("516794******3315", LtvProtocol.MaskPan("5167941234563315"));
    }

    [Fact]
    public void MaskPan_ShortNumber()
    {
        Assert.Equal("12345", LtvProtocol.MaskPan("12345"));
    }

    [Fact]
    public void MaskPan_AlreadyMasked()
    {
        Assert.Equal("516794******3315", LtvProtocol.MaskPan("516794******3315"));
    }

    [Fact]
    public void ExtractLastMessage_SingleMessage()
    {
        Assert.Equal("001003109!", LtvProtocol.ExtractLastMessage("001003109!"));
    }

    [Fact]
    public void ExtractLastMessage_MultipleMessages()
    {
        Assert.Equal("013003000!", LtvProtocol.ExtractLastMessage("001003109!013003000!"));
    }

    [Fact]
    public void ExtractLastMessage_Empty()
    {
        Assert.Equal("", LtvProtocol.ExtractLastMessage(""));
    }

    [Fact]
    public void RoundTrip_BuildThenParse()
    {
        var msg = LtvProtocol.BuildMessage("001", ncai: "0200005", ns: "000042", amountCentimes: 5000);
        var fields = LtvProtocol.ParseMessage(msg);
        Assert.Equal("001", fields["001"]);
        Assert.Equal("5000", fields["002"]);
        Assert.Equal("0200005", fields["003"]);
        Assert.Equal("000042", fields["004"]);
        Assert.Equal("504", fields["012"]);
    }
}

public class BinaryTlvProtocolTests
{
    [Fact]
    public void BuildRequest_SaleTransaction()
    {
        var data = BinaryTlvProtocol.BuildRequest("01", "1000", "ORDER001");
        Assert.True(data.Length > 0);
        // First byte should be tag 0x01
        Assert.Equal(0x01, data[0]);
    }

    [Fact]
    public void WrapWithLength_AddsPrefix()
    {
        var data = new byte[] { 1, 2, 3 };
        var wrapped = BinaryTlvProtocol.WrapWithLength(data);
        Assert.Equal(5, wrapped.Length);
        Assert.Equal(0, wrapped[0]); // High byte of length 3
        Assert.Equal(3, wrapped[1]); // Low byte of length 3
    }

    [Fact]
    public void ParseResponse_RoundTrip()
    {
        var request = BinaryTlvProtocol.BuildRequest("01", "5000", "REF123");
        var fields = BinaryTlvProtocol.ParseResponse(request);
        Assert.Equal("01", fields[0x01]);
        Assert.Equal("5000", fields[0x02]);
        Assert.Equal("REF123", fields[0x03]);
    }
}

public class PortDiscoveryTests
{
    [Fact]
    public void ListPorts_ReturnsArray()
    {
        var ports = PortDiscovery.ListPorts();
        Assert.NotNull(ports);
        // May be empty if no ports connected — that's fine
    }

    [Fact]
    public void FindIngenicoPort_ReturnsNullOrString()
    {
        var port = PortDiscovery.FindIngenicoPort();
        // May be null if no Ingenico connected — that's fine
        // Just verify it doesn't throw
    }
}
