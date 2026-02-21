import os
import sys

script_dir = os.path.dirname(os.path.abspath(__file__))
pyzbar_dir = os.path.join(script_dir, "Lib", "site-packages", "pyzbar")
if os.path.isdir(pyzbar_dir):
    try:
        os.add_dll_directory(pyzbar_dir)
    except Exception:
        pass

try:
    os.add_dll_directory(script_dir)
except Exception:
    pass

import cv2
from qreader import QReader

def main():
    if len(sys.argv) < 2:
        print("")
        return 0

    path = sys.argv[1]
    image = cv2.imread(path)
    if image is None:
        print("")
        return 0

    image_rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    qreader = QReader()
    decoded = qreader.detect_and_decode(image=image_rgb)
    if decoded and decoded[0]:
        print(decoded[0])
    else:
        print("")
    return 0

if __name__ == "__main__":
    sys.exit(main())
