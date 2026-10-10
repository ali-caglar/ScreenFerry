import Foundation
import Testing
@testable import ScreenFerryKit

struct ProtocolMessageTests {
    static func json(_ data: Data) throws -> NSDictionary {
        try #require(try JSONSerialization.jsonObject(with: data) as? NSDictionary)
    }

    @Test(arguments: try ProtocolFixtureTests.fixtureURLs())
    func fixtureRoundTrips(_ url: URL) throws {
        let data = try Data(contentsOf: url)
        let message = try JSONDecoder().decode(ProtocolMessage.self, from: data)
        if case .unknown = message { Issue.record("\(url.lastPathComponent) decoded as unknown") }
        #expect(try Self.json(JSONEncoder().encode(message)) == Self.json(data))
    }

    @Test func unknownTypeIsKept() throws {
        let message = try JSONDecoder().decode(ProtocolMessage.self, from: Data(#"{"protocolVersion":3,"type":"scene.future","x":1}"#.utf8))
        #expect(message == .unknown(type: "scene.future"))
    }

    @Test func rejectsMissingFieldsAndBadHex() {
        for json in [
            #"{"protocolVersion":0,"type":"pair.confirm"}"#,
            #"{"protocolVersion":0,"type":"pair.nonce","nonce":"00"}"#,
            #"{"protocolVersion":0,"type":"pair.nonce","nonce":"\#(String(repeating: "A", count: 64))"}"#,
        ] {
            #expect(throws: DecodingError.self) { try JSONDecoder().decode(ProtocolMessage.self, from: Data(json.utf8)) }
        }
    }

    @Test func framerReassemblesSplitInput() throws {
        let stream = try MessageFramer.frame(.ping) + MessageFramer.frame(.pairConfirm(accepted: true))
        var framer = MessageFramer()
        var messages = [ProtocolMessage]()
        for byte in stream {
            for frame in try framer.append(Data([byte])) {
                messages.append(try JSONDecoder().decode(ProtocolMessage.self, from: frame))
            }
        }
        #expect(messages == [.ping, .pairConfirm(accepted: true)])
    }

    @Test func framerRejectsOversizedLength() {
        var framer = MessageFramer()
        #expect(throws: MessageFramer.FramingError.tooLong(0x0100_0000)) { try framer.append(Data([1, 0, 0, 0])) }
    }
}
