import Foundation
import Testing
@testable import ScreenFerryKit

struct MonitorIdentityVectorTests {
    struct Vector: Decodable, CustomTestStringConvertible {
        let description: String
        let edid: String
        let identity: String

        var testDescription: String { description }

        var bytes: [UInt8] {
            stride(from: 0, to: edid.count, by: 2).map { offset in
                let start = edid.index(edid.startIndex, offsetBy: offset)
                return UInt8(edid[start..<edid.index(start, offsetBy: 2)], radix: 16)!
            }
        }
    }

    static func vectors() throws -> [Vector] {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .appending(path: "../../../../../protocol/test-vectors/monitor-identity.json")
            .standardizedFileURL
        return try JSONDecoder().decode([Vector].self, from: Data(contentsOf: url))
    }

    @Test(arguments: try vectors())
    func matchesSharedVector(_ vector: Vector) throws {
        #expect(try EDID(vector.bytes).identity == vector.identity)
    }
}
