"""Private newline-delimited JSON worker for the Windows iPhone Mirror app."""

from __future__ import annotations

import asyncio
import contextlib
import importlib.metadata
import json
import os
from pathlib import Path
import sys
import threading
import time

# The Windows embeddable Python distribution uses a ._pth file, which means the
# script directory is not automatically added to sys.path. Add it explicitly so
# the installed worker can import its sibling modules without relying on a
# system Python environment or PYTHONPATH.
_WORKER_DIR = str(Path(__file__).resolve().parent)
if _WORKER_DIR not in sys.path:
    sys.path.insert(0, _WORKER_DIR)

from connection_windows import ConnectionSelectionError, discover_wifi
from session import MirrorSession
from setup_actions import SetupError, execute as setup_execute, list_usb_devices

PROTOCOL_VERSION = 1
_OUTPUT_LOCK = threading.Lock()
_TIMING_LOCK = threading.Lock()
_TIMING_PATH = os.environ.get("IPHONE_MIRROR_TIMING_LOG")


def _write(message: dict) -> None:
    with _OUTPUT_LOCK:
        sys.stdout.write(json.dumps(message, separators=(",", ":"), ensure_ascii=False) + "\n")
        sys.stdout.flush()


def emit_event(name: str, data: dict | None = None) -> None:
    _write({"type": "event", "event": name, "data": data or {}})


def emit_response(request_id: str, data: dict | list | None = None) -> None:
    _write({"type": "response", "id": request_id, "ok": True, "data": data or {}})


def emit_error(request_id: str, code: str) -> None:
    _write({"type": "response", "id": request_id, "ok": False, "error": {"code": code}})


def record_timing(command: str, elapsed: float, phase: str | None = None) -> None:
    """Write opt-in input timing without exposing coordinates or payloads."""
    if not _TIMING_PATH:
        return
    entry = {
        "timestamp": time.time(),
        "command": command,
        "duration_ms": round(elapsed * 1000, 3),
    }
    if phase is not None:
        entry["phase"] = phase
    try:
        with _TIMING_LOCK:
            Path(_TIMING_PATH).parent.mkdir(parents=True, exist_ok=True)
            with open(_TIMING_PATH, "a", encoding="utf-8") as stream:
                stream.write(json.dumps(entry, separators=(",", ":")) + "\n")
    except OSError:
        # Diagnostics must never affect the input path.
        pass


def error_code(error: BaseException) -> str:
    if isinstance(error, (SetupError, ConnectionSelectionError)):
        return error.code
    safe_runtime_codes = {
        "clipboard_invalid",
        "input_disconnected",
        "paste_failed",
        "session_not_running",
        "session_already_running",
        "invalid_request",
    }
    text = str(error)
    if text in safe_runtime_codes:
        return text
    name = type(error).__name__
    known = {
        "ConnectionFailedToUsbmuxdError": "apple_device_support_missing",
        "NotPairedError": "usb_trust_required",
        "NotTrustedError": "usb_trust_required",
        "InvalidHostIDError": "usb_trust_required",
        "UserDeniedPairingError": "trust_declined",
        "PairingDialogResponsePendingError": "trust_pending",
        "DeviceHasPasscodeSetError": "iphone_locked",
        "DeveloperDiskImageNotFoundError": "image_required",
        "DeviceVersionNotSupportedError": "unsupported_usb_version",
        "DeviceFeatureNotSupportedError": "display_features_unavailable",
        "NoDeviceConnectedError": "usb_phone_missing",
        "DeviceNotFoundError": "usb_phone_missing",
        "TimeoutError": "connection_failed",
    }
    return known.get(name, "connection_failed")


class Worker:
    def __init__(self):
        self.session: MirrorSession | None = None
        self.session_task: asyncio.Task | None = None
        self.shutdown = False

    async def _session_runner(self, session: MirrorSession):
        try:
            await session.run()
        except Exception as error:
            # stderr contains only exception type, never payloads or device responses.
            print(f"session-failed:{type(error).__name__}", file=sys.stderr, flush=True)
            emit_event("state", {"state": "error", "error_code": error_code(error), "serial": session.serial})
        finally:
            if self.session is session:
                self.session = None
                self.session_task = None

    async def handle(self, command: str, payload: dict):
        if command == "hello":
            if int(payload.get("protocol_version", -1)) != PROTOCOL_VERSION:
                raise RuntimeError("invalid_request")
            return {
                "protocol_version": PROTOCOL_VERSION,
                "python": f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}",
                "pymobiledevice3": importlib.metadata.version("pymobiledevice3"),
            }
        if command == "list_devices":
            return {"devices": await list_usb_devices()}
        if command == "discover_wifi":
            return {"endpoints": await discover_wifi()}
        if command == "start_session":
            if self.session_task is not None and not self.session_task.done():
                raise RuntimeError("session_already_running")
            connection = str(payload.get("connection", "auto")).lower()
            serial = payload.get("serial")
            wifi_address = payload.get("wifi_address")
            wifi_port = payload.get("wifi_port", 49152)
            video_pipe = payload.get("video_pipe")
            if not isinstance(video_pipe, str) or not video_pipe or len(video_pipe) > 200:
                raise RuntimeError("invalid_request")
            self.session = MirrorSession(
                connection=connection,
                serial=serial if isinstance(serial, str) and serial else None,
                wifi_address=wifi_address if isinstance(wifi_address, str) and wifi_address else None,
                wifi_port=int(wifi_port) if isinstance(wifi_port, int) else 49152,
                video_pipe=video_pipe,
                emit_event=emit_event,
            )
            self.session_task = asyncio.create_task(self._session_runner(self.session))
            return {"started": True}
        if command == "stop_session":
            if self.session is not None:
                self.session.stop()
            if self.session_task is not None:
                await asyncio.wait_for(asyncio.shield(self.session_task), 12)
            return {"stopped": True}
        if command == "setup":
            if self.session_task is not None and not self.session_task.done():
                raise RuntimeError("session_already_running")
            action = payload.get("action")
            if not isinstance(action, str):
                raise RuntimeError("invalid_request")
            serial = payload.get("serial")
            return await setup_execute(action, serial if isinstance(serial, str) else None, payload.get("approved") is True)
        if command == "shutdown":
            if self.session is not None:
                self.session.stop()
            if self.session_task is not None:
                with contextlib.suppress(asyncio.TimeoutError):
                    await asyncio.wait_for(asyncio.shield(self.session_task), 12)
            self.shutdown = True
            return {"shutdown": True}

        session = self.session
        if session is None:
            raise RuntimeError("session_not_running")
        if command == "touch":
            phase = str(payload.get("phase", ""))
            started = time.perf_counter()
            await session.touch(phase, int(payload.get("x", 0)), int(payload.get("y", 0)))
            record_timing(command, time.perf_counter() - started, phase)
            return {}
        if command == "scroll":
            started = time.perf_counter()
            await session.scroll(int(payload.get("x", 0)), int(payload.get("y", 0)), float(payload.get("delta", 0)))
            record_timing(command, time.perf_counter() - started)
            return {}
        if command == "key_state":
            usages = payload.get("usages", [])
            if not isinstance(usages, list) or len(usages) > 32:
                raise RuntimeError("invalid_request")
            await session.key_state(usages)
            return {}
        if command == "release_input":
            await session.release_input()
            return {}
        if command == "home":
            await session.home()
            return {}
        if command == "spotlight":
            await session.spotlight()
            return {}
        if command == "app_switcher":
            await session.app_switcher()
            return {}
        if command == "paste":
            text = payload.get("text")
            if not isinstance(text, str):
                raise RuntimeError("clipboard_invalid")
            await session.paste(text)
            return {}
        raise RuntimeError("invalid_request")


async def read_line() -> str:
    return await asyncio.to_thread(sys.stdin.readline)


async def main() -> int:
    worker = Worker()
    while not worker.shutdown:
        line = await read_line()
        if not line:
            break
        try:
            request = json.loads(line)
            request_id = request.get("id")
            command = request.get("command")
            payload = request.get("payload", {})
            if not isinstance(request_id, str) or not isinstance(command, str) or not isinstance(payload, dict):
                continue
            try:
                result = await worker.handle(command, payload)
            except Exception as error:
                print(f"command-failed:{type(error).__name__}", file=sys.stderr, flush=True)
                emit_error(request_id, error_code(error))
            else:
                emit_response(request_id, result)
        except (json.JSONDecodeError, TypeError, ValueError):
            continue
    if worker.session is not None:
        worker.session.stop()
    if worker.session_task is not None:
        with contextlib.suppress(Exception):
            await worker.session_task
    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))
