"""CoreDevice display and HID session used by the Windows GUI worker."""

from __future__ import annotations

import asyncio
import contextlib
import json
import os
import queue
import threading
import time
import uuid

from connection_windows import get_tunnel, select_connection


TOUCH_REPORT_TIMEOUT = 0.35
_TIMING_PATH = os.environ.get("IPHONE_MIRROR_TIMING_LOG")
_TIMING_LOCK = threading.Lock()


def _record_video_timing(event, **values):
    if not _TIMING_PATH:
        return
    entry = {"timestamp": time.time(), "event": event, **values}
    try:
        with _TIMING_LOCK:
            with open(_TIMING_PATH, "a", encoding="utf-8") as stream:
                stream.write(json.dumps(entry, separators=(",", ":")) + "\n")
    except OSError:
        # Diagnostics must never affect the media path.
        pass


def _write_all(stream, data, stop_event):
    remaining = memoryview(data)
    while remaining and not stop_event.is_set():
        written = stream.write(remaining)
        if not written:
            raise BrokenPipeError()
        remaining = remaining[written:]


class TrackedTransport:
    def __init__(self, transport):
        self.transport = transport
        self.last_packet = time.monotonic()

    async def recv(self):
        data = await self.transport.recv()
        self.last_packet = time.monotonic()
        return data

    def __getattr__(self, name):
        return getattr(self.transport, name)


class HevcPipeSink:
    def __init__(self, vps, sps, pps, *, pipe_path, loop, on_ready, on_stop, **_):
        from pymobiledevice3.remote.core_device.hevc_av import parse_sps, remove_emulation_prevention

        state = parse_sps(remove_emulation_prevention(sps[2:]))
        self.width = state.pic_width_in_luma_samples
        self.height = state.pic_height_in_luma_samples
        # Keep only a tiny buffer. A mirror must prefer a fresh frame over
        # preserving a backlog; a large queue turns decoder pressure into
        # seconds of visible input latency even when the transport is live.
        self._queue = queue.Queue(maxsize=30)
        self._header_pending = True
        self._stop = threading.Event()
        self._loop = loop
        self._on_stop = on_stop
        self._stream = open(pipe_path, "wb", buffering=0)
        self._thread = threading.Thread(target=self._write, name="hevc-pipe-writer", daemon=True)
        self._thread.start()
        self.feed(b"".join(b"\x00\x00\x00\x01" + nal for nal in (vps, sps, pps)))
        on_ready(self.width, self.height)

    def feed(self, data):
        if self._stop.is_set():
            return
        _record_video_timing("video_feed", bytes=len(data), queue_depth=self._queue.qsize())
        try:
            self._queue.put_nowait(data)
        except queue.Full:
            # Drain stale pending frames and enqueue the newest one. The
            # writer thread still owns pipe I/O; this keeps the receive path
            # non-blocking and bounds visible latency during decoder bursts.
            keep = 1 if getattr(self, "_header_pending", False) else 0
            while True:
                if self._queue.qsize() <= keep:
                    break
                with contextlib.suppress(queue.Empty):
                    self._queue.get_nowait()
                    continue
                break
            with contextlib.suppress(queue.Full):
                self._queue.put_nowait(data)

    def _write(self):
        try:
            while not self._stop.is_set():
                try:
                    data = self._queue.get(timeout=0.2)
                except queue.Empty:
                    continue
                if self._header_pending:
                    self._header_pending = False
                started = time.perf_counter()
                _write_all(self._stream, data, self._stop)
                _record_video_timing(
                    "video_written",
                    bytes=len(data),
                    queue_depth=self._queue.qsize(),
                    duration_ms=round((time.perf_counter() - started) * 1000, 3),
                )
        except (BrokenPipeError, OSError):
            if not self._stop.is_set():
                self._loop.call_soon_threadsafe(self._on_stop, "player_disconnected")

    def close(self):
        self._stop.set()
        with contextlib.suppress(Exception):
            self._stream.close()
        self._thread.join(timeout=1)


class MirrorSession:
    def __init__(self, *, connection, serial, wifi_address=None, wifi_port=49152, video_pipe, emit_event):
        self.connection = connection
        self.serial = serial
        self.wifi_address = wifi_address
        self.wifi_port = wifi_port
        self.video_pipe = video_pipe
        self.emit_event = emit_event
        self.stop_event = asyncio.Event()
        self.video_ready = asyncio.Event()
        self.loop = asyncio.get_running_loop()
        self.rsd = None
        self.service = None
        self.transport = None
        self.receiver = None
        self.sink = None
        self.session_id = None
        self.stream_tasks = []
        self.hid = None
        self.keyboard = None
        self.indigo = None
        self.contact = None
        self.reported_keys = set()
        self.input_enabled = True
        self.input_lock = asyncio.Lock()
        self.error_code = None

    def stop(self, code=None):
        if code and not self.error_code:
            self.error_code = code
        self.stop_event.set()

    def _stream_failure_code(self):
        """Return a safe error code when either media task has ended.

        The receiver and RTCP tasks are independent. Treating only the
        receiver as authoritative can leave the session advertised as live
        after its feedback loop has died; retrieving the exception also avoids
        an unobserved-task warning without exposing its payload.
        """
        for task in self.stream_tasks:
            if not task.done():
                continue
            if task.cancelled():
                return "stream_ended"
            with contextlib.suppress(BaseException):
                task.exception()
            return "stream_ended"
        return None

    def _video_ready(self, width, height):
        self.emit_event("video_format", {"width": int(width), "height": int(height)})
        self.video_ready.set()

    async def _connect_display(self):
        from pymobiledevice3.remote.core_device.display_service import DisplayService

        delays = (1, 2, 4)
        for attempt in range(len(delays) + 1):
            service = DisplayService(self.rsd)
            try:
                await service.connect()
                return service
            except BaseException as error:
                with contextlib.suppress(Exception):
                    await asyncio.wait_for(service.close(), 1)
                transient = isinstance(
                    error,
                    (TimeoutError, ConnectionResetError, BrokenPipeError, asyncio.IncompleteReadError),
                )
                if not transient or attempt == len(delays):
                    raise
                await asyncio.sleep(delays[attempt])

    async def run(self):
        from pymobiledevice3.remote.core_device.screen_stream import open_media_receiver
        from pymobiledevice3.remote.core_device.vnc_server import VncStreamServer

        mode, serial = await select_connection(self.connection, self.serial)
        self.serial = serial
        self.emit_event(
            "state",
            {"state": "starting", "active_connection": mode, "serial": serial},
        )
        try:
            async with get_tunnel(mode, serial, self.wifi_address, self.wifi_port) as rsd:
                self.rsd = rsd
                self.service = await self._connect_display()
                raw, receiver_ip = open_media_receiver(self.service, (8 * 1024 * 1024, 4 * 1024 * 1024))
                self.transport = TrackedTransport(raw)
                requested_id = uuid.uuid4()
                answer = await asyncio.wait_for(
                    self.service.start_video_stream(
                        receiver_ip=receiver_ip,
                        receiver_port=self.transport.port,
                        sender_ip=rsd.service.address[0],
                        display_id=1,
                        client_session_id=requested_id,
                        allow_rtcp_fb=False,
                        ltrp_enabled=False,
                    ),
                    12,
                )
                sid = answer["connection"]["options"]["avcMediaStreamOptionClientSessionID"]["uuid"]
                self.session_id = sid if isinstance(sid, uuid.UUID) else uuid.UUID(sid)
                self.receiver = VncStreamServer(rsd, bind="127.0.0.1", audio=False, decoder="av")
                self.receiver._transcoder_cls = lambda *args, **kwargs: self._make_sink(*args, **kwargs)
                self.receiver._loop = self.loop
                cfg = answer["connection"].get("streamConfig", {})
                self.receiver._local_ssrc = int(cfg.get("RemoteSSRC", 0))
                self.receiver._remote_ssrc = int(cfg.get("LocalSSRC", 0))
                source_port = int(cfg.get("SourcePort", 0))
                self.receiver._rtcp_dest = (rsd.service.address[0], source_port) if source_port else None
                self.receiver._active_transport = self.transport
                self.stream_tasks = [
                    asyncio.create_task(self.receiver._udp_recv_and_pipe(self.transport)),
                    asyncio.create_task(self.receiver._rtcp_send_loop(self.transport)),
                ]
                await asyncio.wait_for(self.video_ready.wait(), 15)
                await self.warm_input()
                self.emit_event(
                    "state",
                    {"state": "running", "active_connection": mode, "serial": serial},
                )
                while not self.stop_event.is_set():
                    stream_failure = self._stream_failure_code()
                    if stream_failure is not None:
                        self.stop(stream_failure)
                        break
                    if time.monotonic() - self.transport.last_packet > 15:
                        self.stop("stream_timeout")
                        break
                    with contextlib.suppress(asyncio.TimeoutError):
                        await asyncio.wait_for(self.stop_event.wait(), 0.25)
        finally:
            self.emit_event("state", {"state": "stopping", "serial": self.serial})
            await self.cleanup()
            final_state = "error" if self.error_code else "stopped"
            data = {"state": final_state, "serial": self.serial}
            if self.error_code:
                data["error_code"] = self.error_code
            self.emit_event("state", data)

    def _make_sink(self, vps, sps, pps, **kwargs):
        path = rf"\\.\pipe\{self.video_pipe}"
        self.sink = HevcPipeSink(
            vps,
            sps,
            pps,
            pipe_path=path,
            loop=self.loop,
            on_ready=self._video_ready,
            on_stop=self.stop,
            **kwargs,
        )
        return self.sink

    async def cleanup(self):
        await self.release_input()
        await self._close_input_services()
        if self.service is not None and self.session_id is not None:
            with contextlib.suppress(Exception):
                await asyncio.wait_for(self.service.stop_media_stream(self.session_id), 5)
        owned = [*self.stream_tasks]
        if self.receiver is not None:
            owned.extend(getattr(self.receiver, "_pli_tasks", ()))
        for task in owned:
            task.cancel()
        if owned:
            await asyncio.gather(*owned, return_exceptions=True)
        if self.sink is not None:
            await asyncio.to_thread(self.sink.close)
            self.sink = None
        if self.transport is not None:
            with contextlib.suppress(Exception):
                self.transport.close()
            self.transport = None
        if self.service is not None:
            with contextlib.suppress(Exception):
                await asyncio.wait_for(self.service.close(), 2)
            self.service = None

    async def ensure_hid(self):
        if self.hid is None:
            from pymobiledevice3.remote.core_device.hid_service import UniversalHIDServiceService

            self.hid = UniversalHIDServiceService(self.rsd)
            await self.hid.connect()

    async def _send_touchscreen(self, state, x, y):
        """Bound a touchscreen report so a stale Wi-Fi HID stream cannot queue input."""
        await asyncio.wait_for(self.hid.send_touchscreen(state, x, y), TOUCH_REPORT_TIMEOUT)

    async def warm_input(self):
        """Connect touchscreen HID before advertising Live state.

        This removes the Universal HID connection cost from the first touch,
        which was especially visible over Wi-Fi. A failed warm-up is
        non-fatal; the normal input path retries lazily.
        """
        with contextlib.suppress(Exception):
            await asyncio.wait_for(self.ensure_hid(), 4)

    async def _mark_input_failed(self, _error):
        self.input_enabled = False
        self.emit_event("input_state", {"enabled": False, "error_code": "input_disconnected"})
        await self.release_input()
        await self._close_input_services()

    async def _fresh_click_reconnect(self):
        try:
            await asyncio.wait_for(self.ensure_hid(), 4)
            self.input_enabled = True
            self.emit_event("input_state", {"enabled": True})
            return True
        except Exception as error:
            await self._mark_input_failed(error)
            return False

    async def touch(self, phase: str, x: int, y: int):
        from pymobiledevice3.remote.core_device.hid_service import (
            TOUCHSCREEN_STATE_CONTACT,
            TOUCHSCREEN_STATE_RELEASE,
        )

        x = max(0, min(65535, int(x)))
        y = max(0, min(65535, int(y)))
        async with self.input_lock:
            reconnected = False
            if not self.input_enabled:
                if phase == "down":
                    if not await self._fresh_click_reconnect():
                        return
                    reconnected = True
                else:
                    return
            try:
                if not reconnected:
                    await self.ensure_hid()
                if phase in ("down", "move"):
                    self.contact = (x, y)
                    await self._send_touchscreen(TOUCHSCREEN_STATE_CONTACT, x, y)
                elif phase == "up" and self.contact is not None:
                    last = self.contact
                    self.contact = None
                    await self._send_touchscreen(TOUCHSCREEN_STATE_RELEASE, *last)
            except Exception as error:
                await self._mark_input_failed(error)

    async def scroll(self, x: int, y: int, delta: float):
        from pymobiledevice3.remote.core_device.hid_service import (
            TOUCHSCREEN_STATE_CONTACT,
            TOUCHSCREEN_STATE_RELEASE,
        )

        if not self.input_enabled:
            return
        async with self.input_lock:
            try:
                await self.ensure_hid()
                x = max(3277, min(62258, int(x)))
                y = max(13107, min(52428, int(y)))
                amount = max(-4.0, min(4.0, float(delta)))
                end_y = max(6554, min(58981, round(y + amount * 6553)))
                last = (x, y)
                try:
                    for step in range(9):
                        last = (x, round(y + (end_y - y) * step / 8))
                        self.contact = last
                        await self._send_touchscreen(TOUCHSCREEN_STATE_CONTACT, *last)
                        if step < 8:
                            await asyncio.sleep(0.015)
                finally:
                    self.contact = None
                    with contextlib.suppress(Exception):
                        await self._send_touchscreen(TOUCHSCREEN_STATE_RELEASE, *last)
            except Exception as error:
                await self._mark_input_failed(error)

    async def _report_keys(self, desired):
        desired = set(int(value) for value in desired if 0 <= int(value) <= 255)
        old_mods = {value for value in self.reported_keys if 224 <= value <= 231}
        new_mods = {value for value in desired if 224 <= value <= 231}
        kept = (self.reported_keys & desired) - set(range(224, 232))
        for state in (kept | old_mods, kept | new_mods, desired):
            if state != self.reported_keys:
                await self.hid.send_keyboard(self.keyboard, state)
                modifiers_changed = (
                    {value for value in state if 224 <= value <= 231}
                    != {value for value in self.reported_keys if 224 <= value <= 231}
                )
                self.reported_keys = set(state)
                if modifiers_changed and state != desired:
                    await asyncio.sleep(0.005)

    async def key_state(self, usages):
        if not self.input_enabled:
            return
        async with self.input_lock:
            try:
                await self.ensure_hid()
                if self.keyboard is None:
                    self.keyboard = await self.hid.create_keyboard_service()
                await self._report_keys(set(usages))
            except Exception as error:
                await self._mark_input_failed(error)

    async def spotlight(self):
        if not self.input_enabled:
            return
        async with self.input_lock:
            try:
                await self.ensure_hid()
                if self.keyboard is None:
                    self.keyboard = await self.hid.create_keyboard_service()
                await self._report_keys({227})
                await self._report_keys({227, 44})
                await asyncio.sleep(0.06)
                await self._report_keys(set())
            except Exception as error:
                await self._mark_input_failed(error)

    async def app_switcher(self):
        if not self.input_enabled:
            return
        from pymobiledevice3.remote.core_device.hid_service import (
            TOUCHSCREEN_STATE_CONTACT,
            TOUCHSCREEN_STATE_RELEASE,
        )

        async with self.input_lock:
            try:
                await self.ensure_hid()
                # Reproduce the iPhone gesture with the same contact-drag
                # cadence used by pymobiledevice3's developer HID tooling.
                # Starting at the actual bottom edge is important: UIKit
                # treats a start above the edge as ordinary content dragging.
                x = 32768
                start_y = 65535
                # iOS recognizes App Switcher as a short upward edge swipe
                # followed by a hold. Stopping around the middle keeps the
                # contact in the gesture region instead of turning it into a
                # normal long content drag.
                end_y = 32768
                steps = 24
                last_y = start_y
                self.contact = (x, last_y)
                try:
                    await self._send_touchscreen(TOUCHSCREEN_STATE_CONTACT, x, last_y)
                    for step in range(1, steps + 1):
                        last_y = round(start_y + (end_y - start_y) * step / steps)
                        self.contact = (x, last_y)
                        await self._send_touchscreen(TOUCHSCREEN_STATE_CONTACT, x, last_y)
                        await asyncio.sleep(0.28 / steps)
                    await asyncio.sleep(0.6)
                finally:
                    self.contact = None
                    with contextlib.suppress(Exception):
                        await self._send_touchscreen(TOUCHSCREEN_STATE_RELEASE, x, last_y)
            except Exception as error:
                await self._mark_input_failed(error)

    async def home(self):
        from pymobiledevice3.remote.core_device.hid_service import (
            HID_BUTTON_STATE_DOWN,
            HID_BUTTON_STATE_UP,
            IndigoHIDService,
        )

        if not self.input_enabled:
            return
        async with self.input_lock:
            try:
                if self.indigo is None:
                    self.indigo = IndigoHIDService(self.rsd)
                    await self.indigo.connect()
                await self.indigo.send_button(0x0C, 0x40, HID_BUTTON_STATE_DOWN)
                await asyncio.sleep(0.06)
                await self.indigo.send_button(0x0C, 0x40, HID_BUTTON_STATE_UP)
            except Exception as error:
                # Home uses the separate Indigo button service. A failure on
                # that service must not tear down the touchscreen HID channel:
                # the next normal touch should remain usable.
                service = self.indigo
                self.indigo = None
                if service is not None:
                    with contextlib.suppress(Exception):
                        await asyncio.wait_for(service.close(), 1)

    async def paste(self, text: str):
        from pymobiledevice3.remote.core_device.pasteboard_service import PasteboardService

        if not isinstance(text, str) or not text or len(text.encode("utf-8")) > 1024 * 1024:
            raise ValueError("clipboard_invalid")
        if not self.input_enabled:
            raise RuntimeError("input_disconnected")
        async with self.input_lock:
            try:
                async with asyncio.timeout(5):
                    async with PasteboardService(self.rsd) as service:
                        reply = await service.set_text(text)
                text = None
                if not isinstance(reply, dict) or reply.get("command") != "SET_REPLY" or reply.get("error"):
                    raise RuntimeError("pasteboard_not_confirmed")
                reply = None
                await self.ensure_hid()
                if self.keyboard is None:
                    self.keyboard = await self.hid.create_keyboard_service()
                await self._report_keys({227})
                await self._report_keys({227, 25})
                await asyncio.sleep(0.05)
                await self._report_keys({227})
                await self._report_keys(set())
            except Exception as error:
                await self._mark_input_failed(error)
                raise RuntimeError("paste_failed") from error

    async def release_input(self):
        from pymobiledevice3.remote.core_device.hid_service import TOUCHSCREEN_STATE_RELEASE

        if self.hid is not None:
            if self.contact is not None:
                last = self.contact
                self.contact = None
                with contextlib.suppress(Exception):
                    await asyncio.wait_for(
                        self.hid.send_touchscreen(TOUCHSCREEN_STATE_RELEASE, *last),
                        TOUCH_REPORT_TIMEOUT,
                    )
            if self.keyboard is not None:
                with contextlib.suppress(Exception):
                    await asyncio.wait_for(
                        self.hid.send_keyboard(self.keyboard, []),
                        TOUCH_REPORT_TIMEOUT,
                    )
        self.reported_keys.clear()

    async def _close_input_services(self):
        for name in ("hid", "indigo"):
            service = getattr(self, name)
            setattr(self, name, None)
            if service is not None:
                with contextlib.suppress(Exception):
                    await asyncio.wait_for(service.close(), 1)
        self.keyboard = None
