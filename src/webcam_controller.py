import cv2
import numpy as np
import wmi
from PyQt5.QtCore import QTimer

class WebcamController:
    def __init__(self, brightness_slider, exposure_slider):
        self.cap = None
        self.timer = QTimer()
        self.timer.timeout.connect(self.update_frame)
        self.brightness_slider = brightness_slider
        self.exposure_slider = exposure_slider
        self.current_brightness = None

    def start_webcam(self):
        self.cap = cv2.VideoCapture(0)
        self.cap.set(cv2.CAP_PROP_AUTO_EXPOSURE, 0.5)
        self.cap.set(cv2.CAP_PROP_EXPOSURE, self.exposure_slider.value())
        self.timer.start(100)

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
            self.set_brightness(int(self.current_brightness))

    def set_brightness(self, level):
        try:
            wmi_interface = wmi.WMI(namespace="wmi")
            methods = wmi_interface.WmiMonitorBrightnessMethods()[0]
            methods.WmiSetBrightness(int(level), 0)
        except wmi.x_wmi as e:
            print(f"Error setting brightness: {str(e)}")
