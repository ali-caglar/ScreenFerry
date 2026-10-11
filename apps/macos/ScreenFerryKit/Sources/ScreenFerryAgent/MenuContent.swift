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
                AgentView(agent: agent, controller: controller)
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
    @ObservedObject var controller: AgentController

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
            ScenesView(controller: controller)
            HandoffStatusView(agent: agent, controller: controller)
        }
        if !controller.ambiguousMonitors.isEmpty {
            Text("Two connected monitors report the same identity (\(controller.ambiguousMonitors.joined(separator: ", "))), so ScreenFerry can't tell them apart and leaves them out.")
                .font(.caption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
        }
        if !paired.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                Text("Computers").font(.subheadline.weight(.semibold))
                PeerRow(name: "\(agent.name) (this Mac)", status: nil, monitors: controller.localMonitors)
                ForEach(paired) { peer in
                    PeerRow(name: peer.name, status: peer.isConnected, monitors: peer.monitors)
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

private struct ScenesView: View {
    @ObservedObject var controller: AgentController
    @State private var isEditing = false

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Scenes").font(.subheadline.weight(.semibold))
            ForEach(controller.scenes) { entry in
                Button {
                    controller.activate(entry)
                } label: {
                    HStack {
                        Image(systemName: entry.isBuiltIn ? "rectangle.2.swap" : "rectangle.3.group")
                        Text(entry.scene.name)
                        Spacer()
                        if let hotkey = entry.hotkey {
                            Text("⌃⌥⌘\(hotkey)").font(.caption.monospaced()).foregroundStyle(.secondary)
                        }
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .disabled(!entry.isAvailable)
                .contextMenu {
                    if !entry.isBuiltIn {
                        Button("Delete \(entry.scene.name)") { controller.deleteScene(entry.id) }
                    }
                }
            }
            if isEditing {
                SceneEditor(controller: controller) { isEditing = false }
            } else {
                Button("New Scene…") { isEditing = true }
            }
            if let error = controller.sceneError {
                Text(error).font(.caption).foregroundStyle(.red)
            }
        }
    }
}

private struct SceneEditor: View {
    @ObservedObject var controller: AgentController
    let close: () -> Void
    @State private var name = ""
    @State private var owners: [String: String] = [:]

    var body: some View {
        let shared = controller.knownMonitors.filter { $0.seenBy.count > 1 }
        VStack(alignment: .leading, spacing: 8) {
            TextField("Scene name", text: $name)
            if shared.isEmpty {
                Text("No monitor is shared with a connected computer yet.").font(.caption).foregroundStyle(.secondary)
            }
            ForEach(shared) { monitor in
                Picker(monitor.name, selection: Binding(
                    get: { owners[monitor.identity] ?? "" },
                    set: { owners[monitor.identity] = $0.isEmpty ? nil : $0 }
                )) {
                    Text("Leave as is").tag("")
                    ForEach(controller.computers.filter { monitor.seenBy.contains($0.id) }, id: \.id) { computer in
                        Text(computer.name).tag(computer.id)
                    }
                }
            }
            HStack {
                Button("Cancel", role: .cancel, action: close)
                Spacer()
                Button("Save") {
                    controller.addScene(name: name.trimmingCharacters(in: .whitespaces), owners: owners)
                    close()
                }
                .keyboardShortcut(.defaultAction)
                .disabled(name.trimmingCharacters(in: .whitespaces).isEmpty || owners.isEmpty)
            }
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.secondary.opacity(0.1)))
    }
}

private struct HandoffStatusView: View {
    @ObservedObject var agent: Agent
    @ObservedObject var controller: AgentController

    var body: some View {
        if let handoff = agent.handoff {
            let names = Dictionary(controller.knownMonitors.map { ($0.identity, $0.name) }) { a, _ in a }
            VStack(alignment: .leading, spacing: 4) {
                if !handoff.isFinished {
                    HStack(spacing: 6) {
                        ProgressView().controlSize(.small)
                        Text("Moving \(handoff.pending.sorted().map { names[$0] ?? $0 }.joined(separator: ", "))…").font(.caption)
                    }
                } else if handoff.failures.isEmpty && handoff.pending.isEmpty {
                    Text("Monitors moved.").font(.caption).foregroundStyle(.secondary)
                } else if handoff.failures.isEmpty {
                    Text("Didn't finish moving \(handoff.pending.sorted().map { names[$0] ?? $0 }.joined(separator: ", ")).")
                        .font(.caption).foregroundStyle(.orange)
                }
                ForEach(handoff.failures.sorted(by: { $0.key < $1.key }), id: \.key) { identity, message in
                    Text("\(names[identity] ?? identity) — \(message)")
                        .font(.caption).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }
}

private struct PeerRow: View {
    let name: String
    /// Connected or not; nil for this Mac.
    let status: Bool?
    let monitors: [ProtocolMessage.MonitorStatus]

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 6) {
                Circle().fill(status != false ? Color.green : Color.secondary.opacity(0.4)).frame(width: 7, height: 7)
                Text(name)
                Spacer()
                if let status {
                    Text(status ? "Connected" : "Offline").font(.caption).foregroundStyle(.secondary)
                }
            }
            ForEach(monitors, id: \.identity) { monitor in
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
