from PyQt5.QtWidgets import QWidget, QSystemTrayIcon, QMenu, QAction, QApplication
from PyQt5.QtGui import QIcon
from tray_icon_manager import AutoBrightnessGUI
from webcam_controller import WebcamController
from src.brightness import BrightnessController


class AutoBrightnessApp(QWidget):
    def __init__(self):
        super().__init__()
        self.initUI()
        self.initTrayIcon()
        self.webcam_controller = WebcamController(self)
        self.brightness_controller = BrightnessController()

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

    def start_webcam(self):
        self.webcam_controller.start_webcam()
        self.hide()

    def stop_webcam(self):
        self.webcam_controller.stop_webcam()
        self.show()

    def update_brightness_threshold(self, value):
        self.webcam_controller.set_brightness_threshold(value)

    def update_exposure(self, value):
        self.webcam_controller.set_exposure(value)

    def onTrayIconActivated(self, reason):
        if reason == QSystemTrayIcon.DoubleClick:
            if self.isHidden():
                self.show()
            else:
                self.hide()

    def closeEvent(self, event):
        self.webcam_controller.stop_webcam()
        event.accept()
