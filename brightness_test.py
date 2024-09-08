import time
from AppKit import NSScreen

def set_brightness(level):
    try:
        level = max(0, min(100, level)) / 100.0  # Convert to 0-1 range
        screens = NSScreen.screens()
        if screens:
            main_screen = screens[0]
            main_screen.setBrightness_(level)
            print(f"Brightness set to {level * 100:.0f}%")
        else:
            print("No screens found")
    except Exception as e:
        print(f"Unexpected error setting brightness: {str(e)}")

def main():
    # Gradually increase brightness from 0 to 100
    for level in range(0, 101, 5):
        print(f"Setting brightness to {level}%")
        set_brightness(level)
        time.sleep(5)

    # Gradually decrease brightness from 100 to 0
    for level in range(100, -1, -5):
        print(f"Setting brightness to {level}%")
        set_brightness(level)
        time.sleep(5)

if __name__ == "__main__":
    main()