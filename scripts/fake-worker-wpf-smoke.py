import json
import os
import sys
import threading
import time


MODE = os.environ.get("IPHONE_MIRROR_FAKE_MODE", "normal")
START_COUNT = 0
PIPE_STOP = threading.Event()
PIPE_LOCK = threading.Lock()
PIPE_STREAMS = []


def hold_video_pipe(pipe_name):
    if not pipe_name:
        return
    try:
        stream = open("\\\\.\\pipe\\" + pipe_name, "wb", buffering=0)
        with PIPE_LOCK:
            PIPE_STREAMS.append(stream)
        while not PIPE_STOP.wait(0.1):
            pass
    except OSError:
        pass
    finally:
        with PIPE_LOCK:
            if "stream" in locals() and stream in PIPE_STREAMS:
                PIPE_STREAMS.remove(stream)
        if "stream" in locals():
            try:
                stream.close()
            except OSError:
                pass


def reset_video_pipe(pipe_name):
    PIPE_STOP.set()
    with PIPE_LOCK:
        streams = list(PIPE_STREAMS)
        PIPE_STREAMS.clear()
    for stream in streams:
        try:
            stream.close()
        except OSError:
            pass
    PIPE_STOP.clear()
    threading.Thread(target=hold_video_pipe, args=(pipe_name,), daemon=True).start()


def response(request, data=None):
    print(json.dumps({"type": "response", "id": request["id"], "ok": True, "data": data or {}}), flush=True)


def event(name, data):
    print(json.dumps({"type": "event", "event": name, "data": data}), flush=True)


for line in sys.stdin:
    request = json.loads(line)
    command = request.get("command")

    if command == "hello":
        response(request, {"protocol_version": 1})
    elif command == "list_devices":
        response(request, {"devices": [{
            "serial": "fake-wpf-device",
            "name": "Fake WPF iPhone",
            "transport": "usb",
            "trusted": True,
            "developer_mode": True,
            "wifi_paired": False,
        }]})
    elif command == "start_session":
        START_COUNT += 1
        reset_video_pipe(request.get("payload", {}).get("video_pipe"))
        event("state", {"state": "starting", "active_connection": "usb", "serial": "fake-wpf-device"})
        event("video_format", {"width": 390, "height": 844})
        event("state", {"state": "running", "active_connection": "usb", "serial": "fake-wpf-device"})
        response(request, {"started": True})
        if MODE == "missing-stack":
            time.sleep(0.5)
            event("state", {"state": "error", "error_code": "apple_device_support_missing", "serial": "fake-wpf-device"})
        elif MODE in ("locked", "untrusted", "developer-mode"):
            time.sleep(0.5)
            error_code = {
                "locked": "iphone_locked",
                "untrusted": "usb_trust_required",
                "developer-mode": "developer_mode_required",
            }[MODE]
            event("state", {"state": "error", "error_code": error_code, "serial": "fake-wpf-device"})
        elif MODE == "error" or (MODE == "network-retry" and START_COUNT == 1):
            time.sleep(0.5)
            event("state", {"state": "error", "error_code": "stream_timeout", "serial": "fake-wpf-device"})
        elif MODE == "crash":
            time.sleep(0.5)
            break
    elif command == "stop_session":
        PIPE_STOP.set()
        with PIPE_LOCK:
            streams = list(PIPE_STREAMS)
            PIPE_STREAMS.clear()
        for stream in streams:
            try:
                stream.close()
            except OSError:
                pass
        event("state", {"state": "stopping", "serial": "fake-wpf-device"})
        event("state", {"state": "stopped", "serial": "fake-wpf-device"})
        response(request)
    elif command == "shutdown":
        response(request)
        break
    else:
        response(request)
