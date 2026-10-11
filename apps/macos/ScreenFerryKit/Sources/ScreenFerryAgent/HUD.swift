import AppKit
import SwiftUI

/// A short-lived panel at the bottom of the screen with the mouse, like the volume HUD.
@MainActor
final class HUD {
    enum Style {
        case progress, done, failed
    }

    private var panel: NSPanel?
    private var hideTask: Task<Void, Never>?

    /// Shows the message; `.progress` stays until replaced, the others hide after a moment.
    func show(_ title: String, detail: String?, style: Style) {
        let panel = panel ?? makePanel()
        self.panel = panel
        let hosting = NSHostingView(rootView: HUDView(title: title, detail: detail, style: style))
        panel.contentView = hosting
        panel.setContentSize(hosting.fittingSize)

        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { $0.frame.contains(mouse) } ?? NSScreen.main
        if let frame = screen?.visibleFrame {
            panel.setFrameOrigin(NSPoint(x: frame.midX - panel.frame.width / 2, y: frame.minY + 120))
        }
        panel.alphaValue = 1
        panel.orderFrontRegardless()

        hideTask?.cancel()
        guard style != .progress else { return }
        hideTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(style == .failed ? 5 : 2))
            guard !Task.isCancelled, let panel = self?.panel else { return }
            await NSAnimationContext.runAnimationGroup { $0.duration = 0.3; panel.animator().alphaValue = 0 }
            panel.orderOut(nil)
        }
    }

    private func makePanel() -> NSPanel {
        let panel = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.level = .statusBar
        panel.ignoresMouseEvents = true
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        return panel
    }
}

private struct HUDView: View {
    let title: String
    let detail: String?
    let style: HUD.Style

    var body: some View {
        HStack(spacing: 12) {
            switch style {
            case .progress: ProgressView().controlSize(.regular)
            case .done: Image(systemName: "checkmark.circle.fill").font(.title).foregroundStyle(.green)
            case .failed: Image(systemName: "xmark.octagon.fill").font(.title).foregroundStyle(.red)
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.headline)
                if let detail {
                    Text(detail).font(.subheadline).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 14)
        .frame(minWidth: 240, maxWidth: 420, alignment: .leading)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14))
    }
}
