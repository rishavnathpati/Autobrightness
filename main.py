import sys

from PyQt6.QtWidgets import QApplication
from src.app import AutoBrightnessApp

if __name__ == "__main__":
    app = QApplication(sys.argv)
    auto_brightness_app = AutoBrightnessApp()
    sys.exit(app.exec())