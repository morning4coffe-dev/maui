import Foundation
import UIKit

@available(iOS 15.0, macCatalyst 15.0, *)
@objc(MauiUIButtonConfigurationBatcher)
@MainActor
public final class MauiUIButtonConfigurationBatcher: NSObject {

    @objc(applyWithButton:text:font:characterSpacing:textColor:backgroundColor:image:paddingTop:paddingLeft:paddingBottom:paddingRight:rightToLeft:strokeColor:strokeThickness:cornerRadius:)
    public static func apply(
        button: UIButton,
        text: String,
        font: UIFont,
        characterSpacing: Double,
        textColor: UIColor?,
        backgroundColor: UIColor?,
        image: UIImage?,
        paddingTop: Double,
        paddingLeft: Double,
        paddingBottom: Double,
        paddingRight: Double,
        rightToLeft: Bool,
        strokeColor: UIColor?,
        strokeThickness: Double,
        cornerRadius: Double
    ) {
        var configuration: UIButton.Configuration
        if button.traitCollection.userInterfaceIdiom == .mac {
            configuration = .bordered()
        } else {
            configuration = .plain()
        }

        let attributedTitle = NSMutableAttributedString(string: text)
        let titleRange = NSRange(location: 0, length: attributedTitle.length)
        attributedTitle.addAttribute(.font, value: font, range: titleRange)

        if characterSpacing != 0 {
            attributedTitle.addAttribute(
                .kern,
                value: NSNumber(value: characterSpacing),
                range: titleRange
            )
        }

        if let textColor {
            attributedTitle.addAttribute(
                .foregroundColor,
                value: textColor,
                range: titleRange
            )
            configuration.baseForegroundColor = textColor
        }

        configuration.attributedTitle = AttributedString(attributedTitle)
        configuration.image = image

        let additionalPadding = max(0, strokeThickness)
        let left = paddingLeft + additionalPadding
        let right = paddingRight + additionalPadding
        configuration.contentInsets = NSDirectionalEdgeInsets(
            top: paddingTop + additionalPadding,
            leading: rightToLeft ? right : left,
            bottom: paddingBottom + additionalPadding,
            trailing: rightToLeft ? left : right
        )

        var background = configuration.background
        if let backgroundColor {
            configuration.baseBackgroundColor = backgroundColor
            background.backgroundColor = backgroundColor
        }

        if strokeThickness >= 0 {
            background.strokeColor = strokeColor
            background.strokeWidth = CGFloat(strokeThickness)
        }

        if cornerRadius >= 0 {
            configuration.cornerStyle = .fixed
            background.cornerRadius = CGFloat(cornerRadius)
        }

        configuration.background = background
        button.configuration = configuration
    }
}
