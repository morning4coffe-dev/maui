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

    var body: some View {
        Button(model.text, action: onClick)
        .disabled(!model.isEnabled)
        .accessibility(
            label: model.semanticsDescription.isEmpty
                ? Text(model.text)
                : Text(model.semanticsDescription)
        )
        .accessibility(identifier: model.automationId)
        .accessibility(hint: Text(model.semanticsHint))
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

    override func didMoveToSuperview() {
        super.didMoveToSuperview()
        controller?.updateParentController()
    }
}

@available(iOS 13.0, macCatalyst 13.0, *)
@objc(MauiSwiftUIButtonController)
@MainActor
public final class MauiSwiftUIButtonController:
    UIViewController,
    UIGestureRecognizerDelegate
{
    private let model = MauiSwiftUIButtonModel()
    private var hostingController: UIHostingController<MauiSwiftUIButtonContent>?
    private weak var callback: MauiSwiftUIButtonCallback?
    private var disconnected = false
    private var isPressing = false

    @objc public var buttonText: String {
        get { model.text }
        set {
            model.text = newValue
            invalidateIntrinsicContentSize()
        }
    }

    @objc public var buttonEnabled: Bool {
        get { model.isEnabled }
        set {
            if !newValue {
                endPress()
            }

            model.isEnabled = newValue
        }
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

    @objc public var pressingForDiagnostics: Bool {
        isPressing
    }

    public override func loadView() {
        let hostView = MauiSwiftUIButtonHostView()
        hostView.controller = self
        view = hostView

        let content = MauiSwiftUIButtonContent(
            model: model,
            onClick: { [weak self] in self?.performClick() }
        )
        let hostingController = UIHostingController(rootView: content)
        hostingController.view.backgroundColor = .clear
        hostingController.view.translatesAutoresizingMaskIntoConstraints = false

        let pressGestureRecognizer = UILongPressGestureRecognizer(
            target: self,
            action: #selector(handlePress(_:))
        )
        pressGestureRecognizer.minimumPressDuration = 0
        pressGestureRecognizer.cancelsTouchesInView = false
        pressGestureRecognizer.delegate = self
        hostView.addGestureRecognizer(pressGestureRecognizer)

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
        endPress()
        callback = nil
        disconnected = true
        detachFromParent()
    }

    @objc public func performClickForDiagnostics() {
        performClick()
    }

    @objc public func performPressedForDiagnostics() {
        beginPress()
    }

    @objc public func performReleasedForDiagnostics() {
        endPress()
    }

    @objc public func performCancelledForDiagnostics() {
        endPress()
    }

    @objc public func performPressGestureStateForDiagnostics(
        _ state: UIGestureRecognizer.State
    ) {
        handlePressState(state)
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

    public func gestureRecognizer(
        _ gestureRecognizer: UIGestureRecognizer,
        shouldRecognizeSimultaneouslyWith otherGestureRecognizer: UIGestureRecognizer
    ) -> Bool {
        true
    }

    @objc private func handlePress(_ gestureRecognizer: UILongPressGestureRecognizer) {
        handlePressState(gestureRecognizer.state)
    }

    private func handlePressState(_ state: UIGestureRecognizer.State) {
        switch state {
        case .began:
            beginPress()
        case .ended, .cancelled, .failed:
            endPress()
        default:
            break
        }
    }

    private func beginPress() {
        guard !disconnected, model.isEnabled, !isPressing else {
            return
        }

        isPressing = true
        callback?.onPressed()
    }

    private func performClick() {
        endPress()
        callback?.onClick()
    }

    private func endPress() {
        guard isPressing else {
            return
        }

        isPressing = false
        callback?.onReleased()
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
