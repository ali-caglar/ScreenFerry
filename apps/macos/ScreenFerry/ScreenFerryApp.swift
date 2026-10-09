import AppKit
import ScreenFerryKit
import SwiftUI

@main
struct ScreenFerryApp: App {
    var body: some Scene {
        MenuBarExtra("ScreenFerry", systemImage: "display.2") {
            Button("Quit ScreenFerry") {
                NSApplication.shared.terminate(nil)
            }
            .keyboardShortcut("q")
        }
    }
}
