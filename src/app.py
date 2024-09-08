import sys
from PyQt6.QtWidgets import QApplication, QSystemTrayIcon
from PyQt6.QtCore import QTimer
from .tray_icon_manager import TrayIconManager
from .webcam_controller import WebcamController
from .ui import AutoBrightnessUI, set_dark_theme


class AutoBrightnessApp(AutoBrightnessUI):
    def __init__(self):
        super().__init__()
        self.tray_icon_manager = TrayIconManager(self)
        self.is_macos = sys.platform == "darwin"
        self.webcam_controller = None
        self.timer = QTimer()
        self.timer.timeout.connect(self.update_luminance_display)

        # Connect signals
        self.start_stop_signal.connect(self.toggle_webcam)
        self.reset_signal.connect(self.reset_webcam)

        # Show the main window
        self.show()

    def toggle_webcam(self, start):
        if start:
            self.start_webcam()
        else:
            self.stop_webcam()

    def start_webcam(self):
        self.webcam_controller = WebcamController(
            self.brightness_slider, self.exposure_slider
        )
        self.webcam_controller.start_webcam()
        self.timer.start(100)

    def stop_webcam(self):
        if self.webcam_controller:
            self.webcam_controller.stop_webcam()
        self.timer.stop()

    def reset_webcam(self):
        if self.webcam_controller:
            self.webcam_controller.stop_webcam()
            self.webcam_controller = WebcamController(
                self.brightness_slider, self.exposure_slider
            )
            self.webcam_controller.start_webcam()

    def update_luminance_display(self):
        if self.webcam_controller:
            luminance = self.webcam_controller.current_brightness or 0
            brightness = int(luminance) if luminance is not None else 0
            self.update_luminance_label(luminance, brightness)

    def onTrayIconActivated(self, reason):
        if reason == QSystemTrayIcon.ActivationReason.DoubleClick:
            if self.isHidden():
                self.show()
            else:
                self.hide()

    def closeEvent(self, event):
        self.stop_webcam()
        event.accept()


if __name__ == "__main__":
    app = QApplication(sys.argv)
    set_dark_theme(app)
    auto_brightness_app = AutoBrightnessApp()
    sys.exit(app.exec())
