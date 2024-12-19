import sys
from abc import ABC, abstractmethod
from typing import Optional
from .logger import logger


class BrightnessController(ABC):
    @abstractmethod
    def set_brightness(self, level: int) -> bool:
        """Set screen brightness level (0-100)"""
        pass

    @abstractmethod
    def get_brightness(self) -> int:
        """Get current screen brightness level"""
        pass


class WindowsBrightnessController(BrightnessController):
    def __init__(self):
        try:
            import wmi

            self.wmi_interface = wmi.WMI(namespace="wmi")
            self._enabled = True
        except ImportError:
            logger.error("WMI module not available for Windows brightness control")
            self._enabled = False
        except Exception as e:
            logger.error(f"Failed to initialize Windows brightness control: {e}")
            self._enabled = False

    def set_brightness(self, level: int) -> bool:
        if not self._enabled:
            return False

        try:
            methods = self.wmi_interface.WmiMonitorBrightnessMethods()[0]
            methods.WmiSetBrightness(level, 0)
            return True
        except Exception as e:
            logger.error(f"Error setting Windows brightness: {e}")
            return False

    def get_brightness(self) -> int:
        if not self._enabled:
            return 50

        try:
            brightness = self.wmi_interface.WmiMonitorBrightness()[0]
            return brightness.CurrentBrightness
        except Exception as e:
            logger.error(f"Error getting Windows brightness: {e}")
            return 50


class MacOSBrightnessController(BrightnessController):
    def __init__(self):
        try:
            import Cocoa
            import Foundation

            self._enabled = True
        except ImportError:
            logger.error(
                "Cocoa/Foundation modules not available for macOS brightness control"
            )
            self._enabled = False
        except Exception as e:
            logger.error(f"Failed to initialize macOS brightness control: {e}")
            self._enabled = False

    def set_brightness(self, level: int) -> bool:
        if not self._enabled:
            return False

        try:
            import applescript

            script = applescript.AppleScript(
                f"""
                tell application "System Events"
                    set brightness of display 1 to {level / 100}
                end tell
            """
            )
            script.run()
            return True
        except Exception as e:
            logger.error(f"Error setting macOS brightness: {e}")
            return False

    def get_brightness(self) -> int:
        if not self._enabled:
            return 50

        try:
            import applescript

            script = applescript.AppleScript(
                """
                tell application "System Events"
                    get brightness of display 1
                end tell
            """
            )
            result = script.run()
            return int(float(result) * 100)
        except Exception as e:
            logger.error(f"Error getting macOS brightness: {e}")
            return 50


class LinuxBrightnessController(BrightnessController):
    def __init__(self):
        try:
            import screen_brightness_control as sbc

            self.sbc = sbc
            self._enabled = True
        except ImportError:
            logger.error(
                "screen_brightness_control module not available for Linux brightness control"
            )
            self._enabled = False
        except Exception as e:
            logger.error(f"Failed to initialize Linux brightness control: {e}")
            self._enabled = False

    def set_brightness(self, level: int) -> bool:
        if not self._enabled:
            return False

        try:
            self.sbc.set_brightness(level)
            return True
        except Exception as e:
            logger.error(f"Error setting Linux brightness: {e}")
            return False

    def get_brightness(self) -> int:
        if not self._enabled:
            return 50

        try:
            return self.sbc.get_brightness()[0]
        except Exception as e:
            logger.error(f"Error getting Linux brightness: {e}")
            return 50


def get_brightness_controller() -> BrightnessController:
    """Factory function to get the appropriate brightness controller for the current platform"""
    platform = sys.platform

    if platform == "win32":
        return WindowsBrightnessController()
    elif platform == "darwin":
        return MacOSBrightnessController()
    else:  # Assume Linux/Unix
        return LinuxBrightnessController()
