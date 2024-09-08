import sys
from PyQt6.QtWidgets import QApplication, QSystemTrayIcon, QMessageBox
from PyQt6.QtCore import Qt
from PyQt6.QtGui import QPixmap, QPainter, QColor, QFont
from .tray_icon_manager import TrayIconManager
from .webcam_controller import WebcamController
from .ui import AutoBrightnessUI


class AutoBrightnessApp(AutoBrightnessUI):
    def __init__(self):
        super().__init__()
        self.tray_icon_manager = TrayIconManager(self)
        self.is_macos = sys.platform == "darwin"
        self.webcam_controller = None
        self.permission_error_shown = False

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
        self.webcam_controller.frame_ready.connect(self.update_display)
        self.webcam_controller.permission_error.connect(self.show_permission_error)
        self.webcam_controller.start_webcam()

    def stop_webcam(self):
        if self.webcam_controller:
            self.webcam_controller.stop_webcam()

    def reset_webcam(self):
        if self.webcam_controller:
            self.webcam_controller.stop_webcam()
            self.start_webcam()

    def update_display(self, pixmap, luminance, brightness):
        # Update camera view with the grayscale image
        self.camera_label.setPixmap(
            pixmap.scaled(400, 300, aspectRatioMode=Qt.AspectRatioMode.KeepAspectRatio)
        )

        # Update luminance and brightness information
        info_pixmap = QPixmap(400, 60)
        info_pixmap.fill(QColor(58, 66, 84))  # Fill with the background color

        painter = QPainter(info_pixmap)
        painter.setPen(Qt.GlobalColor.white)
        painter.setFont(QFont("Arial", 12))
        painter.drawText(10, 20, f"Luminance: {luminance:.2f}")
        painter.drawText(10, 40, f"Brightness: {brightness}")
        painter.end()

        self.info_label.setPixmap(info_pixmap)

    def show_permission_error(self):
        if not self.permission_error_shown:
            self.permission_error_shown = True
            QMessageBox.warning(
                self,
                "Permission Error",
                "Unable to adjust screen brightness. Please follow these steps to grant permission:\n\n"
                "1. Open System Preferences\n"
                "2. Go to Security & Privacy\n"
                "3. Click on the Privacy tab\n"
                "4. Select 'Accessibility' from the left sidebar\n"
                "5. Click the lock icon to make changes\n"
                "6. Check the box next to your application\n"
                "7. Restart the application\n\n"
                "Brightness control will be disabled until permissions are granted.",
            )

    def onTrayIconActivated(self, reason):
        if reason == QSystemTrayIcon.ActivationReason.Trigger:
            if self.isVisible():
                self.hide()
            else:
                self.show()

    def closeEvent(self, event):
        self.stop_webcam()
        event.accept()


if __name__ == "__main__":
    app = QApplication(sys.argv)
    auto_brightness_app = AutoBrightnessApp()
    sys.exit(app.exec())
