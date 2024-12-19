import sys
from PyQt6.QtWidgets import QApplication, QMessageBox
from PyQt6.QtCore import Qt
from PyQt6.QtGui import QPixmap, QPainter, QColor, QFont

from .config import Config
from .logger import logger
from .webcam_controller import WebcamController
from .ui import AutoBrightnessUI


class AutoBrightnessApp(AutoBrightnessUI):
    def __init__(self):
        try:
            super().__init__()
            self.config = Config()
            
            # Initialize webcam controller
            self.webcam_controller = None
            
            # Connect signals
            self.start_stop_signal.connect(self.toggle_webcam)
            self.reset_signal.connect(self.reset_webcam)
            
            # Show the main window
            self.show()
            logger.info("Application initialized successfully")
        except Exception as e:
            logger.critical(f"Failed to initialize application: {e}")
            QMessageBox.critical(
                None,
                "Critical Error",
                f"Failed to initialize application: {str(e)}\n\nThe application will now exit."
            )
            sys.exit(1)

    def toggle_webcam(self, start: bool) -> None:
        """Toggle webcam on/off"""
        try:
            if start:
                self.start_webcam()
            else:
                self.stop_webcam()
        except Exception as e:
            logger.error(f"Error toggling webcam: {e}")
            self.show_error("Webcam Error", f"Failed to toggle webcam: {str(e)}")

    def start_webcam(self) -> None:
        """Start webcam capture"""
        try:
            self.webcam_controller = WebcamController(
                self.brightness_slider,
                self.exposure_slider
            )
            self.webcam_controller.frame_ready.connect(self.update_display)
            self.webcam_controller.camera_error.connect(
                lambda msg: self.show_error("Camera Error", msg)
            )
            self.webcam_controller.start_webcam()
            logger.info("Webcam started")
        except Exception as e:
            logger.error(f"Error starting webcam: {e}")
            self.show_error("Webcam Error", f"Failed to start webcam: {str(e)}")

    def stop_webcam(self) -> None:
        """Stop webcam capture"""
        try:
            if self.webcam_controller:
                self.webcam_controller.stop_webcam()
                self.webcam_controller = None
                logger.info("Webcam stopped")
        except Exception as e:
            logger.error(f"Error stopping webcam: {e}")
            self.show_error("Webcam Error", f"Failed to stop webcam: {str(e)}")

    def reset_webcam(self) -> None:
        """Reset webcam capture"""
        try:
            if self.webcam_controller:
                self.webcam_controller.stop_webcam()
                self.start_webcam()
                logger.info("Webcam reset")
        except Exception as e:
            logger.error(f"Error resetting webcam: {e}")
            self.show_error("Webcam Error", f"Failed to reset webcam: {str(e)}")

    def update_display(self, pixmap: QPixmap, luminance: float, brightness: int) -> None:
        """Update the display with new camera frame and information"""
        try:
            # Update camera view
            scaled_pixmap = pixmap.scaled(
                360, 270,
                aspectRatioMode=Qt.AspectRatioMode.KeepAspectRatio
            )
            self.camera_label.setPixmap(scaled_pixmap)

            # Update info display
            self.info_label.setText(
                f"Luminance: {luminance:.2f}\nBrightness: {brightness}%"
            )
        except Exception as e:
            logger.error(f"Error updating display: {e}")

    def show_error(self, title: str, message: str) -> None:
        """Show error dialog"""
        try:
            QMessageBox.critical(self, title, message)
            logger.error(f"Error dialog shown: {title} - {message}")
        except Exception as e:
            logger.error(f"Error showing error dialog: {e}")

    def closeEvent(self, event) -> None:
        """Handle application close event"""
        try:
            self.stop_webcam()
            event.accept()
            logger.info("Application closed")
        except Exception as e:
            logger.error(f"Error during application close: {e}")
            event.accept()


if __name__ == "__main__":
    app = QApplication(sys.argv)
    auto_brightness_app = AutoBrightnessApp()
    sys.exit(app.exec())
