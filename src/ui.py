from PyQt6.QtWidgets import (
    QWidget,
    QVBoxLayout,
    QHBoxLayout,
    QLabel,
    QSlider,
    QPushButton,
    QFrame,
    QCheckBox,
)
from PyQt6.QtCore import Qt, pyqtSignal
from PyQt6.QtGui import QFont

from .config import Config
from .logger import logger

class AutoBrightnessUI(QWidget):
    start_stop_signal = pyqtSignal(bool)
    reset_signal = pyqtSignal()

    def __init__(self):
        super().__init__()
        self.config = Config()
        self.initUI()

    def initUI(self):
        """Initialize the user interface"""
        try:
            self.setWindowTitle("Auto Brightness")
            self.setFixedSize(400, 600)
            self.setStyleSheet("""
                QWidget {
                    background-color: #2f3640;
                    color: white;
                    font-size: 14px;
                }
                QPushButton {
                    background-color: #3498db;
                    border: none;
                    padding: 8px;
                    border-radius: 4px;
                    min-height: 40px;
                }
                QPushButton:hover {
                    background-color: #2980b9;
                }
                QFrame {
                    border-radius: 8px;
                    background-color: #3a4254;
                    padding: 10px;
                }
                QSlider::groove:horizontal {
                    height: 6px;
                    background: #4a4f5a;
                    border-radius: 3px;
                }
                QSlider::handle:horizontal {
                    background: #3498db;
                    width: 18px;
                    height: 18px;
                    margin: -6px 0;
                    border-radius: 9px;
                }
                QCheckBox {
                    spacing: 8px;
                }
                QCheckBox::indicator {
                    width: 18px;
                    height: 18px;
                    border-radius: 4px;
                    border: 2px solid #4a4f5a;
                }
                QCheckBox::indicator:checked {
                    background-color: #3498db;
                    border-color: #3498db;
                }
            """)

            main_layout = QVBoxLayout()
            main_layout.setSpacing(16)
            main_layout.setContentsMargins(20, 20, 20, 20)

            # Preview section
            preview_frame = QFrame()
            preview_layout = QVBoxLayout(preview_frame)
            preview_layout.setContentsMargins(0, 0, 0, 0)
            preview_layout.setSpacing(8)

            self.camera_label = QLabel()
            self.camera_label.setFixedSize(360, 270)
            self.camera_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            preview_layout.addWidget(self.camera_label)

            self.info_label = QLabel()
            self.info_label.setFixedSize(360, 50)
            self.info_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            preview_layout.addWidget(self.info_label)

            main_layout.addWidget(preview_frame)

            # Controls section
            controls_frame = QFrame()
            controls_layout = QVBoxLayout(controls_frame)
            controls_layout.setSpacing(16)

            # Brightness control
            brightness_layout = QVBoxLayout()
            brightness_header = QHBoxLayout()
            brightness_label = QLabel("Brightness Threshold")
            brightness_header.addWidget(brightness_label)
            self.brightness_value_label = QLabel(
                f"{self.config.get('brightness', 'default_threshold')}%"
            )
            brightness_header.addWidget(self.brightness_value_label)
            brightness_layout.addLayout(brightness_header)

            self.brightness_slider = QSlider(Qt.Orientation.Horizontal)
            self.brightness_slider.setRange(1, 255)  # Minimum 1 to prevent division by zero
            self.brightness_slider.setValue(
                self.config.get("brightness", "default_threshold")
            )
            self.brightness_slider.valueChanged.connect(self.update_brightness_label)
            brightness_layout.addWidget(self.brightness_slider)
            controls_layout.addLayout(brightness_layout)

            # Exposure control
            exposure_layout = QVBoxLayout()
            exposure_header = QHBoxLayout()
            exposure_label = QLabel("Exposure")
            exposure_header.addWidget(exposure_label)
            self.exposure_value_label = QLabel(
                str(self.config.get("camera", "default_exposure"))
            )
            exposure_header.addWidget(self.exposure_value_label)
            exposure_layout.addLayout(exposure_header)

            self.exposure_slider = QSlider(Qt.Orientation.Horizontal)
            self.exposure_slider.setRange(-10, 10)
            self.exposure_slider.setValue(
                self.config.get("camera", "default_exposure")
            )
            self.exposure_slider.valueChanged.connect(self.update_exposure_label)
            exposure_layout.addWidget(self.exposure_slider)
            controls_layout.addLayout(exposure_layout)

            # Smooth transitions checkbox
            self.smooth_transitions_checkbox = QCheckBox("Smooth Brightness Transitions")
            self.smooth_transitions_checkbox.setChecked(
                self.config.get("advanced", "smooth_transitions")
            )
            controls_layout.addWidget(self.smooth_transitions_checkbox)

            main_layout.addWidget(controls_frame)

            # Action buttons
            buttons_layout = QHBoxLayout()
            buttons_layout.setSpacing(10)

            self.reset_button = QPushButton("Reset")
            self.reset_button.clicked.connect(self.reset_values)
            buttons_layout.addWidget(self.reset_button)

            self.start_stop_button = QPushButton("Start")
            self.start_stop_button.clicked.connect(self.toggle_start_stop)
            buttons_layout.addWidget(self.start_stop_button)

            main_layout.addLayout(buttons_layout)
            self.setLayout(main_layout)

        except Exception as e:
            logger.error(f"Error initializing UI: {e}")

    def update_brightness_label(self, value: int) -> None:
        """Update brightness slider label"""
        try:
            self.brightness_value_label.setText(f"{value}%")
        except Exception as e:
            logger.error(f"Error updating brightness label: {e}")

    def update_exposure_label(self, value: int) -> None:
        """Update exposure slider label"""
        try:
            self.exposure_value_label.setText(str(value))
        except Exception as e:
            logger.error(f"Error updating exposure label: {e}")

    def reset_values(self) -> None:
        """Reset all values to defaults"""
        try:
            self.brightness_slider.setValue(
                self.config.get("brightness", "default_threshold")
            )
            self.exposure_slider.setValue(
                self.config.get("camera", "default_exposure")
            )
            self.smooth_transitions_checkbox.setChecked(
                self.config.get("advanced", "smooth_transitions")
            )
            self.reset_signal.emit()
        except Exception as e:
            logger.error(f"Error resetting values: {e}")

    def toggle_start_stop(self) -> None:
        """Toggle between start and stop states"""
        try:
            if self.start_stop_button.text() == "Start":
                self.start_stop_button.setText("Stop")
                self.start_stop_button.setStyleSheet("background-color: #e74c3c;")
                self.start_stop_signal.emit(True)
            else:
                self.start_stop_button.setText("Start")
                self.start_stop_button.setStyleSheet("background-color: #3498db;")
                self.start_stop_signal.emit(False)
        except Exception as e:
            logger.error(f"Error toggling start/stop: {e}")
