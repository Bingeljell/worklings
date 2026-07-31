// No platform imports: this placement math is deliberately free of CoreGraphics
// (and AppKit) so CompanionCore builds on any Swift platform. The app layer
// bridges these value types to/from its native CGPoint/CGSize/CGRect.

/// A minimal, platform-free 2D point for placement math.
public struct PlacementPoint: Equatable, Sendable {
    public var x: Double
    public var y: Double

    public init(x: Double, y: Double) {
        self.x = x
        self.y = y
    }
}

/// A minimal, platform-free 2D size for placement math.
public struct PlacementSize: Equatable, Sendable {
    public var width: Double
    public var height: Double

    public init(width: Double, height: Double) {
        self.width = width
        self.height = height
    }
}

/// A minimal, platform-free rectangle for placement math, origin at (x, y).
public struct PlacementRect: Equatable, Sendable {
    public var x: Double
    public var y: Double
    public var width: Double
    public var height: Double

    public init(x: Double, y: Double, width: Double, height: Double) {
        self.x = x
        self.y = y
        self.width = width
        self.height = height
    }

    public var minX: Double { x }
    public var minY: Double { y }
    public var maxX: Double { x + width }
    public var maxY: Double { y + height }
}

public struct PetRoamingIntent: Equatable, Sendable {
    public let horizontalOffset: Double
    public let verticalOffset: Double
    public let restDuration: Double
    public let travelDuration: Double

    public init(
        horizontalOffset: Double,
        verticalOffset: Double,
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
        windowSize: PlacementSize,
        visibleFrame: PlacementRect,
        margin: Double = 24
    ) -> PlacementPoint {
        clampedOrigin(
            proposed: PlacementPoint(
                x: visibleFrame.maxX - windowSize.width - margin,
                y: visibleFrame.minY + margin
            ),
            windowSize: windowSize,
            visibleFrame: visibleFrame,
            margin: margin
        )
    }

    public static func clampedOrigin(
        proposed: PlacementPoint,
        windowSize: PlacementSize,
        visibleFrame: PlacementRect,
        margin: Double = 0
    ) -> PlacementPoint {
        let minimumX = visibleFrame.minX + margin
        let minimumY = visibleFrame.minY + margin
        let maximumX = max(minimumX, visibleFrame.maxX - windowSize.width - margin)
        let maximumY = max(minimumY, visibleFrame.maxY - windowSize.height - margin)

        return PlacementPoint(
            x: min(max(proposed.x, minimumX), maximumX),
            y: min(max(proposed.y, minimumY), maximumY)
        )
    }

    public static func roamingOrigin(
        from currentOrigin: PlacementPoint,
        intent: PetRoamingIntent,
        windowSize: PlacementSize,
        visibleFrame: PlacementRect,
        margin: Double = 24,
        minimumTravelDistance: Double = 48
    ) -> PlacementPoint {
        let availableWidth = max(
            0,
            visibleFrame.width - windowSize.width - margin * 2
        )
        let availableHeight = max(
            0,
            visibleFrame.height - windowSize.height - margin * 2
        )
        let offset = PlacementPoint(
            x: intent.horizontalOffset * availableWidth,
            y: intent.verticalOffset * availableHeight
        )

        let destination = clampedOrigin(
            proposed: PlacementPoint(
                x: currentOrigin.x + offset.x,
                y: currentOrigin.y + offset.y
            ),
            windowSize: windowSize,
            visibleFrame: visibleFrame,
            margin: margin
        )

        let dx = destination.x - currentOrigin.x
        let dy = destination.y - currentOrigin.y
        guard (dx * dx + dy * dy).squareRoot() < minimumTravelDistance else {
            return destination
        }

        return clampedOrigin(
            proposed: PlacementPoint(
                x: currentOrigin.x - offset.x,
                y: currentOrigin.y - offset.y
            ),
            windowSize: windowSize,
            visibleFrame: visibleFrame,
            margin: margin
        )
    }
}
