from PyQt6.QtWidgets import QSystemTrayIcon, QMenu, QApplication
from PyQt6.QtGui import QAction, QIcon


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
        self.tray_icon.setIcon(
            QIcon("path/to/your/icon.png")
        )  # Replace with your icon path
        self.tray_icon.setToolTip("Auto Brightness")
        self.tray_icon.activated.connect(self.parent.onTrayIconActivated)
        self.tray_icon.show()

    def toggle_window(self):
        if self.parent.isVisible():
            self.parent.hide()
        else:
            self.parent.show()
