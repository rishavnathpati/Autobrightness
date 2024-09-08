import cv2
import numpy as np
import sys
from PyQt6.QtCore import QTimer, pyqtSignal, QObject
from PyQt6.QtGui import QImage, QPixmap
import screen_brightness_control as sbc
import objc

class WebcamController(QObject):
    frame_ready = pyqtSignal(QPixmap, float, int)
    permission_error = pyqtSignal()

    def __init__(self, brightness_slider, exposure_slider):
        super().__init__()
        self.cap = None
        self.timer = QTimer()
        self.timer.timeout.connect(self.update_frame)
        self.brightness_slider = brightness_slider
        self.exposure_slider = exposure_slider
        self.current_brightness = None
        self.brightness_control_enabled = True
        self.is_macos = sys.platform == "darwin"

    def start_webcam(self):
        self.set_camera_properties()
        self.cap = cv2.VideoCapture(0)
        self.cap.set(cv2.CAP_PROP_AUTO_EXPOSURE, 0.5)
        self.cap.set(cv2.CAP_PROP_EXPOSURE, self.exposure_slider.value())
        self.timer.start(33)  # Approximately 30 fps

    def stop_webcam(self):
        if self.timer is not None:
            self.timer.stop()
        if self.cap is not None:
            self.cap.release()

    def update_frame(self):
        ret, frame = self.cap.read()
        if ret:
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            luminance = np.mean(gray)
            target_brightness = int(luminance / self.brightness_slider.value() * 100)
            smoothing_factor = 0.1
            if self.current_brightness is None:
                self.current_brightness = target_brightness
            else:
                self.current_brightness = (
                    1 - smoothing_factor
                ) * self.current_brightness + smoothing_factor * target_brightness

            # Convert grayscale image to QPixmap
            h, w = gray.shape
            bytes_per_line = w
            q_image = QImage(
                gray.data, w, h, bytes_per_line, QImage.Format.Format_Grayscale8
            )
            pixmap = QPixmap.fromImage(q_image)

            self.frame_ready.emit(pixmap, luminance, int(self.current_brightness))
            self.set_brightness(int(self.current_brightness))

    def set_brightness(self, level):
        if not self.brightness_control_enabled:
            return

        try:
            level = max(0, min(100, level))
            if self.is_macos:
                self.set_brightness_macos(level)
            else:
                self.set_brightness_windows(level)
        except Exception as e:
            print(f"Unexpected error setting brightness: {str(e)}")
            self.brightness_control_enabled = False
            self.permission_error.emit()

    def set_brightness_macos(self, level):
        import applescript
        script = applescript.AppleScript(f'''
            tell application "System Events"
                set brightness of display 1 to {level / 100}
            end tell
        ''')
        script.run()

    def set_brightness_windows(self, level):
        try:
            import wmi
            wmi_interface = wmi.WMI(namespace="wmi")
            methods = wmi_interface.WmiMonitorBrightnessMethods()[0]
            methods.WmiSetBrightness(level, 0)
        except ImportError:
            print("WMI module not available. Unable to set brightness on Windows.")
        except Exception as e:
            print(f"Error setting brightness on Windows: {str(e)}")

    def set_camera_properties(self):
        if self.is_macos:
            objc.loadBundle('AVFoundation', globals(),
                            bundle_path='/System/Library/Frameworks/AVFoundation.framework')
            AVCaptureDevice = objc.lookUpClass('AVCaptureDevice')
            AVCaptureDevice.defaultDeviceWithMediaType_('vide')  # This line might trigger the property to be set
