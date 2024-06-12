from PyQt5.QtWidgets import (
    QWidget,
    QVBoxLayout,
    QGroupBox,
    QHBoxLayout,
    QPushButton,
    QLabel,
    QSystemTrayIcon
)
from PyQt5.QtGui import QImage, QPixmap, QPainter, QFont
from PyQt5.QtCore import Qt, QTimer
import numpy as np
from .tray_icon_manager import TrayIconManager
from .webcam_controller import WebcamController
from .ui import create_controls_group, create_luminance_group


class AutoBrightnessApp(QWidget):
    def __init__(self):
        super().__init__()
        self.tray_icon_manager = TrayIconManager(self)
        self.initUI()

    def initUI(self):
        layout = QVBoxLayout()

        self.luminance_label = QLabel()
        luminance_group = create_luminance_group(self.luminance_label)
        layout.addWidget(luminance_group)

        controls_group, self.brightness_slider, self.exposure_slider = (
            create_controls_group()
        )
        layout.addWidget(controls_group)

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
        self.webcam_controller = WebcamController(
            self.brightness_slider, self.exposure_slider
        )
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
        brightness = (
            int(self.webcam_controller.current_brightness)
            if self.webcam_controller.current_brightness is not None
            else 0
        )
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