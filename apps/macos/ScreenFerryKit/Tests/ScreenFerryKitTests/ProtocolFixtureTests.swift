import Foundation
import Testing
@testable import ScreenFerryKit

struct ProtocolFixtureTests {
    static let fixturesURL = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent()
        .appending(path: "../../../../../protocol/fixtures")
        .standardizedFileURL

    static func fixtureURLs() throws -> [URL] {
        let enumerator = FileManager.default.enumerator(at: fixturesURL, includingPropertiesForKeys: nil)
        let urls = (enumerator?.allObjects as? [URL] ?? []).filter { $0.pathExtension == "json" }
        return urls.sorted { $0.path < $1.path }
    }

    @Test func fixturesExist() throws {
        #expect(try !Self.fixtureURLs().isEmpty, "No fixtures under \(Self.fixturesURL.path)")
    }

    @Test(arguments: try fixtureURLs())
    func envelopeDecodes(_ url: URL) throws {
        let envelope = try JSONDecoder().decode(ProtocolEnvelope.self, from: Data(contentsOf: url))
        #expect(envelope.protocolVersion >= 0)
        #expect(!envelope.type.isEmpty)
    }
}
