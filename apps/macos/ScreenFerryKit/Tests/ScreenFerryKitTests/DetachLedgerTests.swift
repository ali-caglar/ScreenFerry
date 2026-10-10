import Foundation
import Testing
@testable import ScreenFerryKit

struct DetachLedgerTests {
    static func temporaryStore() -> DetachLedgerStore {
        DetachLedgerStore(url: FileManager.default.temporaryDirectory
            .appending(path: "screenferry-tests-\(UUID().uuidString)/ledger.json"))
    }

    @Test func missingFileLoadsEmpty() throws {
        #expect(try Self.temporaryStore().load() == DetachLedger())
    }

    @Test func roundTripsThroughDisk() throws {
        let store = Self.temporaryStore()
        let entry = DetachLedger.Entry(detachedAt: Date(timeIntervalSince1970: 1_800_000_000), platformData: "2")
        try store.update { $0.monitors["SAM-E030-H1AK500000"] = entry }
        #expect(try store.load().monitors == ["SAM-E030-H1AK500000": entry])
        try store.update { $0.monitors["SAM-E030-H1AK500000"] = nil }
        #expect(try store.load().monitors.isEmpty)
    }

    @Test func waitsOutTheRemainingAbsence() {
        let detached = Date(timeIntervalSince1970: 1_000)
        #expect(MinimumAbsence.remainingWait(since: detached, now: detached.addingTimeInterval(10), minimum: 25) == 15)
        #expect(MinimumAbsence.remainingWait(since: detached, now: detached.addingTimeInterval(40), minimum: 25) == 0)
        #expect(MinimumAbsence.remainingWait(since: nil, now: detached, minimum: 25) == 0)
        #expect(MinimumAbsence.remainingWait(since: detached, now: detached, minimum: 0) == 0)
    }
}
