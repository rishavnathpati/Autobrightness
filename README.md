# Auto Brightness

A Python application that automatically adjusts screen brightness based on ambient light using your webcam.

## Features

- Automatic brightness adjustment based on ambient light
- Manual exposure control
- Smooth brightness transitions
- Simple, efficient interface
- Cross-platform support (Windows, macOS, Linux)

## Requirements

- Python 3.8+
- Webcam
- Operating system permissions to adjust screen brightness

## Installation

1. Clone the repository:
```bash
git clone https://github.com/yourusername/autobrightness.git
cd autobrightness
```

2. Install dependencies:
```bash
pip install -r requirements.txt
```

## Usage

Run the application:
```bash
python main.py
```

### Controls

- **Brightness Threshold**: Adjusts how the application maps ambient light to screen brightness
- **Exposure**: Controls the webcam exposure level
- **Smooth Transitions**: Enable/disable gradual brightness changes
- **Reset**: Reset all settings to defaults
- **Start/Stop**: Toggle automatic brightness adjustment

## Configuration

The application settings are stored in `config.json` in the application directory. Default settings:

```json
{
    "camera": {
        "device_index": 0,
        "fps": 30,
        "default_exposure": -2
    },
    "brightness": {
        "default_threshold": 190,
        "smoothing_factor": 0.1,
        "min_brightness": 0,
        "max_brightness": 100
    },
    "ui": {
        "preview_width": 360,
        "preview_height": 270
    },
    "advanced": {
        "smooth_transitions": true
    }
}
```

### Permissions

#### Windows
No special permissions required.

#### macOS
The application requires accessibility permissions to control screen brightness:
1. Open System Preferences
2. Go to Security & Privacy
3. Click on the Privacy tab
4. Select 'Accessibility' from the left sidebar
5. Click the lock icon to make changes
6. Check the box next to the application
7. Restart the application

#### Linux
Ensure your user has permissions to adjust screen brightness. You may need to add your user to the `video` group:
```bash
sudo usermod -a -G video $USER
```

## Logging

Logs are stored in the `logs` directory with the naming format `autobrightness_YYYYMMDD.log`.

## Project Structure

```
autobrightness/
├── logs/
├── src/
│   ├── __init__.py
│   ├── app.py
│   ├── brightness_control.py
│   ├── config.py
│   ├── logger.py
│   ├── ui.py
│   └── webcam_controller.py
├── main.py
├── requirements.txt
└── README.md
```

## License

This project is licensed under the MIT License - see the LICENSE file for details.
