import os
import sys
import warnings
import io
import base64
import contextlib

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PYZBAR_DIR = os.path.join(SCRIPT_DIR, "Lib", "site-packages", "pyzbar")

for dll_dir in (SCRIPT_DIR, PYZBAR_DIR):
    if os.path.isdir(dll_dir):
        try:
            os.add_dll_directory(dll_dir)
        except Exception:
            pass

warnings.filterwarnings("ignore")

try:
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

import cv2
from qreader import QReader


def create_reader():
    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
        return QReader()


def encode_result(text):
    if not text:
        return ""
    raw = text.encode("utf-8", errors="replace")
    return base64.b64encode(raw).decode("ascii")


def decode_path(reader, path):
    image = cv2.imread(path)
    if image is None:
        return ""

    image_rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
        decoded = reader.detect_and_decode(image=image_rgb)
    if decoded and decoded[0]:
        return decoded[0].strip()

    return ""


def main():
    qreader = create_reader()
    sys.stdout.write("READY\n")
    sys.stdout.flush()

    for raw in sys.stdin:
        path = raw.strip().lstrip("\ufeff")
        if path == "__EXIT__":
            break

        if not path:
            sys.stdout.write("\n")
            sys.stdout.flush()
            continue

        try:
            result = decode_path(qreader, path)
        except Exception:
            result = ""

        sys.stdout.write(encode_result(result) + "\n")
        sys.stdout.flush()


if __name__ == "__main__":
    main()
