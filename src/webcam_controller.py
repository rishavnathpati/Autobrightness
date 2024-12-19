import cv2
import numpy as np
from PyQt6.QtCore import QTimer, pyqtSignal, QObject
from PyQt6.QtGui import QImage, QPixmap
from typing import Optional

from .config import Config
from .logger import logger
from .brightness_control import get_brightness_controller


class WebcamController(QObject):
    frame_ready = pyqtSignal(QPixmap, float, int)
    permission_error = pyqtSignal()
    camera_error = pyqtSignal(str)

    def __init__(self, brightness_slider, exposure_slider):
        super().__init__()
        self.config = Config()
        self.brightness_controller = get_brightness_controller()

        self.cap: Optional[cv2.VideoCapture] = None
        self.timer = QTimer()
        self.timer.timeout.connect(self.update_frame)

        self.brightness_slider = brightness_slider
        self.exposure_slider = exposure_slider
        self.current_brightness: Optional[float] = None

        # Load settings from config
        self.device_index = self.config.get("camera", "device_index")
        self.fps = self.config.get("camera", "fps")
        self.smoothing_factor = self.config.get("brightness", "smoothing_factor")

    def start_webcam(self) -> None:
        """Initialize and start the webcam capture"""
        try:
            self.cap = cv2.VideoCapture(self.device_index)
            if not self.cap.isOpened():
                raise RuntimeError("Failed to open camera device")

            # Configure camera settings
            self.cap.set(cv2.CAP_PROP_AUTO_EXPOSURE, 0.5)  # Manual exposure mode
            self.cap.set(cv2.CAP_PROP_EXPOSURE, self.exposure_slider.value())

            # Start the timer for frame updates
            interval = int(1000 / self.fps)  # Convert fps to milliseconds
            self.timer.start(interval)

            logger.info(f"Webcam started: device={self.device_index}, fps={self.fps}")
        except Exception as e:
            error_msg = f"Failed to start webcam: {str(e)}"
            logger.error(error_msg)
            self.camera_error.emit(error_msg)

    def stop_webcam(self) -> None:
        """Stop the webcam capture and release resources"""
        try:
            if self.timer is not None and self.timer.isActive():
                self.timer.stop()

            if self.cap is not None:
                self.cap.release()
                self.cap = None

            logger.info("Webcam stopped")
        except Exception as e:
            logger.error(f"Error stopping webcam: {e}")

    def update_frame(self) -> None:
        """Process the next frame from the webcam"""
        if self.cap is None:
            return

        try:
            ret, frame = self.cap.read()
            if not ret:
                raise RuntimeError("Failed to read frame from camera")

            # Convert to grayscale and calculate luminance efficiently
            # Use direct array indexing for better performance
            gray = cv2.cvtColor(frame[:, :, [2, 1, 0]], cv2.COLOR_BGR2GRAY)  # RGB weights for better luminance
            luminance = float(np.mean(gray))

            # Calculate target brightness with threshold (prevent division by zero)
            threshold = max(1, self.brightness_slider.value())  # Ensure threshold is at least 1
            target_brightness = min(100, max(0, int((luminance / threshold) * 100)))

            # Create brightness representation image
            preview_size = (400, 300)  # Fixed size for better performance
            brightness_image = np.full(preview_size, int(luminance), dtype=np.uint8)
            
            # Convert to QPixmap
            q_image = QImage(
                brightness_image.data,
                preview_size[0],
                preview_size[1],
                preview_size[0],
                QImage.Format.Format_Grayscale8
            )
            pixmap = QPixmap.fromImage(q_image)

            # Apply smoothing if enabled
            if self.config.get("advanced", "smooth_transitions"):
                if self.current_brightness is None:
                    self.current_brightness = float(target_brightness)
                else:
                    self.current_brightness = (
                        (1 - self.smoothing_factor) * self.current_brightness +
                        self.smoothing_factor * target_brightness
                    )
                final_brightness = int(self.current_brightness)
            else:
                final_brightness = target_brightness

            # Emit the processed frame and update brightness
            self.frame_ready.emit(pixmap, luminance, final_brightness)
            
            # Update screen brightness less frequently for better performance
            if not hasattr(self, '_brightness_update_counter'):
                self._brightness_update_counter = 0
            self._brightness_update_counter += 1
            
            if self._brightness_update_counter >= 2:  # Update every 2 frames
                self.set_brightness(final_brightness)
                self._brightness_update_counter = 0

        except Exception as e:
            error_msg = f"Error processing frame: {str(e)}"
            logger.error(error_msg)
            self.camera_error.emit(error_msg)
            self.stop_webcam()

    def set_brightness(self, level: int) -> None:
        """Set the screen brightness level"""
        try:
            if not self.brightness_controller.set_brightness(level):
                self.permission_error.emit()
        except Exception as e:
            logger.error(f"Error setting brightness: {e}")
            self.permission_error.emit()

    def __del__(self) -> None:
        """Cleanup resources when the object is destroyed"""
        self.stop_webcam()
