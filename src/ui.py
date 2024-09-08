from PyQt6.QtWidgets import (
    QWidget,
    QVBoxLayout,
    QHBoxLayout,
    QLabel,
    QSlider,
    QPushButton,
    QGroupBox,
)
from PyQt6.QtCore import Qt, pyqtSignal
from PyQt6.QtGui import QFont, QIcon, QPalette, QColor


class AutoBrightnessUI(QWidget):
    start_stop_signal = pyqtSignal(bool)
    reset_signal = pyqtSignal()

    def __init__(self):
        super().__init__()
        self.initUI()

    def initUI(self):
        self.setWindowTitle("Auto Brightness")
        self.setStyleSheet(
            """
            QWidget {
                background-color: #1a1f2e;
                color: white;
                font-family: Arial;
            }
            QGroupBox {
                background-color: #252c3e;
                border-radius: 8px;
                margin-top: 0.5em;
                padding: 10px;
            }
            QGroupBox::title {
                subcontrol-origin: margin;
                left: 10px;
                padding: 0 3px 0 3px;
            }
            QPushButton {
                background-color: #3498db;
                border-radius: 4px;
                padding: 8px;
                font-weight: bold;
            }
            QPushButton:hover {
                background-color: #2980b9;
            }
            QSlider::groove:horizontal {
                border: 1px solid #4a5568;
                height: 8px;
                background: #2d3748;
                margin: 2px 0;
                border-radius: 4px;
            }
            QSlider::handle:horizontal {
                background: #3498db;
                border: 1px solid #3498db;
                width: 18px;
                margin: -2px 0;
                border-radius: 9px;
            }
        """
        )

        layout = QVBoxLayout()
        layout.setSpacing(10)
        layout.setContentsMargins(20, 20, 20, 20)

        # Title and minimize button
        title_layout = QHBoxLayout()
        title_label = QLabel("Auto Brightness")
        title_label.setFont(QFont("Arial", 18, QFont.Weight.Bold))
        title_layout.addWidget(title_label)
        minimize_button = QPushButton(QIcon("path_to_minimize_icon.png"), "")
        minimize_button.setFixedSize(24, 24)
        minimize_button.setStyleSheet(
            """
            QPushButton {
                background-color: #3498db;
                border-radius: 4px;
            }
            QPushButton:hover {
                background-color: #2980b9;
            }
        """
        )
        title_layout.addWidget(minimize_button)
        layout.addLayout(title_layout)

        # Luminance group
        luminance_group = QGroupBox("Luminance")
        luminance_layout = QVBoxLayout()
        self.luminance_label = QLabel()
        self.luminance_label.setFixedSize(300, 128)
        self.luminance_label.setStyleSheet(
            """
            background-color: #3a4254;
            border-radius: 6px;
        """
        )
        luminance_layout.addWidget(self.luminance_label)
        luminance_group.setLayout(luminance_layout)
        layout.addWidget(luminance_group)

        # Controls group
        controls_group = QGroupBox("Controls")
        controls_layout = QVBoxLayout()

        # Brightness threshold
        brightness_layout = QVBoxLayout()
        self.brightness_label = QLabel("Brightness Threshold: 190%")
        self.brightness_slider = QSlider(Qt.Orientation.Horizontal)
        self.brightness_slider.setRange(0, 255)
        self.brightness_slider.setValue(190)
        self.brightness_slider.valueChanged.connect(self.update_brightness_label)
        brightness_layout.addWidget(self.brightness_label)
        brightness_layout.addWidget(self.brightness_slider)
        controls_layout.addLayout(brightness_layout)

        # Exposure
        exposure_layout = QVBoxLayout()
        self.exposure_label = QLabel("Exposure: -2")
        self.exposure_slider = QSlider(Qt.Orientation.Horizontal)
        self.exposure_slider.setRange(-10, 10)
        self.exposure_slider.setValue(-2)
        self.exposure_slider.valueChanged.connect(self.update_exposure_label)
        exposure_layout.addWidget(self.exposure_label)
        exposure_layout.addWidget(self.exposure_slider)
        controls_layout.addLayout(exposure_layout)

        # Reset button
        self.reset_button = QPushButton("Reset")
        self.reset_button.clicked.connect(self.reset_values)
        controls_layout.addWidget(self.reset_button)

        controls_group.setLayout(controls_layout)
        layout.addWidget(controls_group)

        # Start/Stop button
        self.start_stop_button = QPushButton("Start")
        self.start_stop_button.clicked.connect(self.toggle_start_stop)
        layout.addWidget(self.start_stop_button)

        self.setLayout(layout)

    def update_brightness_label(self, value):
        self.brightness_label.setText(f"Brightness Threshold: {value}%")

    def update_exposure_label(self, value):
        self.exposure_label.setText(f"Exposure: {value}")

    def reset_values(self):
        self.brightness_slider.setValue(190)
        self.exposure_slider.setValue(-2)
        self.reset_signal.emit()

    def toggle_start_stop(self):
        if self.start_stop_button.text() == "Start":
            self.start_stop_button.setText("Stop")
            self.start_stop_button.setStyleSheet("background-color: #e74c3c;")
            self.start_stop_signal.emit(True)
        else:
            self.start_stop_button.setText("Start")
            self.start_stop_button.setStyleSheet("background-color: #3498db;")
            self.start_stop_signal.emit(False)


def set_dark_theme(app):
    app.setStyle("Fusion")
    dark_palette = QPalette()
    dark_palette.setColor(QPalette.ColorRole.Window, QColor(26, 31, 46))
    dark_palette.setColor(QPalette.ColorRole.WindowText, Qt.GlobalColor.white)
    dark_palette.setColor(QPalette.ColorRole.Base, QColor(37, 44, 62))
    dark_palette.setColor(QPalette.ColorRole.AlternateBase, QColor(53, 53, 53))
    dark_palette.setColor(QPalette.ColorRole.ToolTipBase, Qt.GlobalColor.white)
    dark_palette.setColor(QPalette.ColorRole.ToolTipText, Qt.GlobalColor.white)
    dark_palette.setColor(QPalette.ColorRole.Text, Qt.GlobalColor.white)
    dark_palette.setColor(QPalette.ColorRole.Button, QColor(37, 44, 62))
    dark_palette.setColor(QPalette.ColorRole.ButtonText, Qt.GlobalColor.white)
    dark_palette.setColor(QPalette.ColorRole.BrightText, Qt.GlobalColor.red)
    dark_palette.setColor(QPalette.ColorRole.Link, QColor(52, 152, 219))
    dark_palette.setColor(QPalette.ColorRole.Highlight, QColor(52, 152, 219))
    dark_palette.setColor(QPalette.ColorRole.HighlightedText, Qt.GlobalColor.black)
    app.setPalette(dark_palette)
    app.setStyleSheet(
        "QToolTip { color: #ffffff; background-color: #2a82da; border: 1px solid white; }"
    )
