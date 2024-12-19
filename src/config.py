import json
import os
from typing import Dict, Any

class Config:
    DEFAULT_CONFIG = {
        "camera": {
            "device_index": 0,
            "fps": 30,
            "default_exposure": -2,
        },
        "brightness": {
            "default_threshold": 190,
            "smoothing_factor": 0.1,
            "min_brightness": 0,
            "max_brightness": 100,
        },
        "ui": {
            "preview_width": 360,
            "preview_height": 270,
        },
        "advanced": {
            "smooth_transitions": True,
        }
    }

    def __init__(self):
        self.config_path = os.path.join(
            os.path.dirname(os.path.dirname(__file__)),
            "config.json"
        )
        self.settings = self.load_config()

    def load_config(self) -> Dict[str, Any]:
        """Load configuration from file or create default if not exists"""
        try:
            if os.path.exists(self.config_path):
                with open(self.config_path, 'r') as f:
                    return json.load(f)
            return self.save_config(self.DEFAULT_CONFIG)
        except Exception as e:
            print(f"Error loading config: {e}")
            return dict(self.DEFAULT_CONFIG)

    def save_config(self, config: Dict[str, Any]) -> Dict[str, Any]:
        """Save configuration to file"""
        try:
            with open(self.config_path, 'w') as f:
                json.dump(config, f, indent=4)
            return config
        except Exception as e:
            print(f"Error saving config: {e}")
            return dict(self.DEFAULT_CONFIG)

    def get(self, section: str, key: str, default: Any = None) -> Any:
        """Get a configuration value"""
        try:
            return self.settings[section][key]
        except KeyError:
            if default is not None:
                return default
            return self.DEFAULT_CONFIG[section][key]

    def set(self, section: str, key: str, value: Any) -> None:
        """Set a configuration value"""
        if section not in self.settings:
            self.settings[section] = {}
        self.settings[section][key] = value
        self.save_config(self.settings)
