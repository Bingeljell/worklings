import AppKit
import SwiftUI

@MainActor
final class HoverSummaryPanelController {
    private static let panelSize = CGSize(width: 260, height: 58)
    private static let spacing: CGFloat = 8

    private let panel: NSPanel

    init() {
        panel = NSPanel(
            contentRect: CGRect(origin: .zero, size: Self.panelSize),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.ignoresMouseEvents = true
        panel.level = NSWindow.Level(rawValue: NSWindow.Level.floating.rawValue + 1)
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
    }

    func show(summary: String, relativeTo anchorWindow: NSWindow) {
        panel.contentView = NSHostingView(
            rootView: HoverSummaryView(summary: summary)
        )
        panel.setFrameOrigin(origin(relativeTo: anchorWindow))
        panel.orderFrontRegardless()
    }

    func hide() {
        panel.orderOut(nil)
    }

    private func origin(relativeTo anchorWindow: NSWindow) -> CGPoint {
        let anchorFrame = anchorWindow.frame
        let visibleFrame = anchorWindow.screen?.visibleFrame
            ?? NSScreen.main?.visibleFrame
            ?? anchorFrame

        var proposedOrigin = CGPoint(
            x: anchorFrame.midX - Self.panelSize.width / 2,
            y: anchorFrame.maxY + Self.spacing
        )

        if proposedOrigin.y + Self.panelSize.height > visibleFrame.maxY {
            proposedOrigin.y = anchorFrame.minY - Self.panelSize.height - Self.spacing
        }

        return CGPoint(
            x: min(
                max(proposedOrigin.x, visibleFrame.minX),
                visibleFrame.maxX - Self.panelSize.width
            ),
            y: min(
                max(proposedOrigin.y, visibleFrame.minY),
                visibleFrame.maxY - Self.panelSize.height
            )
        )
    }
}

private struct HoverSummaryView: View {
    let summary: String

    var body: some View {
        Text(summary)
            .font(.system(size: 13, weight: .semibold, design: .rounded))
            .foregroundStyle(.primary)
            .multilineTextAlignment(.center)
            .lineLimit(2)
            .padding(.horizontal, 16)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16))
            .overlay {
                RoundedRectangle(cornerRadius: 16)
                    .stroke(.white.opacity(0.3), lineWidth: 1)
            }
            .accessibilityLabel(summary)
    }
}
