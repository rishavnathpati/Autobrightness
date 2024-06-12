from PyQt5.QtWidgets import (
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
from PyQt5.QtCore import Qt
from PyQt5.QtGui import QImage, QPixmap, QPainter, QFont, QIcon
from camera import WebcamController
from brightness import BrightnessController

class AutoBrightnessApp(QWidget):
    def __init__(self):
        super().__init__()
        self.initUI()
        self.initTrayIcon()
        self.webcam_controller = WebcamController(self)
        self.brightness_controller = BrightnessController()

    def initUI(self):
        self.gui = AutoBrightnessGUI(self)
        self.gui.start_button.clicked.connect(self.start_webcam)
        self.gui.stop_button.clicked.connect(self.stop_webcam)
        self.gui.brightness_slider.valueChanged.connect(
            self.update_brightness_threshold
        )
        self.gui.exposure_slider.valueChanged.connect(self.update_exposure)


    def initTrayIcon(self):
        # ... (existing code for tray icon setup) ...

    def onTrayIconActivated(self, reason):
        # ... (existing code for tray icon activation) ...

    def closeEvent(self, event):
        self.webcam_controller.stop_webcam()
        event.accept()
