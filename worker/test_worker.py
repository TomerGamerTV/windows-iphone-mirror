import asyncio
import contextlib
import importlib
import queue
import time
from pathlib import Path
import sys
import threading
import unittest
from unittest import mock
from unittest.mock import AsyncMock

sys.path.insert(0, str(Path(__file__).resolve().parent))

import connection_windows
worker_main = importlib.import_module("worker")
from session import HevcPipeSink, MirrorSession, _write_all
from setup_actions import SetupError, execute as setup_execute, list_usb_devices


class WorkerSafetyTests(unittest.TestCase):
    def test_completed_media_task_is_reported_and_exception_is_consumed(self):
        async def failed_task():
            raise RuntimeError("private stream detail")

        async def check():
            session = MirrorSession(
                connection="wifi",
                serial="test-device",
                video_pipe="test-pipe",
                emit_event=lambda *_args: None,
            )
            task = asyncio.create_task(failed_task())
            await asyncio.sleep(0)
            session.stream_tasks = [task]
            self.assertEqual("stream_ended", session._stream_failure_code())
            self.assertTrue(task.done())

        asyncio.run(check())

    def test_hevc_pipe_write_retries_partial_writes(self):
        class PartialWriter:
            def __init__(self):
                self.data = bytearray()

            def write(self, value):
                chunk = bytes(value[:3])
                self.data.extend(chunk)
                return len(chunk)

        writer = PartialWriter()
        stop = mock.Mock()
        stop.is_set.return_value = False
        expected = b"0123456789abcdef"

        _write_all(writer, expected, stop)

        self.assertEqual(expected, bytes(writer.data))
        self.assertGreater(stop.is_set.call_count, 1)

    def test_hevc_pipe_sink_drops_stale_frame_when_queue_is_full(self):
        sink = object.__new__(HevcPipeSink)
        sink._stop = threading.Event()
        sink._queue = queue.Queue(maxsize=1)
        sink._queue.put_nowait(b"stale")
        sink.feed(b"fresh")
        self.assertEqual(b"fresh", sink._queue.get_nowait())

    def test_unknown_exception_text_is_not_returned_as_error_code(self):
        secret = "PAIRING_SECRET_SHOULD_NOT_LEAK"
        self.assertEqual("connection_failed", worker_main.error_code(RuntimeError(secret)))
        self.assertNotIn(secret, worker_main.error_code(RuntimeError(secret)))

    def test_device_state_exceptions_have_actionable_codes(self):
        cases = {
            "NotTrustedError": "usb_trust_required",
            "PairingDialogResponsePendingError": "trust_pending",
            "DeviceHasPasscodeSetError": "iphone_locked",
            "DeveloperDiskImageNotFoundError": "image_required",
            "DeviceVersionNotSupportedError": "unsupported_usb_version",
            "DeviceFeatureNotSupportedError": "display_features_unavailable",
        }
        for name, expected in cases.items():
            with self.subTest(name=name):
                exception_type = type(name, (Exception,), {})
                self.assertEqual(expected, worker_main.error_code(exception_type()))

    def test_mutating_setup_requires_confirmation_before_device_access(self):
        with self.assertRaises(SetupError) as caught:
            asyncio.run(setup_execute("pair-usb", None, False))
        self.assertEqual("confirmation_required", caught.exception.code)


class RouteFilteringTests(unittest.IsolatedAsyncioTestCase):
    async def test_auto_prefers_matching_usb_device(self):
        device = mock.Mock(is_usb=True, serial="usb-1")
        device.matches_udid.return_value = True
        with mock.patch("pymobiledevice3.usbmux.list_devices", new=AsyncMock(return_value=[device])):
            self.assertEqual(("usb", "usb-1"), await connection_windows.select_connection("auto", "usb-1"))

    async def test_auto_falls_back_to_wifi_when_usb_is_absent(self):
        with mock.patch("pymobiledevice3.usbmux.list_devices", new=AsyncMock(return_value=[])):
            self.assertEqual(("wifi", "saved-1"), await connection_windows.select_connection("auto", "saved-1"))

    async def test_explicit_usb_never_falls_back_to_wifi(self):
        with mock.patch("pymobiledevice3.usbmux.list_devices", new=AsyncMock(return_value=[])):
            with self.assertRaises(connection_windows.ConnectionSelectionError) as caught:
                await connection_windows.select_connection("usb", None)
        self.assertEqual("usb_phone_missing", caught.exception.code)

    async def test_multiple_usb_devices_require_selection(self):
        first = mock.Mock(is_usb=True, serial="usb-1")
        second = mock.Mock(is_usb=True, serial="usb-2")
        with mock.patch("pymobiledevice3.usbmux.list_devices", new=AsyncMock(return_value=[first, second])):
            with self.assertRaises(connection_windows.ConnectionSelectionError) as caught:
                await connection_windows.select_connection("auto", None)
        self.assertEqual("multiple_devices", caught.exception.code)

    async def test_wifi_requires_saved_pairing(self):
        with mock.patch.object(connection_windows, "iter_remote_paired_identifiers", return_value=[]):
            with self.assertRaises(connection_windows.ConnectionSelectionError) as caught:
                await connection_windows.wifi_provider()
        self.assertEqual("wifi_pairing_required", caught.exception.code)

    async def test_wifi_rejects_ambiguous_saved_pairings(self):
        with mock.patch.object(connection_windows, "iter_remote_paired_identifiers", return_value=["one", "two"]):
            with self.assertRaises(connection_windows.ConnectionSelectionError) as caught:
                await connection_windows.wifi_provider()
        self.assertEqual("multiple_devices", caught.exception.code)

    async def test_discover_wifi_returns_sorted_ipv4_endpoints(self):
        answer = mock.Mock(port=49152)
        answer.addresses = [
            mock.Mock(full_ip="2001:db8::10"),
            mock.Mock(full_ip="192.168.1.20"),
        ]
        second = mock.Mock(port=49153)
        second.addresses = [mock.Mock(full_ip="192.168.1.5")]
        with mock.patch.object(connection_windows, "browse_remotepairing", new=AsyncMock(return_value=[answer, second])):
            endpoints = await connection_windows.discover_wifi()
        self.assertEqual(
            [
                {"address": "192.168.1.5", "port": 49153},
                {"address": "192.168.1.20", "port": 49152},
                {"address": "2001:db8::10", "port": 49152},
            ],
            endpoints,
        )

    async def test_discover_wifi_returns_empty_when_browse_fails(self):
        with mock.patch.object(connection_windows, "browse_remotepairing", new=AsyncMock(side_effect=RuntimeError("mdns down"))):
            self.assertEqual([], await connection_windows.discover_wifi())

    async def test_worker_discover_wifi_command_returns_endpoints(self):
        worker = worker_main.Worker()
        with mock.patch.object(worker_main, "discover_wifi", new=AsyncMock(return_value=[{"address": "192.168.1.20", "port": 49152}])):
            result = await worker.handle("discover_wifi", {})
        self.assertEqual({"endpoints": [{"address": "192.168.1.20", "port": 49152}]}, result)

    async def test_wifi_direct_endpoint_skips_mdns_discovery(self):
        service = object()
        with (
            mock.patch.object(connection_windows, "iter_remote_paired_identifiers", return_value=["one"]),
            mock.patch.object(connection_windows, "network_route_allowed", new=AsyncMock(return_value=True)),
            mock.patch.object(connection_windows, "browse_remotepairing", new=AsyncMock(return_value=[])),
            mock.patch.object(connection_windows, "connect_wifi", new=AsyncMock(return_value=service)) as connect,
        ):
            result = await connection_windows.wifi_provider(
                serial="one", direct_address="198.51.100.42", direct_port=49152
            )
        self.assertEqual((service, None), result)
        connect.assert_awaited_once_with("one", "198.51.100.42", 49152)

    async def test_loopback_remote_is_rejected_without_route_probe(self):
        with mock.patch.object(connection_windows.asyncio, "to_thread") as to_thread:
            self.assertFalse(await connection_windows.network_route_allowed("127.0.0.1", 1234))
            to_thread.assert_not_called()

    async def test_vpn_interface_is_rejected(self):
        fake_stats = type("Stats", (), {"isup": True})()
        with (
            mock.patch.object(connection_windows, "_route_source", return_value="192.168.1.20"),
            mock.patch.object(connection_windows, "_interface_for_local_address", return_value="My VPN Adapter"),
            mock.patch.object(connection_windows.psutil, "net_if_stats", return_value={"My VPN Adapter": fake_stats}),
        ):
            self.assertFalse(await connection_windows.network_route_allowed("192.168.1.99", 1234))

    async def test_up_lan_interface_is_allowed(self):
        fake_stats = type("Stats", (), {"isup": True})()
        with (
            mock.patch.object(connection_windows, "_route_source", return_value="192.168.1.20"),
            mock.patch.object(connection_windows, "_interface_for_local_address", return_value="Wi-Fi"),
            mock.patch.object(connection_windows.psutil, "net_if_stats", return_value={"Wi-Fi": fake_stats}),
        ):
            self.assertTrue(await connection_windows.network_route_allowed("192.168.1.99", 1234))

    async def test_apple_usb_ethernet_description_is_rejected(self):
        fake_stats = type("Stats", (), {"isup": True})()
        with (
            mock.patch.object(connection_windows, "_route_source", return_value="172.20.10.2"),
            mock.patch.object(connection_windows, "_interface_for_local_address", return_value="Ethernet 7"),
            mock.patch.object(connection_windows, "_interface_description", return_value="Apple Mobile Device Ethernet"),
            mock.patch.object(connection_windows.psutil, "net_if_stats", return_value={"Ethernet 7": fake_stats}),
        ):
            self.assertFalse(await connection_windows.network_route_allowed("172.20.10.1", 1234))


class DeviceListingTests(unittest.IsolatedAsyncioTestCase):
    async def test_usb_listing_probes_trust_name_and_developer_mode(self):
        device = mock.Mock(is_usb=True, serial="usb-ready")
        client = mock.Mock()
        client.get_value = AsyncMock(return_value="Tomer's iPhone")
        client.get_developer_mode_status = AsyncMock(return_value=True)
        client.close = AsyncMock()
        with (
            mock.patch("pymobiledevice3.usbmux.list_devices", new=AsyncMock(return_value=[device])),
            mock.patch("pymobiledevice3.pair_records.iter_remote_paired_identifiers", return_value=[]),
            mock.patch("pymobiledevice3.lockdown.create_using_usbmux", new=AsyncMock(return_value=client)) as create,
        ):
            result = await list_usb_devices()
        self.assertEqual(
            {
                "serial": "usb-ready",
                "name": "Tomer's iPhone",
                "transport": "usb",
                "trusted": True,
                "developer_mode": True,
                "wifi_paired": False,
            },
            {key: result[0][key] for key in ("serial", "name", "transport", "trusted", "developer_mode", "wifi_paired")},
        )
        create.assert_awaited_once_with(serial="usb-ready", autopair=False, connection_type="USB")
        client.close.assert_awaited_once()

    async def test_usb_listing_preserves_untrusted_guidance(self):
        device = mock.Mock(is_usb=True, serial="usb-untrusted")
        with (
            mock.patch("pymobiledevice3.usbmux.list_devices", new=AsyncMock(return_value=[device])),
            mock.patch("pymobiledevice3.pair_records.iter_remote_paired_identifiers", return_value=[]),
            mock.patch(
                "pymobiledevice3.lockdown.create_using_usbmux",
                new=AsyncMock(side_effect=type("NotTrustedError", (Exception,), {})()),
            ),
        ):
            result = await list_usb_devices()
        self.assertFalse(result[0]["trusted"])
        self.assertEqual("usb_trust_required", result[0]["state_error"])


class FakeHid:
    def __init__(self):
        self.touch = []
        self.keyboard = []

    async def send_touchscreen(self, state, x, y):
        self.touch.append((state, x, y))

    async def send_keyboard(self, keyboard, usages):
        self.keyboard.append(tuple(sorted(usages)))

    async def close(self):
        return None


class HangingTouchHid(FakeHid):
    async def send_touchscreen(self, state, x, y):
        await asyncio.sleep(10)


class SessionInputTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.events = []
        self.session = MirrorSession(
            connection="usb",
            serial="test",
            video_pipe="unused",
            emit_event=lambda name, data: self.events.append((name, data)),
        )
        self.session.hid = FakeHid()
        self.session.keyboard = 1

    async def test_release_clears_touch_and_keyboard_state(self):
        self.session.contact = (120, 240)
        self.session.reported_keys = {4, 225}
        await self.session.release_input()
        self.assertIsNone(self.session.contact)
        self.assertEqual(set(), self.session.reported_keys)
        self.assertEqual((), self.session.hid.keyboard[-1])

    async def test_modifier_transition_precedes_letter(self):
        await self.session._report_keys({225, 4})
        self.assertEqual((225,), self.session.hid.keyboard[0])
        self.assertEqual((4, 225), self.session.hid.keyboard[-1])

    async def test_oversized_clipboard_is_rejected_before_sending(self):
        text = "x" * (1024 * 1024 + 1)
        with self.assertRaises(ValueError):
            await self.session.paste(text)

    async def test_fresh_click_reconnect_replays_original_click(self):
        self.session.input_enabled = False
        self.session.ensure_hid = AsyncMock()
        await self.session.touch("down", 1234, 5678)
        self.session.ensure_hid.assert_awaited_once()
        self.assertTrue(self.session.input_enabled)
        self.assertEqual([(194, 1234, 5678)], self.session.hid.touch)
        self.assertIn(("input_state", {"enabled": True}), self.events)

    async def test_hanging_touch_report_disables_stale_input(self):
        self.session.hid = HangingTouchHid()
        started = time.monotonic()
        await self.session.touch("down", 1234, 5678)
        self.assertLess(time.monotonic() - started, 0.85)
        self.assertFalse(self.session.input_enabled)
        self.assertIsNone(self.session.hid)
        self.assertIn(("input_state", {"enabled": False, "error_code": "input_disconnected"}), self.events)

    async def test_home_failure_preserves_touch_input(self):
        class FailingIndigo:
            async def connect(self):
                raise RuntimeError("indigo_unavailable")

            async def close(self):
                return None

        self.session.indigo = FailingIndigo()
        await self.session.home()
        self.assertTrue(self.session.input_enabled)
        self.assertIsNone(self.session.indigo)
        await self.session.touch("down", 2222, 3333)
        self.assertEqual([(194, 2222, 3333)], self.session.hid.touch)

    async def test_scroll_is_bounded_and_releases_contact(self):
        await self.session.scroll(0, 65535, 100)
        self.assertGreaterEqual(len(self.session.hid.touch), 2)
        contacts = self.session.hid.touch[:-1]
        release = self.session.hid.touch[-1]
        self.assertTrue(all(3277 <= x <= 62258 for _, x, _ in contacts))
        self.assertTrue(all(6554 <= y <= 58981 for _, _, y in contacts))
        self.assertEqual(contacts[-1][1:], release[1:])

    async def test_spotlight_uses_command_space_and_releases_keys(self):
        with mock.patch.object(asyncio, "sleep", new=AsyncMock()):
            await self.session.spotlight()
        self.assertIn((227,), self.session.hid.keyboard)
        self.assertIn((44, 227), self.session.hid.keyboard)
        self.assertEqual((), self.session.hid.keyboard[-1])

    async def test_app_switcher_uses_bottom_swipe_and_releases_contact(self):
        with mock.patch.object(asyncio, "sleep", new=AsyncMock()):
            await self.session.app_switcher()
        self.assertGreaterEqual(len(self.session.hid.touch), 3)
        self.assertEqual(32768, self.session.hid.touch[0][1])
        self.assertEqual(65535, self.session.hid.touch[0][2])
        self.assertEqual(2, self.session.hid.touch[-1][0])
        self.assertEqual(32768, self.session.hid.touch[-1][2])

    async def asyncTearDown(self):
        with contextlib.suppress(Exception):
            await self.session.release_input()


if __name__ == "__main__":
    unittest.main()
