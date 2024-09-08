import sys
from PyQt6.QtWidgets import QApplication
from src.app import AutoBrightnessApp
from src.ui import set_dark_theme

if __name__ == "__main__":
    app = QApplication(sys.argv)
    set_dark_theme(app)
    auto_brightness_app = AutoBrightnessApp()
    sys.exit(app.exec())