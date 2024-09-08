from PyQt6.QtWidgets import (
    QWidget,
    QVBoxLayout,
    QHBoxLayout,
    QLabel,
    QSlider,
    QPushButton,
    QGroupBox,
    QGridLayout,
    QCheckBox,
    QSpacerItem,
    QSizePolicy,
)
from PyQt6.QtCore import Qt, pyqtSignal
from PyQt6.QtGui import QFont, QIcon


class AutoBrightnessUI(QWidget):
    start_stop_signal = pyqtSignal(bool)
    reset_signal = pyqtSignal()

    def __init__(self):
        super().__init__()
        self.initUI()

    def initUI(self):
        self.setWindowTitle("Auto Brightness")

        main_layout = QVBoxLayout()
        main_layout.setSpacing(20)
        main_layout.setContentsMargins(30, 30, 30, 30)

        # Title and minimize button
        title_layout = QHBoxLayout()
        title_label = QLabel("Auto Brightness")
        title_label.setFont(QFont("Arial", 24, QFont.Weight.Bold))
        title_layout.addWidget(title_label)
        title_layout.addStretch()
        minimize_button = QPushButton(QIcon("path_to_minimize_icon.png"), "")
        minimize_button.setFixedSize(30, 30)
        minimize_button.setStyleSheet(
            """
            QPushButton {
                background-color: #3498db;
                border-radius: 15px;
            }
            QPushButton:hover {
                background-color: #2980b9;
            }
        """
        )
        title_layout.addWidget(minimize_button)
        main_layout.addLayout(title_layout)

        # Camera view
        camera_group = QGroupBox("Camera View")
        camera_layout = QVBoxLayout()
        self.camera_label = QLabel()
        self.camera_label.setFixedSize(400, 300)
        self.camera_label.setStyleSheet(
            """
            background-color: #3a4254;
            border-radius: 8px;
        """
        )
        self.camera_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        camera_layout.addWidget(self.camera_label)

        # Add info label
        self.info_label = QLabel()
        self.info_label.setFixedSize(400, 60)
        self.info_label.setStyleSheet(
            """
            background-color: #3a4254;
            border-radius: 8px;
            margin-top: 10px;
        """
        )
        camera_layout.addWidget(self.info_label)

        camera_group.setLayout(camera_layout)
        main_layout.addWidget(camera_group)

        # Controls
        controls_group = QGroupBox("Controls")
        controls_layout = QGridLayout()
        controls_layout.setVerticalSpacing(15)
        controls_layout.setHorizontalSpacing(10)

        # Brightness threshold
        brightness_label = QLabel("Brightness Threshold:")
        controls_layout.addWidget(brightness_label, 0, 0)
        self.brightness_slider = QSlider(Qt.Orientation.Horizontal)
        self.brightness_slider.setRange(0, 255)
        self.brightness_slider.setValue(190)
        self.brightness_slider.valueChanged.connect(self.update_brightness_label)
        controls_layout.addWidget(self.brightness_slider, 0, 1)
        self.brightness_value_label = QLabel("190%")
        controls_layout.addWidget(self.brightness_value_label, 0, 2)

        # Exposure
        exposure_label = QLabel("Exposure:")
        controls_layout.addWidget(exposure_label, 1, 0)
        self.exposure_slider = QSlider(Qt.Orientation.Horizontal)
        self.exposure_slider.setRange(-10, 10)
        self.exposure_slider.setValue(-2)
        self.exposure_slider.valueChanged.connect(self.update_exposure_label)
        controls_layout.addWidget(self.exposure_slider, 1, 1)
        self.exposure_value_label = QLabel("-2")
        controls_layout.addWidget(self.exposure_value_label, 1, 2)

        # Reset button
        self.reset_button = QPushButton("Reset")
        self.reset_button.clicked.connect(self.reset_values)
        controls_layout.addWidget(self.reset_button, 2, 0, 1, 3)

        controls_group.setLayout(controls_layout)
        main_layout.addWidget(controls_group)

        # Advanced settings
        advanced_group = QGroupBox("Advanced Settings")
        advanced_layout = QVBoxLayout()

        self.auto_exposure_checkbox = QCheckBox("Auto Exposure")
        advanced_layout.addWidget(self.auto_exposure_checkbox)

        self.smooth_transitions_checkbox = QCheckBox("Smooth Brightness Transitions")
        advanced_layout.addWidget(self.smooth_transitions_checkbox)

        advanced_group.setLayout(advanced_layout)
        main_layout.addWidget(advanced_group)

        # Start/Stop button
        self.start_stop_button = QPushButton("Start")
        self.start_stop_button.clicked.connect(self.toggle_start_stop)
        self.start_stop_button.setFixedHeight(50)
        main_layout.addWidget(self.start_stop_button)

        # Add stretching space
        main_layout.addItem(
            QSpacerItem(
                20, 40, QSizePolicy.Policy.Minimum, QSizePolicy.Policy.Expanding
            )
        )

        self.setLayout(main_layout)

    def update_brightness_label(self, value):
        self.brightness_value_label.setText(f"{value}%")

    def update_exposure_label(self, value):
        self.exposure_value_label.setText(str(value))

    def reset_values(self):
        self.brightness_slider.setValue(190)
        self.exposure_slider.setValue(-2)
        self.auto_exposure_checkbox.setChecked(False)
        self.smooth_transitions_checkbox.setChecked(True)
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

    def update_camera_view(self, image):
        self.camera_label.setPixmap(image)
