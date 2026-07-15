import CoreGraphics

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
}
