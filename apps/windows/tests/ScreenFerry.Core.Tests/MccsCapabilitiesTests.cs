using ScreenFerry.Core;

namespace ScreenFerry.Core.Tests;

public class MccsCapabilitiesTests
{
    [Fact]
    public void ParsesVcpCodesAndValues()
    {
        var caps = new MccsCapabilities(
            "(prot(monitor)type(lcd)cmds(01 02 03 07 0C E3 F3)vcp(02 10 12 14(05 08 0B) 60(0F 11 12) D6(01 04 05) DF)mccs_ver(2.1))");

        Assert.Equal([0x0F, 0x11, 0x12], caps.Vcp[0x60]);
        Assert.Equal([0x05, 0x08, 0x0B], caps.Vcp[0x14]);
        Assert.Empty(caps.Vcp[0x10]);
        Assert.False(caps.Vcp.ContainsKey(0xE3));
        Assert.Equal(7, caps.Vcp.Count);
    }

    [Fact]
    public void ToleratesMissingOrUnterminatedSections()
    {
        Assert.Empty(new MccsCapabilities("(prot(monitor))").Vcp);
        Assert.Equal([0x11, 0x12], new MccsCapabilities("vcp(10 60(11 12").Vcp[0x60]);
    }

    [Fact]
    public void NamesStandardInputs()
    {
        Assert.Equal("DisplayPort 1", MccsCapabilities.InputSourceName(0x0F));
        Assert.Equal("HDMI 1", MccsCapabilities.InputSourceName(0x11));
        Assert.Null(MccsCapabilities.InputSourceName(0x99));
    }
}
