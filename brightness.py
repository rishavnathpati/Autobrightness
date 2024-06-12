import sys
import cv2
import numpy as np
import wmi
from PyQt5.QtWidgets import (
    QApplication,
    QWidget,
    QVBoxLayout,
    QHBoxLayout,
    QLabel,
    QSlider,
    QPushButton,
    QGroupBox,
    QGridLayout,
    QSystemTrayIcon,
    QMenu,
    QAction,
)
from PyQt5.QtCore import Qt, QTimer
from PyQt5.QtGui import QImage, QPixmap, QPainter, QFont, QIcon


class TrayIconManager:
    def __init__(self, parent):
        self.tray_icon = QSystemTrayIcon(parent)
        self.tray_icon.setIcon(QIcon("icon.png"))  # Replace with your icon file
        self.tray_icon.setVisible(True)
        
        self.tray_menu = QMenu(parent)
        self.show_action = QAction("Show", parent)
        self.show_action.triggered.connect(parent.show)
        self.hide_action = QAction("Hide", parent)
        self.hide_action.triggered.connect(parent.hide)
        self.quit_action = QAction("Quit", parent)
        self.quit_action.triggered.connect(QApplication.quit)
        
        self.tray_menu.addAction(self.show_action)
        self.tray_menu.addAction(self.hide_action)
        self.tray_menu.addSeparator()
        self.tray_menu.addAction(self.quit_action)

        self.tray_icon.setContextMenu(self.tray_menu)
        self.tray_icon.activated.connect(parent.onTrayIconActivated)


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


class AutoBrightnessApp(QWidget):
    def __init__(self):
        super().__init__()
        self.tray_icon_manager = TrayIconManager(self)
        self.initUI()

    def initUI(self):
        layout = QVBoxLayout()

        # Create a group box for the luminance display
        luminance_group = QGroupBox("Luminance")
        luminance_layout = QVBoxLayout()
        self.luminance_label = QLabel()
        self.luminance_label.setFixedSize(200, 200)
        luminance_layout.addWidget(self.luminance_label)
        luminance_group.setLayout(luminance_layout)
        layout.addWidget(luminance_group)

        # Create a group box for the brightness and exposure controls
        controls_group = QGroupBox("Controls")
        controls_layout = QGridLayout()

        brightness_label = QLabel("Brightness Threshold:")
        self.brightness_slider = QSlider(Qt.Horizontal)
        self.brightness_slider.setMinimum(100)
        self.brightness_slider.setMaximum(200)
        self.brightness_slider.setValue(190)
        controls_layout.addWidget(brightness_label, 0, 0)
        controls_layout.addWidget(self.brightness_slider, 0, 1)

        exposure_label = QLabel("Exposure:")
        self.exposure_slider = QSlider(Qt.Horizontal)
        self.exposure_slider.setMinimum(-5)
        self.exposure_slider.setMaximum(5)
        self.exposure_slider.setValue(-2)
        controls_layout.addWidget(exposure_label, 1, 0)
        controls_layout.addWidget(self.exposure_slider, 1, 1)

        controls_group.setLayout(controls_layout)
        layout.addWidget(controls_group)

        # Create a group box for the start and stop buttons
        buttons_group = QGroupBox("Actions")
        buttons_layout = QHBoxLayout()
        self.start_button = QPushButton("Start")
        self.start_button.clicked.connect(self.start_webcam)
        self.stop_button = QPushButton("Stop")
        self.stop_button.clicked.connect(self.stop_webcam)
        self.stop_button.setEnabled(False)
        buttons_layout.addWidget(self.start_button)
        buttons_layout.addWidget(self.stop_button)
        buttons_group.setLayout(buttons_layout)
        layout.addWidget(buttons_group)

        self.setLayout(layout)
        self.setWindowTitle("Auto Brightness")
        self.show()

    def start_webcam(self):
        self.webcam_controller = WebcamController(self.brightness_slider, self.exposure_slider)
        self.webcam_controller.start_webcam()
        self.timer = QTimer()
        self.timer.timeout.connect(self.update_luminance_display)
        self.timer.start(100)
        self.start_button.setEnabled(False)
        self.stop_button.setEnabled(True)

    def stop_webcam(self):
        self.webcam_controller.stop_webcam()
        self.start_button.setEnabled(True)
        self.stop_button.setEnabled(False)

    def update_luminance_display(self):
        luminance = self.webcam_controller.current_brightness or 0
        brightness = int(self.webcam_controller.current_brightness) if self.webcam_controller.current_brightness is not None else 0
        luminance_image = np.full((200, 200), luminance, dtype=np.uint8)
        q_img = QImage(luminance_image.data, 200, 200, QImage.Format_Grayscale8)
        painter = QPainter(q_img)
        painter.setPen(Qt.black)
        painter.setFont(QFont("Arial", 12))
        painter.drawText(10, 30, f"Luminance: {luminance:.2f}")
        painter.drawText(10, 50, f"Brightness: {brightness}")
        painter.end()
        self.luminance_label.setPixmap(QPixmap.fromImage(q_img))

    def onTrayIconActivated(self, reason):
        if reason == QSystemTrayIcon.DoubleClick:
            if self.isHidden():
                self.show()
            else:
                self.hide()

    def closeEvent(self, event):
        self.stop_webcam()
        event.accept()


if __name__ == "__main__":
    app = QApplication(sys.argv)
    auto_brightness_app = AutoBrightnessApp()
    sys.exit(app.exec_())