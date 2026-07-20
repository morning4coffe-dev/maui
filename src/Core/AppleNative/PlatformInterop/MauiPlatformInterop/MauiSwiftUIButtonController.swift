import SwiftUI
import UIKit

@objc(MauiSwiftUIButtonCallback)
public protocol MauiSwiftUIButtonCallback: AnyObject {
    func onClick()
    func onPressed()
    func onReleased()
}

@available(iOS 13.0, macCatalyst 13.0, *)
@MainActor
private final class MauiSwiftUIButtonModel: ObservableObject {
    @Published var text = ""
    @Published var isEnabled = true
    @Published var semanticsDescription = ""
    @Published var semanticsHint = ""
    @Published var automationId = ""
}

@available(iOS 13.0, macCatalyst 13.0, *)
private struct MauiSwiftUIButtonContent: View {
    @ObservedObject var model: MauiSwiftUIButtonModel
    let onClick: () -> Void
    let onPressed: () -> Void
    let onReleased: () -> Void
    @State private var isPressing = false

    var body: some View {
        Button(action: onClick) {
            Text(model.text)
        }
        .disabled(!model.isEnabled)
        .accessibilityLabel(
            model.semanticsDescription.isEmpty
                ? Text(model.text)
                : Text(model.semanticsDescription)
        )
        .accessibility(identifier: model.automationId)
        .accessibilityHint(Text(model.semanticsHint))
        .simultaneousGesture(
            DragGesture(minimumDistance: 0)
                .onChanged { _ in
                    guard model.isEnabled, !isPressing else {
                        return
                    }

                    isPressing = true
                    onPressed()
                }
                .onEnded { _ in
                    guard isPressing else {
                        return
                    }

                    isPressing = false
                    onReleased()
                }
        )
    }
}

@available(iOS 13.0, macCatalyst 13.0, *)
@MainActor
private final class MauiSwiftUIButtonHostView: UIView {
    weak var controller: MauiSwiftUIButtonController?

    override func sizeThatFits(_ size: CGSize) -> CGSize {
        controller?.sizeThatFits(size) ?? super.sizeThatFits(size)
    }

    override var intrinsicContentSize: CGSize {
        controller?.sizeThatFits(
            CGSize(
                width: CGFloat.greatestFiniteMagnitude,
                height: CGFloat.greatestFiniteMagnitude
            )
        )
            ?? super.intrinsicContentSize
    }

    override func didMoveToWindow() {
        super.didMoveToWindow()
        controller?.updateParentController()
    }
}

@available(iOS 13.0, macCatalyst 13.0, *)
@objc(MauiSwiftUIButtonController)
@MainActor
public final class MauiSwiftUIButtonController: UIViewController {
    private let model = MauiSwiftUIButtonModel()
    private var hostingController: UIHostingController<MauiSwiftUIButtonContent>?
    private weak var callback: MauiSwiftUIButtonCallback?
    private var disconnected = false

    @objc public var buttonText: String {
        get { model.text }
        set {
            model.text = newValue
            invalidateIntrinsicContentSize()
        }
    }

    @objc public var buttonEnabled: Bool {
        get { model.isEnabled }
        set { model.isEnabled = newValue }
    }

    @objc public var semanticsDescription: String {
        get { model.semanticsDescription }
        set { model.semanticsDescription = newValue }
    }

    @objc public var semanticsHint: String {
        get { model.semanticsHint }
        set { model.semanticsHint = newValue }
    }

    @objc public var automationId: String {
        get { model.automationId }
        set { model.automationId = newValue }
    }

    @objc public var platformView: UIView {
        loadViewIfNeeded()
        return view
    }

    @objc public var disconnectedForDiagnostics: Bool {
        disconnected
    }

    public override func loadView() {
        let hostView = MauiSwiftUIButtonHostView()
        hostView.controller = self
        view = hostView

        let content = MauiSwiftUIButtonContent(
            model: model,
            onClick: { [weak self] in
                self?.callback?.onClick()
            },
            onPressed: { [weak self] in
                self?.callback?.onPressed()
            },
            onReleased: { [weak self] in
                self?.callback?.onReleased()
            }
        )
        let hostingController = UIHostingController(rootView: content)
        hostingController.view.backgroundColor = .clear
        hostingController.view.translatesAutoresizingMaskIntoConstraints = false

        addChild(hostingController)
        hostView.addSubview(hostingController.view)
        NSLayoutConstraint.activate([
            hostingController.view.leadingAnchor.constraint(equalTo: hostView.leadingAnchor),
            hostingController.view.trailingAnchor.constraint(equalTo: hostView.trailingAnchor),
            hostingController.view.topAnchor.constraint(equalTo: hostView.topAnchor),
            hostingController.view.bottomAnchor.constraint(equalTo: hostView.bottomAnchor),
        ])
        hostingController.didMove(toParent: self)

        if #available(iOS 16.0, macCatalyst 16.0, *) {
            hostingController.sizingOptions = .intrinsicContentSize
        }

        self.hostingController = hostingController
    }

    @objc(connectWithCallback:)
    public func connect(callback: MauiSwiftUIButtonCallback) {
        self.callback = callback
        disconnected = false
        loadViewIfNeeded()
        updateParentController()
    }

    @objc public func disconnect() {
        callback = nil
        disconnected = true
        detachFromParent()
    }

    @objc public func performClickForDiagnostics() {
        callback?.onClick()
    }

    @objc public func performPressedForDiagnostics() {
        callback?.onPressed()
    }

    @objc public func performReleasedForDiagnostics() {
        callback?.onReleased()
    }

    @objc public func sizeThatFits(_ size: CGSize) -> CGSize {
        loadViewIfNeeded()

        guard let hostingController else {
            return .zero
        }

        if #available(iOS 16.0, macCatalyst 16.0, *) {
            return hostingController.sizeThatFits(in: size)
        }

        let proposedSize: CGSize
        let horizontalPriority: UILayoutPriority

        if size.width.isFinite, size.width > 0 {
            proposedSize = CGSize(
                width: size.width,
                height: UIView.layoutFittingCompressedSize.height
            )
            horizontalPriority = .required
        } else {
            proposedSize = UIView.layoutFittingCompressedSize
            horizontalPriority = .fittingSizeLevel
        }

        let measured = hostingController.view.systemLayoutSizeFitting(
            proposedSize,
            withHorizontalFittingPriority: horizontalPriority,
            verticalFittingPriority: .fittingSizeLevel
        )
        return CGSize(
            width: size.width.isFinite ? min(measured.width, size.width) : measured.width,
            height: size.height.isFinite ? min(measured.height, size.height) : measured.height
        )
    }

    fileprivate func updateParentController() {
        guard viewIfLoaded?.window != nil else {
            detachFromParent()
            return
        }

        guard let candidate = findParentController(), candidate !== parent else {
            return
        }

        detachFromParent()
        candidate.addChild(self)
        didMove(toParent: candidate)
    }

    private func findParentController() -> UIViewController? {
        var responder: UIResponder? = viewIfLoaded?.superview

        while let current = responder {
            if let controller = current as? UIViewController, controller !== self {
                return controller
            }
            responder = current.next
        }

        return nil
    }

    private func detachFromParent() {
        guard parent != nil else {
            return
        }

        willMove(toParent: nil)
        removeFromParent()
    }

    private func invalidateIntrinsicContentSize() {
        viewIfLoaded?.invalidateIntrinsicContentSize()
        hostingController?.view.invalidateIntrinsicContentSize()
    }
}
