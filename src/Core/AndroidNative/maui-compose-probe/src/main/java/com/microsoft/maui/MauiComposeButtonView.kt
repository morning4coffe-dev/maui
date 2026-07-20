package com.microsoft.maui

import android.content.Context
import android.view.View
import android.widget.FrameLayout
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.PressInteraction
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.wrapContentSize
import androidx.compose.material3.Button
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.ComposeView
import androidx.compose.ui.platform.ViewCompositionStrategy
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics

class MauiComposeButtonView(context: Context) : FrameLayout(context) {
    private val composeView = ComposeView(context)
    private var currentButtonText by mutableStateOf("")
    private var currentButtonEnabled by mutableStateOf(true)
    private var currentSemanticsDescription by mutableStateOf("")
    private var currentAutomationId by mutableStateOf("")
    private var measuredContentWidth = 0
    private var measuredContentHeight = 0
    private var callback: MauiComposeButtonCallback? = null
    private var disconnected = false

    init {
        composeView.setViewCompositionStrategy(
            ViewCompositionStrategy.DisposeOnDetachedFromWindowOrReleasedFromPool
        )
        addView(
            composeView,
            LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT)
        )
        installContent()
    }

    fun connect(callback: MauiComposeButtonCallback) {
        this.callback = callback

        if (disconnected) {
            disconnected = false
            installContent()
        }
    }

    fun disconnect() {
        callback = null
        composeView.disposeComposition()
        disconnected = true
    }

    fun setButtonText(value: String?) {
        currentButtonText = value.orEmpty()
    }

    fun setButtonEnabled(value: Boolean) {
        currentButtonEnabled = value
    }

    fun setSemanticsDescription(value: String?) {
        currentSemanticsDescription = value.orEmpty()
    }

    fun setAutomationId(value: String?) {
        currentAutomationId = value.orEmpty()

        val previousImportance = importantForAccessibility
        contentDescription = value
        if (previousImportance == View.IMPORTANT_FOR_ACCESSIBILITY_AUTO) {
            importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_AUTO
        }
    }

    fun getButtonTextForDiagnostics(): String = currentButtonText

    fun getButtonEnabledForDiagnostics(): Boolean = currentButtonEnabled

    fun getSemanticsDescriptionForDiagnostics(): String = currentSemanticsDescription

    fun getAutomationIdForDiagnostics(): String = currentAutomationId

    fun getMeasuredContentWidthForDiagnostics(): Int = measuredContentWidth

    fun getMeasuredContentHeightForDiagnostics(): Int = measuredContentHeight

    fun getDisconnectedForDiagnostics(): Boolean = disconnected

    fun performClickForDiagnostics() {
        callback?.onClick()
    }

    fun performPressedForDiagnostics() {
        callback?.onPressed()
    }

    fun performReleasedForDiagnostics() {
        callback?.onReleased()
    }

    private fun installContent() {
        composeView.setContent {
            val interactionSource = remember { MutableInteractionSource() }

            LaunchedEffect(interactionSource) {
                interactionSource.interactions.collect { interaction ->
                    when (interaction) {
                        is PressInteraction.Press -> callback?.onPressed()
                        is PressInteraction.Release,
                        is PressInteraction.Cancel -> callback?.onReleased()
                    }
                }
            }

            MaterialTheme(
                colorScheme =
                    if (isSystemInDarkTheme()) darkColorScheme() else lightColorScheme()
            ) {
                Button(
                    enabled = currentButtonEnabled,
                    interactionSource = interactionSource,
                    modifier = Modifier
                        .wrapContentSize()
                        .onSizeChanged {
                            measuredContentWidth = it.width
                            measuredContentHeight = it.height
                        }
                        .semantics {
                            if (currentSemanticsDescription.isNotEmpty()) {
                                contentDescription = currentSemanticsDescription
                            }
                        }
                        .testTag(currentAutomationId),
                    onClick = { callback?.onClick() }
                ) {
                    Text(currentButtonText)
                }
            }
        }
    }
}
