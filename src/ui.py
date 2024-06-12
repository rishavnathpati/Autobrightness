from PyQt5.QtWidgets import QGroupBox, QVBoxLayout, QHBoxLayout, QLabel, QSlider
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
    controls_layout = QHBoxLayout()

    brightness_label = QLabel("Brightness Threshold:")
    brightness_slider = QSlider(Qt.Horizontal)
    brightness_slider.setMinimum(100)
    brightness_slider.setMaximum(200)
    brightness_slider.setValue(190)
    controls_layout.addWidget(brightness_label)
    controls_layout.addWidget(brightness_slider)

    exposure_label = QLabel("Exposure:")
    exposure_slider = QSlider(Qt.Horizontal)
    exposure_slider.setMinimum(-5)
    exposure_slider.setMaximum(5)
    exposure_slider.setValue(-2)
    controls_layout.addWidget(exposure_label)
    controls_layout.addWidget(exposure_slider)

    controls_group.setLayout(controls_layout)
    return controls_group, brightness_slider, exposure_slider
