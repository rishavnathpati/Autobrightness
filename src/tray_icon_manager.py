from PyQt6.QtWidgets import QSystemTrayIcon, QMenu, QApplication
from PyQt6.QtGui import QAction, QIcon, QPixmap, QColor
import os


class TrayIconManager:
    def __init__(self, parent):
        self.parent = parent
        self.tray_icon = QSystemTrayIcon(parent)
        self.create_tray_icon()

    def create_tray_icon(self):
        tray_menu = QMenu()

        show_hide_action = QAction("Show/Hide", self.parent)
        show_hide_action.triggered.connect(self.toggle_window)
        tray_menu.addAction(show_hide_action)

        quit_action = QAction("Quit", self.parent)
        quit_action.triggered.connect(QApplication.instance().quit)
        tray_menu.addAction(quit_action)

        self.tray_icon.setContextMenu(tray_menu)
        
        # Set the icon
        icon_path = os.path.join(os.path.dirname(__file__), "..", "assets", "icon.png")
        if os.path.exists(icon_path):
            self.tray_icon.setIcon(QIcon(icon_path))
        else:
            print(f"Warning: Tray icon not found at {icon_path}. Using a default icon.")
            self.create_default_icon()

        self.tray_icon.setToolTip("Auto Brightness")
        self.tray_icon.activated.connect(self.parent.onTrayIconActivated)
        self.tray_icon.show()

    def create_default_icon(self):
        pixmap = QPixmap(32, 32)
        pixmap.fill(QColor(58, 66, 84))
        self.tray_icon.setIcon(QIcon(pixmap))

    def toggle_window(self):
        if self.parent.isVisible():
            self.parent.hide()
        else:
            self.parent.show()
