import AppKit
import ScreenFerryKit
import ScreenFerryNet
import SwiftUI

public struct MenuContent: View {
    @ObservedObject var controller: AgentController

    public init(controller: AgentController) {
        self.controller = controller
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if let agent = controller.agent {
                AgentView(agent: agent)
            } else {
                Text("ScreenFerry couldn't start").font(.headline)
                Text(controller.startError ?? "").font(.caption).foregroundStyle(.secondary)
            }
            Divider()
            Button("Quit ScreenFerry") { NSApplication.shared.terminate(nil) }
                .keyboardShortcut("q")
        }
        .padding(14)
        .frame(width: 320)
    }
}

public struct MenuBarIcon: View {
    @ObservedObject var controller: AgentController

    public init(controller: AgentController) {
        self.controller = controller
    }

    public var body: some View {
        if let agent = controller.agent {
            AgentIcon(agent: agent)
        } else {
            Image(systemName: "exclamationmark.triangle")
        }
    }
}

private struct AgentIcon: View {
    @ObservedObject var agent: Agent

    var body: some View {
        Image(systemName: agent.pairingPrompt == nil ? "display.2" : "key.horizontal")
    }
}

private struct AgentView: View {
    @ObservedObject var agent: Agent

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text("ScreenFerry").font(.headline)
            Text(agent.name).font(.caption).foregroundStyle(.secondary)
        }

        if let prompt = agent.pairingPrompt {
            PairingPromptView(prompt: prompt) { agent.confirmPairing($0) }
        }

        let paired = agent.peers.filter(\.isPaired)
        if !paired.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                Text("Computers").font(.subheadline.weight(.semibold))
                ForEach(paired) { peer in
                    PeerRow(peer: peer)
                        .contextMenu {
                            Button("Forget \(peer.name)") { agent.unpair(peer.id) }
                        }
                }
            }
        }

        if agent.isPairingMode {
            PairingModeView(agent: agent)
        } else if agent.pairingPrompt == nil {
            Button("Add Computer…") { agent.setPairingMode(true) }
        }

        if let result = agent.pairingResult {
            Text(result).font(.caption).foregroundStyle(.secondary)
        }
        if let error = agent.listenerError {
            Text("Network: \(error)").font(.caption).foregroundStyle(.red)
        }
    }
}

private struct PeerRow: View {
    let peer: Agent.Peer

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 6) {
                Circle().fill(peer.isConnected ? Color.green : Color.secondary.opacity(0.4)).frame(width: 7, height: 7)
                Text(peer.name)
                Spacer()
                Text(peer.isConnected ? "Connected" : "Offline").font(.caption).foregroundStyle(.secondary)
            }
            ForEach(peer.monitors, id: \.identity) { monitor in
                HStack {
                    Image(systemName: "display").foregroundStyle(.secondary)
                    Text(monitor.name ?? monitor.identity).font(.caption)
                    Spacer()
                    Text(monitor.attached ? "in use" : "released").font(.caption2).foregroundStyle(.secondary)
                }
                .padding(.leading, 13)
            }
        }
    }
}

private struct PairingModeView: View {
    @ObservedObject var agent: Agent

    var body: some View {
        let pairable = agent.peers.filter { !$0.isPaired && $0.isPairable }
        VStack(alignment: .leading, spacing: 8) {
            Text("Add Computer").font(.subheadline.weight(.semibold))
            Text("Open “Add Computer” in ScreenFerry on the other computer too.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            if pairable.isEmpty {
                HStack(spacing: 6) {
                    ProgressView().controlSize(.small)
                    Text("Looking for computers…").font(.caption)
                }
            }
            ForEach(pairable) { peer in
                HStack {
                    Image(systemName: "desktopcomputer")
                    Text(peer.name)
                    Spacer()
                    Button("Pair") { agent.pair(with: peer.id) }
                }
            }
            Button("Cancel") { agent.setPairingMode(false) }
        }
    }
}

private struct PairingPromptView: View {
    let prompt: Agent.PairingPrompt
    let decide: (Bool) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Pair with \(prompt.peerName)?").font(.subheadline.weight(.semibold))
            Text("Check that \(prompt.peerName) shows the same code.")
                .font(.caption).foregroundStyle(.secondary)
            Text("\(prompt.code.prefix(3)) \(prompt.code.suffix(3))")
                .font(.system(size: 28, weight: .semibold, design: .monospaced))
                .frame(maxWidth: .infinity)
            HStack {
                Button("Codes Differ", role: .cancel) { decide(false) }
                Spacer()
                Button("Codes Match") { decide(true) }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.accentColor.opacity(0.1)))
    }
}
