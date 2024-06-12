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


class AutoBrightnessApp(QWidget):
    def __init__(self):
        super().__init__()
        self.timer = None
        self.initUI()
        self.initTrayIcon()

    def initTrayIcon(self):
        self.tray_icon = QSystemTrayIcon(self)
        self.tray_icon.setIcon(QIcon("icon.png"))  # Replace with your icon file
        self.tray_icon.setVisible(True)

        self.tray_menu = QMenu(self)
        self.show_action = QAction("Show", self)
        self.show_action.triggered.connect(self.show)
        self.hide_action = QAction("Hide", self)
        self.hide_action.triggered.connect(self.hide)
        self.quit_action = QAction("Quit", self)
        self.quit_action.triggered.connect(QApplication.quit)
        self.tray_menu.addAction(self.show_action)
        self.tray_menu.addAction(self.hide_action)
        self.tray_menu.addSeparator()
        self.tray_menu.addAction(self.quit_action)

        self.tray_icon.setContextMenu(self.tray_menu)
        self.tray_icon.activated.connect(self.onTrayIconActivated)

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
        self.cap = cv2.VideoCapture(0)
        self.cap.set(cv2.CAP_PROP_AUTO_EXPOSURE, 0.5)
        self.cap.set(cv2.CAP_PROP_EXPOSURE, self.exposure_slider.value())
        self.timer = QTimer()
        self.timer.timeout.connect(self.update_frame)
        self.timer.start(100)
        self.start_button.setEnabled(False)
        self.stop_button.setEnabled(True)

    def stop_webcam(self):
        if self.timer is not None:
            self.timer.stop()
            self.timer = None
        if hasattr(self, "cap"):
            self.cap.release()
        self.start_button.setEnabled(True)
        self.stop_button.setEnabled(False)

    def update_frame(self):
        ret, frame = self.cap.read()
        if ret:
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            luminance = np.mean(gray)
            target_brightness = int(luminance / self.brightness_slider.value() * 100)
            smoothing_factor = 0.1
            if not hasattr(self, "current_brightness"):
                self.current_brightness = target_brightness
            else:
                self.current_brightness = (
                    1 - smoothing_factor
                ) * self.current_brightness + smoothing_factor * target_brightness
            self.set_brightness(int(self.current_brightness))

            luminance_image = np.full((200, 200), luminance, dtype=np.uint8)
            q_img = QImage(luminance_image.data, 200, 200, QImage.Format_Grayscale8)
            painter = QPainter(q_img)
            painter.setPen(Qt.black)
            painter.setFont(QFont("Arial", 12))
            painter.drawText(10, 30, f"Luminance: {luminance:.2f}")
            painter.drawText(10, 50, f"Brightness: {int(self.current_brightness)}")
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
