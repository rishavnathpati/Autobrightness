from PyQt5.QtWidgets import QSystemTrayIcon, QMenu, QAction, QApplication
from PyQt5.QtGui import QIcon

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