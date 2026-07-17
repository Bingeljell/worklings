import CoreGraphics

public struct PetRoamingIntent: Equatable, Sendable {
    public let horizontalOffset: CGFloat
    public let verticalOffset: CGFloat
    public let restDuration: Double
    public let travelDuration: Double

    public init(
        horizontalOffset: CGFloat,
        verticalOffset: CGFloat,
        restDuration: Double,
        travelDuration: Double
    ) {
        self.horizontalOffset = min(max(horizontalOffset, -1), 1)
        self.verticalOffset = min(max(verticalOffset, -1), 1)
        self.restDuration = max(0, restDuration)
        self.travelDuration = max(0, travelDuration)
    }
}

public enum PetRoamingPlanner {
    private static let pattern = [
        PetRoamingIntent(
            horizontalOffset: -0.24,
            verticalOffset: 0,
            restDuration: 7,
            travelDuration: 2.8
        ),
        PetRoamingIntent(
            horizontalOffset: 0.18,
            verticalOffset: 0.04,
            restDuration: 9,
            travelDuration: 2.4
        ),
        PetRoamingIntent(
            horizontalOffset: -0.12,
            verticalOffset: -0.03,
            restDuration: 12,
            travelDuration: 2.2
        ),
        PetRoamingIntent(
            horizontalOffset: 0.26,
            verticalOffset: 0,
            restDuration: 8,
            travelDuration: 3
        )
    ]

    public static func intent(sequenceNumber: UInt64) -> PetRoamingIntent {
        pattern[Int(sequenceNumber % UInt64(pattern.count))]
    }
}

public enum ScreenPlacement {
    public static func defaultOrigin(
        windowSize: CGSize,
        visibleFrame: CGRect,
        margin: CGFloat = 24
    ) -> CGPoint {
        clampedOrigin(
            proposed: CGPoint(
                x: visibleFrame.maxX - windowSize.width - margin,
                y: visibleFrame.minY + margin
            ),
            windowSize: windowSize,
            visibleFrame: visibleFrame,
            margin: margin
        )
    }

    public static func clampedOrigin(
        proposed: CGPoint,
        windowSize: CGSize,
        visibleFrame: CGRect,
        margin: CGFloat = 0
    ) -> CGPoint {
        let minimumX = visibleFrame.minX + margin
        let minimumY = visibleFrame.minY + margin
        let maximumX = max(minimumX, visibleFrame.maxX - windowSize.width - margin)
        let maximumY = max(minimumY, visibleFrame.maxY - windowSize.height - margin)

        return CGPoint(
            x: min(max(proposed.x, minimumX), maximumX),
            y: min(max(proposed.y, minimumY), maximumY)
        )
    }

    public static func roamingOrigin(
        from currentOrigin: CGPoint,
        intent: PetRoamingIntent,
        windowSize: CGSize,
        visibleFrame: CGRect,
        margin: CGFloat = 24,
        minimumTravelDistance: CGFloat = 48
    ) -> CGPoint {
        let availableWidth = max(
            0,
            visibleFrame.width - windowSize.width - margin * 2
        )
        let availableHeight = max(
            0,
            visibleFrame.height - windowSize.height - margin * 2
        )
        let offset = CGPoint(
            x: intent.horizontalOffset * availableWidth,
            y: intent.verticalOffset * availableHeight
        )

        let destination = clampedOrigin(
            proposed: CGPoint(
                x: currentOrigin.x + offset.x,
                y: currentOrigin.y + offset.y
            ),
            windowSize: windowSize,
            visibleFrame: visibleFrame,
            margin: margin
        )

        guard hypot(
            destination.x - currentOrigin.x,
            destination.y - currentOrigin.y
        ) < minimumTravelDistance else {
            return destination
        }

        return clampedOrigin(
            proposed: CGPoint(
                x: currentOrigin.x - offset.x,
                y: currentOrigin.y - offset.y
            ),
            windowSize: windowSize,
            visibleFrame: visibleFrame,
            margin: margin
        )
    }
}
