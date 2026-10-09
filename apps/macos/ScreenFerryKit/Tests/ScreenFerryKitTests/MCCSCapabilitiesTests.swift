import Testing
@testable import ScreenFerryKit

struct MCCSCapabilitiesTests {
    @Test func parsesVCPCodesAndValues() {
        let caps = MCCSCapabilities(
            "(prot(monitor)type(lcd)cmds(01 02 03 07 0C E3 F3)vcp(02 10 12 14(05 08 0B) 60(0F 11 12) D6(01 04 05) DF)mccs_ver(2.1))"
        )
        #expect(caps.vcp[0x60] == [0x0F, 0x11, 0x12])
        #expect(caps.vcp[0x14] == [0x05, 0x08, 0x0B])
        #expect(caps.vcp[0x10] == [])
        #expect(caps.vcp[0xDF] == [])
        #expect(caps.vcp[0xE3] == nil)
        #expect(caps.vcp.count == 7)
    }

    @Test func toleratesMissingOrUnterminatedSections() {
        #expect(MCCSCapabilities("(prot(monitor))").vcp.isEmpty)
        #expect(MCCSCapabilities("vcp(10 60(11 12").vcp[0x60] == [0x11, 0x12])
    }

    @Test func namesStandardInputs() {
        #expect(MCCSCapabilities.inputSourceName(0x0F) == "DisplayPort 1")
        #expect(MCCSCapabilities.inputSourceName(0x11) == "HDMI 1")
        #expect(MCCSCapabilities.inputSourceName(0x99) == nil)
    }
}
