import AppKit
import CompanionCore
import Foundation
import SwiftUI

struct WorklingPetView: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @ObservedObject var session: PetSession
    @ObservedObject var motion: CompanionMotionState

    private var presentation: PetPresentation {
        PetPresentation.make(state: session.state, reaction: session.reaction)
    }

    private var careStatus: PetCareStatus {
        session.careStatus
    }

    var body: some View {
        ZStack {
            Ellipse()
                .fill(.black.opacity(0.18))
                .frame(width: 92, height: 20)
                .blur(radius: 3)
                .offset(y: 62)

            TimelineView(
                .periodic(from: .now, by: motion.isWalking ? 0.14 : 0.7)
            ) { context in
                WorklingSprite(
                    family: session.state.family,
                    frame: spriteFrame(at: context.date)
                )
                    .scaleEffect(
                        x: motion.facingDirection == .left ? 1 : -1,
                        y: 1
                    )
            }

            if let thought = presentation.thought {
                Text(thought)
                    .font(.system(size: 12, weight: .semibold, design: .rounded))
                    .foregroundStyle(Color(red: 0.13, green: 0.08, blue: 0.25))
                    .padding(.horizontal, 11)
                    .padding(.vertical, 7)
                    .background(.white.opacity(0.95), in: Capsule())
                    .shadow(color: .black.opacity(0.16), radius: 5, y: 2)
                    .offset(y: -79)
                    .transition(.scale.combined(with: .opacity))
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .contentShape(Rectangle())
        .animation(.easeInOut(duration: 0.2), value: presentation)
        .accessibilityLabel(
            "\(session.state.name), \(session.state.family.displayName), "
                + presentation.moodLabel.lowercased()
        )
        .accessibilityHint(careStatus.hoverSummary)
    }

    private func spriteFrame(at date: Date) -> WorklingSpriteFrame {
        if let reaction = session.reaction {
            return reaction == .tooTiredToPlay ? .sleepy : .caredFor
        }

        if motion.isWalking, !reduceMotion {
            let phase = Int(date.timeIntervalSinceReferenceDate / 0.14) % 4
            return switch phase {
            case 0: .walkContact
            case 1: .walkPassing
            case 2: .walkContactOpposite
            default: .walkPassingOpposite
            }
        }

        switch presentation.face {
        case .happy:
            return .happy
        case .neutral:
            guard !reduceMotion else {
                return .idle
            }
            let phase = Int(date.timeIntervalSinceReferenceDate / 0.7)
            return phase.isMultiple(of: 2) ? .idle : .idleBlink
        case .hungry:
            return .hungry
        case .sleepy:
            return .sleepy
        case .sad:
            return .sad
        case .wary:
            return .wary
        }
    }
}

private struct WorklingSprite: View {
    private static let sourceCellSize: CGFloat = 256
    private static let cellSize: CGFloat = 168

    let family: PetFamily
    let frame: WorklingSpriteFrame

    private static let wildkinSpriteSheet = loadSpriteSheet(
        resourceName: "worklings-wildkin-spritesheet",
        familyName: "Wildkin"
    )
    private static let elementalSpriteSheet = loadSpriteSheet(
        resourceName: "worklings-elemental-spritesheet",
        familyName: "Elemental"
    )
    private static let relicbornSpriteSheet = loadSpriteSheet(
        resourceName: "worklings-relicborn-spritesheet",
        familyName: "Relicborn"
    )

    private static func loadSpriteSheet(
        resourceName: String,
        familyName: String
    ) -> CGImage? {
        let resourceURL = Bundle.main.url(
            forResource: resourceName,
            withExtension: "png"
        ) ?? Bundle.module.url(
            forResource: resourceName,
            withExtension: "png"
        )

        guard let resourceURL,
              let sourceImage = NSImage(contentsOf: resourceURL) else {
            NSLog("Worklings could not load the %@ sprite sheet.", familyName)
            return nil
        }

        var proposedRect = NSRect(origin: .zero, size: sourceImage.size)
        return sourceImage.cgImage(
            forProposedRect: &proposedRect,
            context: nil,
            hints: nil
        )
    }

    var body: some View {
        Group {
            if let frameImage {
                Image(decorative: frameImage, scale: 1, orientation: .up)
                    .resizable()
                    .interpolation(.none)
            } else {
                Image(systemName: "pawprint.fill")
                    .resizable()
                    .scaledToFit()
                    .foregroundStyle(.secondary)
                    .padding(48)
            }
        }
        .frame(width: Self.cellSize, height: Self.cellSize)
    }

    private var frameImage: CGImage? {
        spriteSheet?.cropping(
            to: CGRect(
                x: CGFloat(frame.column) * Self.sourceCellSize,
                y: CGFloat(frame.row) * Self.sourceCellSize,
                width: Self.sourceCellSize,
                height: Self.sourceCellSize
            )
        )
    }

    private var spriteSheet: CGImage? {
        switch family {
        case .wildkin: Self.wildkinSpriteSheet
        case .elemental: Self.elementalSpriteSheet
        case .relicborn: Self.relicbornSpriteSheet
        }
    }
}

private enum WorklingSpriteFrame {
    case idle
    case idleBlink
    case walkContact
    case walkPassing
    case walkContactOpposite
    case walkPassingOpposite
    case happy
    case caredFor
    case hungry
    case sleepy
    case sad
    case wary

    var column: Int {
        switch self {
        case .idle, .walkContactOpposite, .hungry:
            return 0
        case .idleBlink, .walkPassingOpposite, .sleepy:
            return 1
        case .walkContact, .happy, .sad:
            return 2
        case .walkPassing, .caredFor, .wary:
            return 3
        }
    }

    var row: Int {
        switch self {
        case .idle, .idleBlink, .walkContact, .walkPassing:
            return 0
        case .walkContactOpposite, .walkPassingOpposite, .happy, .caredFor:
            return 1
        case .hungry, .sleepy, .sad, .wary:
            return 2
        }
    }
}
