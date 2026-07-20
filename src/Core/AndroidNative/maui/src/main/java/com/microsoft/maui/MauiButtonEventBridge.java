package com.microsoft.maui;

import android.view.MotionEvent;
import android.view.View;

import com.google.android.material.button.MaterialButton;

import java.lang.ref.WeakReference;

public final class MauiButtonEventBridge
        implements View.OnClickListener, View.OnTouchListener {

    private WeakReference<MaterialButton> buttonReference;
    private WeakReference<ButtonEventCallback> callbackReference;

    private MauiButtonEventBridge(MaterialButton button, ButtonEventCallback callback) {
        buttonReference = new WeakReference<>(button);
        callbackReference = new WeakReference<>(callback);
    }

    public static MauiButtonEventBridge attach(
            MaterialButton button,
            ButtonEventCallback callback) {
        MauiButtonEventBridge bridge = new MauiButtonEventBridge(button, callback);
        button.setOnClickListener(bridge);
        button.setOnTouchListener(bridge);
        return bridge;
    }

    public void detach() {
        MaterialButton button = buttonReference.get();
        if (button != null) {
            button.setOnClickListener(null);
            button.setOnTouchListener(null);
        }

        buttonReference.clear();
        callbackReference.clear();
    }

    @Override
    public void onClick(View view) {
        ButtonEventCallback callback = callbackReference.get();
        if (callback != null) {
            callback.onClicked();
        }
    }

    @Override
    public boolean onTouch(View view, MotionEvent event) {
        if (event == null) {
            return false;
        }

        ButtonEventCallback callback = callbackReference.get();
        if (callback == null) {
            return false;
        }

        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                callback.onPressed();
                break;
            case MotionEvent.ACTION_CANCEL:
            case MotionEvent.ACTION_UP:
                callback.onReleased();
                break;
        }

        return false;
    }
}
