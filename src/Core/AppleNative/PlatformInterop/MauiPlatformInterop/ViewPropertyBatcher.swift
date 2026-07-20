import Foundation
import UIKit

@objc(MauiViewPropertyBatcher)
public class MauiViewPropertyBatcher: NSObject {

    @objc(applyWithPlatformView:containerView:hasContainer:hidden:semanticContentAttribute:enabled:applyOpacity:opacity:)
    @discardableResult
    public static func apply(
        platformView: UIView,
        containerView: UIView,
        hasContainer: Bool,
        hidden: Bool,
        semanticContentAttribute: UISemanticContentAttribute,
        enabled: Bool,
        applyOpacity: Bool,
        opacity: Double
    ) -> Bool {
        if hidden {
            platformView.isHidden = true

            if hasContainer {
                containerView.isHidden = true
            }
        }

        let flowDirectionChanged = platformView.semanticContentAttribute != semanticContentAttribute
        platformView.semanticContentAttribute = semanticContentAttribute

        if let control = platformView as? UIControl {
            control.isEnabled = enabled
        } else {
            platformView.isUserInteractionEnabled = enabled
        }

        if applyOpacity {
            if hasContainer {
                containerView.alpha = CGFloat(opacity)
                platformView.alpha = 1
            } else {
                platformView.alpha = CGFloat(opacity)
            }
        }

        return flowDirectionChanged
    }
}
