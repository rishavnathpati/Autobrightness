from PyQt5.QtWidgets import (
    QGroupBox,
    QVBoxLayout,
    QGridLayout,
    QLabel,
    QSlider,
    QSpinBox,
    QPushButton,
    QStyle,
    QApplication,
)
from PyQt5.QtCore import Qt


def create_luminance_group(luminance_label):
    luminance_group = QGroupBox("Luminance")
    luminance_layout = QVBoxLayout()
    luminance_label.setFixedSize(200, 200)
    luminance_layout.addWidget(luminance_label)
    luminance_group.setLayout(luminance_layout)
    return luminance_group


def create_controls_group():
    controls_group = QGroupBox("Controls")
    controls_layout = QGridLayout()

    # Brightness control
    brightness_label = QLabel("Brightness Threshold:")
    brightness_label.setToolTip("Adjust the brightness threshold")
    brightness_slider = QSlider(Qt.Horizontal)
    brightness_slider.setMinimum(100)
    brightness_slider.setMaximum(200)
    brightness_slider.setValue(190)
    brightness_spinbox = QSpinBox()
    brightness_spinbox.setRange(100, 200)
    brightness_spinbox.setValue(190)
    brightness_unit = QLabel("%")

    # Exposure control
    exposure_label = QLabel("Exposure:")
    exposure_label.setToolTip("Adjust the camera exposure")
    exposure_slider = QSlider(Qt.Horizontal)
    exposure_slider.setMinimum(-5)
    exposure_slider.setMaximum(5)
    exposure_slider.setValue(-2)
    exposure_spinbox = QSpinBox()
    exposure_spinbox.setRange(-5, 5)
    exposure_spinbox.setValue(-2)

    # Reset button
    reset_button = QPushButton("Reset")
    # Get the current style and use it to set the icon
    style = QApplication.style()
    reset_button.setIcon(style.standardIcon(QStyle.SP_BrowserReload))

    # Layout
    controls_layout.addWidget(brightness_label, 0, 0)
    controls_layout.addWidget(brightness_slider, 0, 1)
    controls_layout.addWidget(brightness_spinbox, 0, 2)
    controls_layout.addWidget(brightness_unit, 0, 3)
    controls_layout.addWidget(exposure_label, 1, 0)
    controls_layout.addWidget(exposure_slider, 1, 1)
    controls_layout.addWidget(exposure_spinbox, 1, 2)
    controls_layout.addWidget(reset_button, 2, 0, 1, 4)

    # Connect sliders and spinboxes
    brightness_slider.valueChanged.connect(brightness_spinbox.setValue)
    brightness_spinbox.valueChanged.connect(brightness_slider.setValue)
    exposure_slider.valueChanged.connect(exposure_spinbox.setValue)
    exposure_spinbox.valueChanged.connect(exposure_slider.setValue)

    controls_group.setLayout(controls_layout)
    return controls_group, brightness_slider, exposure_slider, reset_button
