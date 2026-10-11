import ScreenFerryAgent
import SwiftUI

@main
struct ScreenFerryApp: App {
    @StateObject private var controller = AgentController()

    var body: some Scene {
        MenuBarExtra {
            MenuContent(controller: controller)
        } label: {
            MenuBarIcon(controller: controller)
        }
        .menuBarExtraStyle(.window)
    }
}
